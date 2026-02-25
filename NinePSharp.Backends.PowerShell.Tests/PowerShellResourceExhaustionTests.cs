using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Backends.PowerShell;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace NinePSharp.Backends.PowerShell.Tests;

public class PowerShellResourceExhaustionTests
{
    private readonly ITestOutputHelper _output;

    public PowerShellResourceExhaustionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Massive_Script_Output_Does_Not_Block_Server_Throughput()
    {
        INinePFileSystem fs = new PowerShellFileSystem();
        
        // 1. Create a "Generator" job that produces 50MB of text
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "jobs" }));
        await fs.MkdirAsync(new Tmkdir(0, 1, 1, "big-output", 0755, 0));
        
        var jobFs = (PowerShellFileSystem)fs.Clone();
        await jobFs.WalkAsync(new Twalk(1, 2, 3, new[] { "big-output" }));

        // Use a script that builds a massive string
        string script = "$data = 'A' * 10MB; 1..5 | % { $data }"; 
        var scriptFs = (PowerShellFileSystem)jobFs.Clone();
        await scriptFs.WalkAsync(new Twalk(1, 3, 4, new[] { "script.ps1" }));

        var sw = Stopwatch.StartNew();
        _output.WriteLine("Starting massive script execution...");
        await scriptFs.WriteAsync(new Twrite(1, 4, 0, Encoding.UTF8.GetBytes(script)));

        // 2. While the script is chugging (In-Process), try to perform a lightweight operation
        // on a DIFFERENT instance (simulating another client).
        var otherClientFs = fs.Clone();
        var baselineSw = Stopwatch.StartNew();
        
        // This SHOULD be near-instant (< 10ms)
        await otherClientFs.WalkAsync(new Twalk(2, 1, 4, new[] { "jobs" }));
        baselineSw.Stop();
        
        _output.WriteLine($"Concurrent 'Walk' took: {baselineSw.ElapsedMilliseconds}ms");

        // 3. Wait for completion (bounded to avoid hanging the test run forever)
        string status = "";
        const int maxPolls = 120;
        for (int poll = 0; poll < maxPolls; poll++)
        {
            var statusFs = (PowerShellFileSystem)jobFs.Clone();
            await statusFs.WalkAsync(new Twalk(1, 3, 5, new[] { "status" }));
            var res = await statusFs.ReadAsync(new Tread(1, 5, 0, 100));
            status = Encoding.UTF8.GetString(res.Data.ToArray()).Trim();
            if (status == "Completed" || status == "Failed")
            {
                break;
            }
            await Task.Delay(500);
        }

        if (status == "Failed")
        {
            var errorsFs = (PowerShellFileSystem)jobFs.Clone();
            await errorsFs.WalkAsync(new Twalk(1, 3, 6, new[] { "errors" }));
            var errors = await errorsFs.ReadAsync(new Tread(1, 6, 0, 8192));
            throw new Xunit.Sdk.XunitException($"PowerShell job failed: {Encoding.UTF8.GetString(errors.Data.ToArray())}");
        }

        Assert.Equal("Completed", status);
        _output.WriteLine($"Total execution time: {sw.ElapsedMilliseconds}ms");
        
        // If baselineSw took > 100ms, it means the massive script blocked the dispatcher/threadpool
        Assert.True(baselineSw.ElapsedMilliseconds < 500, $"Server was blocked for {baselineSw.ElapsedMilliseconds}ms by a background script!");
    }

    [Fact]
    public async Task Infinite_Loop_Job_Can_Be_Cancelled_Via_Remove()
    {
        INinePFileSystem fs = new PowerShellFileSystem();
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "jobs" }));
        await fs.MkdirAsync(new Tmkdir(0, 1, 1, "infinite-job", 0755, 0));

        var jobFs = (PowerShellFileSystem)fs.Clone();
        await jobFs.WalkAsync(new Twalk(1, 2, 3, new[] { "infinite-job" }));

        string script = "while($true) { Start-Sleep -Seconds 1 }";
        var scriptFs = (PowerShellFileSystem)jobFs.Clone();
        await scriptFs.WalkAsync(new Twalk(1, 3, 4, new[] { "script.ps1" }));

        _output.WriteLine("Starting infinite loop...");
        // This starts execution in a Task
        await scriptFs.WriteAsync(new Twrite(1, 4, 0, Encoding.UTF8.GetBytes(script)));

        // Give it a moment to enter the loop
        await Task.Delay(1000);

        // Verify it is running
        var statusFs = (PowerShellFileSystem)jobFs.Clone();
        await statusFs.WalkAsync(new Twalk(1, 3, 5, new[] { "status" }));
        var statusRes = await statusFs.ReadAsync(new Tread(1, 5, 0, 100));
        Assert.Equal("Running", Encoding.UTF8.GetString(statusRes.Data.ToArray()).Trim());

        // NOW: The Plan 9 way to cancel a process is to remove its directory/control file
        _output.WriteLine("Attempting to cancel job via Tremove...");
        await jobFs.RemoveAsync(new Tremove(1, 7));

        // Verify it's gone
        INinePFileSystem verifierFs = new PowerShellFileSystem();
        await verifierFs.WalkAsync(new Twalk(1, 7, 8, new[] { "jobs" }));
        var readdir = await verifierFs.ReaddirAsync(new Treaddir(0, 1, 8, 0, 1024));
        Assert.DoesNotContain("infinite-job", Encoding.UTF8.GetString(readdir.Data.ToArray()));
    }
}
