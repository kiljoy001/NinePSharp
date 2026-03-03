using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Examples;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests;

public class ProtocolGapEvidenceTests
{
    private class MockGapHandler : InMemoryHandler
    {
        public Stat LastWstat { get; private set; }
        public bool WstatCalled { get; private set; }
        public int ReadDelayMs { get; set; } = 0;

        public override Task<Rwstat> WstatAsync(string[] path, Twstat t, CancellationToken ct)
        {
            WstatCalled = true;
            LastWstat = t.Stat;
            return Task.FromResult(new Rwstat(t.Tag));
        }

        public override async Task<Rread> ReadAsync(string[] path, Tread t, CancellationToken ct)
        {
            if (ReadDelayMs > 0)
            {
                // Give Coyote/Async scheduler points
                await Task.Delay(ReadDelayMs, ct);
            }
            // Naively return what is requested to show the bug
            return new Rread(t.Tag, new byte[t.Count]);
        }
    }

    private static NinePFSDispatcher CreateDispatcher(INinePRequestHandler handler)
    {
        return new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, handler);
    }

    // --- 1. Twstat All-Ones Property ---
    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool Property_Twstat_Sanitization_Invariant(Stat update)
    {
        var handler = new MockGapHandler();
        var dispatcher = CreateDispatcher(handler);
        var s1 = "session-twstat";

        dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000).Wait();
        
        var task = dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTwstat(new Twstat(2, 1, update)), NinePDialect.NineP2000);
        task.Wait();

        // INVARIANT: If the input had an all-ones field, the handler MUST NOT see it.
        // We check 'length' as the primary example.
        if (update.Length == 0xFFFFFFFFFFFFFFFFUL)
        {
            // If the handler saw the all-ones length, the invariant is broken.
            if (handler.LastWstat.Length == 0xFFFFFFFFFFFFFFFFUL)
                return false; 
        }
        
        return true;
    }

    // --- 2. msize Enforcement Fuzzing ---
    [Property(MaxTest = 50)]
    public bool Property_MSize_Strict_Compliance(ushort negotiatedMSize, uint requestedCount)
    {
        // Protocols require msize >= 64 or so, and Tread count can be large.
        if (negotiatedMSize < 128 || negotiatedMSize > 8192) return true;
        if (requestedCount < 1 || requestedCount > 10000) return true;

        var handler = new MockGapHandler();
        var dispatcher = CreateDispatcher(handler);
        var s1 = "session-msize";

        // Setup session with negotiated size
        dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTversion(new Tversion(1, negotiatedMSize, "9P2000")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTattach(new Tattach(2, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTopen(new Topen(3, 1, 0)), NinePDialect.NineP2000).Wait();

        var task = dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTread(new Tread(4, 1, 0, requestedCount)), NinePDialect.NineP2000);
        task.Wait();

        if (task.Result is Rread rread)
        {
            // INVARIANT: Total message size (Header + Payload) must be <= negotiatedMSize
            // Tread size is header (7) + count (4) + data (N) = 11 + N
            uint totalMsgSize = 11u + (uint)rread.Data.Length;
            if (totalMsgSize > negotiatedMSize)
                return false; // VIOLATION
        }

        return true;
    }

    // --- 3. Directory Read Alignment Property ---
    [Property(MaxTest = 50)]
    public bool Property_Directory_Read_Atomicity(uint offset, uint count)
    {
        if (count == 0 || count > 1000) return true;
        
        var handler = new InMemoryHandler();
        handler.AddFile("f1", "c");
        handler.AddFile("f2", "c");
        handler.AddFile("f3", "c");
        var dispatcher = CreateDispatcher(handler);
        var s1 = "session-dir";

        dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000).Wait();
        dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTopen(new Topen(2, 1, 0)), NinePDialect.NineP2000).Wait();

        var task = dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTread(new Tread(3, 1, (ulong)offset, count)), NinePDialect.NineP2000);
        task.Wait();

        if (task.Result is Rread rread && rread.Data.Length > 0)
        {
            // INVARIANT: The byte stream must be parseable into integral Stat structures.
            // If we find a partial stat at the end, it's a violation.
            try 
            {
                int cursor = 0;
                var data = rread.Data.Span;
                while (cursor < data.Length)
                {
                    int start = cursor;
                    // Try to parse a stat. If it's a partial stat, the constructor or size check will fail.
                    var s = new Stat(data, ref cursor, NinePDialect.NineP2000);
                    if (cursor > data.Length) return false; // Overshot!
                }
            }
            catch 
            {
                return false; // Failed to parse complete stats
            }
        }

        return true;
    }

    // --- 4. Multi-Flush Chaos Fuzzing (Coyote) ---
    [Fact]
    public void Coyote_MultiFlush_Adversarial_Fuzzing()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(20)
            .WithMaxSchedulingSteps(1000);
            
        var engine = TestingEngine.Create(configuration, async () =>
        {
            var handler = new MockGapHandler { ReadDelayMs = 5 };
            var dispatcher = CreateDispatcher(handler);
            var s1 = "session-flush-chaos";

            await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);
            await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTopen(new Topen(2, 1, 0)), NinePDialect.NineP2000);

            // Start a request
            var tRead = CoyoteTask.Run(async () => {
                return await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTread(new Tread(100, 1, 0, 10)), NinePDialect.NineP2000);
            });

            // Fuzz with multiple concurrent flushes
            var flushes = Enumerable.Range(200, 5).Select(tag => 
                CoyoteTask.Run(async () => {
                    await CoyoteTask.Yield();
                    return await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTflush(new Tflush((ushort)tag, 100)), NinePDialect.NineP2000);
                })
            ).ToArray();

            await CoyoteTask.WhenAll(tRead);
            await CoyoteTask.WhenAll(flushes);
        });

        engine.Run();
        
        if (engine.TestReport.NumOfFoundBugs > 0)
        {
            Assert.Fail($"Multi-flush fuzzing detected deadlock/race: {engine.TestReport.BugReports.First()}");
        }
    }
}
