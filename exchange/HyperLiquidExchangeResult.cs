

class PlaceOrderAsyncResult
{
    public long OrderId { get; }
    public long TakeProfitOrderId { get; }
    public long StopLossOrderId { get; }

    public PlaceOrderAsyncResult(long OrderId, long TakeProfitOrderId, long StopLossOrderId)
    {
        this.OrderId = OrderId;
        this.TakeProfitOrderId = TakeProfitOrderId;
        this.StopLossOrderId = StopLossOrderId;
    }

    public override string ToString()
    {
        return $"OrderId: {OrderId}, TakeProfitOrderId: {TakeProfitOrderId}, StopLossOrderId: {StopLossOrderId}";
    }
}