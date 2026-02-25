using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Cluster;
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
        public async Task Walk_Clone_Preserves_Path_State()
        {
            var config = new EthereumBackendConfig { MountPath = "/eth", RpcUrl = "http://localhost" };
            var mockRpc = new Mock<JsonRpcClient>(new HttpClient(), config.RpcUrl, string.Empty, string.Empty);
            var mockVault = new Mock<ILuxVaultService>();
            var fs = new EthereumFileSystem(config, mockRpc.Object, mockVault.Object);

            await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "wallets" }));
            var clonedFs = fs.Clone();

            var tread = new Tread((ushort)1, 2u, 0uL, 1000u);
            var rread = await clonedFs.ReadAsync(tread);
            
            var stats = ParseDir(rread.Data.ToArray());
            Assert.Contains(stats, s => s.Name == "use");
            Assert.Contains(stats, s => s.Name == "status");
        }

        [Fact]
        public async Task ReadAsync_Handles_Offsets_For_Incremental_Listing()
        {
            var config = new DatabaseBackendConfig { ProviderName = "System.Data.SQLite", ConnectionString = "Data Source=:memory:" };
            var mockVault = new Moq.Mock<ILuxVaultService>();
            var fs = new DatabaseFileSystem(config, mockVault.Object);

            var fullRead = await fs.ReadAsync(new Tread((ushort)1, 1u, 0uL, 4096u));
            Assert.NotEmpty(fullRead.Data.ToArray());

            int firstEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(fullRead.Data.Span.Slice(0, 2)) + 2;
            Assert.True(firstEntrySize > 0);

            var tread1 = new Tread((ushort)1, 1u, 0uL, (uint)firstEntrySize);
            var rread1 = await fs.ReadAsync(tread1);
            Assert.Equal(firstEntrySize, rread1.Data.Length);

            var tread2 = new Tread((ushort)1, 1u, (ulong)rread1.Data.Length, 1000u);
            var rread2 = await fs.ReadAsync(tread2);

            Assert.NotEmpty(rread2.Data.ToArray());
            
            var combined = rread1.Data.ToArray().Concat(rread2.Data.ToArray()).ToArray();
            var stats = ParseDir(combined);
            Assert.Contains(stats, s => s.Name == "query");
            Assert.Contains(stats, s => s.Name == "status");
        }

        [Fact]
        public async Task Walk_Failed_Does_Not_Create_NewFid()
        {
            var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<NinePFSDispatcher>>();
            var mockBackend = new Moq.Mock<IProtocolBackend>();
            var mockFs = new Moq.Mock<INinePFileSystem>();
            var mockCluster = new Mock<IClusterManager>();
            
            mockBackend.Setup(b => b.MountPath).Returns("/test");
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            
            var dispatcher = new NinePFSDispatcher(logger.Object, new[] { mockBackend.Object }, mockCluster.Object);

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(1, 1, 0, "scott", "test")), false);

            var twalk = new Twalk(1, 1, 2, new[] { "valid", "invalid" });
            mockFs.Setup(f => f.WalkAsync(twalk)).ReturnsAsync(new Rwalk(1, new[] { new Qid(QidType.QTDIR, 0, 1) }));
            mockFs.Setup(f => f.Clone()).Returns(mockFs.Object);

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(twalk), false);

            var tread = new Tread(1, 2, 0, 100);
            var response = await dispatcher.DispatchAsync(NinePMessage.NewMsgTread(tread), false);

            Assert.IsType<Rerror>(response);
            Assert.Equal("Unknown FID", ((Rerror)response).Ename);
        }

        [Fact]
        public async Task Walk_Nwname0_Clones_Fid()
        {
            var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<NinePFSDispatcher>>();
            var mockBackend = new Moq.Mock<IProtocolBackend>();
            var mockFs = new Moq.Mock<INinePFileSystem>();
            var mockCluster = new Mock<IClusterManager>();
            
            mockBackend.Setup(b => b.MountPath).Returns("/test");
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(mockFs.Object);
            
            var dispatcher = new NinePFSDispatcher(logger.Object, new[] { mockBackend.Object }, mockCluster.Object);

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(1, 1, 0, "scott", "test")), false);

            var twalk = new Twalk(1, 1, 2, Array.Empty<string>());
            mockFs.Setup(f => f.Clone()).Returns(mockFs.Object);
            mockFs.Setup(f => f.WalkAsync(twalk)).ReturnsAsync(new Rwalk(1, Array.Empty<Qid>()));

            var response = await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(twalk), false);

            Assert.IsType<Rwalk>(response);
            Assert.Empty(((Rwalk)response).Wqid);

            var tread = new Tread(1, 2, 0, 100);
            mockFs.Setup(f => f.ReadAsync(tread)).ReturnsAsync(new Rread(1, Array.Empty<byte>()));
            var readResponse = await dispatcher.DispatchAsync(NinePMessage.NewMsgTread(tread), false);
            Assert.IsType<Rread>(readResponse);
        }
    }
}
