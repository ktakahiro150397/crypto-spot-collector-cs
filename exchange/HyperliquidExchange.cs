using CryptoExchange.Net.SharedApis;
using HyperLiquid.Net.Clients;
using HyperLiquid.Net.Enums;
using HyperLiquid.Net.Objects.Models;
using NSec.Cryptography;
using Serilog;

/// <summary>
/// シンボル情報とレバレッジ設定を保持するクラス
/// </summary>
record SymbolWithLeverage(
    HyperLiquidFuturesSymbol Symbol,
    int Leverage,
    MarginType MarginType
);

public class HyperLiquidExchange
{
    private HyperLiquidRestClient MainWalletClient { get; }
    private HyperLiquidRestClient ApiWalletClient { get; }

    private readonly Dictionary<string, HyperLiquidFuturesSymbol> _symbols = new();
    private readonly Dictionary<string, SymbolWithLeverage> _symbolInfoCache = new();
    private static readonly ILogger _logger = Log.ForContext<HyperLiquidExchange>();

    public HyperLiquidExchange(HyperLiquidRestClient mainWalletClient, HyperLiquidRestClient apiWalletClient)
    {
        MainWalletClient = mainWalletClient;
        ApiWalletClient = apiWalletClient;

        _logger.Information("HyperLiquidExchangeを初期化中...");

        // シンボル情報を初期化(桁数・最大レバレッジを取得)
        var exchangeResult = mainWalletClient.FuturesApi.ExchangeData.GetExchangeInfoAsync().Result;
        if (!exchangeResult.Success)
        {
            _logger.Error("取引所情報の取得に失敗しました: {Error}", exchangeResult.Error);
            throw new Exception($"Failed to get exchange info: {exchangeResult.Error}");
        }

        _symbols = exchangeResult.Data.GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.First());

