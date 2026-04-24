using System.Text;
using System.Threading.Tasks;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Interfaces;
using NinePSharp.Server.FileSystem;
using Xunit;

namespace NinePSharp.Client.Tests;

public class RemoteFsTests
{
    [Fact]
    public async Task RemoteFs_Integration_Works()
    {
        var root = new NinePDir("/");
        root.AddChild(new NinePFile("hello.txt", Encoding.UTF8.GetBytes("hello")));
        var backend = new FileSystemBackend(root);
        backend.Dialect = NinePDialect.NineP2000L;

        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

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

                    ISerializable? resp = (MessageTypes)type switch {
                        MessageTypes.Tversion => new Rversion(tag, 8192, "9P2000.L"),
                        MessageTypes.Tattach => await backend.AttachAsync(new Tattach(full), default),
                        MessageTypes.Twalk => await backend.WalkAsync(new string[0], new Twalk(full), default),
                        MessageTypes.Topen => await backend.OpenAsync(new string[0], new Topen(full), default),
                        MessageTypes.Tread => await backend.ReadAsync(new[] { "hello.txt" }, new Tread(full), default),
                        MessageTypes.Tstat => await backend.StatAsync(new[] { "hello.txt" }, new Tstat(full), default),
                        MessageTypes.Tclunk => await backend.ClunkAsync(new string[0], new Tclunk(full), ct: default),
                        _ => null
                    };

                    if (resp != null) {
                        byte[] buf = new byte[resp.Size];
                        resp.WriteTo(buf);
                        await serverStream.WriteAsync(buf, default);
                        await serverStream.FlushAsync();
                    }
                }
            } catch {}
        });

        var fs = await client.MountAsync();
        var content = await fs.ReadFileAsync("/hello.txt");
        var stat = await fs.StatAsync("/hello.txt");
        
        Assert.Equal("hello", Encoding.UTF8.GetString(content));
        Assert.Equal("hello.txt", stat.Name);
    }
}
