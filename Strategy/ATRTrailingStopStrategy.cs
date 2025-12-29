using CryptoExchange.Net.SharedApis;
using Serilog;
using Skender.Stock.Indicators;

public class ATRTrailingStopStrategy : IStrategyDecisioner
{
    private readonly int _atrPeriod;
    private readonly decimal _atrMultiplier;

    public ATRTrailingStopStrategy(int atrPeriod, decimal atrMultiplier)
    {
        _atrPeriod = atrPeriod;
        _atrMultiplier = atrMultiplier;
    }

    public StrategyDecisionResult DecideSignal(string symbol, PerpetualPosition position, List<OhlcvData> ohlcvData)
    {
        // ATRトレーリングストップロジックの実装

        var atrTrailing = ohlcvData.OrderBy(item => item.TimestampUtc).GetAtrStop();

        // ATRトレーリングに基づいた現在の売買シグナル、ストップロス価格の取得
        var latestAtrTrailing = atrTrailing.LastOrDefault();
        if (latestAtrTrailing == null)
        {
            Log.Warning("ATRトレーリングストップの計算に失敗しました。OHLCVデータが不十分です。");
            return StrategyDecisionResult.CreateNoOperationResult(
                strategyName: nameof(ATRTrailingStopStrategy),
                reason: "ATRトレーリングストップの計算に失敗しました。OHLCVデータが不十分です。");
        }

        var isLong = latestAtrTrailing.SellStop.HasValue;
        var isShort = latestAtrTrailing.BuyStop.HasValue;
        var stopLossPrice = latestAtrTrailing.AtrStop;

        if (!isLong && !isShort)
        {
            Log.Warning("ATRトレーリングストップに基づく明確なエントリーシグナルがありません。");
            return StrategyDecisionResult.CreateNoOperationResult(
                strategyName: nameof(ATRTrailingStopStrategy),
                reason: "ATRトレーリングストップに基づく明確なエントリーシグナルがありません。");
        }

        // 現在ポジションを持っている場合は追加しない
        if (position.PositionItems.FirstOrDefault(item => item.CloseDate == null) != null)
        {
            Log.Debug("既にポジションを保有しているため、新規エントリーは行いません。");
            return StrategyDecisionResult.CreateNoOperationResult(
                strategyName: nameof(ATRTrailingStopStrategy),
                reason: "既にポジションを保有しているため、新規エントリーは行いません。");
        }

        var side = isLong ? SharedPositionSide.Long : SharedPositionSide.Short;
        var reason = isLong ? "ATRトレーリングストップに基づくロングエントリー" :
                     isShort ? "ATRトレーリングストップに基づくショートエントリー" :
                     "ATRトレーリングストップに基づくシグナルなし";

        return new StrategyDecisionResult
        (
            // side: side,
            strategyName: nameof(ATRTrailingStopStrategy),
            reason: reason
        // stopLossPrice: stopLossPrice // 実際のストップロス価格を設定
        )
        {
            Operation = StrategyDecisionOperation.OpenPosition,
            Side = side,
            StopLossPrice = stopLossPrice
        };
    }
}