        _logger.Information("HyperLiquidExchangeを初期化しました。登録シンボル数: {Count}", _symbols.Count);
    }

    /// <summary>
    /// シンボル情報とレバレッジを取得（遅延初期化・キャッシュ付き）
    /// </summary>
    private async Task<SymbolWithLeverage> GetSymbolInfoAsync(string symbol)
    {
        // キャッシュにあればそれを返す
        if (_symbolInfoCache.TryGetValue(symbol, out var cached))
        {
            _logger.Debug("シンボル情報をキャッシュから取得しました: {Symbol}", symbol);
            return cached;
        }

        // シンボルが存在するか確認
        if (!_symbols.TryGetValue(symbol, out var symbolData))
        {
            _logger.Error("シンボルが見つかりません: {Symbol}", symbol);
            throw new Exception($"Symbol not found: {symbol}");
        }

        // レバレッジ情報を取得
        var leverageResult = await MainWalletClient.FuturesApi.Account.GetUserSymbolAsync(symbol);

        var info = leverageResult.Success
            ? new SymbolWithLeverage(symbolData, leverageResult.Data.Leverage.Value, leverageResult.Data.Leverage.MarginType)
            : new SymbolWithLeverage(symbolData, 1, MarginType.Cross);

        _symbolInfoCache[symbol] = info;
        _logger.Debug("シンボル情報を取得しました。シンボル: {Symbol}, レバレッジ: {Leverage}, マージンタイプ: {MarginType}",
            symbol, info.Leverage, info.MarginType);
        return info;
    }

    /// <summary>
    /// 指定したシンボル・USDCの組み合わせでポジションを作成
    /// </summary>
    /// <param name="symbol"></param>
    /// <param name="amountToBuyUSDC"></param>
    /// <returns></returns>
    async Task<PlaceOrderAsyncResult> PlaceOrderAsync(string symbol, OrderSide side, decimal amountToBuyUSDC, decimal price, decimal tpPrice, decimal slPrice)
    {
        _logger.Information("注文を作成します。シンボル: {Symbol}, 売買: {Side}, 金額: {Amount} USDC, 価格: {Price}, TP: {TpPrice}, SL: {SlPrice}",
            symbol, side, amountToBuyUSDC, price, tpPrice, slPrice);

        var symbolInfo = await GetSymbolInfoAsync(symbol);
        var quantity = Math.Round(amountToBuyUSDC / price, symbolInfo.Symbol.QuantityDecimals);

        var orders = new List<HyperLiquidOrderRequest>();

        var positionRequest = new HyperLiquidOrderRequest(
            symbol: symbol,
            side: side,
            orderType: OrderType.Market,
            quantity: quantity,
            price: price
        );
        orders.Add(positionRequest);

        var tpRequest = new HyperLiquidOrderRequest(
            symbol: symbol,
            side: side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
            orderType: OrderType.TakeProfitMarket,
            quantity: quantity,
            price: tpPrice,
            triggerPrice: tpPrice,
            tpSlType: TpSlType.TakeProfit,
            reduceOnly: true
        );
        orders.Add(tpRequest);

        var slRequest = new HyperLiquidOrderRequest(
            symbol: symbol,
            side: side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
            orderType: OrderType.StopMarket,
            quantity: quantity,
            price: slPrice,
            triggerPrice: slPrice,
            tpSlType: TpSlType.StopLoss,
            reduceOnly: true
        );
        orders.Add(slRequest);

        var orderResult = await ApiWalletClient.FuturesApi.Trading.PlaceMultipleOrdersAsync(orders);
        if (!orderResult.Success)
        {
            _logger.Error("注文の作成に失敗しました。シンボル: {Symbol}, エラー: {Error}", symbol, orderResult.Error);
            throw new Exception($"Order placement failed: {orderResult.Error}");
        }

        _logger.Information("注文を作成しました。シンボル: {Symbol}, 数量: {Quantity}", symbol, quantity);

        return new PlaceOrderAsyncResult(
            OrderId: orderResult.Data[0].Data.OrderId,
            TakeProfitOrderId: orderResult.Data[1].Data.OrderId,
            StopLossOrderId: orderResult.Data[2].Data.OrderId
        );
    }

    /// <summary>
    /// 指定したシンボル・USDCの組み合わせでポジションを作成（TP/SL比率指定）
    /// </summary>
    /// <param name="symbol">シンボル名</param>
    /// <param name="side">売買方向</param>
    /// <param name="amountToBuyUSDC">購入金額(USDC)</param>
    /// <param name="tpRatio">証拠金に対する利益確定比率 (0.5 = +50%で利確)</param>
    /// <param name="slRatio">証拠金に対する損失確定比率 (0.2 = -20%で損切り)</param>
    /// <returns></returns>
    public async Task<PlaceOrderAsyncResult> PlaceOrderAsync(string symbol, OrderSide side, decimal amountToBuyUSDC, decimal tpRatio, decimal slRatio)
    {
        _logger.Information("注文を作成します（比率指定）。シンボル: {Symbol}, 売買: {Side}, 金額: {Amount} USDC, TP比率: {TpRatio}, SL比率: {SlRatio}",
            symbol, side, amountToBuyUSDC, tpRatio, slRatio);

        var symbolInfo = await GetSymbolInfoAsync(symbol);

        var tickerResult = await MainWalletClient.FuturesApi.ExchangeData.GetExchangeInfoAndTickersAsync();
        if (!tickerResult.Success)
        {
            _logger.Error("ティッカー情報の取得に失敗しました: {Error}", tickerResult.Error);
            throw new Exception($"Failed to get ticker info: {tickerResult.Error}");
        }

        var currentPrice = tickerResult.Data.Tickers.First(t => t.Symbol == symbol).MarkPrice;
        var leverage = symbolInfo.Leverage;

        _logger.Debug("現在価格: {CurrentPrice}, レバレッジ: {Leverage}", currentPrice, leverage);

        // レバレッジを考慮した価格変動率を計算
        // 証拠金利益率 = 価格変動率 × レバレッジ
        // → 価格変動率 = 証拠金利益率 / レバレッジ
        var tpPriceChangeRatio = tpRatio / leverage;
        var slPriceChangeRatio = slRatio / leverage;

        decimal tpPrice, slPrice;

        // HyperLiquidでは価格の桁数はAPIから取得できないため、
        // 有効数字5桁で丸める（一般的な価格精度）
        const int priceSigFigs = 5;

        if (side == OrderSide.Buy)
        {
            // ロング: 価格上昇で利益、下落で損失
            tpPrice = RoundToSignificantFigures(currentPrice * (1 + tpPriceChangeRatio), priceSigFigs);
            slPrice = RoundToSignificantFigures(currentPrice * (1 - slPriceChangeRatio), priceSigFigs);
        }
        else if (side == OrderSide.Sell)
        {
            // ショート: 価格下落で利益、上昇で損失
            tpPrice = RoundToSignificantFigures(currentPrice * (1 - tpPriceChangeRatio), priceSigFigs);
            slPrice = RoundToSignificantFigures(currentPrice * (1 + slPriceChangeRatio), priceSigFigs);
        }
        else
        {
            throw new Exception("Invalid order side");
        }

        return await PlaceOrderAsync(symbol, side, amountToBuyUSDC, currentPrice, tpPrice, slPrice);
    }

    /// <summary>
    /// 指定したシンボルのポジションをクローズする
    /// </summary>
    /// <param name="symbol"></param>
    /// <returns></returns>
    public async Task<HyperLiquidOrderResult> CloseOrderAsync(string symbol)
    {
        _logger.Information("ポジションをクローズします。シンボル: {Symbol}", symbol);
        {

            // 現在の注文情報を取得
            var openOrdersResult = await MainWalletClient.FuturesApi.Trading.GetOpenOrdersAsync();

            if (!openOrdersResult.Success)
            {
                _logger.Error("ポジション情報の取得に失敗しました: {Error}", openOrdersResult.Error);
                throw new Exception($"Failed to get position info: {openOrdersResult.Error}");
            }

            var openOrders = openOrdersResult.Data.Where(x => x.Symbol == symbol).ToList();
            if (openOrders.Any())
            {
                _logger.Debug("未決済注文をキャンセルします。件数: {Count}", openOrders.Count);
                var cancelRequests = openOrders.Select(positions =>
                    new HyperLiquidCancelRequest(
                        symbol: symbol,
                        orderId: positions.OrderId
                    )
                ).ToList();
                var cancelResult = await ApiWalletClient.FuturesApi.Trading.CancelOrdersAsync(cancelRequests);
                if (!cancelResult.Success)
                {
                    _logger.Error("注文のキャンセルに失敗しました: {Error}", cancelResult.Error);
                    throw new Exception($"Failed to cancel orders: {cancelResult.Error}");
                }
                _logger.Debug("注文をキャンセルしました");
            }
        }
        {

            // 現在保持しているポジションを取得
            var positionsResult = await MainWalletClient.FuturesApi.Account.GetAccountInfoAsync();
            var symbolPosition = positionsResult.Data.Positions.FirstOrDefault(position => position.Position.Symbol == symbol);
            if (symbolPosition != null)
            {
                var closeQuantity = symbolPosition.Position.PositionQuantity;
                if (closeQuantity == null || closeQuantity == 0)
                {
                    _logger.Warning("クローズする数量がありません。シンボル: {Symbol}", symbol);
                    throw new Exception($"{symbol} : closeQuantity is null or zero");
                }

                // ポジション数量の符号でサイドを判定
                // 正 = ロング → クローズはSell
                // 負 = ショート → クローズはBuy
                var isLong = closeQuantity > 0;
                var closeSide = isLong ? OrderSide.Sell : OrderSide.Buy;
                var absQuantity = Math.Abs(closeQuantity.Value);

                _logger.Debug("ポジションをクローズします。数量: {Quantity}, 方向: {Side}", absQuantity, closeSide);

                // 現在価格で成行決済注文を出す
                var tickerResult = await MainWalletClient.FuturesApi.ExchangeData.GetExchangeInfoAndTickersAsync();
                if (!tickerResult.Success)
                {
                    _logger.Error("ティッカー情報の取得に失敗しました: {Error}", tickerResult.Error);
                    throw new Exception($"Failed to get ticker info: {tickerResult.Error}");
                }

                var currentPrice = tickerResult.Data.Tickers.First(t => t.Symbol == symbol).MarkPrice;

                var marketCloseRequest = await ApiWalletClient.FuturesApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: closeSide,
                    orderType: OrderType.Market,
                    quantity: absQuantity,
                    price: currentPrice
                );

                if (marketCloseRequest.Success)
                {
                    _logger.Information("ポジションをクローズしました。シンボル: {Symbol}, 数量: {Quantity}", symbol, absQuantity);
                    return marketCloseRequest.Data;
                }
                else
                {
                    _logger.Error("成行決済注文の作成に失敗しました: {Error}", marketCloseRequest.Error);
                    throw new Exception($"Failed to place market close order: {marketCloseRequest.Error}");
                }
            }
            else
            {
                // ポジションなし
                _logger.Debug("クローズするポジションがありません。シンボル: {Symbol}", symbol);
                return new HyperLiquidOrderResult()
                {
                };
            }
        }
    }

    public async Task<HyperLiquidKline[]> GetKlinesAsync(string symbol, KlineInterval interval, DateTime startDate, DateTime endDate, int limit = 300)
    {
        _logger.Debug("ローソク足データを取得します。シンボル: {Symbol}, 期間: {StartDate} - {EndDate}", symbol, startDate, endDate);
        var ret = new List<HyperLiquidKline>();
        DateTime fetchStartDate = startDate;

        // var klineResult = await MainWalletClient.FuturesApi.ExchangeData.GetKlinesAsync(
        //     symbol: symbol,
        //     interval: interval,
        //     startTime: startDate,
        //     endTime: endDate
        // );

        // if (!klineResult.Success)
        // {
        //     _logger.Error("ローソク足データの取得に失敗しました: {Error}", klineResult.Error);
        //     throw new Exception($"Failed to get klines: {klineResult.Error}");
        // }

        // var LatestTimestamp = klineResult.Data.Max(k => k.OpenTime);
        // if (LatestTimestamp < endDate)
        // {
        //     _logger.Debug("取得したローソク足データの最新日時が指定終了日時よりも前です。シンボル: {Symbol}, 最新日時: {LatestTimestamp}, 終了日時: {EndDate}",
        //         symbol, LatestTimestamp, endDate);


        // }

        var klineResult = await GetKlinesRecursiveAsync(symbol, interval, startDate, endDate, new HyperLiquidKline[] { }, limit);

        // _logger.Debug("ローソク足データを取得しました。件数: {Count}", klineResult.Data.Length);

        if (klineResult.Length == 0)
        {
            _logger.Warning("指定期間内にローソク足データが存在しません。シンボル: {Symbol}, 期間: {StartDate} - {EndDate}", symbol, startDate, endDate);
            return Array.Empty<HyperLiquidKline>();
        }
        else
        {
            _logger.Debug("ローソク足データを取得しました。件数: {Count}", klineResult.Length);
            return klineResult;
        }
    }

    private async Task<HyperLiquidKline[]> GetKlinesRecursiveAsync(string symbol, KlineInterval interval, DateTime startDate, DateTime endDate, HyperLiquidKline[] existingKlines, int limit = 300)
    {
        const int ApiPageLimit = 5000; // HyperLiquid の実測上の上限（API側で固定されている場合がある）
        var combinedList = existingKlines != null ? existingKlines.ToList() : new List<HyperLiquidKline>();

        DateTime currentStart = startDate;
        DateTime currentEnd = endDate;
        int iteration = 0;
        const int maxIterations = 1000; // 無限ループ防止

        while (true)
        {
            iteration++;
            if (iteration > maxIterations)
            {
                _logger.Warning("ローソク足取得が長時間続いているため中断します。iteration overflow");
                break;
            }

            _logger.Debug("ローソク足データを取得: {Symbol} {Start} - {End}", symbol, currentStart, currentEnd);

            var klineResult = await MainWalletClient.FuturesApi.ExchangeData.GetKlinesAsync(
                symbol: symbol,
                interval: interval,
                startTime: currentStart,
                endTime: currentEnd
            );

            if (!klineResult.Success)
            {
                _logger.Error("ローソク足データの取得に失敗しました: {Error}", klineResult.Error);
                throw new Exception($"Failed to get klines: {klineResult.Error}");
            }

            if (klineResult.Data == null || klineResult.Data.Length == 0)
            {
                _logger.Debug("指定ウィンドウにデータがありません: {Start} - {End}", currentStart, currentEnd);
                break;
            }

            var fetched = klineResult.Data;
            _logger.Debug("取得件数: {Count} (範囲: {Min} - {Max})", fetched.Length, fetched.Min(k => k.OpenTime), fetched.Max(k => k.OpenTime));

            combinedList.AddRange(fetched);

            // 重複を取り除き、時刻順にソート
            var deduped = combinedList
                .GroupBy(k => k.OpenTime)
                .Select(g => g.First())
                .OrderBy(k => k.OpenTime)
                .ToList();

            var earliestReturned = fetched.Min(k => k.OpenTime);
            var latestReturned = fetched.Max(k => k.OpenTime);

            // 完了条件: 取得データの最新が要求終了日時を超えている（または到達している）
            if (latestReturned >= endDate && earliestReturned <= startDate)
            {
                _logger.Debug("ローソク足取得完了。最新日時: {Latest}", latestReturned);
                return deduped.ToArray();
            }

            // 取得件数が API の上限に達している -> 範囲が切られている可能性あり
            // API が返すデータがリクエスト開始側からの先頭（earliestReturned == currentStart）なら、次は start を進める
            // ただし、API が end 側から最新の方を返してきた場合（earliestReturned > currentStart）には end をずらして古い範囲を取得する
            if (earliestReturned > currentStart)
            {
                // API が end 側の最新を返してきた（ウィンドウが end に寄っている）ので、end をずらして古い範囲を取得
                currentEnd = earliestReturned.AddMilliseconds(-1);
                _logger.Debug("APIが最新寄りで切られたデータを返したため end を後退させます。新 end: {End}", currentEnd);
            }
            else
            {
                // API が start 側から順に返しているので、start を進めて残りを取得
                currentStart = latestReturned.AddMilliseconds(1);
                _logger.Debug("APIが start 側から返したため start を進めます。新 start: {Start}", currentStart);
            }

            combinedList = deduped; // 保持して次ループへ
        }

        // 最終的に重複除去して返す
        var finalResult = combinedList
            .GroupBy(k => k.OpenTime)
            .Select(g => g.First())
            .OrderBy(k => k.OpenTime)
            .ToArray();

        return finalResult;
    }

    /// <summary>
    /// 有効数字で丸める
    /// </summary>
    private static decimal RoundToSignificantFigures(decimal value, int significantFigures)
    {
        if (value == 0) return 0;

        var scale = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)Math.Abs(value))) + 1 - significantFigures);
        return scale * Math.Round(value / scale);
    }


}