using CryptoExchange.Net.Authentication;
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
        // バックテスト実行処理
        foreach (var date in _backTestDateList)
        {
            // 各日時点でのOHLCVデータを取得
            var ohlcvData = await _repository.GetOhlcvDataBySymbolAsync(symbol,
                startDate: date,
                endDate: date);
            Log.Debug("バックテスト日時: {Date}, 取得OHLCVデータ件数: {Count}, timestamputc: {timestamp}", date, ohlcvData.Count, ohlcvData.First().TimestampUtc);


        }
    }
}