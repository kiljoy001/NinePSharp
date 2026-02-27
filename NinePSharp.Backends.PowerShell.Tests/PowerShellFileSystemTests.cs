using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Backends.PowerShell.Tests;

public class PowerShellFileSystemTests
{
    [Fact]
    public async Task Create_Outside_Jobs_Root_Is_Rejected()
    {
        var fs = new PowerShellFileSystem();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fs.CreateAsync(PowerShellTestHelpers.BuildCreate(tag: 1, fid: 1, name: "should-fail")));

        Assert.Contains("Cannot create", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Whitespace_Script_Does_Not_Transition_Job_Into_Running()
    {
        var fs = new PowerShellFileSystem();
        string jobName = $"ps-whitespace-{Guid.NewGuid():N}";

        try
        {
            var jobs = (PowerShellFileSystem)fs.Clone();
            await jobs.WalkAsync(new Twalk(1, 1, 1, new[] { "jobs" }));
            await jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(tag: 2, fid: 1, name: jobName));

            var script = (PowerShellFileSystem)jobs.Clone();
            await script.WalkAsync(new Twalk(3, 1, 1, new[] { jobName, "script.ps1" }));
            await script.WriteAsync(new Twrite(4, 1, 0, Encoding.UTF8.GetBytes("   \t  \n")));

            var status = (PowerShellFileSystem)jobs.Clone();
            await status.WalkAsync(new Twalk(5, 1, 1, new[] { jobName, "status" }));
            var statusRead = await status.ReadAsync(new Tread(6, 1, 0, 128));

            string value = Encoding.UTF8.GetString(statusRead.Data.ToArray()).Trim();
            Assert.Equal("Created", value);
        }
        finally
        {
            await PowerShellTestHelpers.RemoveJobIfPresentAsync(fs, jobName);
        }
    }

    [Fact]
    public async Task Write_To_Removed_Job_Script_Is_Not_Acknowledged()
    {
        var fs = new PowerShellFileSystem();
        string jobName = $"ps-remove-race-{Guid.NewGuid():N}";
        byte[] payload = Encoding.UTF8.GetBytes("Write-Output 'x'");

        var jobs = (PowerShellFileSystem)fs.Clone();
        await jobs.WalkAsync(new Twalk(10, 1, 1, new[] { "jobs" }));
        await jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(tag: 11, fid: 1, name: jobName));

        var writer = (PowerShellFileSystem)jobs.Clone();
        await writer.WalkAsync(new Twalk(12, 1, 1, new[] { jobName, "script.ps1" }));

        var remover = (PowerShellFileSystem)jobs.Clone();
        await remover.WalkAsync(new Twalk(13, 1, 1, new[] { jobName }));
        await remover.RemoveAsync(new Tremove(14, 1));

        await Assert.ThrowsAsync<NinePNotSupportedException>(() =>
            writer.WriteAsync(new Twrite(15, 1, 0, payload)));

        var names = await PowerShellTestHelpers.ListJobsAsync(fs);
        Assert.DoesNotContain(jobName, names);
    }
}
