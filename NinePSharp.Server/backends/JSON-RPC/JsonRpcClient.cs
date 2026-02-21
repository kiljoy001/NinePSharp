using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EdjCase.JsonRpc.Client;
using EdjCase.JsonRpc.Common;

namespace NinePSharp.Server.Backends.JsonRpc;

/// <summary>
/// General-purpose JSON-RPC 2.0 transport backed by EdjCase.JsonRpc.Client.
/// Works with any HTTP JSON-RPC server — not specific to any domain or technology.
/// </summary>
public class JsonRpcTransport : IJsonRpcTransport
{
    private readonly RpcClient _client;
    private static int _idCounter;

    public JsonRpcTransport(string url, string user, string password)
    {
        var builder = RpcClient.Builder(new Uri(url));

        if (!string.IsNullOrEmpty(user))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{password}"));
            builder = builder.UsingAuthHeader(new AuthenticationHeaderValue("Basic", encoded));
        }

        _client = builder.Build();
    }

    /// <summary>
    /// Call a JSON-RPC method and return the raw result as a <see cref="JsonNode"/>.
    /// Throws <see cref="InvalidOperationException"/> if the server returns an error.
    /// </summary>
    public async Task<JsonNode?> CallAsync(string method, object?[]? args = null) // implements IJsonRpcTransport
    {
        var id = new RpcId(Interlocked.Increment(ref _idCounter));

        RpcRequest request = args == null || args.Length == 0
            ? RpcRequest.WithNoParameters(method, id)
            : RpcRequest.WithParameterList(method, new List<object?>(args!), id);

        var response = await _client.SendAsync<JsonNode?>(request);

        if (response.HasError)
            throw new InvalidOperationException(
                $"JSON-RPC error {response.Error!.Code}: {response.Error.Message}");

        return response.Result;
    }
}
