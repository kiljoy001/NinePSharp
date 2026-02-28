using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using Moq;
using Xunit;

namespace NinePSharp.Tests
{
    public class ProtocolAdherenceTests
    {
        private List<Stat> ParseDir(byte[] data)
        {
            var stats = new List<Stat>();
            int offset = 0;
            while (offset < data.Length)
            {
                stats.Add(new Stat(data, ref offset));
            }
            return stats;
        }

        [Fact]
        public async Task Walk_Failed_Does_Not_Create_NewFid()
        {
            var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<NinePFSDispatcher>>();
            var mockBackend = new Moq.Mock<IProtocolBackend>();
            var mockFs = new Moq.Mock<INinePFileSystem>();
            var mockCluster = new Mock<IRemoteMountProvider>();
            
            mockBackend.Setup(b => b.MountPath).Returns("/test");
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            
            var dispatcher = new NinePFSDispatcher(logger.Object, new[] { mockBackend.Object }, mockCluster.Object);

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(1, 1, 0, "scott", "test")), NinePDialect.NineP2000);

            var twalk = new Twalk(1, 1, 2, new[] { "valid", "invalid" });
            mockFs.Setup(f => f.WalkAsync(twalk)).ReturnsAsync(new Rwalk(1, new[] { new Qid(QidType.QTDIR, 0, 1) }));
            mockFs.Setup(f => f.Clone()).Returns(mockFs.Object);

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(twalk), NinePDialect.NineP2000);

            var tread = new Tread(1, 2, 0, 100);
            var response = await dispatcher.DispatchAsync(NinePMessage.NewMsgTread(tread), NinePDialect.NineP2000);

            Assert.IsType<Rerror>(response);
            Assert.Equal("Unknown FID", ((Rerror)response).Ename);
        }

        [Fact]
        public async Task Walk_Nwname0_Clones_Fid()
        {
            var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<NinePFSDispatcher>>();
            var mockBackend = new Moq.Mock<IProtocolBackend>();
            var mockFs = new Moq.Mock<INinePFileSystem>();
            var mockCluster = new Mock<IRemoteMountProvider>();
            
            mockBackend.Setup(b => b.MountPath).Returns("/test");
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            
            var dispatcher = new NinePFSDispatcher(logger.Object, new[] { mockBackend.Object }, mockCluster.Object);

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(1, 1, 0, "scott", "test")), NinePDialect.NineP2000);

            var twalk = new Twalk(1, 1, 2, Array.Empty<string>());
            mockFs.Setup(f => f.Clone()).Returns(mockFs.Object);
            mockFs.Setup(f => f.WalkAsync(twalk)).ReturnsAsync(new Rwalk(1, Array.Empty<Qid>()));

            var response = await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(twalk), NinePDialect.NineP2000);

            Assert.IsType<Rwalk>(response);
            Assert.Empty(((Rwalk)response).Wqid);

            var tread = new Tread(1, 2, 0, 100);
            mockFs.Setup(f => f.ReadAsync(tread)).ReturnsAsync(new Rread(1, Array.Empty<byte>()));
            var readResponse = await dispatcher.DispatchAsync(NinePMessage.NewMsgTread(tread), NinePDialect.NineP2000);
            Assert.IsType<Rread>(readResponse);
        }
    }
}
