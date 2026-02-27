using NinePSharp.Constants;
using System;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using Xunit;

namespace NinePSharp.Tests;

public class SrvBackendTests
{
    [Fact]
    public async Task Write_Creates_Pipe_And_Read_Retrieves_Data()
    {
        // Arrange
        var fs = new SrvFileSystem();
        var pipeName = "test-pipe";
        var content = "SecretData";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Act - Write
        // Walk to root
        var walkRoot = await fs.WalkAsync(new Twalk(1, 0, 1, Array.Empty<string>()));
        
        // Write to a new pipe name
        // Srv logic: Write to root with a specific name creates the pipe? 
        // Or walk to name (which doesn't exist yet)?
        // Let's check SrvFileSystem implementation.
        // It seems WriteAsync checks if _currentPath.Count == 0, throws.
        // So we must walk to the name first.
        
        // Walk to 'test-pipe'
        var walkPipe = await fs.WalkAsync(new Twalk(1, 0, 1, new[] { pipeName }));
        // SrvFileSystem.WalkAsync allows walking to anything, it just creates a Qid.
        
        // Write content
        var write = await fs.WriteAsync(new Twrite(1, 1, 0, contentBytes));
        
        // Assert Write
        Assert.Equal((uint)contentBytes.Length, write.Count);

        // Act - Read
        // Read from the same path (we are already there)
        var read = await fs.ReadAsync(new Tread(1, 1, 0, 8192));
        var result = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert Read
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task Read_Root_Lists_Pipes()
    {
        // Arrange
        var fs = new SrvFileSystem();
        var pipeName = "list-test";
        var contentBytes = Encoding.UTF8.GetBytes("data");

        // Create a pipe
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { pipeName }));
        await fs.WriteAsync(new Twrite(1, 1, 0, contentBytes));

        // Act - Read Root
        // We need a fresh FS or clone pointing to root
        var rootFs = fs.Clone(); // Clone preserves the static dictionary?
        // Wait, the dictionary is static: private static readonly ConcurrentDictionary<string, SrvEntry> _pipes = new();
        // So a new instance sees it too.
        var freshFs = new SrvFileSystem(); // Points to root by default
        
        var read = await freshFs.ReadAsync(new Tread(1, 0, 0, 8192));
        var listing = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert
        Assert.Contains(pipeName, listing);
    }

    [Fact]
    public async Task Remove_Deletes_Pipe()
    {
        // Arrange
        var fs = new SrvFileSystem();
        var pipeName = "delete-test";
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { pipeName }));
        await fs.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes("data")));

        // Act
        await fs.RemoveAsync(new Tremove(1, 1));

        // Assert
        var rootFs = new SrvFileSystem();
        var read = await rootFs.ReadAsync(new Tread(1, 0, 0, 8192));
        var listing = Encoding.UTF8.GetString(read.Data.ToArray());
        
        Assert.DoesNotContain(pipeName, listing);
    }
}
