
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