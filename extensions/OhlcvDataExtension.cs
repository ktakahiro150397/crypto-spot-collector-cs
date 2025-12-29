

using System.Globalization;
using System.Text;

namespace Extensions
{
    public static class OhlcvDataExtension
    {
        public static IEnumerable<string> ToTabSeparatedLines(this IEnumerable<OhlcvData> ohlcvData, bool includeHeader = true)
        {
            if (ohlcvData == null) throw new ArgumentNullException(nameof(ohlcvData));

            if (includeHeader)
                yield return "Date\tOpen\tHigh\tLow\tClose\tVolume";

            foreach (var data in ohlcvData)
            {
                // 日付は ISO 8601 ラウンドトリップ形式
                var date = data.Date.ToString("o", CultureInfo.InvariantCulture);

                // 値を文字列に変換
                var open = data.Open.ToString(CultureInfo.InvariantCulture);
                var high = data.High.ToString(CultureInfo.InvariantCulture);
                var low = data.Low.ToString(CultureInfo.InvariantCulture);
                var close = data.Close.ToString(CultureInfo.InvariantCulture);
                var volume = data.Volume.ToString(CultureInfo.InvariantCulture);

                yield return string.Join('\t', date, open, high, low, close, volume);
            }
        }

        public static void SaveAsTsv(this IEnumerable<OhlcvData> ohlcvData, string filePath, bool includeHeader = true, Encoding? encoding = null)
        {
            if (ohlcvData == null) throw new ArgumentNullException(nameof(ohlcvData));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is required", nameof(filePath));

            encoding ??= Encoding.UTF8;

            var lines = ohlcvData.ToTabSeparatedLines(includeHeader).ToList();
            File.WriteAllLines(filePath, lines, encoding);
        }
    }
}