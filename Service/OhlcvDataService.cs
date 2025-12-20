
using HyperLiquid.Net.Enums;


/// <summary>
/// DB・メモリからOHLCVデータを取得・管理するサービス
/// </summary>
public class OhlcvDataService
{
    private readonly MySQLRepository _repository;

    private readonly KlineInterval _interval;

    public OhlcvDataService(MySQLRepository repository, KlineInterval interval)
    {
        _repository = repository;
        _interval = interval;
    }

}