using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Client.Tests;

public sealed class AttachDialectTests
{
    [Theory]
    [InlineData("9P2000", false)]
    [InlineData("9P2000.u", true)]
    [InlineData("9P2000.L", true)]
    public async Task AttachMatchesNegotiatedDialectAndResetsOnRenegotiation(string version, bool extended)
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task server = Task.Run(
            async () =>
        {
            byte[] firstVersion = await ReadAsync();
            await ReplyAsync(new Rversion(Tag(firstVersion), 8192, version));
            byte[] firstAttach = await ReadAsync();
            var attach = new Tattach(firstAttach, extended);
            Assert.Equal(extended ? uint.MaxValue : (uint?)null, attach.NUname);
            Assert.Equal(extended ? 28 : 24, firstAttach.Length);
            await ReplyAsync(new Rattach(attach.Tag, new Qid(QidType.QTDIR, 0, 1)));
            byte[] secondVersion = await ReadAsync();
            await ReplyAsync(new Rversion(Tag(secondVersion), 8192, "9P2000"));
            byte[] secondAttach = await ReadAsync();
            Assert.Equal(24, secondAttach.Length);
            await ReplyAsync(new Rattach(Tag(secondAttach), new Qid(QidType.QTDIR, 0, 1)));
        },
            timeout.Token);

        await client.VersionAsync(version: version).WaitAsync(timeout.Token);
        await client.AttachAsync(1, NinePConstants.NoFid, "user", "/").WaitAsync(timeout.Token);
        await client.VersionAsync(version: "9P2000").WaitAsync(timeout.Token);
        await client.AttachAsync(1, NinePConstants.NoFid, "user", "/").WaitAsync(timeout.Token);
        await server.WaitAsync(timeout.Token);

        async Task<byte[]> ReadAsync()
        {
            byte[] header = new byte[7];
            await serverStream.ReadExactlyAsync(header, timeout.Token);
            byte[] packet = new byte[BinaryPrimitives.ReadUInt32LittleEndian(header)];
            header.CopyTo(packet, 0);
            await serverStream.ReadExactlyAsync(packet.AsMemory(7), timeout.Token);
            return packet;
        }

        async Task ReplyAsync(ISerializable reply)
        {
            byte[] bytes = new byte[reply.Size];
            reply.WriteTo(bytes);
            await serverStream.WriteAsync(bytes, timeout.Token);
        }
    }

    private static ushort Tag(byte[] bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5));
}
