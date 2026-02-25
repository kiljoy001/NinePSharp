using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Backends.PowerShell;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Backends.PowerShell.Tests;

public class PowerShellFileSystemTests
{
    [Fact]
    public async Task PowerShell_Job_Execution_Succeeds()
    {
        var fs = new PowerShellFileSystem();
        
        // 1. Create a job
        var jobsFs = (PowerShellFileSystem)fs.Clone();
        await jobsFs.WalkAsync(new Twalk(1, 1, 2, new[] { "jobs" }));
        await jobsFs.MkdirAsync(new Tmkdir(0, 1, 1, "ps-test", 0755, 0));

        // 2. Walk into the job
        var testJobFs = (PowerShellFileSystem)jobsFs.Clone();
        await testJobFs.WalkAsync(new Twalk(1, 2, 3, new[] { "ps-test" }));

        // 3. Upload script.ps1
        string script = "Get-Date | Select-Object Year";
        var scriptFs = (PowerShellFileSystem)testJobFs.Clone();
        await scriptFs.WalkAsync(new Twalk(1, 3, 4, new[] { "script.ps1" }));
        await scriptFs.WriteAsync(new Twrite(1, 4, 0, Encoding.UTF8.GetBytes(script)));

        // 4. Wait for completion
        int retries = 0;
        string status = "";
        while (retries < 20) {
            var statusFs = (PowerShellFileSystem)testJobFs.Clone();
            await statusFs.WalkAsync(new Twalk(1, 3, 5, new[] { "status" }));
            var res = await statusFs.ReadAsync(new Tread(1, 5, 0, 100));
            status = Encoding.UTF8.GetString(res.Data.ToArray()).Trim();
            if (status == "Completed" || status == "Failed") break;
            await Task.Delay(200);
            retries++;
        }

        if (status == "Failed")
        {
            var errorFs = (PowerShellFileSystem)testJobFs.Clone();
            await errorFs.WalkAsync(new Twalk(1, 3, 7, new[] { "errors" }));
            var errorRes = await errorFs.ReadAsync(new Tread(1, 7, 0, 8192));
            throw new Exception($"Job failed with errors: {Encoding.UTF8.GetString(errorRes.Data.ToArray())}");
        }

        Assert.Equal("Completed", status);

        // 5. Read output
        var outputFs = (PowerShellFileSystem)testJobFs.Clone();
        await outputFs.WalkAsync(new Twalk(1, 3, 6, new[] { "output.json" }));
        var outputRes = await outputFs.ReadAsync(new Tread(1, 6, 0, 1024));
        string json = Encoding.UTF8.GetString(outputRes.Data.ToArray());
        
        Assert.Contains(DateTime.Now.Year.ToString(), json);
    }
}
