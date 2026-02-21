using System;

namespace NinePSharp.Server.Configuration.Models;

public class DatabaseBackendConfig : BackendConfigBase
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty; // e.g., Npgsql, Microsoft.Data.Sqlite, System.Data.SqlClient
}
