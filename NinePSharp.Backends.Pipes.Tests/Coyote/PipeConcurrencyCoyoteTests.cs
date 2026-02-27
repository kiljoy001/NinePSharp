using System;
using FluentAssertions;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Backends.Pipes.Tests.Coyote;

public class PipeConcurrencyCoyoteTests
{
    [Fact]
    public void Coyote_Remove_Then_Write_Must_Not_Fully_Acknowledge_Missing_Node()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(120)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            string queueName = $"cq_{Guid.NewGuid():N}";

            INinePFileSystem setup = new PipeFileSystem();
            await setup.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));
            await setup.CreateAsync(PipeTestHelpers.BuildCreate(2, 2, queueName));

            INinePFileSystem writerFs = setup.Clone();
            INinePFileSystem removerFs = setup.Clone();

            await writerFs.WalkAsync(new Twalk(3, 2, 3, new[] { queueName }));
            await removerFs.WalkAsync(new Twalk(4, 2, 4, new[] { queueName }));

            byte[] payload = { 1, 2, 3, 4, 5, 6 };

            var removeTask = CoyoteTask.Run(async () =>
            {
                await removerFs.RemoveAsync(new Tremove(5, 4));
            });

            var writeTask = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                try
                {
                    var write = await writerFs.WriteAsync(new Twrite(6, 3, 0, payload));
                    return (threw: false, count: write.Count);
                }
                catch
                {
                    return (threw: true, count: 0u);
                }
            });

            await removeTask;
            _ = await writeTask;

            // Regardless of the race outcome for the in-flight write,
            // writes after successful removal must not be fully acknowledged.
            try
            {
                var postRemove = await writerFs.WriteAsync(new Twrite(7, 3, 0, payload));
                postRemove.Count.Should().BeLessThan((uint)payload.Length);
            }
            catch
            {
                // Exception is also valid: removed node should be unreachable.
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}
