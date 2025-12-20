using Skender.Stock.Indicators;


/// <summary>
/// OHLCVデータエンティティ
/// </summary>
public class OhlcvData : IQuote
{
    public long Id { get; set; }
    public int CryptocurrencyId { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public decimal Volume { get; set; }
    public DateTime TimestampUtc { get; set; }
    public DateTime CreatedAt { get; set; }

    public decimal Open => OpenPrice;

    public decimal High => HighPrice;

    public decimal Low => LowPrice;

    public decimal Close => ClosePrice;

    public DateTime Date => TimestampUtc;
}