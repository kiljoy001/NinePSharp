using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Backends.Compute;
using NinePSharp.Messages;
using NinePSharp.Server.Configuration.Models;
using Xunit;

namespace NinePSharp.Backends.Compute.Tests;

public class ComputeFileSystemTests
{
    private readonly ComputeBackendConfig _config = new() { MountPath = "/compute" };

    [Fact]
    public async Task Walk_To_Status_Succeeds()
    {
        var fs = new ComputeFileSystem(_config);
        var twalk = new Twalk(1, 1, 2, new[] { "status" });
        var res = await fs.WalkAsync(twalk);

        Assert.Single(res.Wqid);
        Assert.Equal(0, (int)res.Wqid[0].Type & 0x80); // Should not be a directory
    }

    [Fact]
    public async Task Walk_To_Jobs_Is_Directory()
    {
        var fs = new ComputeFileSystem(_config);
        var twalk = new Twalk(1, 1, 2, new[] { "jobs" });
        var res = await fs.WalkAsync(twalk);

        Assert.Single(res.Wqid);
        Assert.NotEqual(0, (int)res.Wqid[0].Type & 0x80); // Should be a directory
    }

    [Property]
    public bool Arbitrary_Walks_Never_Throw(string[] path)
    {
        if (path == null) return true;
        // Clean the path of nulls or empty strings which might be problematic for logic but shouldn't throw
        var cleanPath = path.Select(s => s ?? "null").ToArray();

        var fs = new ComputeFileSystem(_config);
        try
        {
            var twalk = new Twalk(1, 1, 2, cleanPath);
            var task = fs.WalkAsync(twalk);
            task.Wait();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task Wasm_Job_Execution_Succeeds()
    {
        var fs = new ComputeFileSystem(_config);
        
        // 1. Create a job
        var jobsFs = (ComputeFileSystem)fs.Clone();
        await jobsFs.WalkAsync(new Twalk(1, 1, 2, new[] { "jobs" }));
        await jobsFs.MkdirAsync(new Tmkdir(0, 1, 1, "test-job", 0755, 0));

        // 2. Walk into the job
        var testJobFs = (ComputeFileSystem)jobsFs.Clone();
        await testJobFs.WalkAsync(new Twalk(1, 2, 3, new[] { "test-job" }));

        // 3. Upload code.wasm
        byte[] tinyWasm = {
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00, 0x01, 0x04, 0x01, 0x60,
            0x00, 0x00, 0x03, 0x02, 0x01, 0x00, 0x07, 0x0a, 0x01, 0x06, 0x5f, 0x73,
            0x74, 0x61, 0x72, 0x74, 0x00, 0x00, 0x0a, 0x04, 0x01, 0x02, 0x00, 0x0b
        };

        var codeFs = (ComputeFileSystem)testJobFs.Clone();
        await codeFs.WalkAsync(new Twalk(1, 3, 4, new[] { "code.wasm" }));
        await codeFs.WriteAsync(new Twrite(1, 4, 0, tinyWasm));

        // 4. Wait for completion
        int retries = 0;
        string status = "";
        while (retries < 20) {
            var statusFs = (ComputeFileSystem)testJobFs.Clone();
            await statusFs.WalkAsync(new Twalk(1, 3, 5, new[] { "status" }));
            var res = await statusFs.ReadAsync(new Tread(1, 5, 0, 100));
            status = Encoding.UTF8.GetString(res.Data.ToArray()).Trim();
            if (status == "Completed" || status == "Failed") break;
            await Task.Delay(100);
            retries++;
        }

        Assert.Equal("Completed", status);
    }
}
