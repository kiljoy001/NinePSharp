using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Backends.Pipes.Tests;

public class PipeIntegrationTests
{
    [Fact]
    public async Task ObjectQueue_Ensures_Message_Atomicity()
    {
        INinePFileSystem fs = new PipeFileSystem();
        
        // 1. Create a queue
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));
        await fs.MkdirAsync(new Tmkdir(0, 1, 2, "test_queue", 0755, 0));

        // 2. Write two distinct objects
        await fs.WalkAsync(new Twalk(1, 2, 3, new[] { "test_queue" }));
        await fs.WriteAsync(new Twrite(1, 3, 0, Encoding.UTF8.GetBytes("message-1")));
        await fs.WriteAsync(new Twrite(1, 3, 0, Encoding.UTF8.GetBytes("message-2")));

        // 3. Read back - should get exactly "message-1"
        var read1 = await fs.ReadAsync(new Tread(1, 3, 0, 1024));
        Encoding.UTF8.GetString(read1.Data.ToArray()).Should().Be("message-1");

        // 4. Read again - should get exactly "message-2"
        var read2 = await fs.ReadAsync(new Tread(1, 3, 0, 1024));
        Encoding.UTF8.GetString(read2.Data.ToArray()).Should().Be("message-2");
    }

    [Fact]
    public async Task DataPipe_Supports_Streaming_And_Short_Reads()
    {
        INinePFileSystem fs = new PipeFileSystem();
        
        // 1. Create a pipe
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "pipes" }));
        await fs.MkdirAsync(new Tmkdir(0, 1, 2, "test_pipe", 0755, 0));

        // 2. Write a large chunk of data
        await fs.WalkAsync(new Twalk(1, 2, 3, new[] { "test_pipe" }));
        await fs.WriteAsync(new Twrite(1, 3, 0, Encoding.UTF8.GetBytes("HELLO-STREAMING-WORLD")));

        // 3. Perform a short read (only 5 bytes)
        var read1 = await fs.ReadAsync(new Tread(1, 3, 0, 5));
        Encoding.UTF8.GetString(read1.Data.ToArray()).Should().Be("HELLO");

        // 4. Read the rest
        var read2 = await fs.ReadAsync(new Tread(1, 3, 0, 1024));
        Encoding.UTF8.GetString(read2.Data.ToArray()).Should().Be("-STREAMING-WORLD");
    }

    [Fact]
    public async Task Remove_Pipe_Cancels_Waiters()
    {
        INinePFileSystem fs = new PipeFileSystem();
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));
        await fs.MkdirAsync(new Tmkdir(0, 1, 2, "cancel_test", 0755, 0));

        await fs.WalkAsync(new Twalk(1, 2, 3, new[] { "cancel_test" }));
        
        // Start a read task that will block
        var readTask = fs.ReadAsync(new Tread(1, 3, 0, 1024));
        
        await Task.Delay(100);
        readTask.IsCompleted.Should().BeFalse("Read should be blocking on empty queue");

        // Remove the queue
        await fs.WalkAsync(new Twalk(1, 1, 4, new[] { "queues", "cancel_test" }));
        await fs.RemoveAsync(new Tremove(1, 4));

        // The read task should now throw or return empty/error
        Func<Task> act = async () => await readTask;
        await act.Should().ThrowAsync<Exception>("Blocking read must be cancelled when node is removed");
    }
}
