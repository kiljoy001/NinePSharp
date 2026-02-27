using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Backends.Pipes.Tests;

public class AtomicQueueTests
{
    private readonly INinePFileSystem _fs = new PipeFileSystem();

    private async Task SetupQueue(string name)
    {
        await _fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));
        await _fs.CreateAsync(PipeTestHelpers.BuildCreate(1, 2, name));
        await _fs.WalkAsync(new Twalk(1, 2, 3, new[] { name }));
    }

    #region Unit Tests

    [Fact]
    public async Task Queue_Atomic_Framing_Test()
    {
        string qName = "atomic_framing";
        await SetupQueue(qName);

        var msg1 = Encoding.UTF8.GetBytes("MSG-1-BLOCK");
        var msg2 = Encoding.UTF8.GetBytes("MSG-2-SMALL");

        // Write two messages
        await _fs.WriteAsync(new Twrite(1, 3, 0, msg1));
        await _fs.WriteAsync(new Twrite(1, 3, 0, msg2));

        // Read with huge count - should ONLY get msg1
        var read1 = await _fs.ReadAsync(new Tread(1, 3, 0, 1024 * 1024));
        read1.Data.ToArray().Should().BeEquivalentTo(msg1);
        read1.Data.Length.Should().Be(msg1.Length);

        // Read again - should get msg2
        var read2 = await _fs.ReadAsync(new Tread(1, 3, 0, 1024 * 1024));
        read2.Data.ToArray().Should().BeEquivalentTo(msg2);
    }

    [Fact]
    public async Task Queue_Empty_Read_Blocks_Until_Write()
    {
        string qName = "blocking_read";
        await SetupQueue(qName);

        var readTask = _fs.ReadAsync(new Tread(1, 3, 0, 1024));
        
        await Task.Delay(50);
        readTask.IsCompleted.Should().BeFalse("Read should block on empty queue");

        var payload = Encoding.UTF8.GetBytes("late-arrival");
        await _fs.WriteAsync(new Twrite(1, 3, 0, payload));

        var result = await readTask;
        result.Data.ToArray().Should().BeEquivalentTo(payload);
    }

    #endregion

    #region Property Tests (FsCheck)

    [Property(MaxTest = 100)]
    public bool Queue_Maintains_FIFO_Integrity(byte[][] messages)
    {
        if (messages == null || messages.Length == 0) return true;

        INinePFileSystem fs = new PipeFileSystem();
        string name = "fifo_prop";
        
        // Setup
        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" })).Wait();
        fs.CreateAsync(PipeTestHelpers.BuildCreate(1, 2, name)).Wait();
        fs.WalkAsync(new Twalk(1, 2, 3, new[] { name })).Wait();

        // Write all
        foreach (var msg in messages)
        {
            var m = msg ?? Array.Empty<byte>();
            fs.WriteAsync(new Twrite(1, 3, 0, m)).Wait();
        }

        // Read all and verify
        foreach (var original in messages)
        {
            var expected = original ?? Array.Empty<byte>();
            var read = fs.ReadAsync(new Tread(1, 3, 0, 1024 * 64)).Result;
            if (!read.Data.ToArray().SequenceEqual(expected)) return false;
        }

        return true;
    }

    #endregion

    #region Coyote Concurrency Tests

    [Fact]
    public void Coyote_Concurrent_Queue_Producers_Consumers()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(50)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            string qName = "coyote_queue";
            INinePFileSystem setup = new PipeFileSystem();
            await setup.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));
            await setup.CreateAsync(PipeTestHelpers.BuildCreate(2, 2, qName));

            int msgCount = 10;
            var producerFs = setup.Clone();
            var consumerFs = setup.Clone();

            await producerFs.WalkAsync(new Twalk(3, 2, 3, new[] { qName }));
            await consumerFs.WalkAsync(new Twalk(4, 2, 4, new[] { qName }));

            var consumerTask = CoyoteTask.Run(async () =>
            {
                int received = 0;
                for (int i = 0; i < msgCount; i++)
                {
                    var read = await consumerFs.ReadAsync(new Tread(5, 4, 0, 1024));
                    if (read.Data.Length > 0) received++;
                }
                return received;
            });

            var producerTask = CoyoteTask.Run(async () =>
            {
                for (int i = 0; i < msgCount; i++)
                {
                    byte[] data = BitConverter.GetBytes(i);
                    await producerFs.WriteAsync(new Twrite(6, 3, 0, data));
                }
            });

            await producerTask;
            int totalReceived = await consumerTask;

            totalReceived.Should().Be(msgCount, "Queue must deliver all messages exactly once in concurrent environment");
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }

    #endregion
}
