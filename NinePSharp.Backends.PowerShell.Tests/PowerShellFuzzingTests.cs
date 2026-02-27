using System;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Backends.PowerShell.Tests;

public class PowerShellFuzzingTests
{
    [Fact]
    public async Task Fuzz_State_Machine_Operations_Do_Not_Throw_Unexpected_Runtime_Exceptions()
    {
        var random = new Random(91357);
        var fs = new PowerShellFileSystem();
        string seededJob = $"fuzz-{Guid.NewGuid():N}";

        var jobs = (PowerShellFileSystem)fs.Clone();
        await jobs.WalkAsync(new Twalk(1, 1, 1, new[] { "jobs" }));
        await jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(2, 1, seededJob));

        try
        {
            for (int i = 0; i < 500; i++)
            {
                try
                {
                    switch (random.Next(0, 7))
                    {
                        case 0:
                        {
                            await fs.WalkAsync(new Twalk((ushort)(10 + i), 1, 1, RandomPath(random)));
                            break;
                        }
                        case 1:
                        {
                            await fs.OpenAsync(new Topen((ushort)(10 + i), 1, (byte)random.Next(0, 4)));
                            break;
                        }
                        case 2:
                        {
                            await fs.ReadAsync(new Tread((ushort)(10 + i), 1, (ulong)random.Next(0, 256), (uint)random.Next(1, 512)));
                            break;
                        }
                        case 3:
                        {
                            await fs.StatAsync(new Tstat((ushort)(10 + i), 1));
                            break;
                        }
                        case 4:
                        {
                            var writer = (PowerShellFileSystem)fs.Clone();
                            await writer.WalkAsync(new Twalk((ushort)(10 + i), 1, 1, new[] { "jobs", seededJob, "script.ps1" }));
                            // Whitespace avoids environment dependence on /usr/bin/pwsh.
                            var payload = Encoding.UTF8.GetBytes(new string(' ', random.Next(1, 64)));
                            await writer.WriteAsync(new Twrite((ushort)(20 + i), 1, 0, payload));
                            break;
                        }
                        case 5:
                        {
                            var creator = (PowerShellFileSystem)fs.Clone();
                            var path = RandomPath(random);
                            if (path.Length > 0)
                            {
                                await creator.WalkAsync(new Twalk((ushort)(30 + i), 1, 1, path));
                            }

                            string name = $"rand-{i:x}-{random.Next(0, 65535):x}";
                            await creator.CreateAsync(PowerShellTestHelpers.BuildCreate((ushort)(40 + i), 1, name));
                            break;
                        }
                        default:
                        {
                            await fs.ClunkAsync(new Tclunk((ushort)(50 + i), 1));
                            break;
                        }
                    }
                }
                catch (Exception ex) when (IsExpectedFuzzException(ex))
                {
                    // Expected protocol-path rejections are part of fuzzing corpus exploration.
                }
            }
        }
        finally
        {
            await PowerShellTestHelpers.RemoveJobIfPresentAsync(fs, seededJob);
        }

        var names = await PowerShellTestHelpers.ListJobsAsync(fs);
        Assert.NotNull(names);
    }

    private static string[] RandomPath(Random random)
    {
        string[] atoms =
        {
            "..",
            ".",
            "jobs",
            "status",
            "script.ps1",
            "output.json",
            "errors",
            "missing",
            $"id-{random.Next(0, 999):000}"
        };

        int len = random.Next(0, 5);
        var path = new string[len];
        for (int i = 0; i < len; i++)
        {
            path[i] = atoms[random.Next(0, atoms.Length)];
        }

        return path;
    }

    private static bool IsExpectedFuzzException(Exception ex)
    {
        return ex is InvalidOperationException
            || ex is NinePNotSupportedException
            || ex is ArgumentOutOfRangeException;
    }
}
