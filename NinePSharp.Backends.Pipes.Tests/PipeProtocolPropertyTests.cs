using System;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Backends.Pipes.Tests;

public class PipeProtocolPropertyTests
{
    [Property(MaxTest = 40)]
    public void Walk_ToMissing_Node_DoesNot_Report_FullSuccess(string rawName)
    {
        INinePFileSystem fs = new PipeFileSystem();
        string missingName = BuildUniqueName(rawName);

        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" })).GetAwaiter().GetResult();
        var walk = fs.WalkAsync(new Twalk(2, 2, 3, new[] { missingName })).GetAwaiter().GetResult();

        // Missing children must not be acknowledged as a full successful walk.
        Assert.True(walk.Wqid.Length < 1, $"Walk unexpectedly succeeded for missing node '{missingName}'");
    }

    [Property(MaxTest = 35)]
    public void Write_ToMissing_Node_Must_Not_Acknowledge_All_Bytes(string rawName, NonEmptyArray<byte> payload)
    {
        INinePFileSystem fs = new PipeFileSystem();
        string missingName = BuildUniqueName(rawName);
        byte[] bytes = payload.Get.Take(256).ToArray();

        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" })).GetAwaiter().GetResult();
        fs.WalkAsync(new Twalk(2, 2, 3, new[] { missingName })).GetAwaiter().GetResult();

        bool threw = false;
        uint acknowledged = 0;
        try
        {
            var write = fs.WriteAsync(new Twrite(3, 3, 0, bytes)).GetAwaiter().GetResult();
            acknowledged = write.Count;
        }
        catch
        {
            threw = true;
        }

        Assert.True(threw || acknowledged < bytes.Length,
            $"Write to missing node '{missingName}' was fully acknowledged ({acknowledged}/{bytes.Length})");
    }

    [Property(MaxTest = 25)]
    public void Read_On_Populated_Directory_Should_Not_Be_Always_Empty(PositiveInt countSeed)
    {
        int nodeCount = Math.Clamp(countSeed.Get % 6 + 1, 1, 6);
        INinePFileSystem setupFs = new PipeFileSystem();

        setupFs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" })).GetAwaiter().GetResult();
        for (int i = 0; i < nodeCount; i++)
        {
            string queueName = $"q{i:00}_{Guid.NewGuid():N}";
            setupFs.CreateAsync(PipeTestHelpers.BuildCreate((ushort)(10 + i), 2, queueName)).GetAwaiter().GetResult();
        }

        INinePFileSystem dirFs = new PipeFileSystem();
        dirFs.WalkAsync(new Twalk(50, 1, 2, new[] { "queues" })).GetAwaiter().GetResult();
        var read = dirFs.ReadAsync(new Tread(51, 2, 0, 8192)).GetAwaiter().GetResult();

        Assert.True(read.Data.Length > 0, "Directory reads should expose entries when queues exist");
    }

    private static string BuildUniqueName(string? raw)
    {
        string baseName = string.IsNullOrWhiteSpace(raw)
            ? "missing"
            : new string(raw.Where(char.IsLetterOrDigit).Take(12).ToArray());

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "missing";
        }

        return $"{baseName}_{Guid.NewGuid():N}";
    }
}
