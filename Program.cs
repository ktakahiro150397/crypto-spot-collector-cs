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

var longResult = await exchange.PlaceOrderAsync("ETH", OrderSide.Buy, 100m, 1.1m, 0.9m);
Console.WriteLine($"Long Order : {longResult}");

var shortResult = await exchange.PlaceOrderAsync("SOL", OrderSide.Sell, 100m, 1.1m, 0.9m);
Console.WriteLine($"Short Order : {shortResult}");


// // var futures = await restClient.FuturesApi.ExchangeData.GetExchangeInfoAndTickersAsync();
// // Console.WriteLine(futures);

// var result = await restClient.FuturesApi.Account.GetAccountInfoAsync();
// if (result.Success)
// {
//     Console.WriteLine("Account Info:");
//     Console.WriteLine(result.Data);
// }
// else
// {
//     Console.WriteLine($"Failed to get account info. Error: {result.Error}");
// }

// // 現在価格を取得
// var tickerResult = await restClient.FuturesApi.ExchangeData.GetExchangeInfoAndTickersAsync();
// var ethTicker = tickerResult.Data.Tickers.FirstOrDefault(t => t.Symbol == "ETH");
// if (ethTicker == null)
// {
//     Console.WriteLine("ETH ticker not found");
//     return;
// }

// var currentPrice = ethTicker.MarkPrice;
// Console.WriteLine($"ETH Current Price: {currentPrice}");

// // Market Buy: 現在価格より少し高い価格を設定（スリッページ許容）
// // Market Sell: 現在価格より少し低い価格を設定
// var slippagePercent = 0.01m; // 1%のスリッページ許容
// var orderPrice = Math.Round(currentPrice * (1 + slippagePercent), 1);

// var orderResult = await restClient.FuturesApi.Trading.PlaceOrderAsync(
//     symbol: "ETH",
//     side: OrderSide.Buy,
//     orderType: OrderType.Market,
//     quantity: 0.01m,
//     price: orderPrice
// );

// if (orderResult.Success)
// {
//     Console.WriteLine($"Order placed successfully. Order ID: {orderResult.Data.OrderId}");
// }
// else
// {
//     Console.WriteLine($"Failed to place order. Error: {orderResult.Error}");
// }

// // ========================================
// // TP/SL（Take Profit / Stop Loss）の注文方法
// // ========================================

// // ポジションを持っている場合、TP/SLを設定する
// // 例: ETH Longポジションを持っている場合

// // Take Profit (TP) 注文 - 利益確定
// // 現在価格より高い価格で売り注文を出す（Longの場合）
// var tpPrice = Math.Round(currentPrice * 1.05m, 1);  // 5%上昇したら利確
// var tpResult = await restClient.FuturesApi.Trading.PlaceOrderAsync(
//     symbol: "ETH",
//     side: OrderSide.Sell,              // Longポジションを閉じるのでSell
//     orderType: OrderType.StopMarket,   // トリガー時に成行で執行
//     quantity: 0.01m,                   // ポジションサイズ
//     price: tpPrice,                    // 執行価格（StopMarketの場合はtriggerPriceと同じでOK）
//     triggerPrice: tpPrice,             // この価格に達したらトリガー
//     tpSlType: TpSlType.TakeProfit,     // Take Profitとして設定
//     reduceOnly: true,                  // ポジションを減らすだけ（新規ポジションを作らない）
//     tpSlGrouping: TpSlGrouping.PositionTpSl  // ポジション全体に対するTP/SL
// );

// if (tpResult.Success)
// {
//     Console.WriteLine($"Take Profit order placed. Order ID: {tpResult.Data.OrderId}, Trigger Price: {tpPrice}");
// }
// else
// {
//     Console.WriteLine($"Failed to place TP order. Error: {tpResult.Error}");
// }

// // Stop Loss (SL) 注文 - 損切り
// // 現在価格より低い価格で売り注文を出す（Longの場合）
// var slPrice = Math.Round(currentPrice * 0.97m, 1);  // 3%下落したら損切り
// var slResult = await restClient.FuturesApi.Trading.PlaceOrderAsync(
//     symbol: "ETH",
//     side: OrderSide.Sell,              // Longポジションを閉じるのでSell
//     orderType: OrderType.StopMarket,   // トリガー時に成行で執行
//     quantity: 0.01m,                   // ポジションサイズ
//     price: slPrice,                    // 執行価格
//     triggerPrice: slPrice,             // この価格に達したらトリガー
//     tpSlType: TpSlType.StopLoss,       // Stop Lossとして設定
//     reduceOnly: true,                  // ポジションを減らすだけ
//     tpSlGrouping: TpSlGrouping.PositionTpSl
// );

