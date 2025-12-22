using HyperLiquid.Net.Enums;
using Serilog;


/// <summary>
/// DB・メモリからOHLCVデータを取得・管理するサービス
/// </summary>
public class OhlcvDataService : IDisposable
{
    private readonly OhlcvDataRepository _dataRepository;
    private readonly HyperLiquidExchange _exchange;
    private readonly KlineInterval _interval;
    private readonly string _symbol;
    private readonly TimeSpan _refreshInterval;
    private readonly object _cacheLock = new();
    private static readonly ILogger _logger = Log.ForContext<OhlcvDataService>();

    private List<OhlcvData> _ohlcvDataCache = new();
    private Timer? _refreshTimer;
    private bool _disposed = false;

    private const int FetchDataCount = 10;

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
        HyperLiquidExchange exchange,
        string symbol,
        KlineInterval interval,
        TimeSpan? refreshInterval = null,
        int dataCount = 250)
    {
        _dataRepository = dataRepository;
        _exchange = exchange;
        _symbol = symbol;
        _interval = interval;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(1);
        DataCount = dataCount;

        _logger.Debug("OhlcvDataServiceを初期化しました。シンボル: {Symbol}, インターバル: {Interval}, 更新間隔: {RefreshInterval}秒",
            symbol, interval, _refreshInterval.TotalSeconds);
    }

    /// <summary>
    /// キャッシュの定期更新を開始する
    /// </summary>
    public async Task StartAsync()
    {
        _logger.Information("OHLCVキャッシュの定期更新を開始します。シンボル: {Symbol}", _symbol);

        // 現在時刻の0秒まで待機する
        var delay = TimeSpan.FromSeconds(60 - DateTime.UtcNow.Second);
        _logger.Debug("初回更新まで待機中です: {Delay}秒", delay.TotalSeconds);
        await Task.Delay(delay);

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
        _logger.Information("OHLCVキャッシュの定期更新を停止します。シンボル: {Symbol}", _symbol);
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
            _logger.Debug("OHLCVデータを取得中。シンボル: {Symbol}", _symbol);
            var intervalSeconds = (int)_interval;
            var candle = await _exchange.GetKlinesAsync(
                _symbol,
                _interval,
                startDate: DateTime.UtcNow.AddSeconds(-intervalSeconds * FetchDataCount),
                endDate: DateTime.UtcNow);
            _logger.Debug("OHLCVデータを取得しました。シンボル: {Symbol}, 件数: {Count}", _symbol, candle.Count());

            await _dataRepository.AddOrUpdateOhlcvDataAsync(_symbol, candle.Select(c => new OhlcvData
            {
                OpenPrice = c.OpenPrice,
                HighPrice = c.HighPrice,
                LowPrice = c.LowPrice,
                ClosePrice = c.ClosePrice,
                Volume = c.Volume,
                TimestampUtc = c.OpenTime,
                CreatedAt = DateTime.UtcNow
            }).ToList());
            _logger.Debug("OHLCVデータをDBに保存しました。シンボル: {Symbol}", _symbol);

            _logger.Debug("OHLCVキャッシュを更新中。シンボル: {Symbol}", _symbol);
            // OhlcvDataRepositoryを使用してデータを取得（集計も含む）
            var aggregatedData = await _dataRepository.GetLatestOhlcvDataAsync(_symbol, _interval, DataCount);

            lock (_cacheLock)
            {
                _ohlcvDataCache = aggregatedData;
            }

            _logger.Debug("OHLCVキャッシュを更新しました。シンボル: {Symbol}, 件数: {Count}", _symbol, aggregatedData.Count);

            // イベントを発火
            OnDataUpdated(new OhlcvDataUpdatedEventArgs(aggregatedData.AsReadOnly()));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OHLCVキャッシュ更新エラー。シンボル: {Symbol}", _symbol);
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