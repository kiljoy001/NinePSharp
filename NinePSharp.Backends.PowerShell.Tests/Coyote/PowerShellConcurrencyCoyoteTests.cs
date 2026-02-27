using System;
using System.Linq;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Messages;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Backends.PowerShell.Tests.Coyote;

public class PowerShellConcurrencyCoyoteTests
{
    [Fact]
    public void Coyote_Concurrent_Create_With_Same_Name_Allows_At_Most_One_Job_Instance()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(200)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            string jobName = $"coyote-create-{Guid.NewGuid():N}";
            var root = new PowerShellFileSystem();

            var jobs = (PowerShellFileSystem)root.Clone();
            await jobs.WalkAsync(new Twalk(1, 1, 1, new[] { "jobs" }));

            var a = (PowerShellFileSystem)jobs.Clone();
            var b = (PowerShellFileSystem)jobs.Clone();

            var createA = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                try
                {
                    await a.CreateAsync(PowerShellTestHelpers.BuildCreate(2, 1, jobName));
                    return true;
                }
                catch
                {
                    return false;
                }
            });

            var createB = CoyoteTask.Run(async () =>
            {
                try
                {
                    await b.CreateAsync(PowerShellTestHelpers.BuildCreate(3, 1, jobName));
                    await CoyoteTask.Yield();
                    return true;
                }
                catch
                {
                    return false;
                }
            });

            bool[] outcomes = await CoyoteTask.WhenAll(createA, createB);
            int successCount = outcomes.Count(v => v);

            var names = await PowerShellTestHelpers.ListJobsAsync(root, 100);
            int entryCount = names.Count(n => string.Equals(n, jobName, StringComparison.Ordinal));

            if (successCount > 1 || entryCount > 1)
            {
                throw new Exception($"Duplicate job creation detected for '{jobName}'. SuccessCount={successCount}, EntryCount={entryCount}");
            }

            await PowerShellTestHelpers.RemoveJobIfPresentAsync(root, jobName, 110);
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }

    [Fact]
    public void Coyote_Remove_Race_With_Write_Does_Not_Acknowledge_Missing_Job()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(150)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            string jobName = $"coyote-remove-{Guid.NewGuid():N}";
            byte[] payload = new byte[] { (byte)' ', (byte)'\t', (byte)'\n' };

            var root = new PowerShellFileSystem();
            var jobs = (PowerShellFileSystem)root.Clone();
            await jobs.WalkAsync(new Twalk(1, 1, 1, new[] { "jobs" }));
            await jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(2, 1, jobName));

            var writer = (PowerShellFileSystem)jobs.Clone();
            await writer.WalkAsync(new Twalk(3, 1, 1, new[] { jobName, "script.ps1" }));

            var remover = (PowerShellFileSystem)jobs.Clone();
            await remover.WalkAsync(new Twalk(4, 1, 1, new[] { jobName }));

            var removeTask = CoyoteTask.Run(async () =>
            {
                await remover.RemoveAsync(new Tremove(5, 1));
            });

            var writeTask = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                try
                {
                    var res = await writer.WriteAsync(new Twrite(6, 1, 0, payload));
                    return (threw: false, count: res.Count);
                }
                catch
                {
                    return (threw: true, count: 0u);
                }
            });

            await removeTask;
            _ = await writeTask;

            bool acceptedAfterRemove;
            try
            {
                var post = await writer.WriteAsync(new Twrite(7, 1, 0, payload));
                acceptedAfterRemove = post.Count == payload.Length;
            }
            catch
            {
                acceptedAfterRemove = false;
            }

            if (acceptedAfterRemove)
            {
                throw new Exception("Write after remove should not be fully acknowledged.");
            }

            var names = await PowerShellTestHelpers.ListJobsAsync(root, 120);
            if (names.Contains(jobName, StringComparer.Ordinal))
            {
                throw new Exception($"Removed job '{jobName}' is still visible in /jobs.");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}
