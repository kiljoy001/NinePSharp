using System;
using System.Linq;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Messages;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Backends.PowerShell.Tests.Coyote;

public class PowerShellListRemoveCoyoteTests
{
    [Fact]
    public void Coyote_Concurrent_List_And_Remove_Eventually_Converges()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(180)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            string jobName = $"coyote-list-remove-{Guid.NewGuid():N}";
            var root = new PowerShellFileSystem();

            var jobs = (PowerShellFileSystem)root.Clone();
            await jobs.WalkAsync(new Twalk(1, 1, 1, new[] { "jobs" }));
            await jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(2, 1, jobName));

            var remover = (PowerShellFileSystem)jobs.Clone();
            await remover.WalkAsync(new Twalk(3, 1, 1, new[] { jobName }));

            var listTask = CoyoteTask.Run(async () =>
            {
                int seen = 0;
                for (int i = 0; i < 8; i++)
                {
                    var names = await PowerShellTestHelpers.ListJobsAsync(root, (ushort)(50 + i));
                    if (names.Contains(jobName, StringComparer.Ordinal))
                    {
                        seen++;
                    }
                    await CoyoteTask.Yield();
                }
                return seen;
            });

            var removeTask = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                await remover.RemoveAsync(new Tremove(4, 1));
            });

            _ = await listTask;
            await removeTask;

            var finalListing = await PowerShellTestHelpers.ListJobsAsync(root, 120);
            if (finalListing.Contains(jobName, StringComparer.Ordinal))
            {
                throw new Exception($"Job '{jobName}' still visible after remove/list race.");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}
