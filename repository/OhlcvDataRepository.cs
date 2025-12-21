using HyperLiquid.Net.Enums;
using Serilog;

/// <summary>
/// OHLCVデータの取得・集計を行うリポジトリ
/// DBに保存されている足のインターバルを自動検出し、指定したインターバルに集計して返す
/// </summary>
public class OhlcvDataRepository
{
    private readonly MySQLRepository _repository;
    private static readonly ILogger _logger = Log.ForContext<OhlcvDataRepository>();

    /// <summary>
    /// DBに保存されているベースインターバル（分）のキャッシュ
    /// シンボルごとにキャッシュする
    /// </summary>
    private readonly Dictionary<string, int> _baseIntervalCache = new();

    public OhlcvDataRepository(MySQLRepository repository)
    {
        _repository = repository;
        _logger.Debug("OhlcvDataRepositoryを初期化しました");
    }

    /// <summary>
    /// 指定シンボルの最新N件のOHLCVデータを、指定したインターバルで取得する
    /// </summary>
    /// <param name="symbol">シンボル</param>
    /// <param name="interval">目標のインターバル</param>
    /// <param name="count">取得する足の本数</param>
    /// <param name="isExceptLastIncompleteCandle">最新の不完全な足を除外するかどうか</param>
    /// <returns>集計されたOHLCVデータ</returns>
    public async Task<List<OhlcvData>> GetLatestOhlcvDataAsync(string symbol, KlineInterval interval, int count, bool isExceptLastIncompleteCandle = true)
    {
        _logger.Debug("OHLCVデータを取得します。シンボル: {Symbol}, インターバル: {Interval}, 件数: {Count}", symbol, interval, count);

        var targetMinutes = GetIntervalMinutes(interval);
        var baseIntervalMinutes = await GetBaseIntervalMinutesAsync(symbol);

        if (baseIntervalMinutes <= 0)
        {
            _logger.Warning("ベースインターバルが取得できません。シンボル: {Symbol}", symbol);
            return new List<OhlcvData>(); // データがない場合
        }

        // バリデーション
        if (targetMinutes < baseIntervalMinutes)
        {
            _logger.Error("インターバルが小さすぎます。要求: {TargetMinutes}分, ベース: {BaseMinutes}分", targetMinutes, baseIntervalMinutes);
            throw new ArgumentException(
                $"指定されたインターバル({interval}: {targetMinutes}分)はDBの基準インターバル({baseIntervalMinutes}分)より小さいため対応できません。");
        }

        if (targetMinutes % baseIntervalMinutes != 0)
        {
            _logger.Error("インターバルがベースの倍数ではありません。要求: {TargetMinutes}分, ベース: {BaseMinutes}分", targetMinutes, baseIntervalMinutes);
            throw new ArgumentException(
                $"指定されたインターバル({interval}: {targetMinutes}分)はDBの基準インターバル({baseIntervalMinutes}分)の倍数である必要があります。");
        }

        // 目標のインターバルに必要なベース足の本数を計算
        // 最新足を除外する場合は1つ多く取得する必要がある
        var candlesPerInterval = targetMinutes / baseIntervalMinutes;
        var extraCandles = isExceptLastIncompleteCandle ? candlesPerInterval : 0;
        var requiredBaseCandles = (count * candlesPerInterval) + extraCandles;

        // ベース足のデータを取得
        var baseData = await _repository.GetLatestOhlcvDataBySymbolAsync(symbol, requiredBaseCandles);

        if (baseData.Count == 0)
        {
            _logger.Debug("ベースデータが見つかりません。シンボル: {Symbol}", symbol);
            return new List<OhlcvData>();
        }

        // 指定されたインターバルに集計
        var aggregatedData = AggregateOhlcvData(baseData, targetMinutes);

        // 最新の不完全な足を除外する場合
        if (isExceptLastIncompleteCandle && aggregatedData.Count > 0)
        {
            // 最新の足が不完全かどうかを判定
            var latestCandle = aggregatedData.Last();
            if (IsIncompleteCandle(latestCandle.TimestampUtc, targetMinutes))
            {
                aggregatedData = aggregatedData.Take(aggregatedData.Count - 1).ToList();
            }
        }

        _logger.Debug("OHLCVデータを取得しました。シンボル: {Symbol}, 件数: {Count}", symbol, aggregatedData.Count);

        // count件に制限
        return aggregatedData.TakeLast(count).ToList();
    }

    /// <summary>
    /// 指定シンボルのOHLCVデータを期間指定で、指定したインターバルで取得する
    /// </summary>
    public async Task<List<OhlcvData>> GetOhlcvDataAsync(string symbol, KlineInterval interval, DateTime startDate, DateTime endDate)
    {
        var targetMinutes = GetIntervalMinutes(interval);
        var baseIntervalMinutes = await GetBaseIntervalMinutesAsync(symbol);

        if (baseIntervalMinutes <= 0)
        {
            return new List<OhlcvData>(); // データがない場合
        }

        // バリデーション
        if (targetMinutes < baseIntervalMinutes)
        {
            throw new ArgumentException(
                $"指定されたインターバル({interval}: {targetMinutes}分)はDBの基準インターバル({baseIntervalMinutes}分)より小さいため対応できません。");
        }

        if (targetMinutes % baseIntervalMinutes != 0)
        {
            throw new ArgumentException(
                $"指定されたインターバル({interval}: {targetMinutes}分)はDBの基準インターバル({baseIntervalMinutes}分)の倍数である必要があります。");
        }

        // ベース足のデータを取得
        var baseData = await _repository.GetOhlcvDataBySymbolAsync(symbol, startDate, endDate);

        if (baseData.Count == 0)
        {
            return new List<OhlcvData>();
        }

        // 指定されたインターバルに集計
        return AggregateOhlcvData(baseData, targetMinutes);
    }

