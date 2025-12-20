// See https://aka.ms/new-console-template for more information
using CryptoExchange.Net.Authentication;
using HyperLiquid.Net;
using HyperLiquid.Net.Clients;
using HyperLiquid.Net.Enums;

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

var candle = await exchange.GetKlinesAsync("ETH", KlineInterval.ThirtyMinutes, startDate: DateTime.UtcNow.AddHours(-2), endDate: DateTime.UtcNow);
Console.WriteLine($"Candle : {candle.Count()}");

// var longResult = await exchange.PlaceOrderAsync("ETH", OrderSide.Buy, 100m, 1.1m, 0.9m);
// Console.WriteLine($"Long Order : {longResult}");

// var shortResult = await exchange.PlaceOrderAsync("SOL", OrderSide.Sell, 100m, 1.1m, 0.9m);
// Console.WriteLine($"Short Order : {shortResult}");

// var closeResult = await exchange.CloseOrderAsync("ETH");
// Console.WriteLine($"Close Order : {closeResult}");

// var closeResult2 = await exchange.CloseOrderAsync("SOL");
// Console.WriteLine($"Close Order : {closeResult2}");

// MySQL接続テスト
Console.WriteLine("\n=== MySQL接続テスト ===");
var repository = new MySQLRepository(config.ConnectionString);

// 接続テスト
var isConnected = await repository.TestConnectionAsync();
if (isConnected)
{
    // 全ての暗号通貨を取得
    var cryptocurrencies = await repository.GetAllCryptocurrenciesAsync();
    Console.WriteLine($"\n登録されている暗号通貨: {cryptocurrencies.Count}件");
    foreach (var crypto in cryptocurrencies)
    {
        Console.WriteLine($"  - {crypto.Symbol}: {crypto.Name}");
    }

    // BTCを取得
    var btc = await repository.GetCryptocurrencyBySymbolAsync("BTC");
    if (btc != null)
    {
        Console.WriteLine($"\nBTC詳細: ID={btc.Id}, Name={btc.Name}, Created={btc.CreatedAt}");
    }

    var insertCandle = candle.Select(c => new OhlcvData
    {
        //CryptocurrencyId = btc!.Id,
        OpenPrice = c.OpenPrice,
        HighPrice = c.HighPrice,
        LowPrice = c.LowPrice,
        ClosePrice = c.ClosePrice,
        Volume = c.Volume,
        TimestampUtc = c.OpenTime,
        CreatedAt = DateTime.UtcNow
    }).ToList();
    await repository.AddOrUpdateOhlcvDataAsync("BTC", insertCandle);
    Console.WriteLine($"\nOHLCVデータを追加または更新しました。 件数: {insertCandle.Count}");

    var fartcoinCandle = await exchange.GetKlinesAsync("FARTCOIN", KlineInterval.ThirtyMinutes, startDate: DateTime.UtcNow.AddHours(-12), endDate: DateTime.UtcNow);
    var insertFartcoinCandle = fartcoinCandle.Select(c => new OhlcvData
    {
        //CryptocurrencyId = btc!.Id,
        OpenPrice = c.OpenPrice,
        HighPrice = c.HighPrice,
        LowPrice = c.LowPrice,
        ClosePrice = c.ClosePrice,
        Volume = c.Volume,
        TimestampUtc = c.OpenTime,
        CreatedAt = DateTime.UtcNow
    }).ToList();
    await repository.AddOrUpdateOhlcvDataAsync("FARTCOIN", insertFartcoinCandle);
    Console.WriteLine($"\nFARTCOINのOHLCVデータを追加または更新しました。 件数: {insertFartcoinCandle.Count}");
}