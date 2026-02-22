using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

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

public class DatabaseBackendConfig : BackendConfigBase
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty; // e.g., Npgsql, Microsoft.Data.Sqlite, System.Data.SqlClient
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? VaultKey { get; set; }
    public int MaxRows { get; set; } = 500;
    public bool AllowAdHocQuery { get; set; } = true;
    public List<DatabaseQueryConfig> Queries { get; set; } = new();
    public NoSqlHttpConfig? NoSql { get; set; }
}
