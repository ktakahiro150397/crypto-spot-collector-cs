
using HyperLiquid.Net.Enums;


/// <summary>
/// DB・メモリからOHLCVデータを取得・管理するサービス
/// </summary>
public class OhlcvDataService : IDisposable
{
    private readonly OhlcvDataRepository _dataRepository;
    private readonly KlineInterval _interval;
    private readonly string _symbol;
    private readonly TimeSpan _refreshInterval;
    private readonly object _cacheLock = new();

    private List<OhlcvData> _ohlcvDataCache = new();
    private Timer? _refreshTimer;
    private bool _disposed = false;

    /// <summary>
    /// データをキャッシュする足の本数
    /// </summary>
    public int DataCount { get; }

    /// <summary>
    /// キャッシュされたOHLCVデータを取得する（読み取り専用）
    /// </summary>
    public IReadOnlyList<OhlcvData> CachedData
    {
        get
        {
            lock (_cacheLock)
            {
                return _ohlcvDataCache.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// キャッシュが更新されたときに発火するイベント
    /// </summary>
    public event EventHandler<OhlcvDataUpdatedEventArgs>? DataUpdated;

    public OhlcvDataService(
        OhlcvDataRepository dataRepository,
        string symbol,
        KlineInterval interval,
        TimeSpan? refreshInterval = null,
        int dataCount = 250)
    {
        _dataRepository = dataRepository;
        _symbol = symbol;
        _interval = interval;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(1);
        DataCount = dataCount;
    }

    /// <summary>
    /// キャッシュの定期更新を開始する
    /// </summary>
    public async Task StartAsync()
    {
        // 初回のデータ取得
        await RefreshCacheAsync();

        // 定期更新タイマーを開始
        _refreshTimer = new Timer(
            async _ => await RefreshCacheAsync(),
            null,
            _refreshInterval,
            _refreshInterval);
    }

    /// <summary>
    /// キャッシュの定期更新を停止する
    /// </summary>
    public void Stop()
    {
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    /// <summary>
    /// キャッシュを手動で更新する
    /// </summary>
    public async Task RefreshCacheAsync()
    {
        try
        {
            // OhlcvDataRepositoryを使用してデータを取得（集計も含む）
            var aggregatedData = await _dataRepository.GetLatestOhlcvDataAsync(_symbol, _interval, DataCount);

            lock (_cacheLock)
            {
                _ohlcvDataCache = aggregatedData;
            }

            // イベントを発火
            OnDataUpdated(new OhlcvDataUpdatedEventArgs(aggregatedData.AsReadOnly()));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"キャッシュ更新エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// DataUpdatedイベントを発火する
    /// </summary>
    protected virtual void OnDataUpdated(OhlcvDataUpdatedEventArgs e)
    {
        DataUpdated?.Invoke(this, e);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// OHLCVデータ更新イベント引数
/// </summary>
public class OhlcvDataUpdatedEventArgs : EventArgs
{
    public IReadOnlyList<OhlcvData> UpdatedData { get; }

    public OhlcvDataUpdatedEventArgs(IReadOnlyList<OhlcvData> updatedData)
    {
        UpdatedData = updatedData;
    }
}