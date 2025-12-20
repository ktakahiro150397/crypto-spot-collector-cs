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

    public async Task<Cryptocurrency?> GetCryptocurrencyByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            "SELECT id, symbol, name, created_at, updated_at FROM cryptocurrencies WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("@id", id);

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
    /// OHLCVデータを追加または更新する
    /// </summary>
    public async Task AddOrUpdateOhlcvDataAsync(string symbol, List<OhlcvData> ohlcvData)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Cryptocurrenciesテーブルに存在するか確認
        var currency = await GetCryptocurrencyBySymbolAsync(symbol: symbol);
        if (currency == null)
        {
            // 存在しない場合は新規追加
            await using (var insertCurrencyCmd = new MySqlCommand(
                "INSERT INTO cryptocurrencies (symbol, name, created_at, updated_at) VALUES (@symbol, @name, NOW(), NOW())",
                connection))
            {
                insertCurrencyCmd.Parameters.AddWithValue("@symbol", symbol);
                insertCurrencyCmd.Parameters.AddWithValue("@name", symbol); // 名前が不明なためシンボルを使用
                await insertCurrencyCmd.ExecuteNonQueryAsync();
                currency = await GetCryptocurrencyBySymbolAsync(symbol: symbol);
            }
        }

        if (currency == null)
        {
            throw new InvalidOperationException("Failed to retrieve or create cryptocurrency record.");
        }

        // OHLCVデータを挿入または更新
        foreach (var data in ohlcvData)
        {
            await using var command = new MySqlCommand(
                @"INSERT INTO ohlcv_data (cryptocurrency_id, open_price, high_price, low_price, close_price, volume, timestamp_utc, created_at)
                  VALUES (@cryptocurrencyId, @openPrice, @highPrice, @lowPrice, @closePrice, @volume, @timestampUtc, NOW())
                  ON DUPLICATE KEY UPDATE
                      open_price = VALUES(open_price),
                      high_price = VALUES(high_price),
                      low_price = VALUES(low_price),
                      close_price = VALUES(close_price),
                      volume = VALUES(volume)",
                connection);

            command.Parameters.AddWithValue("@cryptocurrencyId", currency.Id);
            command.Parameters.AddWithValue("@openPrice", data.OpenPrice);
            command.Parameters.AddWithValue("@highPrice", data.HighPrice);
            command.Parameters.AddWithValue("@lowPrice", data.LowPrice);
            command.Parameters.AddWithValue("@closePrice", data.ClosePrice);
            command.Parameters.AddWithValue("@volume", data.Volume);
            command.Parameters.AddWithValue("@timestampUtc", data.TimestampUtc);

            await command.ExecuteNonQueryAsync();
        }

    }
}
