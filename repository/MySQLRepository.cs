using MySqlConnector;
using Serilog;

public class MySQLRepository
{
    private readonly string _connectionString;
    private static readonly ILogger _logger = Log.ForContext<MySQLRepository>();

    public MySQLRepository(string connectionString)
    {
        _connectionString = connectionString;
        _logger.Debug("MySQLRepositoryを初期化しました");
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
            _logger.Information("MySQL接続成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MySQL接続エラー");
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
        _logger.Debug("OHLCVデータの保存を開始します。シンボル: {Symbol}, 件数: {Count}", symbol, ohlcvData.Count);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Cryptocurrenciesテーブルに存在するか確認
        var currency = await GetCryptocurrencyBySymbolAsync(symbol: symbol);
        if (currency == null)
        {
            _logger.Information("新しいシンボルを登録します: {Symbol}", symbol);
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
            _logger.Error("暗号通貨レコードの取得または作成に失敗しました: {Symbol}", symbol);
            throw new InvalidOperationException("Failed to retrieve or create cryptocurrency record.");
        }

        // OHLCVデータをバルクインサート（重複時は更新）
        const int batchSize = 1000; // 1回のクエリで挿入する最大件数
        for (int i = 0; i < ohlcvData.Count; i += batchSize)
        {
            var batch = ohlcvData.Skip(i).Take(batchSize).ToList();

            var valuePlaceholders = new List<string>();
            await using var command = new MySqlCommand();
            command.Connection = connection;

            for (int j = 0; j < batch.Count; j++)
            {
                valuePlaceholders.Add($"(@cryptocurrencyId{j}, @openPrice{j}, @highPrice{j}, @lowPrice{j}, @closePrice{j}, @volume{j}, @timestampUtc{j}, NOW())");
                command.Parameters.AddWithValue($"@cryptocurrencyId{j}", currency.Id);
                command.Parameters.AddWithValue($"@openPrice{j}", batch[j].OpenPrice);
                command.Parameters.AddWithValue($"@highPrice{j}", batch[j].HighPrice);
                command.Parameters.AddWithValue($"@lowPrice{j}", batch[j].LowPrice);
                command.Parameters.AddWithValue($"@closePrice{j}", batch[j].ClosePrice);
                command.Parameters.AddWithValue($"@volume{j}", batch[j].Volume);
                command.Parameters.AddWithValue($"@timestampUtc{j}", batch[j].TimestampUtc);
            }

            command.CommandText = $@"INSERT INTO ohlcv_data (cryptocurrency_id, open_price, high_price, low_price, close_price, volume, timestamp_utc, created_at)
                  VALUES {string.Join(", ", valuePlaceholders)}
                  ON DUPLICATE KEY UPDATE
                      open_price = VALUES(open_price),
                      high_price = VALUES(high_price),
                      low_price = VALUES(low_price),
                      close_price = VALUES(close_price),
                      volume = VALUES(volume)";

            await command.ExecuteNonQueryAsync();
        }

        _logger.Information("OHLCVデータをバルクインサートしました。シンボル: {Symbol}, 件数: {Count}", symbol, ohlcvData.Count);
    }

    public async Task<List<OhlcvData>> GetOhlcvDataBySymbolAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        _logger.Debug("OHLCVデータを取得します。シンボル: {Symbol}, 期間: {StartDate} - {EndDate}", symbol, startDate, endDate);

        var ohlcvDataList = new List<OhlcvData>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var currency = await GetCryptocurrencyBySymbolAsync(symbol);
        if (currency == null)
        {
            _logger.Warning("指定されたシンボルが見つかりません: {Symbol}", symbol);
            return ohlcvDataList; // 空のリストを返す
        }

        await using var command = new MySqlCommand(
            @"SELECT id, cryptocurrency_id, open_price, high_price, low_price, close_price, volume, timestamp_utc, created_at
              FROM ohlcv_data
              WHERE cryptocurrency_id = @cryptocurrencyId AND timestamp_utc BETWEEN @startDate AND @endDate",
            connection);

        command.Parameters.AddWithValue("@cryptocurrencyId", currency.Id);
        command.Parameters.AddWithValue("@startDate", startDate);
        command.Parameters.AddWithValue("@endDate", endDate);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ohlcvDataList.Add(new OhlcvData
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

        return ohlcvDataList;
    }

    /// <summary>
    /// 指定シンボルの最新N件のOHLCVデータを取得する
    /// </summary>
    public async Task<List<OhlcvData>> GetLatestOhlcvDataBySymbolAsync(string symbol, int count)
    {
        _logger.Debug("最新OHLCVデータを取得します。シンボル: {Symbol}, 件数: {Count}", symbol, count);

        var ohlcvDataList = new List<OhlcvData>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var currency = await GetCryptocurrencyBySymbolAsync(symbol);
        if (currency == null)
        {
            _logger.Warning("指定されたシンボルが見つかりません: {Symbol}", symbol);
            return ohlcvDataList; // 空のリストを返す
        }

        await using var command = new MySqlCommand(
            @"SELECT id, cryptocurrency_id, open_price, high_price, low_price, close_price, volume, timestamp_utc, created_at
              FROM ohlcv_data
              WHERE cryptocurrency_id = @cryptocurrencyId
              ORDER BY timestamp_utc DESC
              LIMIT @count",
            connection);

        command.Parameters.AddWithValue("@cryptocurrencyId", currency.Id);
        command.Parameters.AddWithValue("@count", count);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ohlcvDataList.Add(new OhlcvData
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

        // 時系列順（古い順）に並び替えて返す
        ohlcvDataList.Reverse();
        return ohlcvDataList;
    }
}
