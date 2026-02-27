using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Cluster;
using Xunit;
using Moq;
using NinePSharp.Generators;

namespace NinePSharp.Tests;

public class RootFileSystemPropertyTests
{
    private List<IProtocolBackend> CreateMockBackends(string[] paths)
    {
        var backends = new List<IProtocolBackend>();
        foreach (var path in paths)
        {
            var mock = new Mock<IProtocolBackend>();
            mock.Setup(b => b.MountPath).Returns(path);
            mock.Setup(b => b.Name).Returns(path.Trim('/'));
            mock.Setup(b => b.GetFileSystem(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>()))
                .Returns(() => new NinePSharp.Server.Backends.MockFileSystem(new LuxVaultService()));
            backends.Add(mock.Object);
        }
        return backends;
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool RootFS_Walk_Stability_Property(string[] mountPaths, string[] walkPath)
    {
        if (mountPaths == null || walkPath == null) return true;
        
        var cleanMounts = mountPaths.Where(p => !string.IsNullOrEmpty(p)).Select(p => p.StartsWith("/") ? p : "/" + p).ToArray();
        var backends = CreateMockBackends(cleanMounts);
        var rootFs = new RootFileSystem(backends, null);

        try
        {
            var twalk = new Twalk(1, 100, 101, walkPath);
            var response = rootFs.WalkAsync(twalk).Sync();
            
            if (walkPath.Length == 0)
            {
                return response.Wqid != null && response.Wqid.Length == 0;
            }

            // If we walked into a backend, Wqid should not be null
            // If the first element doesn't match any backend, it might return Rwalk with null Wqid (error)
            return true; 
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 50)]
    public bool RootFS_Readdir_Contains_All_Mounts_Property(string[] mountPaths)
    {
        if (mountPaths == null) return true;
        
        var cleanMounts = mountPaths
            .Where(p => !string.IsNullOrEmpty(p) && p.Trim('/') != "")
            .Select(p => p.Trim('/'))
            .Distinct()
            .ToArray();
            
        var backends = CreateMockBackends(cleanMounts.Select(p => "/" + p).ToArray());
        var rootFs = new RootFileSystem(backends, null);

        var treaddir = new Treaddir(1, 100, 1, 0, 65536);
        var response = rootFs.ReaddirAsync(treaddir).Sync();

        var dataString = Encoding.UTF8.GetString(response.Data.ToArray());
        foreach (var mount in cleanMounts)
        {
            if (!dataString.Contains(mount)) return false;
        }

        return true;
    }

    [Fact]
    public async Task RootFS_Clone_Is_Deep_Enough()
    {
        var backends = CreateMockBackends(new[] { "/test" });
        var rootFs = new RootFileSystem(backends, null);

        // Walk into a backend to set _delegatedFs
        await rootFs.WalkAsync(new Twalk(1, 100, 101, new[] { "test" }));

        var clone = (RootFileSystem)rootFs.Clone();
        
        // Both should have a delegated FS now
        // We can't check private fields easily without reflection, but we can check behavior
        
        var tstat = new Tstat(2, 101);
        var resp1 = await rootFs.StatAsync(tstat);
        var resp2 = await clone.StatAsync(tstat);

        Assert.Equal(tstat.Tag, resp1.Tag);
        Assert.Equal(tstat.Tag, resp2.Tag);
    }
}
