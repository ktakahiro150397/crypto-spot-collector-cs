// See https://aka.ms/new-console-template for more information
using CryptoExchange.Net.Authentication;
using HyperLiquid.Net;
using HyperLiquid.Net.Clients;
using HyperLiquid.Net.Enums;
using Skender.Stock.Indicators;
using Chart;
using Logging;
using Serilog;

// ログシステムの初期化
LoggingConfiguration.Initialize();

try
{
    var config = Config.LoadSettings("secrets.json");

    var isTestnet = config.IsTestnet;

    // 対応するウォレットアドレス
    var walletAddressMain = config.WalletAddress.MainWalletAddress;
    // 対応するウォレットアドレス
    var walletAddress = config.WalletAddress.APIWalletAddress;
    // 秘密鍵
    var privateKey = config.WalletAddress.APIWalletAddressKey;

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

    var exchange = new HyperLiquidExchange(restClientMainWallet, restClient);

    var candle = await exchange.GetKlinesAsync("ETH",
    KlineInterval.OneMinute,
    startDate: DateTime.UtcNow.AddMonths(-3),
    endDate: DateTime.UtcNow);
    Console.WriteLine($"Candle : {candle.Count()}");

    var repo = new MySQLRepository(config.ConnectionString);
    repo.AddOrUpdateOhlcvDataAsync("ETH", candle.Select(c => new OhlcvData
    {
        OpenPrice = c.OpenPrice,
        HighPrice = c.HighPrice,
        LowPrice = c.LowPrice,
        ClosePrice = c.ClosePrice,
        Volume = c.Volume,
        TimestampUtc = c.OpenTime,
        CreatedAt = DateTime.UtcNow
    }).ToList()).Wait();
    Console.WriteLine("Inserted candle data into MySQL");

    var dataRepo = new OhlcvDataRepository(repo);
    var thirtyMinData = await dataRepo.GetLatestOhlcvDataAsync(
        symbol: "ETH",
        interval: KlineInterval.ThirtyMinutes,
        count: 20
    );

    Console.WriteLine($"30分足データ取得件数: {thirtyMinData.Count}");
    foreach (var data in thirtyMinData)
    {
        Console.WriteLine($"{data.TimestampUtc}: O={data.OpenPrice}, H={data.HighPrice}, L={data.LowPrice}, C={data.ClosePrice}, V={data.Volume}");
    }


    // var fetchedCandle = await repo.GetOhlcvDataBySymbolAsync("ETH", DateTime.UtcNow.AddMonths(-3), DateTime.UtcNow);
    // Console.WriteLine($"Fetched Candle from MySQL: {fetchedCandle.Count}");

    // // ATR Trailling Stop
    // var atrResults = fetchedCandle.GetAtrStop().ToList();
    // foreach (var result in atrResults.TakeLast(10))  // 最後の10件だけコンソール出力
    // {
    //     Console.WriteLine($"{result.Date}: {result.AtrStop} : {result.BuyStop} : {result.SellStop}");
    // }

    // // ATR Trailing Stopのグラフを生成（直近7日間のみ表示）
    // Console.WriteLine("\n=== ATR Trailing Stop グラフ生成 ===");
    // AtrChartGenerator.SaveAtrStopChart(fetchedCandle, atrResults, "ETH", "atr_trailing_stop.png", displayDays: 7, width: 1920, height: 1080);
    // AtrChartGenerator.SaveCandlestickWithAtrStop(fetchedCandle, atrResults, "ETH", "candlestick_atr_stop.png", displayDays: 7, width: 1920, height: 1080);

    // var longResult = await exchange.PlaceOrderAsync("ETH", OrderSide.Buy, 100m, 1.1m, 0.9m);
    // Console.WriteLine($"Long Order : {longResult}");

    // var shortResult = await exchange.PlaceOrderAsync("SOL", OrderSide.Sell, 100m, 1.1m, 0.9m);
    // Console.WriteLine($"Short Order : {shortResult}");

    // var closeResult = await exchange.CloseOrderAsync("ETH");
    // Console.WriteLine($"Close Order : {closeResult}");

    // var closeResult2 = await exchange.CloseOrderAsync("SOL");
    // Console.WriteLine($"Close Order : {closeResult2}");

    // // MySQL接続テスト
    // Console.WriteLine("\n=== MySQL接続テスト ===");
    // var repository = new MySQLRepository(config.ConnectionString);

    // // 接続テスト
    // var isConnected = await repository.TestConnectionAsync();
    // if (isConnected)
    // {
    //     // 全ての暗号通貨を取得
    //     var cryptocurrencies = await repository.GetAllCryptocurrenciesAsync();
    //     Console.WriteLine($"\n登録されている暗号通貨: {cryptocurrencies.Count}件");
    //     foreach (var crypto in cryptocurrencies)
    //     {
    //         Console.WriteLine($"  - {crypto.Symbol}: {crypto.Name}");
    //     }

    //     // BTCを取得
    //     var btc = await repository.GetCryptocurrencyBySymbolAsync("BTC");
    //     if (btc != null)
    //     {
    //         Console.WriteLine($"\nBTC詳細: ID={btc.Id}, Name={btc.Name}, Created={btc.CreatedAt}");
    //     }

    //     var insertCandle = candle.Select(c => new OhlcvData
    //     {
    //         //CryptocurrencyId = btc!.Id,
    //         OpenPrice = c.OpenPrice,
    //         HighPrice = c.HighPrice,
    //         LowPrice = c.LowPrice,
    //         ClosePrice = c.ClosePrice,
    //         Volume = c.Volume,
    //         TimestampUtc = c.OpenTime,
    //         CreatedAt = DateTime.UtcNow
    //     }).ToList();
    //     await repository.AddOrUpdateOhlcvDataAsync("BTC", insertCandle);
    //     Console.WriteLine($"\nOHLCVデータを追加または更新しました。 件数: {insertCandle.Count}");

    //     var fartcoinCandle = await exchange.GetKlinesAsync("FARTCOIN", KlineInterval.ThirtyMinutes, startDate: DateTime.UtcNow.AddHours(-12), endDate: DateTime.UtcNow);
    //     var insertFartcoinCandle = fartcoinCandle.Select(c => new OhlcvData
    //     {
    //         //CryptocurrencyId = btc!.Id,
    //         OpenPrice = c.OpenPrice,
    //         HighPrice = c.HighPrice,
    //         LowPrice = c.LowPrice,
    //         ClosePrice = c.ClosePrice,
    //         Volume = c.Volume,
    //         TimestampUtc = c.OpenTime,
    //         CreatedAt = DateTime.UtcNow
    //     }).ToList();
    //     await repository.AddOrUpdateOhlcvDataAsync("FARTCOIN", insertFartcoinCandle);
    //     Console.WriteLine($"\nFARTCOINのOHLCVデータを追加または更新しました。 件数: {insertFartcoinCandle.Count}");


    //     var ohlcvData = await repository.GetOhlcvDataBySymbolAsync("BTC", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
    //     Console.WriteLine($"\nBTCのOHLCVデータ: {ohlcvData.Count}件");
    //     foreach (var data in ohlcvData)
    //     {
    //         Console.WriteLine($"  - {data.TimestampUtc}: O={data.OpenPrice}, H={data.HighPrice}, L={data.LowPrice}, C={data.ClosePrice}, V={data.Volume}");
    //     }
    // }


    // var notificationService = new DiscordNotificationService(config.DiscordNotificationUrl);
    // await notificationService.SendNotificationAsync("MySQL接続テストが完了しました。");

}
catch (Exception ex)
{
    Log.Fatal(ex, "アプリケーションで致命的なエラーが発生しました");
    throw;
}
finally
{
    LoggingConfiguration.Shutdown();
}