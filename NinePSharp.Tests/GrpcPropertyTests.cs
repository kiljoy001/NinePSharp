using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.gRPC;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class GrpcPropertyTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<IGrpcTransport> _transportMock = new();
    private readonly GrpcBackendConfig _config = new() { Host = "localhost", Port = 50051 };

    [Property]
    public bool Metadata_Parsing_Is_Robust_Property(string input)
    {
        if (input == null) return true;

        var fs = new GrpcFileSystem(_config, _transportMock.Object, _vaultMock.Object);
        
        // Walk to .metadata
        fs.WalkAsync(new Twalk(1, 0, 1, new[] { ".metadata" })).Wait();

        try
        {
            // Write randomized data to metadata
            var data = Encoding.UTF8.GetBytes(input);
            fs.WriteAsync(new Twrite(1, 1, 0, data)).Wait();

            // Read it back to ensure no crash and basic consistency
            var read = fs.ReadAsync(new Tread(1, 1, 0, 8192)).Result;
            return true;
        }
        catch (Exception)
        {
            // We only fail if the parser itself throws an unhandled exception
            return false;
        }
    }

    [Property]
    public bool Grpc_Clone_Isolation_Property(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return true;
        // Strip colon and newline from key/value as they are delimiters
        key = key.Replace(":", "").Replace("\n", "").Replace("\r", "").Trim();
        value = value.Replace("\n", "").Replace("\r", "").Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return true;

        var fs1 = new GrpcFileSystem(_config, _transportMock.Object, _vaultMock.Object);
        
        // Set metadata in fs1
        fs1.WalkAsync(new Twalk(1, 0, 1, new[] { ".metadata" })).Wait();
        fs1.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes($"{key}: {value}"))).Wait();

        // Clone to fs2
        var fs2 = (GrpcFileSystem)fs1.Clone();

        // Mutate fs1 metadata
        fs1.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes($"{key}: mutated"))).Wait();

        // Read fs2 metadata - should remain the original value
        var read2 = fs2.ReadAsync(new Tread(1, 1, 0, 8192)).Result;
        var content2 = Encoding.UTF8.GetString(read2.Data.ToArray());

        return content2.Contains($"{key}: {value}") && !content2.Contains("mutated");
    }

    [Property]
    public bool Grpc_Path_Navigation_Stability_Property(string[] path)
    {
        if (path == null) return true;
        // Clean path segments to avoid nulls/empty
        var cleanPath = path.Where(p => p != null).ToArray();

        var fs = new GrpcFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        try
        {
            // Walk any randomized path
            var response = fs.WalkAsync(new Twalk(1, 0, 1, cleanPath)).Result;
            
            // Regardless of whether the path exists, it should return a valid Rwalk, never crash
            return response.Tag == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