    /// <summary>
    /// DBに保存されているベースインターバル（分）を取得する
    /// 連続する2つのデータの時間差から推測する
    /// </summary>
    public async Task<int> GetBaseIntervalMinutesAsync(string symbol)
    {
        // キャッシュにあればそれを返す
        if (_baseIntervalCache.TryGetValue(symbol, out var cachedInterval))
        {
            return cachedInterval;
        }

        // DBから最新2件を取得して時間差を計算
        var latestData = await _repository.GetLatestOhlcvDataBySymbolAsync(symbol, 10);

        if (latestData.Count < 2)
        {
            return 0; // データが不足
        }

        // 連続するデータ間の時間差を計算し、最小値をベースインターバルとする
        var intervals = new List<int>();
        for (int i = 1; i < latestData.Count; i++)
        {
            var diff = (int)(latestData[i].TimestampUtc - latestData[i - 1].TimestampUtc).TotalMinutes;
            if (diff > 0)
            {
                intervals.Add(diff);
            }
        }

        if (intervals.Count == 0)
        {
            return 0;
        }

        // 最頻値を使用（欠損データがあっても正しく検出するため）
        var baseIntervalMinutes = intervals
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;

        // キャッシュに保存
        _baseIntervalCache[symbol] = baseIntervalMinutes;

        return baseIntervalMinutes;
    }

    /// <summary>
    /// ベースインターバルのキャッシュをクリアする
    /// </summary>
    public void ClearBaseIntervalCache()
    {
        _baseIntervalCache.Clear();
    }

    /// <summary>
    /// OHLCVデータを指定されたインターバル（分）に集計する
    /// </summary>
    private List<OhlcvData> AggregateOhlcvData(List<OhlcvData> baseData, int targetMinutes)
    {
        if (baseData.Count == 0) return new List<OhlcvData>();

        var aggregatedList = new List<OhlcvData>();

        // タイムスタンプを目標インターバルの開始時刻でグルーピング
        foreach (var group in baseData.GroupBy(d => GetIntervalStartTime(d.TimestampUtc, targetMinutes)))
        {
            var candles = group.OrderBy(c => c.TimestampUtc).ToList();

            if (candles.Count == 0) continue;

            var aggregated = new OhlcvData
            {
                Id = candles.First().Id,
                CryptocurrencyId = candles.First().CryptocurrencyId,
                TimestampUtc = group.Key,
                OpenPrice = candles.First().OpenPrice,
                HighPrice = candles.Max(c => c.HighPrice),
                LowPrice = candles.Min(c => c.LowPrice),
                ClosePrice = candles.Last().ClosePrice,
                Volume = candles.Sum(c => c.Volume),
                CreatedAt = candles.Last().CreatedAt
            };

            aggregatedList.Add(aggregated);
        }

        return aggregatedList.OrderBy(d => d.TimestampUtc).ToList();
    }

    /// <summary>
    /// タイムスタンプをインターバルの開始時刻に揃える
    /// </summary>
    private DateTime GetIntervalStartTime(DateTime timestamp, int intervalMinutes)
    {
        // 日をまたぐインターバル（1日以上）の場合
        if (intervalMinutes >= 1440) // 1日
        {
            var days = intervalMinutes / 1440;
            var daysSinceEpoch = (timestamp.Date - DateTime.UnixEpoch.Date).Days;
            var intervalStart = (daysSinceEpoch / days) * days;
            return DateTime.UnixEpoch.Date.AddDays(intervalStart);
        }

        // 時間内のインターバル
        var totalMinutes = (int)(timestamp - timestamp.Date).TotalMinutes;
        var intervalStartMinutes = (totalMinutes / intervalMinutes) * intervalMinutes;
        return timestamp.Date.AddMinutes(intervalStartMinutes);
    }

    /// <summary>
    /// 指定されたタイムスタンプの足が不完全（まだ確定していない）かどうかを判定する
    /// </summary>
    /// <param name="candleTimestamp">足のタイムスタンプ（開始時刻）</param>
    /// <param name="intervalMinutes">インターバル（分）</param>
    /// <returns>不完全な足の場合はtrue</returns>
    private bool IsIncompleteCandle(DateTime candleTimestamp, int intervalMinutes)
    {
        var now = DateTime.UtcNow;
        var candleEndTime = candleTimestamp.AddMinutes(intervalMinutes);

        // 足の終了時刻がまだ来ていない場合は不完全
        return candleEndTime > now;
    }

    /// <summary>
    /// KlineIntervalを分数に変換する
    /// </summary>
    public static int GetIntervalMinutes(KlineInterval interval)
    {
        return interval switch
        {
            KlineInterval.OneMinute => 1,
            KlineInterval.ThreeMinutes => 3,
            KlineInterval.FiveMinutes => 5,
            KlineInterval.FifteenMinutes => 15,
            KlineInterval.ThirtyMinutes => 30,
            KlineInterval.OneHour => 60,
            KlineInterval.TwoHours => 120,
            KlineInterval.FourHours => 240,
            KlineInterval.EightHours => 480,
            KlineInterval.TwelveHours => 720,
            KlineInterval.OneDay => 1440,
            KlineInterval.ThreeDays => 4320,
            KlineInterval.OneWeek => 10080,
            KlineInterval.OneMonth => 43200, // 30日として計算
            _ => throw new ArgumentException($"サポートされていないインターバル: {interval}")
        };
    }
}
