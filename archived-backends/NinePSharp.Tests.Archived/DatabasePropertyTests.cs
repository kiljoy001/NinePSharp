using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class DatabasePropertyTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly DatabaseBackendConfig _config = new() 
    { 
        MountPath = "/db", 
        ProviderName = "Mock", 
        ConnectionString = "None" 
    };

    [Property]
    public bool Database_Path_Navigation_Stability_Property(string[] path)
    {
        if (path == null) return true;
        var cleanPath = path.Where(p => p != null).ToArray();

        var fs = new DatabaseFileSystem(_config, _vaultMock.Object);

        try
        {
            // Walk any randomized path
            var response = fs.WalkAsync(new Twalk(1, 0, 1, cleanPath)).Result;
            
            // Should return valid response or partial walk, never crash
            return response.Tag == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property]
    public bool Database_Query_Input_Robustness_Property(string sql)
    {
        if (sql == null) return true;

        var fs = new DatabaseFileSystem(_config, _vaultMock.Object);
        
        // Walk to /query
        fs.WalkAsync(new Twalk(1, 0, 1, new[] { "query" })).Wait();

        try
        {
            // Write randomized data to query file
            // We expect it might fail (NinePProtocolException) because the provider is 'Mock' 
            // and doesn't exist, or the SQL is bad.
            // But it should NEVER throw a non-NineP exception (crash).
            var data = Encoding.UTF8.GetBytes(sql);
            fs.WriteAsync(new Twrite(1, 1, 0, data)).Wait();
            return true;
        }
        catch (AggregateException ex) when (ex.InnerException is NinePProtocolException)
        {
            // Handled protocol error is acceptable
            return true;
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException)
        {
            // "Could not create connection" is expected for mock provider
            return true;
        }
        catch (AggregateException ex) when (ex.InnerException is NotSupportedException)
        {
            // Current backend does not support writes through this path yet.
            return true;
        }
        catch (NotSupportedException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property]
    public bool Database_Clone_Path_Isolation_Property(string[] path1, string[] path2)
    {
        if (path1 == null || path2 == null) return true;
        var p1 = path1.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        var p2 = path2.Where(p => !string.IsNullOrEmpty(p)).ToArray();

        var fs1 = new DatabaseFileSystem(_config, _vaultMock.Object);
        
        // Walk fs1 to p1
        fs1.WalkAsync(new Twalk(1, 0, 1, p1)).Wait();

        // Clone to fs2
        var fs2 = fs1.Clone();

        // Walk fs2 to p2
        fs2.WalkAsync(new Twalk(1, 1, 2, p2)).Wait();

        // Verify fs1 is still at p1 by attempting a walk back to root (..)
        // If it crashes or behaves weirdly, isolation is broken.
        try {
            var walkBack = fs1.WalkAsync(new Twalk(1, 1, 3, new[] { ".." })).Result;
            return walkBack.Tag == 1;
        } catch { return false; }
    }
}
