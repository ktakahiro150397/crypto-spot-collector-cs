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
        var candle = await _exchange.GetKlinesAsync(symbol,
        _interval,
        startDate: _startDate,
        endDate: _endDate);
        Log.Debug("Candle Count: {count}", candle.Count());

        // 2. データをDBに保存
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
        Log.Debug("OHLCVデータをDBに保存しました。シンボル: {Symbol}", symbol);
    }

    private async Task ExecuteBackTestAsync(string symbol)
    {
        var backTestPositions = new BackTestPosition();

        // バックテスト実行処理
        foreach (var date in _backTestDateList)
        {
            // 各日時点でのOHLCVデータを取得
            var ohlcvData = await _repository.GetOhlcvDataBySymbolAsync(symbol,
                startDate: date,
                endDate: date);
            Log.Debug("バックテスト日時: {Date}, 取得OHLCVデータ件数: {Count}, timestamputc: {timestamp}", date, ohlcvData.Count, ohlcvData.First().TimestampUtc);

            var quantity = BuyUSDC / ohlcvData.First().ClosePrice;

            var signalIsLong = (date.Minute % 60) < 30; // ダミーの売買シグナル（30分ごとにロング・ショートを切り替え）
            var signalIsShort = false;

            if (signalIsLong)
            {
                // ロングポジションを追加
                var positionItem = new BackTestPositionItem
                {
                    OpenDate = date,
                    OpenPrice = ohlcvData.First().ClosePrice,
                    Quantity = quantity,
                    side = SharedPositionSide.Long
                };
                backTestPositions.AddPositionItem(positionItem);
                Log.Debug("ロングポジションを追加しました。日時: {Date}, 価格: {Price}", date, ohlcvData.First().ClosePrice);
            }
            else if (signalIsShort)
            {
                // ショートポジションを追加
                var positionItem = new BackTestPositionItem
                {
                    OpenDate = date,
                    OpenPrice = ohlcvData.First().ClosePrice,
                    Quantity = quantity,
                    side = SharedPositionSide.Short
                };
                backTestPositions.AddPositionItem(positionItem);
                Log.Debug("ショートポジションを追加しました。日時: {Date}, 価格: {Price}", date, ohlcvData.First().ClosePrice);
            }
        }

        // バックテスト結果のサマリーを出力
        backTestPositions.OutputPositionSummary();
    }
}

public class BackTestPosition
{
    public List<BackTestPositionItem> PositionItems { get; set; } = new List<BackTestPositionItem>();

    public decimal GetTotalPnl
    {
        get
        {
            return PositionItems.Sum(item => item.GetPnl);
        }
    }

    public void AddPositionItem(BackTestPositionItem item)
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

public class BackTestPositionItem
{
    public DateTime OpenDate { get; set; }
    public decimal OpenPrice { get; set; }
    public DateTime? CloseDate { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal Quantity { get; set; }
    public SharedPositionSide side { get; set; }

    public decimal GetPnl
    {
        get
        {
            if (ClosePrice.HasValue)
            {
                if (side == SharedPositionSide.Long)
                {
                    return (ClosePrice.Value - OpenPrice) * Quantity;
                }
                else if (side == SharedPositionSide.Short)
                {
                    return (OpenPrice - ClosePrice.Value) * Quantity;
                }
            }
            return 0m;
        }
    }
}