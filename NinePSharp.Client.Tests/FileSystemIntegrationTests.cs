using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.FileSystem;
using Xunit;

namespace NinePSharp.Client.Tests;

public class FileSystemIntegrationTests
{
    [Fact]
    public async Task FileSystemBackend_Integration_ReadWrite_Works()
    {
        var root = new NinePDir("/");
        root.AddChild(new NinePFile("hello.txt", Encoding.UTF8.GetBytes("Hello 9P!")));
        
        var backend = new FileSystemBackend(root);
        backend.Dialect = NinePDialect.NineP2000L;

        // Act - Walk
        var rwalk = await backend.WalkAsync(new string[0], new Twalk(1, 0, 1, new[] { "hello.txt" }), default);
        Assert.Single(rwalk.Wqid);

        // Act - Read
        var rread = await backend.ReadAsync(new[] { "hello.txt" }, new Tread(1, 1, 0, 100), default);
        var content = Encoding.UTF8.GetString(rread.Data.ToArray());
        Assert.Equal("Hello 9P!", content);

        // Act - Write
        await backend.WriteAsync(new[] { "hello.txt" }, new Twrite(1, 1, 0, Encoding.UTF8.GetBytes("Updated")), default);
        var rread2 = await backend.ReadAsync(new[] { "hello.txt" }, new Tread(1, 1, 0, 100), default);
        Assert.Equal("Updated", Encoding.UTF8.GetString(rread2.Data.ToArray()));
    }

    [Fact]
    public async Task RemoteFs_Integration_With_FileSystemBackend()
    {
        var root = new NinePDir("/");
        root.AddChild(new NinePFile("hello.txt", Encoding.UTF8.GetBytes("Hello 9P!")));
        var backend = new FileSystemBackend(root);
        backend.Dialect = NinePDialect.NineP2000L;

        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

        // Server loop handling backend
        var serverTask = Task.Run(async () => {
            try {
                while (true) {
                    byte[] header = new byte[NinePConstants.HeaderSize];
                    await serverStream.ReadExactlyAsync(header, default);
                    uint size = BitConverter.ToUInt32(header, 0);
                    byte type = header[4];
                    ushort tag = BitConverter.ToUInt16(header, 5);
                    byte[] payload = new byte[size - NinePConstants.HeaderSize];
                    if (payload.Length > 0) await serverStream.ReadExactlyAsync(payload, default);

                    byte[] full = new byte[size];
                    header.CopyTo(full, 0); payload.CopyTo(full, NinePConstants.HeaderSize);

                    ISerializable? response = (MessageTypes)type switch {
                        MessageTypes.Tversion => new Rversion(tag, 8192, "9P2000.L"),
                        MessageTypes.Tattach => await backend.AttachAsync(new Tattach(full), default),
                        MessageTypes.Twalk => await backend.WalkAsync(new string[0], new Twalk(full), default),
                        MessageTypes.Topen => await backend.OpenAsync(new string[0], new Topen(full), default),
                        MessageTypes.Tread => await backend.ReadAsync(new[] { "hello.txt" }, new Tread(full), default),
                        MessageTypes.Tclunk => await backend.ClunkAsync(new string[0], new Tclunk(full), ct: default),
                        _ => null
                    };

                    if (response != null) {
                        byte[] buf = new byte[response.Size];
                        response.WriteTo(buf);
                        await serverStream.WriteAsync(buf, default);
                        await serverStream.FlushAsync();
                    }
                }
            } catch {}
        });

        var fs = await client.MountAsync();
        var content = await fs.ReadFileAsync("/hello.txt");
        Assert.Equal("Hello 9P!", Encoding.UTF8.GetString(content));
    }
}
