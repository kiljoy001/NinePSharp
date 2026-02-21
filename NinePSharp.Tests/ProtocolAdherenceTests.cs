using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Moq;
using Xunit;

namespace NinePSharp.Tests
{
    public class ProtocolAdherenceTests
    {
        [Fact]
        public async Task Walk_Clone_Preserves_Path_State()
        {
            // Setup Ethereum FS
            var config = new EthereumBackendConfig { RpcUrl = "http://localhost" };
            var mockWeb3 = new Mock<Nethereum.Web3.IWeb3>();
            var mockVault = new Mock<ILuxVaultService>();
            var fs = new EthereumFileSystem(config, mockWeb3.Object, mockVault.Object);

            // 1. Walk to 'wallets'
            var twalk1 = new Twalk(1, 1, 2, new[] { "wallets" });
            await fs.WalkAsync(twalk1);

            // 2. Clone the FS (simulating Twalk with newfid != fid)
            var clonedFs = fs.Clone();

            // 3. Read from cloned FS - it should still be at 'wallets'
            var tread = new Tread(1, 2, 0, 100);
            var rread = await clonedFs.ReadAsync(tread);
            
            string content = Encoding.UTF8.GetString(rread.Data.ToArray());
            
            // Expected content if at 'wallets': "create\nunlock\n"
            Assert.Contains("create", content);
            Assert.Contains("unlock", content);
            Assert.DoesNotContain("contracts", content);
        }

        [Fact]
        public async Task ReadAsync_Handles_Offsets_For_Incremental_Listing()
        {
            // Setup Database FS
            var config = new DatabaseBackendConfig { ProviderName = "System.Data.SQLite", ConnectionString = "Data Source=:memory:" };
            var mockVault = new Moq.Mock<ILuxVaultService>();
            var fs = new DatabaseFileSystem(config, mockVault.Object);

            // 1. Read first part of root (offset 0, count 20)
            // Note: Each Stat entry is ~40-60 bytes, so 20 bytes should return a partial entry 
            // OR the server should return only complete entries that fit.
            // 9P protocol: Read returns AT MOST 'count' bytes.
            var tread1 = new Tread(1, 1, 0, 30); 
            var rread1 = await fs.ReadAsync(tread1);
            
            Assert.NotEmpty(rread1.Data.ToArray());
            Assert.True(rread1.Data.Length <= 30);

            // 2. Read next part (offset = rread1.Data.Length)
            var tread2 = new Tread(1, 1, (ulong)rread1.Data.Length, 1000);
            var rread2 = await fs.ReadAsync(tread2);

            Assert.NotEmpty(rread2.Data.ToArray());
            
            // The combined data should contain our expected tables
            string combined = Encoding.UTF8.GetString(rread1.Data.ToArray().Concat(rread2.Data.ToArray()).ToArray());
            Assert.Contains("Users", combined);
            Assert.Contains("Products", combined);
        }

        [Fact]
        public async Task Walk_Failed_Does_Not_Create_NewFid()
        {
            var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<NinePFSDispatcher>>();
            var mockBackend = new Moq.Mock<IProtocolBackend>();
            var mockFs = new Moq.Mock<INinePFileSystem>();
            
            mockBackend.Setup(b => b.MountPath).Returns("/test");
            mockBackend.Setup(b => b.GetFileSystem(null)).Returns(mockFs.Object);
            
            var dispatcher = new NinePFSDispatcher(logger.Object, new[] { mockBackend.Object });

            // 1. Attach to get FID 1
            await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTattach(new Tattach(1, 1, 0, "scott", "test")));

            // 2. Setup mock walk to FAIL (return Rwalk with fewer QIDs than requested)
            // In 9P, if Rwalk QIDs < Twalk Wnames, it's a partial walk and newfid is NOT created.
            var twalk = new Twalk(1, 1, 2, new[] { "valid", "invalid" });
            mockFs.Setup(f => f.WalkAsync(twalk)).ReturnsAsync(new Rwalk(1, new[] { new Qid(QidType.QTDIR, 0, 1) }));
            mockFs.Setup(f => f.Clone()).Returns(mockFs.Object);

            await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTwalk(twalk));

            // 3. Attempt to use FID 2 - it should fail because the walk wasn't full
            var tread = new Tread(1, 2, 0, 100);
            var response = await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTread(tread));

            Assert.IsType<Rerror>(response);
            Assert.Equal("Unknown FID", ((Rerror)response).Ename);
        }
    }
}
