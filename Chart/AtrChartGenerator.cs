using ScottPlot;
using Skender.Stock.Indicators;

namespace Chart;

/// <summary>
/// ATR Trailing Stopの結果をグラフ画像として出力するクラス
/// </summary>
public static class AtrChartGenerator
{
    /// <summary>
    /// ATR Trailing Stopの結果をグラフ画像として保存します
    /// </summary>
    /// <param name="ohlcvData">OHLCVデータ</param>
    /// <param name="atrResults">ATR Trailing Stopの結果</param>
    /// <param name="symbol">通貨シンボル</param>
    /// <param name="outputPath">出力ファイルパス</param>
    /// <param name="displayDays">表示する日数（nullで全期間）</param>
    /// <param name="width">画像の幅</param>
    /// <param name="height">画像の高さ</param>
    public static void SaveAtrStopChart(
        IEnumerable<OhlcvData> ohlcvData,
        IEnumerable<AtrStopResult> atrResults,
        string symbol,
        string outputPath = "atr_trailing_stop.png",
        int? displayDays = null,
        int width = 1200,
        int height = 600)
    {
        var plot = new Plot();

        // データを配列に変換
        var ohlcvList = ohlcvData.ToList();
        var atrList = atrResults.ToList();

        // 表示範囲を絞る（計算済みデータから表示分だけ取得）
        if (displayDays.HasValue)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-displayDays.Value);
            var startIndex = ohlcvList.FindIndex(d => d.TimestampUtc >= cutoffDate);
            if (startIndex > 0)
            {
                ohlcvList = ohlcvList.Skip(startIndex).ToList();
                atrList = atrList.Skip(startIndex).ToList();
            }
        }

        // X軸用の日付データ
        var dates = ohlcvList.Select(d => d.TimestampUtc.ToOADate()).ToArray();
        var closePrices = ohlcvList.Select(d => (double)d.ClosePrice).ToArray();

        // ATR Stop データ
        var atrStopValues = atrList.Select(r => r.AtrStop.HasValue ? (double)r.AtrStop.Value : double.NaN).ToArray();
        var buyStopValues = atrList.Select(r => r.BuyStop.HasValue ? (double)r.BuyStop.Value : double.NaN).ToArray();
        var sellStopValues = atrList.Select(r => r.SellStop.HasValue ? (double)r.SellStop.Value : double.NaN).ToArray();

        // 終値のラインプロット
        var closeLine = plot.Add.Scatter(dates, closePrices);
        closeLine.LegendText = "Close Price";
        closeLine.Color = Colors.Blue;
        closeLine.LineWidth = 1.5f;
        closeLine.MarkerSize = 0;  // ラインのみ表示

        // ATR Stop のラインプロット（値がある部分のみ）
        if (atrStopValues.Any(v => !double.IsNaN(v)))
        {
            var atrStopLine = plot.Add.Scatter(dates, atrStopValues);
            atrStopLine.LegendText = "ATR Stop";
            atrStopLine.Color = Colors.Orange;
            atrStopLine.LineWidth = 2f;
            atrStopLine.MarkerSize = 0;
        }

        // Buy Stop のラインプロット（緑色）
        if (buyStopValues.Any(v => !double.IsNaN(v)))
        {
            var buyStopLine = plot.Add.Scatter(dates, buyStopValues);
            buyStopLine.LegendText = "Buy Stop";
            buyStopLine.Color = Colors.Green;
            buyStopLine.LineWidth = 1.5f;
            buyStopLine.LinePattern = LinePattern.Dashed;
            buyStopLine.MarkerSize = 0;
        }

        // Sell Stop のラインプロット（赤色）
        if (sellStopValues.Any(v => !double.IsNaN(v)))
        {
            var sellStopLine = plot.Add.Scatter(dates, sellStopValues);
            sellStopLine.LegendText = "Sell Stop";
            sellStopLine.Color = Colors.Red;
            sellStopLine.LineWidth = 1.5f;
            sellStopLine.LinePattern = LinePattern.Dashed;
            sellStopLine.MarkerSize = 0;
        }

        // グラフの設定
        plot.Title($"{symbol} ATR Trailing Stop");
        plot.XLabel("Date");
        plot.YLabel("Price");

        // X軸を日付形式で表示
        plot.Axes.DateTimeTicksBottom();

        // 凡例を右上に表示
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        plot.Legend.OutlineColor = Colors.Black;
        plot.Legend.BackgroundColor = Colors.White.WithAlpha(0.9);

        // 画像として保存
        plot.SavePng(outputPath, width, height);

        Console.WriteLine($"グラフを保存しました: {outputPath}");
    }

    /// <summary>
    /// ローソク足チャートとATR Trailing Stopを組み合わせたグラフを保存します
    /// </summary>
    public static void SaveCandlestickWithAtrStop(
        IEnumerable<OhlcvData> ohlcvData,
        IEnumerable<AtrStopResult> atrResults,
        string symbol,
        string outputPath = "candlestick_atr_stop.png",
        int? displayDays = null,
        int width = 1400,
        int height = 700)
    {
        var plot = new Plot();

        // データを配列に変換
        var ohlcvList = ohlcvData.ToList();
        var atrList = atrResults.ToList();

        // 表示範囲を絞る（計算済みデータから表示分だけ取得）
        if (displayDays.HasValue)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-displayDays.Value);
            var startIndex = ohlcvList.FindIndex(d => d.TimestampUtc >= cutoffDate);
            if (startIndex > 0)
            {
                ohlcvList = ohlcvList.Skip(startIndex).ToList();
                atrList = atrList.Skip(startIndex).ToList();
            }
        }

        // ローソク足データを作成
        var ohlcs = ohlcvList.Select(d => new OHLC(
            (double)d.OpenPrice,
            (double)d.HighPrice,
            (double)d.LowPrice,
            (double)d.ClosePrice,
            d.TimestampUtc,
            TimeSpan.FromMinutes(30)  // 30分足
        )).ToList();

        // ローソク足を追加
        var candlestick = plot.Add.Candlestick(ohlcs);
        candlestick.Sequential = false;  // 日付ベースで表示

        // X軸用の日付データ
        var dates = ohlcvList.Select(d => d.TimestampUtc.ToOADate()).ToArray();

        // ATR Stop データ
        var atrStopValues = atrList.Select(r => r.AtrStop.HasValue ? (double)r.AtrStop.Value : double.NaN).ToArray();

        // ATR Stop のラインプロット
        if (atrStopValues.Any(v => !double.IsNaN(v)))
        {
            var atrStopLine = plot.Add.Scatter(dates, atrStopValues);
            atrStopLine.LegendText = "ATR Stop";
            atrStopLine.Color = Colors.Orange;
            atrStopLine.LineWidth = 2.5f;
            atrStopLine.MarkerSize = 0;
        }

        // グラフの設定
        plot.Title($"{symbol} Candlestick with ATR Trailing Stop");
        plot.XLabel("Date");
        plot.YLabel("Price");

        // X軸を日付形式で表示
        plot.Axes.DateTimeTicksBottom();

        // 凡例を右上に表示
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        plot.Legend.OutlineColor = Colors.Black;
        plot.Legend.BackgroundColor = Colors.White.WithAlpha(0.9);

        // 画像として保存
        plot.SavePng(outputPath, width, height);

        Console.WriteLine($"ローソク足チャートを保存しました: {outputPath}");
    }
}
