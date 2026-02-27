using System;
using System.Linq;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Backends.PowerShell.Tests;

public class PowerShellLifecyclePropertyFuzzTests
{
    [Property(MaxTest = 45)]
    public bool Job_Status_Remains_Created_For_Whitespace_Scripts_Property(PositiveInt whitespaceSeed)
    {
        int count = Math.Clamp(whitespaceSeed.Get % 96 + 1, 1, 96);
        string jobName = $"ws-{Guid.NewGuid():N}";
        var fs = new PowerShellFileSystem();

        try
        {
            var jobs = (PowerShellFileSystem)fs.Clone();
            jobs.WalkAsync(new Twalk(1, 1, 1, new[] { "jobs" })).GetAwaiter().GetResult();
            jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(2, 1, jobName)).GetAwaiter().GetResult();

            var script = (PowerShellFileSystem)jobs.Clone();
            script.WalkAsync(new Twalk(3, 1, 1, new[] { jobName, "script.ps1" })).GetAwaiter().GetResult();
            script.WriteAsync(new Twrite(4, 1, 0, Encoding.UTF8.GetBytes(new string(' ', count)))).GetAwaiter().GetResult();

            var status = (PowerShellFileSystem)jobs.Clone();
            status.WalkAsync(new Twalk(5, 1, 1, new[] { jobName, "status" })).GetAwaiter().GetResult();
            var read = status.ReadAsync(new Tread(6, 1, 0, 128)).GetAwaiter().GetResult();
            string value = Encoding.UTF8.GetString(read.Data.ToArray()).Trim();

            return value == "Created";
        }
        finally
        {
            PowerShellTestHelpers.RemoveJobIfPresentAsync(fs, jobName).GetAwaiter().GetResult();
        }
    }

    [Property(MaxTest = 40)]
    public bool Jobs_Directory_Visibility_Follows_Create_Remove_Property(NonEmptyString seed)
    {
        string baseName = PowerShellTestHelpers.SanitizeJobName(seed.Get);
        string jobName = $"{baseName}-{Guid.NewGuid():N}";
        var fs = new PowerShellFileSystem();

        try
        {
            var jobs = (PowerShellFileSystem)fs.Clone();
            jobs.WalkAsync(new Twalk(10, 1, 1, new[] { "jobs" })).GetAwaiter().GetResult();
            jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(11, 1, jobName)).GetAwaiter().GetResult();

            var listed = PowerShellTestHelpers.ListJobsAsync(fs).GetAwaiter().GetResult();
            if (!listed.Contains(jobName, StringComparer.Ordinal))
            {
                return false;
            }

            var job = (PowerShellFileSystem)jobs.Clone();
            job.WalkAsync(new Twalk(12, 1, 1, new[] { jobName })).GetAwaiter().GetResult();
            job.RemoveAsync(new Tremove(13, 1)).GetAwaiter().GetResult();

            var listedAfter = PowerShellTestHelpers.ListJobsAsync(fs).GetAwaiter().GetResult();
            return !listedAfter.Contains(jobName, StringComparer.Ordinal);
        }
        finally
        {
            PowerShellTestHelpers.RemoveJobIfPresentAsync(fs, jobName).GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Fuzz_Job_Names_With_PathLike_Content_Do_Not_Crash_State_Machine()
    {
        string[] corpus =
        {
            "../escape",
            "./dot",
            "spaces are valid",
            "unicode-Δ",
            "name/with/slash",
            "name\\with\\backslash",
            "NUL-\0-not-allowed",
            "very-very-very-very-very-very-long-name",
            "errors",
            "status",
            "script.ps1"
        };

        var random = new Random(0x900D);
        var fs = new PowerShellFileSystem();
        var created = new System.Collections.Generic.List<string>();

        try
        {
            for (int i = 0; i < 200; i++)
            {
                string raw = corpus[random.Next(0, corpus.Length)];
                string safe = PowerShellTestHelpers.SanitizeJobName(raw);
                string jobName = $"{safe}-{i:x}";

                var jobs = (PowerShellFileSystem)fs.Clone();
                await jobs.WalkAsync(new Twalk((ushort)(20 + i), 1, 1, new[] { "jobs" }));

                try
                {
                    await jobs.CreateAsync(PowerShellTestHelpers.BuildCreate((ushort)(21 + i), 1, jobName));
                    created.Add(jobName);
                }
                catch (Exception ex)
                {
                    Assert.True(ex is InvalidOperationException, $"Unexpected create exception: {ex.GetType().Name}");
                }

                var names = await PowerShellTestHelpers.ListJobsAsync(fs, (ushort)(40 + i));
                Assert.NotNull(names);
            }
        }
        finally
        {
            foreach (string job in created.Distinct(StringComparer.Ordinal))
            {
                await PowerShellTestHelpers.RemoveJobIfPresentAsync(fs, job, 400);
            }
        }
    }
}
