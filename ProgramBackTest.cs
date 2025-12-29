using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.SharedApis;
using HyperLiquid.Net;
using HyperLiquid.Net.Clients;
using HyperLiquid.Net.Enums;
using Logging;
using Serilog;
using System.CommandLine;
using System.CommandLine.Parsing;

public class ProgramBackTest
{
    private DateTime _startDate;
    private DateTime _endDate;
    private KlineInterval _interval;
    private Config _config;
    private HyperLiquidExchange _exchange;
    private MySQLRepository _repository;

    private List<DateTime> _backTestDateList
    {
        get
        {
            var dateList = new List<DateTime>();
            var currentDate = _startDate;

            while (currentDate <= _endDate)
            {
                dateList.Add(currentDate);
                currentDate = currentDate.AddMinutes(OhlcvDataRepository.GetIntervalMinutes(_interval));
            }

            return dateList;
        }
    }

    private const string Symbol = "ETH";

    private const int BuyUSDC = 200;

    private const int ohlcvDataFetchCount = 250;

    public ProgramBackTest(DateTime startDate, DateTime endDate, KlineInterval interval)
    {
        _startDate = startDate;
        _endDate = endDate;
        _interval = interval;

        _config = Config.LoadSettings("secrets.json");

        var isTestnet = _config.IsTestnet;

        // 対応するウォレットアドレス
        var walletAddressMain = _config.WalletAddress.MainWalletAddress;
        // 対応するウォレットアドレス
        var walletAddress = _config.WalletAddress.APIWalletAddress;
        // 秘密鍵
        var privateKey = _config.WalletAddress.APIWalletAddressKey;

        var restClientMainWallet = new HyperLiquidRestClient(options =>
        {
            // key = ウォレットアドレス, secret = 秘密鍵
            options.ApiCredentials = new ApiCredentials(walletAddressMain, "dummy");
            if (isTestnet)
            {
                // 組み込みのTestnet環境を使用
                options.Environment = HyperLiquidEnvironment.Testnet;
            }
        });

        var restClient = new HyperLiquidRestClient(options =>
        {
            // key = ウォレットアドレス, secret = 秘密鍵
            options.ApiCredentials = new ApiCredentials(walletAddress, privateKey);
            if (isTestnet)
            {
                // 組み込みのTestnet環境を使用
                options.Environment = HyperLiquidEnvironment.Testnet;
            }
        });

        _exchange = new HyperLiquidExchange(restClientMainWallet, restClient);
        _repository = new MySQLRepository(_config.ConnectionString);
    }


    public async Task ProgramBackTestMain()
    {
        // 初期化
        await InitializeBackTestAsync(Symbol);

        // バックテスト実行
        await ExecuteBackTestAsync(Symbol);

    }

    private async Task InitializeBackTestAsync(string symbol)
    {
        // 初期化処理
        // 1. データを取得
        var fetchStartDate = _startDate.AddMinutes(-(ohlcvDataFetchCount + 10) * OhlcvDataRepository.GetIntervalMinutes(_interval));
        var candle = await _exchange.GetKlinesAsync(symbol,
        _interval,
        startDate: fetchStartDate,
        endDate: _endDate,
        limit: 100000);
        Log.Debug("Candle Count: {count}", candle.Count());

        // 2. データをDBに保存
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
        Log.Debug("OHLCVデータをDBに保存しました。シンボル: {Symbol}, 開始期間: {startDate}, 終了期間: {endDate}", symbol, fetchStartDate, _endDate);
    }

