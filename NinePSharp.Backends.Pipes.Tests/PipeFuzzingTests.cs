using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Backends.Pipes.Tests;

public class PipeFuzzingTests
{
    [Fact]
    public async Task Fuzz_MissingNode_Writes_Are_Never_Fully_Acknowledged()
    {
        var random = new Random(0xC0FFEE);

        for (int i = 0; i < 120; i++)
        {
            INinePFileSystem fs = new PipeFileSystem();
            string missingName = $"missing_{i}_{random.Next(100000, 999999)}";
            byte[] payload = new byte[random.Next(1, 256)];
            random.NextBytes(payload);

            await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));
            await fs.WalkAsync(new Twalk(2, 2, 3, new[] { missingName }));

            bool threw = false;
            uint acknowledged = 0;
            try
            {
                var write = await fs.WriteAsync(new Twrite(3, 3, 0, payload));
                acknowledged = write.Count;
            }
            catch
            {
                threw = true;
            }

            Assert.True(threw || acknowledged < payload.Length,
                $"Iteration {i}: write to missing node '{missingName}' was fully acknowledged ({acknowledged}/{payload.Length})");
        }
    }

    [Fact]
    public async Task Fuzz_Populated_Directory_Reads_Should_Eventually_Return_Entries()
    {
        var random = new Random(1337);

        for (int iteration = 0; iteration < 60; iteration++)
        {
            INinePFileSystem fs = new PipeFileSystem();
            await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" }));

            int createCount = random.Next(1, 8);
            for (int i = 0; i < createCount; i++)
            {
                string name = $"q_{iteration}_{i}_{random.Next(1000, 9999)}";
                await fs.CreateAsync(PipeTestHelpers.BuildCreate((ushort)(10 + i), 2, name));
            }

            // Fresh view from a new fs instance to avoid depending on mutable _currentPath state.
            INinePFileSystem dirView = new PipeFileSystem();
            await dirView.WalkAsync(new Twalk(40, 1, 2, new[] { "queues" }));

            uint count = (uint)random.Next(64, 4096);
            var read = await dirView.ReadAsync(new Tread(41, 2, 0, count));

            Assert.True(read.Data.Length > 0,
                $"Iteration {iteration}: expected non-empty directory bytes after creating {createCount} queue(s)");
        }
    }

    [Fact]
    public async Task Fuzz_MissingWalk_FullQidAck_Is_Rejected()
    {
        var random = new Random(9001);

        for (int i = 0; i < 100; i++)
        {
            INinePFileSystem fs = new PipeFileSystem();
            string leaf = $"n_{i}_{random.Next(10000, 99999)}";

            await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "pipes" }));
            var walk = await fs.WalkAsync(new Twalk(2, 2, 3, new[] { leaf }));

            Assert.True(walk.Wqid.Length < 1,
                $"Iteration {i}: missing leaf '{leaf}' was incorrectly acknowledged as a successful walk");
        }
    }
}
