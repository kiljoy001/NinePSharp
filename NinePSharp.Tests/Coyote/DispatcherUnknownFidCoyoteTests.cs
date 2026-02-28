using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class DispatcherUnknownFidCoyoteTests
{
    [Fact]
    public void Coyote_UnknownFid_Operations_Stay_ProtocolBounded()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(120)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var dispatcher = new NinePFSDispatcher(
                NullLogger<NinePFSDispatcher>.Instance,
                Array.Empty<IProtocolBackend>(),
                new Mock<IRemoteMountProvider>().Object);

            const uint fid = 424242;
            var messages = BuildUnknownFidMessages(fid, 100);

            var tasks = messages
                .Select(m => CoyoteTask.Run(() => dispatcher.DispatchAsync(m, NinePDialect.NineP2000U)))
                .ToArray();

            var results = await CoyoteTask.WhenAll(tasks);

            if (results.Any(r => r is not Rerror and not Rlerror))
            {
                throw new Exception("Unexpected non-error response for unknown FID operation.");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }

    private static List<NinePMessage> BuildUnknownFidMessages(uint fid, ushort tagBase)
    {
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "x", "u", "g", "m");
        return new List<NinePMessage>
        {
            NinePMessage.NewMsgTwalk(new Twalk((ushort)(tagBase + 0), fid, fid + 1, new[] { "any" })),
            NinePMessage.NewMsgTopen(new Topen((ushort)(tagBase + 1), fid, 0)),
            NinePMessage.NewMsgTread(new Tread((ushort)(tagBase + 2), fid, 0, 64)),
            NinePMessage.NewMsgTwrite(new Twrite((ushort)(tagBase + 3), fid, 0, new byte[] { 1, 2, 3 })),
            NinePMessage.NewMsgTstat(new Tstat((ushort)(tagBase + 4), fid)),
            NinePMessage.NewMsgTcreate(BuildTcreate((ushort)(tagBase + 5), fid, "f", 0644, 0)),
            NinePMessage.NewMsgTwstat(new Twstat((ushort)(tagBase + 6), fid, stat)),
            NinePMessage.NewMsgTremove(new Tremove((ushort)(tagBase + 7), fid))
        };
    }

    private static Tcreate BuildTcreate(ushort tag, uint fid, string name, uint perm, byte mode)
    {
        int nameLen = Encoding.UTF8.GetByteCount(name);
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameLen + 4 + 1);
        byte[] data = new byte[size];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), size);
        data[4] = (byte)MessageTypes.Tcreate;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5, 2), tag);

        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), fid);
        offset += 4;

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)nameLen);
        offset += 2;

        Encoding.UTF8.GetBytes(name).CopyTo(data.AsSpan(offset, nameLen));
        offset += nameLen;

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), perm);
        offset += 4;

        data[offset] = mode;

        return new Tcreate(data);
    }
}
