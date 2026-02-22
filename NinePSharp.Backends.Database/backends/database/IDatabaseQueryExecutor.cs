using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server.Backends;

internal interface IDatabaseQueryExecutor
{
    string Engine { get; }
    Task<string> ExecuteAsync(string query);
}

internal sealed class AdoNetQueryExecutor : IDatabaseQueryExecutor
{
    private readonly DatabaseBackendConfig _config;
    private readonly string _effectiveUsername;
    private readonly string _effectivePassword;

    public string Engine => $"sql:{_config.ProviderName}";

    public AdoNetQueryExecutor(DatabaseBackendConfig config, string? usernameOverride, string? passwordOverride)
    {
        _config = config;
        _effectiveUsername = !string.IsNullOrWhiteSpace(usernameOverride) ? usernameOverride : config.Username;
        _effectivePassword = !string.IsNullOrWhiteSpace(passwordOverride) ? passwordOverride : config.Password;
    }

    public async Task<string> ExecuteAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(_config.ProviderName))
        {
            throw new InvalidOperationException("ProviderName must be configured for SQL query execution.");
        }

        var factory = DbProviderFactories.GetFactory(_config.ProviderName);
        await using var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException($"Could not create connection for provider '{_config.ProviderName}'.");
        connection.ConnectionString = BuildConnectionString();

        if (connection is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync();
        }
        else
        {
            connection.Open();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = query;

        if (command is not DbCommand dbCommand)
        {
            throw new InvalidOperationException("Database command must derive from DbCommand.");
        }

        await using var reader = await dbCommand.ExecuteReaderAsync();
        if (reader.FieldCount == 0)
        {
            return JsonSerializer.Serialize(new { rowsAffected = reader.RecordsAffected });
        }

        var rows = new List<Dictionary<string, object?>>();
        int maxRows = _config.MaxRows <= 0 ? int.MaxValue : _config.MaxRows;
        while (await reader.ReadAsync() && rows.Count < maxRows)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private string BuildConnectionString()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = _config.ConnectionString ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(_effectiveUsername))
        {
            builder["User ID"] = _effectiveUsername;
        }

        if (!string.IsNullOrWhiteSpace(_effectivePassword))
        {
            builder["Password"] = _effectivePassword;
        }

        return builder.ConnectionString;
    }
}

internal sealed class NoSqlHttpQueryExecutor : IDatabaseQueryExecutor
{
    private readonly NoSqlHttpConfig _noSqlConfig;
    private readonly HttpClient _httpClient;
    private readonly string _effectiveUsername;
    private readonly string _effectivePassword;

    public string Engine => $"nosql:http:{_noSqlConfig.EndpointUrl}";

    public NoSqlHttpQueryExecutor(
        DatabaseBackendConfig config,
        string? usernameOverride,
        string? passwordOverride,
        HttpMessageHandler? handler = null)
    {
        _noSqlConfig = config.NoSql ?? throw new InvalidOperationException("NoSql config is required for HTTP NoSQL execution.");
        _effectiveUsername = !string.IsNullOrWhiteSpace(usernameOverride) ? usernameOverride : config.Username;
        _effectivePassword = !string.IsNullOrWhiteSpace(passwordOverride) ? passwordOverride : config.Password;

        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
    }

    public async Task<string> ExecuteAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(_noSqlConfig.EndpointUrl))
        {
            throw new InvalidOperationException("NoSql.EndpointUrl must be configured for NoSQL query execution.");
        }

        var method = new HttpMethod(string.IsNullOrWhiteSpace(_noSqlConfig.Method) ? "POST" : _noSqlConfig.Method.ToUpperInvariant());
        using var request = new HttpRequestMessage(method, _noSqlConfig.EndpointUrl);

        foreach (var header in _noSqlConfig.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!string.IsNullOrWhiteSpace(_effectiveUsername))
        {
            string token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_effectiveUsername}:{_effectivePassword}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (method != HttpMethod.Get)
        {
            string field = string.IsNullOrWhiteSpace(_noSqlConfig.QueryField) ? "query" : _noSqlConfig.QueryField;
            var body = new JsonObject { [field] = query };
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        string payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"NoSQL endpoint returned {(int)response.StatusCode}: {payload}");
        }

        return payload;
    }
}
