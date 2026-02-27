using System;
using System.Linq;
using System.Threading;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Backends.PowerShell.Tests;

public class PowerShellPropertyTests
{
    private static int _counter;

    [Property(MaxTest = 80)]
    public bool Walk_Never_Reports_More_Qids_Than_Requested_Path_Elements(string[]? path)
    {
        string[] normalized = (path ?? Array.Empty<string>())
            .Take(5)
            .Select(s => s ?? string.Empty)
            .ToArray();

        var fs = new PowerShellFileSystem();
        var walk = fs.WalkAsync(new Twalk(1, 1, 1, normalized)).GetAwaiter().GetResult();
        return walk.Wqid.Length <= normalized.Length;
    }

    [Property(MaxTest = 40)]
    public bool Create_Remove_Roundtrip_Updates_Jobs_Directory_View(NonEmptyString seed)
    {
        string nameRoot = PowerShellTestHelpers.SanitizeJobName(seed.Get);
        string jobName = $"{nameRoot}-{Interlocked.Increment(ref _counter):x}";

        var fs = new PowerShellFileSystem();
        try
        {
            var jobs = (PowerShellFileSystem)fs.Clone();
            jobs.WalkAsync(new Twalk(10, 1, 1, new[] { "jobs" })).GetAwaiter().GetResult();

            var created = jobs.CreateAsync(PowerShellTestHelpers.BuildCreate(11, 1, jobName)).GetAwaiter().GetResult();
            bool qidLooksLikeDirectory = created.Qid.Type == QidType.QTDIR;

            var listedBefore = PowerShellTestHelpers.ListJobsAsync(fs).GetAwaiter().GetResult();
            bool appearsAfterCreate = listedBefore.Contains(jobName, StringComparer.Ordinal);

            var job = (PowerShellFileSystem)jobs.Clone();
            var walk = job.WalkAsync(new Twalk(12, 1, 1, new[] { jobName })).GetAwaiter().GetResult();
            bool walkReachedJob = walk.Wqid.Length == 1;

            job.RemoveAsync(new Tremove(13, 1)).GetAwaiter().GetResult();

            var listedAfter = PowerShellTestHelpers.ListJobsAsync(fs).GetAwaiter().GetResult();
            bool absentAfterRemove = !listedAfter.Contains(jobName, StringComparer.Ordinal);

            return qidLooksLikeDirectory && appearsAfterCreate && walkReachedJob && absentAfterRemove;
        }
        finally
        {
            PowerShellTestHelpers.RemoveJobIfPresentAsync(fs, jobName).GetAwaiter().GetResult();
        }
    }
}
