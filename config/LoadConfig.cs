using System.Text.Json;
using System.Text.Json.Serialization;

public class Config
{
    public WalletAddress WalletAddress { get; set; } = new();
    public string ConnectionString { get; set; } = string.Empty;
    public bool IsTestnet { get; set; }

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