
using CryptoExchange.Net.SharedApis;

/// <summary>
/// 永久先物ポジション情報
/// </summary>
public class PerpetualPosition
{
    public List<PerpetualPositionItem> PositionItems { get; set; } = new List<PerpetualPositionItem>();

    public decimal GetTotalPnl
    {
        get
        {
            return PositionItems.Sum(item => item.GetPnl);
        }
    }
}

/// <summary>
/// 永久先物ポジションアイテム
/// </summary>
public class PerpetualPositionItem
{
    /// <summary>
    /// ポジション開始日時
    /// </summary>
    public DateTime OpenDate { get; set; }

    /// <summary>
    /// ポジション開始価格
    /// </summary>
    public decimal OpenPrice { get; set; }

    /// <summary>
    /// ポジション終了日時
    /// </summary>
    public DateTime? CloseDate { get; set; }

    /// <summary>
    /// ポジション終了価格
    /// </summary>
    public decimal? ClosePrice { get; set; }

    /// <summary>
    /// ポジション数量
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// ポジションサイド
    /// </summary>
    public SharedPositionSide side { get; set; }

    /// <summary>
    /// ポジションのストップロス価格
    /// </summary>
    public decimal? StopLossPrice { get; set; }

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
                else // Short
                {
                    return (OpenPrice - ClosePrice.Value) * Quantity;
                }
            }
            else
            {
                return 0m; // 未決済ポジションのPnLは0とする
            }
        }
    }
}