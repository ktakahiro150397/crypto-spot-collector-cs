// See https://aka.ms/new-console-template for more information
using CryptoExchange.Net.Authentication;
using HyperLiquid.Net;
using HyperLiquid.Net.Clients;
using HyperLiquid.Net.Enums;
using Logging;
using Serilog;
using System.CommandLine;
using System.CommandLine.Parsing;

// ログシステムの初期化
LoggingConfiguration.Initialize(minimumLevel: Serilog.Events.LogEventLevel.Debug);

try
{
    // 引数解析
    var modeOption = new Option<string>(
        name: "--mode"
    )
    {
        Description = "実行モードを指定します。'bot'または'backtest'。",
        DefaultValueFactory = (argResult) => ""
    };


    RootCommand rootCommand = new("HyperLiquid Trading Bot")
    {
        modeOption
    };

    rootCommand.SetAction(async (parseResult) =>
    {
        var mode = parseResult.GetValue(modeOption);
        Log.Debug($"実行モード: {mode}");

        switch (mode.ToLower())
        {
            case "bot":
                Log.Information("トレーディングボットモードで起動します");
                break;
            case "backtest":
                Log.Information("バックテストモードで起動します");
                var startDate = new DateTime(2025, 10, 1);
                var endDate = new DateTime(2025, 12, 24);
                var interval = KlineInterval.ThirtyMinutes;

                var programBackTest = new ProgramBackTest(
                    startDate,
                    endDate,
                    interval
                );
                await programBackTest.ProgramBackTestMain();
                break;
            case "test":
                Log.Information("テストモードで起動します");
                await ProgramTest.ProgamTestMain();
                break;
            default:
                Log.Warning("不明なモードが指定されました。");
                throw new ArgumentException("不明なモードが指定されました。'bot','backtest','test'を指定してください。");
        }
    });

    return rootCommand.Parse(args).Invoke();
}
catch (Exception ex)
{
    Log.Fatal(ex, "アプリケーションで致命的なエラーが発生しました");
    throw;
}
finally
{
    LoggingConfiguration.Shutdown();
}