// if (slResult.Success)
// {
//     Console.WriteLine($"Stop Loss order placed. Order ID: {slResult.Data.OrderId}, Trigger Price: {slPrice}");
// }
// else
// {
//     Console.WriteLine($"Failed to place SL order. Error: {slResult.Error}");
// }

// // ========================================
// // TP/SL注文のOrder IDを取得する方法
// // ========================================
// // HyperLiquidのAPIはトリガー注文（TP/SL）のOrder IDを
// // レスポンスで返さないため、オープン注文から検索する必要がある

// var openOrdersResult = await restClientMainWallet.FuturesApi.Trading.GetOpenOrdersAsync();
// if (openOrdersResult.Success)
// {
//     Console.WriteLine("\n=== Open Orders ===");
//     foreach (var order in openOrdersResult.Data)
//     {
//         Console.WriteLine($"Order ID: {order.OrderId}, Symbol: {order.ExchangeSymbol}, " +
//                           $"Price: {order.Price}, Side: {order.OrderSide} ");
//     }
// }

// // 既存のTP/SL注文を削除
// var tpSlOrderIds = openOrdersResult.Data.Where(order => order.Symbol == "ETH");
// foreach (var tpSlOrderId in tpSlOrderIds)
// {
//     var cancelResult = await restClient.FuturesApi.Trading.CancelOrderAsync(
//         symbol: "ETH",
//         orderId: tpSlOrderId.OrderId
//     );
//     if (cancelResult.Success)
//     {
//         Console.WriteLine($"Cancelled TP/SL order. Order ID: {tpSlOrderId.OrderId}");
//     }
//     else
//     {
//         Console.WriteLine($"Failed to cancel TP/SL order. Order ID: {tpSlOrderId.OrderId}, Error: {cancelResult.Error}");
//     }
// }

// // 再度TP/SL注文

// // Take Profit (TP) 注文 - 利益確定
// // 現在価格より高い価格で売り注文を出す（Longの場合）
// tpPrice = Math.Round(currentPrice * 1.1m, 1);  // 5%上昇したら利確
// tpResult = await restClient.FuturesApi.Trading.PlaceOrderAsync(
//     symbol: "ETH",
//     side: OrderSide.Sell,              // Longポジションを閉じるのでSell
//     orderType: OrderType.StopMarket,   // トリガー時に成行で執行
//     quantity: 0.01m,                   // ポジションサイズ
//     price: tpPrice,                    // 執行価格（StopMarketの場合はtriggerPriceと同じでOK）
//     triggerPrice: tpPrice,             // この価格に達したらトリガー
//     tpSlType: TpSlType.TakeProfit,     // Take Profitとして設定
//     reduceOnly: true,                  // ポジションを減らすだけ（新規ポジションを作らない）
//     tpSlGrouping: TpSlGrouping.PositionTpSl  // ポジション全体に対するTP/SL
// );

// if (tpResult.Success)
// {
//     Console.WriteLine($"Take Profit order placed. Order ID: {tpResult.Data.OrderId}, Trigger Price: {tpPrice}");
// }
// else
// {
//     Console.WriteLine($"Failed to place TP order. Error: {tpResult.Error}");
// }

// // Stop Loss (SL) 注文 - 損切り
// // 現在価格より低い価格で売り注文を出す（Longの場合）
// slPrice = Math.Round(currentPrice * 0.97m, 1);  // 3%下落したら損切り
// slResult = await restClient.FuturesApi.Trading.PlaceOrderAsync(
//     symbol: "ETH",
//     side: OrderSide.Sell,              // Longポジションを閉じるのでSell
//     orderType: OrderType.StopMarket,   // トリガー時に成行で執行
//     quantity: 0.01m,                   // ポジションサイズ
//     price: slPrice,                    // 執行価格
//     triggerPrice: slPrice,             // この価格に達したらトリガー
//     tpSlType: TpSlType.StopLoss,       // Stop Lossとして設定
//     reduceOnly: true,                  // ポジションを減らすだけ
//     tpSlGrouping: TpSlGrouping.PositionTpSl
// );
