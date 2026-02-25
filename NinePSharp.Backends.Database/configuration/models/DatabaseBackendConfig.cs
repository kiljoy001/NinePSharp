using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Configuration for a specific database query exposed as a virtual file.
/// </summary>
public class DatabaseQueryConfig
{
    /// <summary>File name exposed in the mount (example: users.json).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SQL/NoSQL query executed when the file is read.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>If true, clients can write a replacement query into this file.</summary>
    public bool Writable { get; set; }

    /// <summary>Optional description for operators.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for HTTP-based NoSQL database connections.
/// </summary>
public class NoSqlHttpConfig
{
    /// <summary>HTTP endpoint that accepts query requests.</summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>HTTP method used for query requests (POST by default).</summary>
    public string Method { get; set; } = "POST";

    /// <summary>JSON field name used to send query text (default: query).</summary>
    public string QueryField { get; set; } = "query";

    /// <summary>Optional static headers sent on every query request.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();
}

/// <summary>
/// Configuration for the database backend.
/// </summary>
public class DatabaseBackendConfig : BackendConfigBase
{
    /// <summary>Gets or sets the connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;
    /// <summary>Gets or sets the provider name (e.g., Npgsql, Microsoft.Data.Sqlite).</summary>
    public string ProviderName { get; set; } = string.Empty;
    /// <summary>Gets or sets the username for authentication.</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>Gets or sets the password for authentication.</summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>Gets or sets the optional LuxVault key for decrypting credentials.</summary>
    public string? VaultKey { get; set; }
    /// <summary>Gets or sets the maximum number of rows returned per query.</summary>
    public int MaxRows { get; set; } = 500;
    /// <summary>Gets or sets a value indicating whether ad-hoc queries are allowed.</summary>
    public bool AllowAdHocQuery { get; set; } = true;
    /// <summary>Gets or sets the list of pre-configured queries.</summary>
    public List<DatabaseQueryConfig> Queries { get; set; } = new();
    /// <summary>Gets or sets the optional NoSQL HTTP configuration.</summary>
    public NoSqlHttpConfig? NoSql { get; set; }
}
