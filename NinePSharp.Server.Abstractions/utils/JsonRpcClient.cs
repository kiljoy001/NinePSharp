using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Server.Utils;

public class JsonRpcClient
{
    private readonly HttpClient _httpClient;
    private static int _idCounter;

    public JsonRpcClient(HttpClient httpClient, string? url = null, string? user = null, string? password = null)
    {
        _httpClient = httpClient;
        if (url != null) _httpClient.BaseAddress = new Uri(url);
        
        if (!string.IsNullOrEmpty(user))
        {
            var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        }
    }

    public async Task<JsonNode?> CallAsync(string method, object?[]? args = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _idCounter);
        var request = new
        {
            jsonrpc = "2.0",
            method,
            @params = args ?? Array.Empty<object?>(),
            id
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(string.Empty, content, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"JSON-RPC request failed with status {response.StatusCode}: {error}");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonNode.Parse(body);
        
        if (doc == null || doc["result"] == null && doc["error"] != null)
        {
            var error = doc?["error"]?.ToString() ?? "Unknown error";
            throw new Exception($"JSON-RPC error: {error}");
        }

        return doc["result"];
    }
}
