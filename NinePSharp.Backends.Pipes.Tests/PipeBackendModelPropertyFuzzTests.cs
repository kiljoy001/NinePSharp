using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Backends.Pipes.Tests;

public class PipeBackendModelPropertyFuzzTests
{
    [Property(MaxTest = 55)]
    public bool Create_Write_Read_Remove_RoundTrip_Property(NonEmptyString rawName, NonEmptyArray<byte> payload, PositiveInt categorySeed)
    {
        string category = (categorySeed.Get % 2 == 0) ? "queues" : "pipes";
        string name = BuildUniqueName(rawName.Get);
        byte[] bytes = payload.Get.Take(512).ToArray();
        if (bytes.Length == 0)
        {
            bytes = new byte[] { 0x01 };
        }

        INinePFileSystem fs = new PipeFileSystem();
        fs.WalkAsync(new Twalk(1, 1, 1, new[] { category })).GetAwaiter().GetResult();
        fs.CreateAsync(PipeTestHelpers.BuildCreate(2, 1, name)).GetAwaiter().GetResult();

        var writer = fs.Clone();
        writer.WalkAsync(new Twalk(3, 1, 1, new[] { name })).GetAwaiter().GetResult();
        var write = writer.WriteAsync(new Twrite(4, 1, 0, bytes)).GetAwaiter().GetResult();

        var reader = fs.Clone();
        reader.WalkAsync(new Twalk(5, 1, 1, new[] { name })).GetAwaiter().GetResult();
        var read = reader.ReadAsync(new Tread(6, 1, 0, (uint)bytes.Length)).GetAwaiter().GetResult();

        var remover = fs.Clone();
        remover.WalkAsync(new Twalk(7, 1, 1, new[] { name })).GetAwaiter().GetResult();
        remover.RemoveAsync(new Tremove(8, 1)).GetAwaiter().GetResult();

        bool postRemoveAccepted;
        try
        {
            var post = writer.WriteAsync(new Twrite(9, 1, 0, bytes)).GetAwaiter().GetResult();
            postRemoveAccepted = post.Count == bytes.Length;
        }
        catch
        {
            postRemoveAccepted = false;
        }

        var listingView = new PipeFileSystem();
        listingView.WalkAsync(new Twalk(10, 1, 1, new[] { category })).GetAwaiter().GetResult();
        var listing = listingView.ReadAsync(new Tread(11, 1, 0, 8192)).GetAwaiter().GetResult();
        var names = ParseStatNames(listing.Data.Span);

        bool removedFromListing = !names.Contains(name, StringComparer.Ordinal);
        bool writeAckValid = write.Count == bytes.Length;
        bool readLooksValid = read.Data.Length > 0 && read.Data.Length <= bytes.Length;

        return writeAckValid && readLooksValid && removedFromListing && !postRemoveAccepted;
    }

    [Fact]
    public async System.Threading.Tasks.Task Fuzz_Mixed_Pipe_And_Queue_Operation_Sequences()
    {
        var random = new Random(0xA11CE);

        for (int i = 0; i < 220; i++)
        {
            string category = random.Next(0, 2) == 0 ? "queues" : "pipes";
            string name = $"fuzz_{category}_{i}_{random.Next(100000, 999999)}";

            INinePFileSystem fs = new PipeFileSystem();
            await fs.WalkAsync(new Twalk((ushort)(20 + i), 1, 1, new[] { category }));
            await fs.CreateAsync(PipeTestHelpers.BuildCreate((ushort)(30 + i), 1, name));

            var nodeView = fs.Clone();
            await nodeView.WalkAsync(new Twalk((ushort)(40 + i), 1, 1, new[] { name }));

            byte[] payload = new byte[random.Next(1, 384)];
            random.NextBytes(payload);

            try
            {
                var write = await nodeView.WriteAsync(new Twrite((ushort)(50 + i), 1, 0, payload));
                Assert.True(write.Count == payload.Length, $"Iteration {i}: write ack mismatch");

                uint readCount = (uint)Math.Max(1, random.Next(1, payload.Length + 1));
                var read = await nodeView.ReadAsync(new Tread((ushort)(60 + i), 1, 0, readCount));
                Assert.True(read.Data.Length > 0, $"Iteration {i}: expected non-empty read after write");
            }
            catch (Exception ex)
            {
                Assert.True(ex is NinePProtocolException || ex is NinePNotFoundException || ex is NinePInvalidOperationException,
                    $"Iteration {i}: unexpected exception {ex.GetType().Name}");
            }

            var remover = fs.Clone();
            await remover.WalkAsync(new Twalk((ushort)(70 + i), 1, 1, new[] { name }));
            await remover.RemoveAsync(new Tremove((ushort)(80 + i), 1));
        }
    }

    private static string BuildUniqueName(string raw)
    {
        string filtered = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').Take(16).ToArray());
        if (string.IsNullOrWhiteSpace(filtered))
        {
            filtered = "node";
        }

        return $"{filtered}_{Guid.NewGuid():N}";
    }

    private static IReadOnlyList<string> ParseStatNames(ReadOnlySpan<byte> bytes)
    {
        var names = new List<string>();
        int offset = 0;

        while (offset + 2 <= bytes.Length)
        {
            int start = offset;
            try
            {
                var stat = new Stat(bytes, ref offset);
                if (offset <= start)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(stat.Name))
                {
                    names.Add(stat.Name);
                }
            }
            catch
            {
                break;
            }
        }

        return names;
    }
}
