using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Examples;
using NinePSharp.Generators;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests;

public class AuthFlushAdversarialTests
{
    private class SlowAuthHandler : InMemoryHandler
    {
        public bool ShouldDelay { get; set; } = false;
        public int DelayMs { get; set; } = 1000;
        public bool AuthReadCancelled { get; private set; }

        public override async Task<byte[]> AuthReadAsync(uint afid, ulong offset, uint count, CancellationToken ct)
        {
            if (ShouldDelay)
            {
                try
                {
                    for (int i = 0; i < 50; i++)
                    {
                        await Task.Yield();
                        if (ct.IsCancellationRequested) break;
                    }
                    
                    if (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(DelayMs, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    AuthReadCancelled = true;
                    throw;
                }

                if (ct.IsCancellationRequested)
                {
                    AuthReadCancelled = true;
                    ct.ThrowIfCancellationRequested();
                }
            }
            return Encoding.UTF8.GetBytes("auth-data");
        }

        public override Task<uint> AuthWriteAsync(uint afid, ulong offset, byte[] data, CancellationToken ct)
        {
            return Task.FromResult((uint)data.Length);
        }
    }

    private static NinePFSDispatcher CreateDispatcher(INinePRequestHandler handler)
    {
        return new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, handler);
    }

    // --- Tauth Adversarial Tests ---

    [Fact]
    public async Task Tauth_Afid_Already_In_Use_As_Fid_Coexistence()
    {
        var handler = new InMemoryHandler();
        var dispatcher = CreateDispatcher(handler);

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 10, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);
        var response = await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTauth(new Tauth(2, 10, "user", "/")), NinePDialect.NineP2000);
        response.Should().BeOfType<Rauth>();
    }

    // --- Tflush Adversarial Tests ---

    [Fact]
    public async Task Tflush_Self_Flush_Succeeds()
    {
        var handler = new InMemoryHandler();
        var dispatcher = CreateDispatcher(handler);
        var response = await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(1, 1)), NinePDialect.NineP2000);
        response.Should().BeOfType<Rflush>();
    }

    [Fact]
    public async Task Tflush_Of_Slow_Read_Cancels_And_Waits()
    {
        var handler = new SlowAuthHandler { ShouldDelay = true, DelayMs = 1000 };
        var dispatcher = CreateDispatcher(handler);

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTauth(new Tauth(1, 10, "user", "/")), NinePDialect.NineP2000);
        var readTask = dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTread(new Tread(2, 10, 0, 100)), NinePDialect.NineP2000);

        await Task.Delay(200);
        var flushTask = dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTflush(new Tflush(3, 2)), NinePDialect.NineP2000);

        var flushResponse = await flushTask;
        flushResponse.Should().BeOfType<Rflush>();
        
        var readResponse = await readTask;
        readResponse.Should().BeOfType<Rerror>();
        handler.AuthReadCancelled.Should().BeTrue();
    }

    // --- Coyote Concurrency Tests ---

    [Fact]
    public void Coyote_Concurrent_Tflush_And_Completion()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(10)
            .WithMaxSchedulingSteps(500);
            
        var engine = TestingEngine.Create(configuration, async () =>
        {
            var handler = new SlowAuthHandler { ShouldDelay = true, DelayMs = 1 };
            var dispatcher = CreateDispatcher(handler);
            var s1 = "session-1";

            await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTauth(new Tauth(1, 10, "user", "/")), NinePDialect.NineP2000);

            var tRead = CoyoteTask.Run(async () =>
            {
                return await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTread(new Tread(2, 10, 0, 100)), NinePDialect.NineP2000);
            });

            var tFlush = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                return await dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTflush(new Tflush(3, 2)), NinePDialect.NineP2000);
            });

            await CoyoteTask.WhenAll(tRead, tFlush);
        });

        engine.Run();
        
        if (engine.TestReport.NumOfFoundBugs > 0)
        {
            Assert.Fail($"Coyote found {engine.TestReport.NumOfFoundBugs} bugs: {engine.TestReport.BugReports.First()}");
        }
    }

    // --- FsCheck Property Fuzzing ---

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 50)]
    public bool Tauth_Aggressive_Fuzzing_Property(Tauth tauth)
    {
        var handler = new InMemoryHandler();
        var dispatcher = CreateDispatcher(handler);
        var s1 = "session-1";

        var task = dispatcher.DispatchAsync(s1, NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000);
        task.Wait();
        
        return task.Result is Rauth || task.Result is Rerror;
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 30)]
    public bool Auth_State_Machine_Violation_Fuzzing_Property(Microsoft.FSharp.Collections.FSharpList<NinePMessage> sequence)
    {
        var handler = new InMemoryHandler();
        var dispatcher = CreateDispatcher(handler);
        var s1 = "session-1";

        foreach (var msg in sequence)
        {
            try 
            {
                var task = dispatcher.DispatchAsync(s1, msg, NinePDialect.NineP2000);
                task.Wait();
                if (task.Result == null) return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        return true;
    }

    [Fact]
    public async Task Random_Mutation_Fuzzing_Tauth()
    {
        var handler = new InMemoryHandler();
        var dispatcher = CreateDispatcher(handler);
        var rng = new Random();
        var s1 = "mutation-fuzz-session";

        for (int i = 0; i < 20; i++)
        {
            var tauth = new Tauth(1, 10, "valid_user", "/");
            var bytes = new byte[tauth.Size];
            tauth.WriteTo(bytes);

            // Mutate 1-3 random bytes
            int mutations = rng.Next(1, 4);
            for (int m = 0; m < mutations; m++)
            {
                bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
            }

            try 
            {
                var result = NinePParser.parse(NinePDialect.NineP2000, new ReadOnlyMemory<byte>(bytes));
                if (result.IsOk)
                {
                    var response = await dispatcher.DispatchAsync(s1, result.ResultValue, NinePDialect.NineP2000);
                    response.Should().NotBeNull();
                }
            }
            catch (Exception)
            {
                // Graceful handling
            }
        }
    }
}
