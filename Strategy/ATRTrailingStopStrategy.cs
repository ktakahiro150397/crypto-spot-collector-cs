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
            // 必要な場合はストップロス価格の更新を行う
            var existingPosition = position.PositionItems.First(item => item.CloseDate == null);

            if (existingPosition.side == SharedPositionSide.Long && existingPosition.StopLossPrice < stopLossPrice)
            {
                Log.Information("ストップロス価格を更新します。旧価格: {OldStopLoss}, 新価格: {NewStopLoss}", existingPosition.StopLossPrice, stopLossPrice);
                return new StrategyDecisionResult
                (
                    strategyName: nameof(ATRTrailingStopStrategy),
                    reason: "ATRトレーリングストップに基づくストップロス価格の更新"
                )
                {
                    Operation = StrategyDecisionOperation.UpdateStopLossPrice,
                    Side = existingPosition.side,
                    StopLossPrice = stopLossPrice
                };
            }
            else if (existingPosition.side == SharedPositionSide.Short && existingPosition.StopLossPrice > stopLossPrice)
            {
                Log.Information("ストップロス価格を更新します。旧価格: {OldStopLoss}, 新価格: {NewStopLoss}", existingPosition.StopLossPrice, stopLossPrice);
                return new StrategyDecisionResult
                (
                    strategyName: nameof(ATRTrailingStopStrategy),
                    reason: "ATRトレーリングストップに基づくストップロス価格の更新"
                )
                {
                    Operation = StrategyDecisionOperation.UpdateStopLossPrice,
                    Side = existingPosition.side,
                    StopLossPrice = stopLossPrice
                };
            }
            else
            {
                return StrategyDecisionResult.CreateNoOperationResult(
                    strategyName: nameof(ATRTrailingStopStrategy),
                    reason: "既存ポジションがあるため、新規エントリーは行いません。");
            }
        }

        var side = isLong ? SharedPositionSide.Long : SharedPositionSide.Short;
        var reason = isLong ? "ATRトレーリングストップに基づくロングエントリー" :
                     isShort ? "ATRトレーリングストップに基づくショートエントリー" :
                     "ATRトレーリングストップに基づくシグナルなし";

        return new StrategyDecisionResult
        (
            strategyName: nameof(ATRTrailingStopStrategy),
            reason: reason
        )
        {
            Operation = StrategyDecisionOperation.OpenPosition,
            Side = side,
            StopLossPrice = stopLossPrice
        };
    }
}