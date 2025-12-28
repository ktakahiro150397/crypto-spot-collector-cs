using CryptoExchange.Net.SharedApis;

/// <summary>
/// ストラテジークラスでの意思決定結果
/// </summary>
public class StrategyDecisionResult
{
    /// <summary>
    /// ポジションサイド
    /// </summary>
    public SharedPositionSide Side { get; set; }

    /// <summary>
    /// ストラテジー名
    /// </summary>
    public string StrategyName { get; set; }

    /// <summary>
    /// 意思決定理由
    /// </summary>
    public string Reason { get; set; }

    public decimal? StopLossPrice { get; set; }

    public StrategyDecisionResult(
        SharedPositionSide side,
        string strategyName,
        string reason,
        decimal? stopLossPrice)
    {
        Side = side;
        StrategyName = strategyName;
        Reason = reason;
        StopLossPrice = stopLossPrice;
    }
}