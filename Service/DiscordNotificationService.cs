

using System.Text.Json;

public class DiscordNotificationService
{
    private readonly string _webhookUrl;

    public DiscordNotificationService(string webhookUrl)
    {
        _webhookUrl = webhookUrl;
    }

    public async Task SendNotificationAsync(string message)
    {
        using var httpClient = new HttpClient();
        var payload = new
        {
            content = message
        };
        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(_webhookUrl, content);
        response.EnsureSuccessStatusCode();
    }
}