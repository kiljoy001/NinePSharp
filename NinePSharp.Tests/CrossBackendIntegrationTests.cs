using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;

namespace NinePSharp.Tests;

public class CrossBackendIntegrationTests
{
    private static readonly ILuxVaultService _vault = new LuxVaultService();

    private Mock<IProtocolBackend> CreateMockBackend(string name, string mountPath, INinePFileSystem fs)
    {
        var backend = new Mock<IProtocolBackend>();
        backend.Setup(b => b.Name).Returns(name);
        backend.Setup(b => b.MountPath).Returns(mountPath);
        backend.Setup(b => b.GetFileSystem()).Returns(fs.Clone());
        return backend;
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Lists_Multiple_MockAndSecretBackends()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mockBackend.Object, secretBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act - ReaddirAsync at root should list both backends
        var result = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().BeGreaterThan(0);

        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("mock");
        dataString.Should().Contain("secrets");
    }

    [Fact]
    public async Task RootFileSystem_StatfsAsync_At_Root_With_Multiple_Backends()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mockBackend.Object, secretBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act - StatfsAsync at root
        var result = await rootFs.StatfsAsync(new Tstatfs(100, 1, 1));

        // Assert
        result.Should().BeOfType<Rstatfs>();
        result.BSize.Should().Be(4096);
        result.Blocks.Should().BeGreaterThan(0);
        result.Files.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RootFileSystem_With_Only_MockBackend()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var backends = new List<IProtocolBackend> { mockBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act
        var result = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert
        result.Should().BeOfType<Rreaddir>();
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("mock");
        dataString.Should().NotContain("secrets");
    }

    [Fact]
    public async Task RootFileSystem_With_Only_SecretBackend()
    {
        // Arrange
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);
        var backends = new List<IProtocolBackend> { secretBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act
        var result = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert
        result.Should().BeOfType<Rreaddir>();
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("secrets");
        dataString.Should().NotContain("mock");
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Pagination_With_Multiple_Backends()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mockBackend.Object, secretBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act - Read with offset
        var result1 = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));
        var result2 = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 50, 8192));

        // Assert
        result1.Count.Should().BeGreaterThan(0);
        result2.Count.Should().BeLessThanOrEqualTo(result1.Count);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Respects_Count_Limit()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mockBackend.Object, secretBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act - Read with small count limit
        var result = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 50));

        // Assert - Should respect count limit
        result.Count.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task RootFileSystem_With_Three_Different_Backends()
    {
        // Arrange
        var mockFs1 = new MockFileSystem(_vault);
        var mockFs2 = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mock1 = CreateMockBackend("mock1", "/mock1", mockFs1);
        var mock2 = CreateMockBackend("mock2", "/mock2", mockFs2);
        var secret = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mock1.Object, mock2.Object, secret.Object };
        var rootFs = new RootFileSystem(backends);

        // Act
        var result = await rootFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert - Should list all three backends
        result.Count.Should().BeGreaterThan(0);
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("mock1");
        dataString.Should().Contain("mock2");
        dataString.Should().Contain("secrets");
    }

    [Fact]
    public async Task RootFileSystem_Clone_Preserves_Backends_List()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mockBackend.Object, secretBackend.Object };
        var rootFs1 = new RootFileSystem(backends);

        // Act - Clone
        var rootFs2 = (RootFileSystem)rootFs1.Clone();
        var result = await rootFs2.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert - Clone should still have access to backends
        result.Count.Should().BeGreaterThan(0);
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("mock");
        dataString.Should().Contain("secrets");
    }

    [Fact]
    public async Task MockFileSystem_And_SecretFileSystem_Have_Different_Statfs_Results()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        // Act - Get statfs from each
        var mockStatfs = await mockFs.StatfsAsync(new Tstatfs(100, 1, 1));
        var secretStatfs = await secretFs.StatfsAsync(new Tstatfs(100, 1, 1));

        // Assert - Both should return valid results
        mockStatfs.Should().BeOfType<Rstatfs>();
        secretStatfs.Should().BeOfType<Rstatfs>();

        // Both should have same filesystem constants
        mockStatfs.FsType.Should().Be(0x01021997);
        secretStatfs.FsType.Should().Be(0x01021997);
        mockStatfs.BSize.Should().Be(4096);
        secretStatfs.BSize.Should().Be(4096);
    }

    [Fact]
    public async Task MockFileSystem_And_SecretFileSystem_Have_Independent_Readdir()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        // Act - Readdir from each
        var mockReaddir = await mockFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));
        var secretReaddir = await secretFs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert - Each should list different content
        var mockData = Encoding.UTF8.GetString(mockReaddir.Data.Span);
        var secretData = Encoding.UTF8.GetString(secretReaddir.Data.Span);

        // Secret FS should have its specific files
        secretData.Should().Contain("provision");
        secretData.Should().Contain("unlock");
        secretData.Should().Contain("vault");

        // Mock FS starts empty
        mockReaddir.Count.Should().Be(0);
    }

    [Fact]
    public async Task RootFileSystem_StatfsAsync_Returns_Consistent_Values()
    {
        // Arrange
        var mockFs = new MockFileSystem(_vault);
        var secretFs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);

        var mockBackend = CreateMockBackend("mock", "/mock", mockFs);
        var secretBackend = CreateMockBackend("secret", "/secrets", secretFs);

        var backends = new List<IProtocolBackend> { mockBackend.Object, secretBackend.Object };
        var rootFs = new RootFileSystem(backends);

        // Act - Call statfs multiple times
        var result1 = await rootFs.StatfsAsync(new Tstatfs(100, 1, 1));
        var result2 = await rootFs.StatfsAsync(new Tstatfs(100, 1, 1));

        // Assert - Should return consistent values
        result1.BSize.Should().Be(result2.BSize);
        result1.Blocks.Should().Be(result2.Blocks);
        result1.FsType.Should().Be(result2.FsType);
    }
}
