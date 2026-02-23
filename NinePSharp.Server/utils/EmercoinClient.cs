using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NinePSharp.Server.Utils;

public record EmercoinConfig
{
    public string EndpointUrl { get; set; } = "http://127.0.0.1:6662/";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public interface IEmercoinNvsClient
{
    Task<string?> GetNameValueAsync(string name);
}

public class EmercoinNvsClient : IEmercoinNvsClient
{
    private readonly HttpClient _httpClient;
    private readonly EmercoinConfig _config;
    private readonly ILogger<EmercoinNvsClient> _logger;

    public EmercoinNvsClient(HttpClient httpClient, IOptions<EmercoinConfig> config, ILogger<EmercoinNvsClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;

        if (!string.IsNullOrEmpty(_config.Username))
        {
            var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.Username}:{_config.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        }
    }

    public async Task<string?> GetNameValueAsync(string name)
    {
        try
        {
            var request = new
            {
                jsonrpc = "1.0",
                id = "ninepsharp",
                method = "name_show",
                @params = new[] { name }
            };

            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.EndpointUrl, content);
            
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind != JsonValueKind.Null)
            {
                return result.GetProperty("value").GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch name {Name} from Emercoin", name);
        }
        
        return null;
    }
}
