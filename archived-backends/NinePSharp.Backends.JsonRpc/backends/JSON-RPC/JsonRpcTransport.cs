using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.JsonRpc;

/// <summary>
/// General-purpose JSON-RPC transport that accepts both JSON-RPC 1.0 and 2.0 envelopes.
/// </summary>
public class JsonRpcTransport : IJsonRpcTransport
{
    private readonly HttpClient _httpClient;
    private static int _idCounter;

    public JsonRpcTransport(string url, string user, string? password, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("JSON-RPC endpoint URL must be provided.", nameof(url));
        }

        var client = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: true);
        client.BaseAddress = new Uri(url, UriKind.Absolute);
        _httpClient = client;

        if (!string.IsNullOrEmpty(user))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{password ?? string.Empty}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    public JsonRpcTransport(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Call a JSON-RPC method and return the raw result as a <see cref="JsonNode"/>.
    /// </summary>
    public async Task<JsonNode?> CallAsync(string method, object?[]? args = null)
    {
        var id = Interlocked.Increment(ref _idCounter);
        object requestPayload = new
        {
            jsonrpc = "2.0",
            method,
            @params = args ?? Array.Empty<object?>(),
            id
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
        };

        using var httpResponse = await _httpClient.SendAsync(request);
        string responseBody = await httpResponse.Content.ReadAsStringAsync();

        if (httpResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"The server returned an invalid status code of '{httpResponse.StatusCode}'. Response content: {responseBody}");
        }

        JsonNode? envelope;
        try
        {
            envelope = JsonNode.Parse(responseBody);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unable to parse response from server: '{responseBody}'", ex);
        }

        if (envelope is not JsonObject responseObject)
        {
            throw new InvalidOperationException($"Unable to parse response from server: '{responseBody}'");
        }

        if (responseObject.TryGetPropertyValue("error", out JsonNode? errorNode) &&
            errorNode is not null &&
            errorNode.GetValueKind() != JsonValueKind.Null)
        {
            int code = -1;
            string message = "Unknown JSON-RPC error";
            if (errorNode is JsonObject errorObject)
            {
                if (errorObject.TryGetPropertyValue("code", out var codeNode) &&
                    codeNode is not null &&
                    codeNode.GetValueKind() == JsonValueKind.Number)
                {
                    if (codeNode is JsonValue codeValue)
                    {
                        _ = codeValue.TryGetValue<int>(out code);
                    }
                }

                if (errorObject.TryGetPropertyValue("message", out var messageNode) &&
                    messageNode is not null &&
                    messageNode.GetValueKind() == JsonValueKind.String)
                {
                    if (messageNode is JsonValue messageValue)
                    {
                        message = messageValue.GetValue<string>() ?? message;
                    }
                }
            }

            throw new InvalidOperationException($"JSON-RPC error {code}: {message}");
        }

        if (!responseObject.TryGetPropertyValue("result", out JsonNode? resultNode))
        {
            throw new InvalidOperationException($"Unable to parse response from server: '{responseBody}'");
        }

        return resultNode?.DeepClone();
    }
}
