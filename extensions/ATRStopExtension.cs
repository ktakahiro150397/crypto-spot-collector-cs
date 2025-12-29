using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Skender.Stock.Indicators;

namespace Extensions
{
    /// <summary>
    /// ATR Trailing Stop 結果の拡張メソッド
    /// </summary>
    public static class ATRStopExtension
    {
        /// <summary>
        /// ATR Stop の結果をタブ区切りの行に変換して返します。
        /// ヘッダー付き（Date, AtrStop, BuyStop, SellStop）
        /// </summary>
        /// <param name="results">変換する結果</param>
        /// <param name="includeHeader">ヘッダーを含めるかどうか</param>
        /// <returns>行の列挙</returns>
        public static IEnumerable<string> ToTabSeparatedLines(this IEnumerable<AtrStopResult> results, bool includeHeader = true)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            if (includeHeader)
                yield return "Date\tAtrStop\tBuyStop\tSellStop";

            foreach (var r in results)
            {
                // 日付は ISO 8601 ラウンドトリップ形式
                var date = r.Date.ToString("o", CultureInfo.InvariantCulture);

                // 値が null の場合は空文字にする
                var atr = r.AtrStop.HasValue ? r.AtrStop.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                var buy = r.BuyStop.HasValue ? r.BuyStop.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                var sell = r.SellStop.HasValue ? r.SellStop.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

                yield return string.Join('\t', date, atr, buy, sell);
            }
        }

        /// <summary>
        /// ATR Stop の結果をタブ区切りでファイルに保存します。
        /// </summary>
        /// <param name="results">保存する結果</param>
        /// <param name="filePath">出力ファイルパス</param>
        /// <param name="includeHeader">ヘッダーを含めるかどうか</param>
        /// <param name="encoding">ファイルのエンコーディング（null の場合 UTF-8）</param>
        public static void SaveAsTsv(this IEnumerable<AtrStopResult> results, string filePath, bool includeHeader = true, Encoding? encoding = null)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is required", nameof(filePath));

            encoding ??= Encoding.UTF8;

            var lines = results.ToTabSeparatedLines(includeHeader).ToList();
            File.WriteAllLines(filePath, lines, encoding);
        }
    }
}

