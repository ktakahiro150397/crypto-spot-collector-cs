using MySqlConnector;

public class MySQLRepository
{
    private readonly string _connectionString;

    public MySQLRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// データベース接続をテストする
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            Console.WriteLine("MySQL接続成功！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MySQL接続エラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 全ての暗号通貨を取得する
    /// </summary>
    public async Task<List<Cryptocurrency>> GetAllCryptocurrenciesAsync()
    {
        var cryptocurrencies = new List<Cryptocurrency>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            "SELECT id, symbol, name, created_at, updated_at FROM cryptocurrencies",
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            cryptocurrencies.Add(new Cryptocurrency
            {
                Id = reader.GetInt32("id"),
                Symbol = reader.GetString("symbol"),
                Name = reader.GetString("name"),
                CreatedAt = reader.GetDateTime("created_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            });
        }

        return cryptocurrencies;
    }

    /// <summary>
    /// シンボルで暗号通貨を取得する
    /// </summary>
    public async Task<Cryptocurrency?> GetCryptocurrencyBySymbolAsync(string symbol)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            "SELECT id, symbol, name, created_at, updated_at FROM cryptocurrencies WHERE symbol = @symbol",
            connection);
        command.Parameters.AddWithValue("@symbol", symbol);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Cryptocurrency
            {
                Id = reader.GetInt32("id"),
                Symbol = reader.GetString("symbol"),
                Name = reader.GetString("name"),
                CreatedAt = reader.GetDateTime("created_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            };
        }

        return null;
    }

    /// <summary>
    /// OHLCVデータを取得する
    /// </summary>
    public async Task<List<OhlcvData>> GetOhlcvDataAsync(int cryptocurrencyId, DateTime? from = null, DateTime? to = null)
    {
        var ohlcvList = new List<OhlcvData>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT id, cryptocurrency_id, open_price, high_price, low_price, close_price, volume, timestamp_utc, created_at 
                    FROM ohlcv_data 
                    WHERE cryptocurrency_id = @cryptocurrencyId";

        if (from.HasValue)
            sql += " AND timestamp_utc >= @from";
        if (to.HasValue)
            sql += " AND timestamp_utc <= @to";

        sql += " ORDER BY timestamp_utc DESC";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@cryptocurrencyId", cryptocurrencyId);
        if (from.HasValue)
            command.Parameters.AddWithValue("@from", from.Value);
        if (to.HasValue)
            command.Parameters.AddWithValue("@to", to.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ohlcvList.Add(new OhlcvData
            {
                Id = reader.GetInt64("id"),
                CryptocurrencyId = reader.GetInt32("cryptocurrency_id"),
                OpenPrice = reader.GetDecimal("open_price"),
                HighPrice = reader.GetDecimal("high_price"),
                LowPrice = reader.GetDecimal("low_price"),
                ClosePrice = reader.GetDecimal("close_price"),
                Volume = reader.GetDecimal("volume"),
                TimestampUtc = reader.GetDateTime("timestamp_utc"),
                CreatedAt = reader.GetDateTime("created_at")
            });
        }

        return ohlcvList;
    }
}

/// <summary>
/// 暗号通貨エンティティ
/// </summary>
public class Cryptocurrency
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// OHLCVデータエンティティ
/// </summary>
public class OhlcvData
{
    public long Id { get; set; }
    public int CryptocurrencyId { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public decimal Volume { get; set; }
    public DateTime TimestampUtc { get; set; }
    public DateTime CreatedAt { get; set; }
}