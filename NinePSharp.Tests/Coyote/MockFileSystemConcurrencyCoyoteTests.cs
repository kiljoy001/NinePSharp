using NinePSharp.Constants;
using System.Linq;
using FluentAssertions;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class MockFileSystemConcurrencyCoyoteTests
{
    [Fact]
    public void Coyote_Concurrent_Remove_Same_File_One_Succeeds_One_Fails_NotFound()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(200)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var fs = new MockFileSystem(new LuxVaultService());
            await fs.LcreateAsync(new Tlcreate(100, 1, 1, "race.bin", 0, 0644, 0));
            await fs.WalkAsync(new Twalk(2, 1, 1, new[] { "race.bin" }));

            var t1 = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                try
                {
                    await fs.RemoveAsync(new Tremove(3, 1));
                    return "removed";
                }
                catch (NinePProtocolException ex) when (ex.Message.Contains("File not found"))
                {
                    return "missing";
                }
            });

            var t2 = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                try
                {
                    await fs.RemoveAsync(new Tremove(4, 1));
                    return "removed";
                }
                catch (NinePProtocolException ex) when (ex.Message.Contains("File not found"))
                {
                    return "missing";
                }
            });

            var outcomes = await CoyoteTask.WhenAll(t1, t2);
            outcomes.Count(o => o == "removed").Should().Be(1);
            outcomes.Count(o => o == "missing").Should().Be(1);
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}