    private async Task ExecuteBackTestAsync(string symbol)
    {
        var backTestPositions = new BackTestPosition();

        // テストするストラテジ
        var atrTrailingStop = new ATRTrailingStopStrategy(14, 3.0m);

        // バックテスト実行処理
        foreach (var date in _backTestDateList)
        {
            // ストップロスの実行
            // TODO : 同一時間足ごとに実行されているため結果不正確な可能性あり
            await ExecuteStopLossAsync(backTestPositions, symbol, date);

            // 各日時点でのOHLCVデータを取得
            var fetchStartDate = date.AddMinutes(-ohlcvDataFetchCount * OhlcvDataRepository.GetIntervalMinutes(_interval));
            var ohlcvData = await _repository.GetOhlcvDataBySymbolAsync(symbol,
                startDate: fetchStartDate,
                endDate: date);
            Log.Debug("バックテスト日時: {Date}, 取得OHLCVデータ件数: {Count}, timestamputc: {timestamp}", date, ohlcvData.Count, ohlcvData.First().TimestampUtc);

            var quantity = BuyUSDC / ohlcvData.First().ClosePrice;

            var decision = atrTrailingStop.DecideSignal(symbol, backTestPositions, ohlcvData);
            Log.Debug("ストラテジ判断結果詳細: {Decision}", decision.ToString());
            if (decision.Operation == StrategyDecisionOperation.None)
            {
                Log.Debug("ストラテジ判断結果: ポジション変更なし");
                continue;
            }

            // Log.Debug("ストラテジ判断結果: サイド: {Side}, ストラテジ名: {StrategyName}, 理由: {Reason}",
            //     decision.Side,
            //     decision.StrategyName,
            //     decision.Reason);

            // var signalIsLong = (date.Minute % 60) < 30; // ダミーの売買シグナル（30分ごとにロング・ショートを切り替え）
            // var signalIsShort = false;

            // TODO : ポジションストップロス価格の更新
            if (decision.Operation == StrategyDecisionOperation.OpenPosition)
            {
                if (decision.Side == SharedPositionSide.Long)
                {
                    // ロングポジションを追加
                    var positionItem = new PerpetualPositionItem
                    {
                        OpenDate = date,
                        OpenPrice = ohlcvData.First().ClosePrice,
                        Quantity = quantity,
                        side = SharedPositionSide.Long,
                        StopLossPrice = decision.StopLossPrice
                    };
                    backTestPositions.AddPositionItem(positionItem);
                    Log.Debug("ロングポジションを追加しました。日時: {Date}, 価格: {Price}, ストップロス価格: {stopLossPrice}, 理由： {reason}", date, ohlcvData.First().ClosePrice, decision.StopLossPrice, decision.Reason);
                }
                else if (decision.Side == SharedPositionSide.Short)
                {
                    // ショートポジションを追加
                    var positionItem = new PerpetualPositionItem
                    {
                        OpenDate = date,
                        OpenPrice = ohlcvData.First().ClosePrice,
                        Quantity = quantity,
                        side = SharedPositionSide.Short,
                        StopLossPrice = decision.StopLossPrice // 5%のストップロス設定
                    };
                    backTestPositions.AddPositionItem(positionItem);
                    Log.Debug("ショートポジションを追加しました。日時: {Date}, 価格: {Price}, 理由： {reason}", date, ohlcvData.First().ClosePrice, decision.Reason);
                }
            }
            else if (decision.Operation == StrategyDecisionOperation.UpdateStopLossPrice)
            {
                // TODO : ストップロス価格の更新処理
                backTestPositions.UpdateStopLossPrice(decision.Side!.Value, decision.StopLossPrice!.Value);
            }
        }

        // バックテスト結果のサマリーを出力
        backTestPositions.OutputPositionSummary();
    }

    private async Task ExecuteStopLossAsync(BackTestPosition position, string symbol, DateTime date)
    {
        // 対象日付のclose価格を取得し、ストップロス判定を行う
        var ohlcvData = await _repository.GetOhlcvDataBySymbolAsync(symbol,
            startDate: date,
            endDate: date);

        if (ohlcvData.Count == 0)
        {
            Log.Warning("OHLCVデータが存在しません。日時: {Date}", date);
            return;
        }

        var closePrice = ohlcvData.First().ClosePrice;
        Log.Debug("ストップロス判定日時: {Date}, Close価格: {ClosePrice}", date, closePrice);

        foreach (var item in position.PositionItems)
        {
            if (item.StopLossPrice.HasValue && !item.CloseDate.HasValue)
            {
                if (item.side == SharedPositionSide.Long && closePrice <= item.StopLossPrice.Value)
                {
                    // ロングポジションのストップロス発動
                    item.CloseDate = date;
                    item.ClosePrice = item.StopLossPrice.Value;
                    Log.Information("ロングポジションのストップロスが発動しました。日時: {Date}, クローズ価格: {ClosePrice}", date, item.ClosePrice);
                }
                else if (item.side == SharedPositionSide.Short && closePrice >= item.StopLossPrice.Value)
                {
                    // ショートポジションのストップロス発動
                    item.CloseDate = date;
                    item.ClosePrice = item.StopLossPrice.Value;
                    Log.Information("ショートポジションのストップロスが発動しました。日時: {Date}, クローズ価格: {ClosePrice}", date, item.ClosePrice);
                }
            }
        }
    }
}

