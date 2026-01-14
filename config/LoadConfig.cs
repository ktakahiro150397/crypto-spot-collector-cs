using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog.Events;

public class Config
{
    public WalletAddress WalletAddress { get; set; } = new();
    public string ConnectionString { get; set; } = string.Empty;
    public string DiscordNotificationUrl { get; set; } = string.Empty;
    public bool IsTestnet { get; set; }
    public LoggingConfig Logging { get; set; } = new();
    public BotConfig Bot { get; set; } = new();

    private static Config? _instance;

    public static Config Instance => _instance ?? throw new InvalidOperationException("Config not loaded. Call LoadSettings first.");

    public static Config LoadSettings(string filePath = "secrets.json")
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Config file not found: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _instance = JsonSerializer.Deserialize<Config>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize config.");

        return _instance;
    }
}

public class WalletAddress
{
    public string MainWalletAddress { get; set; } = string.Empty;
    public string APIWalletAddress { get; set; } = string.Empty;
    public string APIWalletAddressKey { get; set; } = string.Empty;
}

public class LoggingConfig
{
    public string LogDirectory { get; set; } = "logs";
    public string MinimumLevel { get; set; } = "Information";
    public int RetainedFileCountLimit { get; set; } = 30;

    public LogEventLevel GetMinimumLevel()
    {
        return MinimumLevel.ToLower() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }
}

public class BotConfig
{
    public List<string> Symbols { get; set; } = new();
    public string Timeframe { get; set; } = "30m";
    public int PositionSizeUSDC { get; set; } = 200;
    public int PositionUpdateIntervalMinutes { get; set; } = 15;
    public int StrategyCheckIntervalMinutes { get; set; } = 30;
}