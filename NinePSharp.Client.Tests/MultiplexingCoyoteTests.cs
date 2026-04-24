using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Client.Tests;

public class MultiplexingCoyoteTests
{
    [Fact]
    public void Coyote_Multiplexing_Race_Condition_Test()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(20)
            .WithMaxSchedulingSteps(1000);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var (clientStream, serverStream) = LoopbackStream.CreatePair();
            var cts = new CancellationTokenSource();

            var serverTask = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        byte[] header = new byte[NinePConstants.HeaderSize];
                        await serverStream.ReadExactlyAsync(header, cts.Token);
                        
                        uint size = BitConverter.ToUInt32(header, 0);
                        ushort tag = BitConverter.ToUInt16(header, 5);
                        byte type = header[4];

                        if (size > NinePConstants.HeaderSize)
                        {
                            byte[] payload = new byte[size - NinePConstants.HeaderSize];
                            await serverStream.ReadExactlyAsync(payload, cts.Token);
                        }

                        if (type == (byte)MessageTypes.Tversion)
                        {
                            var rversion = new Rversion(tag, 8192, "9P2000.L");
                            byte[] resp = new byte[rversion.Size];
                            rversion.WriteTo(resp);
                            await serverStream.WriteAsync(resp, cts.Token);
                        }
                        else if (type == (byte)MessageTypes.Tclunk)
                        {
                            var rclunk = new Rclunk(tag);
                            byte[] resp = new byte[rclunk.Size];
                            rclunk.WriteTo(resp);
                            await serverStream.WriteAsync(resp, cts.Token);
                        }
                    }
                }
                catch { }
            });

            try
            {
                using var client = new NinePClient(clientStream);
                await client.VersionAsync();

                var tasks = Enumerable.Range(1, 3).Select(i => client.ClunkAsync((uint)i)).ToArray();
                await Task.WhenAll(tasks);
            }
            finally
            {
                cts.Cancel();
            }
        });

        engine.Run();

        if (engine.TestReport.NumOfFoundBugs > 0)
        {
            Assert.Fail($"Coyote found a bug: {engine.TestReport.BugReports.First()}");
        }
    }
}