/// <summary>
/// バックテスト用永久先物ポジション情報
/// </summary>
public class BackTestPosition : PerpetualPosition
{

    /// <summary>
    /// ポジションアイテムを追加する
    /// </summary>
    /// <param name="item"></param>
    public void AddPositionItem(PerpetualPositionItem item)
    {
        // 同一サイドのポジションがすでに存在する場合、数量を加算する
        var existingItem = PositionItems
            .FirstOrDefault(p => p.side == item.side && !p.CloseDate.HasValue);
        if (existingItem != null)
        {
            // 既存ポジションの数量を加算
            Log.Debug("既存ポジションに数量を加算します。サイド: {Side}, 既存数量: {ExistingQuantity}, 追加数量: {AddQuantity}",
                item.side,
                existingItem.Quantity,
                item.Quantity);
            existingItem.Quantity += item.Quantity;
            existingItem.OpenPrice = (existingItem.OpenPrice + item.OpenPrice) / 2; // 平均価格で更新
            return;
        }

        // 反対サイドのポジションが存在する場合、そのポジションをクローズする
        var oppositeSide = item.side == SharedPositionSide.Long ? SharedPositionSide.Short : SharedPositionSide.Long;
        var oppositeItem = PositionItems
            .FirstOrDefault(p => p.side == oppositeSide && !p.CloseDate.HasValue);
        if (oppositeItem != null)
        {
            Log.Debug("反対サイドのポジションをクローズします。サイド: {Side}, クローズ価格: {ClosePrice}",
                oppositeItem.side,
                item.OpenPrice);
            oppositeItem.CloseDate = item.OpenDate;
            oppositeItem.ClosePrice = item.OpenPrice;
        }

        // 新規ポジションを追加
        Log.Debug("新規ポジションを追加します。サイド: {Side}, 開始価格: {OpenPrice}, 数量: {Quantity}",
            item.side,
            item.OpenPrice,
            item.Quantity);
        PositionItems.Add(item);
    }

    /// <summary>
    /// ポジションのストップロス価格を更新する
    /// </summary>
    public void UpdateStopLossPrice(SharedPositionSide side, decimal newStopLossPrice)
    {
        var existingItem = PositionItems
            .FirstOrDefault(p => p.side == side && !p.CloseDate.HasValue);
        if (existingItem != null)
        {
            Log.Debug("ポジションのストップロス価格を更新します。サイド: {Side}, 旧ストップロス価格: {OldStopLossPrice}, 新ストップロス価格: {NewStopLossPrice}",
                side,
                existingItem.StopLossPrice,
                newStopLossPrice);
            existingItem.StopLossPrice = newStopLossPrice;
        }
    }

    public void OutputPositionSummary()
    {
        Log.Information("バックテストポジションサマリー:");
        foreach (var item in PositionItems)
        {
            Log.Information("サイド: {Side}, 開始日時: {OpenDate}, 開始価格: {OpenPrice}, 終了日時: {CloseDate}, 終了価格: {ClosePrice}, 数量: {Quantity}, PnL: {Pnl}",
                item.side,
                item.OpenDate,
                item.OpenPrice,
                item.CloseDate?.ToString() ?? "未決済",
                item.ClosePrice?.ToString() ?? "未決済",
                item.Quantity,
                item.GetPnl);
        }
        Log.Information("総合PnL: {TotalPnl}", GetTotalPnl);
    }
}
