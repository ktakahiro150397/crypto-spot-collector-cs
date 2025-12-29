using CryptoExchange.Net.SharedApis;

/// <summary>
/// ストラテジークラスでの意思決定結果
/// </summary>
public class StrategyDecisionResult
{
    /// <summary>
    /// 操作内容
    /// </summary>
    public StrategyDecisionOperation Operation { get; set; }

    /// <summary>
    /// ポジションサイド
    /// </summary>
    public SharedPositionSide? Side { get; set; }

    /// <summary>
    /// ストップロス価格
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// ストラテジー名
    /// </summary>
    public string StrategyName { get; set; }

    /// <summary>
    /// 意思決定理由
    /// </summary>
    public string Reason { get; set; }

    public StrategyDecisionResult(
        string strategyName,
        string reason)
    {
        StrategyName = strategyName;
        Reason = reason;
    }

    public static StrategyDecisionResult CreateNoOperationResult(string strategyName, string reason)
    {
        return new StrategyDecisionResult
        (
            strategyName: strategyName,
            reason: reason
        )
        {
            Operation = StrategyDecisionOperation.None
        };
    }

    public override string ToString()
    {
        return $"Strategy: {StrategyName}, Operation: {Operation}, Side: {Side}, StopLossPrice: {StopLossPrice}, Reason: {Reason}";
    }
}

public enum StrategyDecisionOperation
{

    /// <summary>
    /// 操作なし
    /// </summary>
    None,

    /// <summary>
    /// ポジションを開く
    /// </summary>
    OpenPosition,

    /// <summary>
    /// ポジションを閉じる(Reduce Only)
    /// </summary>
    ClosePosition,

    /// <summary>
    /// ストップロス価格を更新
    /// </summary>
    UpdateStopLossPrice,
}