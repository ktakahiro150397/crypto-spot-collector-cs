using Serilog;
using Serilog.Events;

namespace Logging;

/// <summary>
/// アプリケーション全体のログ設定を管理するクラス
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// ログの初期化を行う（設定ファイルから）
    /// </summary>
    /// <param name="loggingConfig">ログ設定</param>
    public static void Initialize(LoggingConfig loggingConfig)
    {
        var logDirectory = loggingConfig.LogDirectory;
        var minimumLevel = loggingConfig.GetMinimumLevel();
        var retainedFileCountLimit = loggingConfig.RetainedFileCountLimit;

        // ログディレクトリが存在しない場合は作成
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(logDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: retainedFileCountLimit,
                shared: true)  // 複数プロセスからの書き込みを許可
            .CreateLogger();

        Log.Information("ログシステムを初期化しました。出力先: {LogDirectory}, レベル: {Level}", logDirectory, minimumLevel);
    }

    /// <summary>
    /// ログの初期化を行う（レガシー: 直接パラメータ指定）
    /// </summary>
    /// <param name="logDirectory">ログファイルの出力先ディレクトリ（デフォルト: logs）</param>
    /// <param name="minimumLevel">最小ログレベル（デフォルト: Information）</param>
    public static void Initialize(
        string logDirectory = "logs",
        LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var loggingConfig = new LoggingConfig
        {
            LogDirectory = logDirectory,
            MinimumLevel = minimumLevel.ToString()
        };
        Initialize(loggingConfig);
    }

    /// <summary>
    /// ログシステムをシャットダウンする
    /// アプリケーション終了時に必ず呼び出すこと
    /// </summary>
    public static void Shutdown()
    {
        Log.Information("アプリケーションを終了します");
        Log.CloseAndFlush();
    }
}
