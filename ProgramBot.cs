using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.SharedApis;
using HyperLiquid.Net;
using HyperLiquid.Net.Clients;
using HyperLiquid.Net.Enums;
using Logging;
using Serilog;
using System.CommandLine;
using System.CommandLine.Parsing;

public class ProgramBot
{
    private Config _config;
    private HyperLiquidExchange _exchange;
    private MySQLRepository _repository;
    private OhlcvDataRepository _dataRepository;
    private ATRTrailingStopStrategy _strategy;

    // シンボルごとのサービスとポジション管理
    private Dictionary<string, OhlcvDataService> _dataServices;
    private Dictionary<string, RealTimePosition> _positions;

    // キャンセレーショントークン
    private CancellationTokenSource _cancellationTokenSource;

    // タイムフレーム
    private KlineInterval _timeframe;

    public ProgramBot()
    {
        _config = Config.LoadSettings("secrets.json");

        var isTestnet = _config.IsTestnet;

        // ウォレットアドレスと秘密鍵
        var walletAddressMain = _config.WalletAddress.MainWalletAddress;
        var walletAddress = _config.WalletAddress.APIWalletAddress;
        var privateKey = _config.WalletAddress.APIWalletAddressKey;

        var restClientMainWallet = new HyperLiquidRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(walletAddressMain, "dummy");
            if (isTestnet)
            {
                options.Environment = HyperLiquidEnvironment.Testnet;
            }
        });

        var restClient = new HyperLiquidRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(walletAddress, privateKey);
            if (isTestnet)
            {
                options.Environment = HyperLiquidEnvironment.Testnet;
            }
        });

        _exchange = new HyperLiquidExchange(restClientMainWallet, restClient);
        _repository = new MySQLRepository(_config.ConnectionString);
        _dataRepository = new OhlcvDataRepository(_repository);

        // ストラテジー初期化
        _strategy = new ATRTrailingStopStrategy(14, 3.0m);

        _dataServices = new Dictionary<string, OhlcvDataService>();
        _positions = new Dictionary<string, RealTimePosition>();
        _cancellationTokenSource = new CancellationTokenSource();

        // タイムフレームを設定から取得
        _timeframe = ParseTimeframe(_config.Bot.Timeframe);
    }

    public async Task RunBotAsync()
    {
        Log.Information("========== リアルタイム自動取引ボットを起動します ==========");
        Log.Information("対象シンボル: {Symbols}", string.Join(", ", _config.Bot.Symbols));
        Log.Information("タイムフレーム: {Timeframe}", _config.Bot.Timeframe);
        Log.Information("ポジション更新間隔: {PositionUpdateInterval}分", _config.Bot.PositionUpdateIntervalMinutes);
        Log.Information("ストラテジー実行間隔: {StrategyCheckInterval}分", _config.Bot.StrategyCheckIntervalMinutes);

        try
        {
            // 初期化処理
            await InitializeAsync();

            // 2つの独立したループを並列実行
            var strategyTask = StrategyExecutionLoopAsync(_cancellationTokenSource.Token);
            var positionTask = PositionUpdateLoopAsync(_cancellationTokenSource.Token);

            Log.Information("========== ボットの実行を開始しました ==========");

            // 両方のタスクを待機
            await Task.WhenAll(strategyTask, positionTask);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ボットの実行中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private async Task InitializeAsync()
    {
        Log.Information("初期化処理を開始します...");

        // 各シンボルごとに初期化（並列実行）
        var initTasks = _config.Bot.Symbols.Select(async symbol =>
        {
            Log.Information("シンボル {Symbol} を初期化中...", symbol);

            // 1. 過去2ヶ月分のデータを取得してDB保存
            await InitializeHistoricalDataAsync(symbol);

            // 2. OhlcvDataServiceを作成・開始
            await InitializeDataServiceAsync(symbol);

            // 3. 取引所ポジションと同期
            await SyncPositionWithExchangeAsync(symbol);

            Log.Information("シンボル {Symbol} の初期化が完了しました", symbol);
        });

        await Task.WhenAll(initTasks);

        Log.Information("全シンボルの初期化が完了しました");
    }

    /// <summary>
    /// 過去データの初期化
    /// </summary>
    private async Task InitializeHistoricalDataAsync(string symbol)
    {
        Log.Information("シンボル {Symbol} の過去データを取得中...", symbol);

        var startDate = DateTime.UtcNow.AddMonths(-2);
        var endDate = DateTime.UtcNow;

        var candle = await _exchange.GetKlinesAsync(
            symbol,
            _timeframe,
            startDate: startDate,
            endDate: endDate,
            limit: 100000);

        Log.Debug("取得したデータ件数: {Count}", candle.Count());

        // DBに保存
        await _repository.DeleteOhlcvDataBySymbolAsync(symbol);
        await _repository.AddOrUpdateOhlcvDataAsync(symbol, candle.Select(c => new OhlcvData
        {
            OpenPrice = c.OpenPrice,
            HighPrice = c.HighPrice,
            LowPrice = c.LowPrice,
            ClosePrice = c.ClosePrice,
            Volume = c.Volume,
            TimestampUtc = c.OpenTime,
            CreatedAt = DateTime.UtcNow
        }).ToList());

        Log.Information("シンボル {Symbol} の過去データをDBに保存しました（期間: {StartDate} - {EndDate}）",
            symbol, startDate, endDate);
    }

    /// <summary>
    /// OhlcvDataServiceの初期化
    /// </summary>
    private async Task InitializeDataServiceAsync(string symbol)
    {
        var dataService = new OhlcvDataService(
            dataRepository: _dataRepository,
            exchange: _exchange,
            symbol: symbol,
            interval: _timeframe,
            refreshInterval: TimeSpan.FromMinutes(1), // 1分ごとに更新
            dataCount: 250
        );

        // データ更新イベントをサブスクライブ
        dataService.DataUpdated += (s, e) =>
        {
            Log.Debug("シンボル {Symbol} のデータが更新されました。件数: {Count}, 最新日時: {LatestTime}",
                symbol, e.UpdatedData.Count, e.UpdatedData.LastOrDefault()?.TimestampUtc);
        };

        // サービスをバックグラウンドで開始（待機しない）
        _ = Task.Run(async () =>
        {
            try
            {
                await dataService.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "シンボル {Symbol} のOhlcvDataService開始エラー", symbol);
            }
        });

        _dataServices[symbol] = dataService;

        Log.Information("シンボル {Symbol} のOhlcvDataServiceを起動しました", symbol);
    }

    /// <summary>
    /// ストラテジー実行ループ
    /// </summary>
    private async Task StrategyExecutionLoopAsync(CancellationToken cancellationToken)
    {
        Log.Information("ストラテジー実行ループを開始します（間隔: {Interval}分）",
            _config.Bot.StrategyCheckIntervalMinutes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 次の実行時刻まで待機
                await WaitForNextExecutionTimeAsync(
                    _config.Bot.StrategyCheckIntervalMinutes,
                    cancellationToken);

                Log.Information("========== ストラテジー実行開始 [{Time}] ==========", DateTime.UtcNow);

                // 全シンボルを並列処理
                var tasks = _config.Bot.Symbols.Select(symbol =>
                    ExecuteStrategyForSymbolAsync(symbol, cancellationToken));
                await Task.WhenAll(tasks);

                Log.Information("========== ストラテジー実行完了 ==========");
            }
            catch (OperationCanceledException)
            {
                Log.Information("ストラテジー実行ループが停止されました");
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ストラテジー実行ループでエラーが発生しました");
            }
        }
    }

    /// <summary>
    /// ポジション更新ループ
    /// </summary>
    private async Task PositionUpdateLoopAsync(CancellationToken cancellationToken)
    {
        Log.Information("ポジション更新ループを開始します（間隔: {Interval}分）",
            _config.Bot.PositionUpdateIntervalMinutes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 指定された間隔で待機
                await Task.Delay(
                    TimeSpan.FromMinutes(_config.Bot.PositionUpdateIntervalMinutes),
                    cancellationToken);

                Log.Debug("ポジション状態を更新中...");

                // 全シンボルのポジションを更新
                var tasks = _config.Bot.Symbols.Select(SyncPositionWithExchangeAsync);
                await Task.WhenAll(tasks);

                Log.Debug("ポジション状態の更新が完了しました");
            }
            catch (OperationCanceledException)
            {
                Log.Information("ポジション更新ループが停止されました");
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ポジション更新ループでエラーが発生しました");
            }
        }
    }

    /// <summary>
    /// シンボルごとのストラテジー実行
    /// </summary>
    private async Task ExecuteStrategyForSymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            Log.Information("シンボル {Symbol} のストラテジーを実行中...", symbol);

            // Repository経由で最新250件のデータを取得
            var ohlcvData = await _dataRepository.GetLatestOhlcvDataAsync(symbol, _timeframe, 250);

            if (ohlcvData.Count == 0)
            {
                Log.Warning("シンボル {Symbol} のデータが不足しています", symbol);
                return;
            }

            Log.Debug("シンボル {Symbol} のOHLCVデータ件数: {Count}, 最新価格: {Price}",
                symbol, ohlcvData.Count, ohlcvData.Last().ClosePrice);

            // ストラテジー実行
            var decision = _strategy.DecideSignal(symbol, _positions[symbol], ohlcvData);

            Log.Information("シンボル {Symbol} ストラテジー判断: {Operation} {Side} - {Reason}",
                symbol, decision.Operation, decision.Side, decision.Reason);

            // 判断に基づいて処理
            await ExecuteStrategyDecisionAsync(symbol, decision);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "シンボル {Symbol} のストラテジー実行エラー", symbol);
        }
    }

    /// <summary>
    /// ストラテジー判断に基づいた処理実行
    /// </summary>
    private async Task ExecuteStrategyDecisionAsync(string symbol, StrategyDecisionResult decision)
    {
        if (decision.Operation == StrategyDecisionOperation.None)
        {
            return;
        }

        if (decision.Operation == StrategyDecisionOperation.OpenPosition)
        {
            await OpenPositionAsync(symbol, decision);
        }
        else if (decision.Operation == StrategyDecisionOperation.UpdateStopLossPrice)
        {
            await UpdateStopLossAsync(symbol, decision);
        }
    }

    /// <summary>
    /// ポジションを開く
    /// </summary>
    private async Task OpenPositionAsync(string symbol, StrategyDecisionResult decision)
    {
        try
        {
            // 既存ポジションがある場合は警告のみ（Hyperliquidは1シンボル1ポジション）
            if (_positions[symbol].IsActive)
            {
                Log.Warning("シンボル {Symbol} は既にポジションが存在します。新規ポジションは作成しません", symbol);
                return;
            }

            Log.Information("シンボル {Symbol} でポジションを開きます。サイド: {Side}, 金額: {Amount} USDC, SL価格: {SlPrice}",
                symbol, decision.Side, _config.Bot.PositionSizeUSDC, decision.StopLossPrice);

            var side = decision.Side == SharedPositionSide.Long ? OrderSide.Buy : OrderSide.Sell;
            var slRatio = 0.05m; // 5%のストップロス（仮）

            // 注文を実行
            var result = await _exchange.PlaceOrderAsync(
                symbol: symbol,
                side: side,
                amountToBuyUSDC: _config.Bot.PositionSizeUSDC,
                tpRatio: 0.1m,
                slRatio: slRatio
            );

            Log.Information("ポジションを開きました。シンボル: {Symbol}, 注文ID: {OrderId}",
                symbol, result.OrderId);

            // ローカルのポジション状態を更新
            _positions[symbol].IsActive = true;
            _positions[symbol].LastUpdateTime = DateTime.UtcNow;

            // 取引所のポジション情報と同期
            await SyncPositionWithExchangeAsync(symbol);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "シンボル {Symbol} のポジションオープンエラー", symbol);
        }
    }

    /// <summary>
    /// ストップロス価格を更新
    /// </summary>
    private async Task UpdateStopLossAsync(string symbol, StrategyDecisionResult decision)
    {
        try
        {
            if (!_positions[symbol].IsActive)
            {
                Log.Debug("シンボル {Symbol} にアクティブなポジションがありません", symbol);
                return;
            }

            if (!decision.StopLossPrice.HasValue)
            {
                Log.Warning("シンボル {Symbol} のストップロス価格が指定されていません", symbol);
                return;
            }

            Log.Information("シンボル {Symbol} のストップロス価格を更新します。新価格: {NewPrice}",
                symbol, decision.StopLossPrice.Value);

            // 取引所のストップロス注文を更新
            await _exchange.UpdateStopLossAsync(symbol, decision.StopLossPrice.Value);

            // ローカルのポジション情報も更新
            _positions[symbol].UpdateStopLossPrice(decision.Side!.Value, decision.StopLossPrice.Value);
            _positions[symbol].LastUpdateTime = DateTime.UtcNow;

            Log.Information("シンボル {Symbol} のストップロス価格を更新しました", symbol);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "シンボル {Symbol} のストップロス更新エラー", symbol);
        }
    }

    /// <summary>
    /// 取引所のポジション情報と同期
    /// </summary>
    private async Task SyncPositionWithExchangeAsync(string symbol)
    {
        try
        {
            // 取引所から実ポジションを取得
            var position = await _exchange.GetCurrentPositionAsync(symbol);

            if (!_positions.ContainsKey(symbol))
            {
                _positions[symbol] = new RealTimePosition();
            }

            if (position != null)
            {
                Log.Debug("シンボル {Symbol} のポジションを同期しました。サイド: {Side}, 数量: {Quantity}, SL: {SlPrice}",
                    symbol, position.side, position.Quantity, position.StopLossPrice);

                // 既存のポジション情報をクリア
                _positions[symbol].PositionItems.Clear();

                // 新しいポジション情報を追加
                _positions[symbol].PositionItems.Add(position);
                _positions[symbol].IsActive = true;
            }
            else
            {
                Log.Debug("シンボル {Symbol} にポジションがありません", symbol);
                _positions[symbol].PositionItems.Clear();
                _positions[symbol].IsActive = false;
            }

            _positions[symbol].LastUpdateTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "シンボル {Symbol} のポジション同期エラー", symbol);
        }
    }

    /// <summary>
    /// 次の実行時刻まで待機
    /// </summary>
    private async Task WaitForNextExecutionTimeAsync(int intervalMinutes, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nextExecution = now.AddMinutes(intervalMinutes - (now.Minute % intervalMinutes))
            .AddSeconds(-now.Second)
            .AddMilliseconds(-now.Millisecond);

        var delay = nextExecution - now;

        if (delay.TotalSeconds > 0)
        {
            Log.Debug("次の実行時刻まで待機中: {Delay}秒（次回実行: {NextTime}）",
                (int)delay.TotalSeconds, nextExecution);
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// タイムフレーム文字列をKlineIntervalに変換
    /// </summary>
    private KlineInterval ParseTimeframe(string timeframe)
    {
        return timeframe.ToLower() switch
        {
            "1m" => KlineInterval.OneMinute,
            "3m" => KlineInterval.ThreeMinutes,
            "5m" => KlineInterval.FiveMinutes,
            "15m" => KlineInterval.FifteenMinutes,
            "30m" => KlineInterval.ThirtyMinutes,
            "1h" => KlineInterval.OneHour,
            "2h" => KlineInterval.TwoHours,
            "4h" => KlineInterval.FourHours,
            "1d" => KlineInterval.OneDay,
            _ => throw new ArgumentException($"Unsupported timeframe: {timeframe}")
        };
    }

    /// <summary>
    /// ボットを停止
    /// </summary>
    public void Stop()
    {
        Log.Information("ボットを停止します...");
        _cancellationTokenSource.Cancel();

        // データサービスを停止
        foreach (var service in _dataServices.Values)
        {
            service.Stop();
            service.Dispose();
        }

        Log.Information("ボットが停止しました");
    }
}

/// <summary>
/// リアルタイム用永久先物ポジション情報
/// </summary>
public class RealTimePosition : PerpetualPosition
{
    /// <summary>
    /// アクティブなポジションが存在するか
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTime? LastUpdateTime { get; set; }

    /// <summary>
    /// ストップロス価格を更新
    /// </summary>
    public void UpdateStopLossPrice(SharedPositionSide side, decimal newStopLossPrice)
    {
        var existingItem = PositionItems
            .FirstOrDefault(p => p.side == side && !p.CloseDate.HasValue);
        if (existingItem != null)
        {
            Log.Debug("ポジションのストップロス価格を更新します。サイド: {Side}, 旧SL: {OldSl}, 新SL: {NewSl}",
                side, existingItem.StopLossPrice, newStopLossPrice);
            existingItem.StopLossPrice = newStopLossPrice;
        }
    }
}
