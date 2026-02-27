using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Coyote.SystematicTesting;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Backends.Pipes.Tests.Coyote;

public class PipeQueueCoyoteTests
{
    [Fact]
    public void Coyote_ObjectQueue_MultiProducer_SingleConsumer_NoLoss_NoDuplication()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(180)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var queue = new ObjectQueueNode("coyote-queue");
            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 8; i++)
            {
                expected.Add($"A{i}");
                expected.Add($"B{i}");
            }

            var producerA = CoyoteTask.Run(async () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    await CoyoteTask.Yield();
                    await queue.WriteAsync(Encoding.UTF8.GetBytes($"A{i}"));
                }
            });

            var producerB = CoyoteTask.Run(async () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    await queue.WriteAsync(Encoding.UTF8.GetBytes($"B{i}"));
                    await CoyoteTask.Yield();
                }
            });

            var consumer = CoyoteTask.Run(async () =>
            {
                var received = new List<string>(16);
                for (int i = 0; i < 16; i++)
                {
                    var msg = await queue.ReadAsync(1024);
                    received.Add(Encoding.UTF8.GetString(msg.Span));
                }

                return received;
            });

            await CoyoteTask.WhenAll(producerA, producerB);
            var received = await consumer;
            queue.Close();

            if (received.Count != 16)
            {
                throw new Exception($"Expected 16 queue messages, received {received.Count}");
            }

            var unique = received.ToHashSet(StringComparer.Ordinal);
            if (!unique.SetEquals(expected))
            {
                throw new Exception(
                    $"Queue message set mismatch. Expected: {string.Join(",", expected.OrderBy(x => x))}; Received: {string.Join(",", unique.OrderBy(x => x))}");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}
