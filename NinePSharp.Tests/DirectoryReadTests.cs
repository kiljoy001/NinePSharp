using System.Text;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.FileSystem;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

// 9P2000's directory read, as 9front's clients make it: Tread on an open directory gives whole stat
// records from the offset, as many as fit in the count, and nothing once they are all read.
public class DirectoryReadTests
{
    [Fact]
    public async Task A_directory_read_gives_a_stat_record_for_each_entry()
    {
        NinePFSDispatcher dispatcher = await OpenRootAsync();

        Rread read = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 4, 2, 0, 8192);

        Assert.Equal(["notes", "docs"], Names(read.Data.ToArray()));
        Assert.Empty((await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 5, 2, read.Count, 8192)).Data.ToArray());
    }

    [Fact]
    public async Task A_directory_read_gives_only_whole_records()
    {
        NinePFSDispatcher dispatcher = await OpenRootAsync();
        int first = (await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 4, 2, 0, 8192)).Data.Span[0] + 2;

        Rread read = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 5, 2, 0, (uint)first + 5);

        Assert.Equal(["notes"], Names(read.Data.ToArray()));
        Assert.Equal(["docs"], Names((await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 6, 2, read.Count, 8192)).Data.ToArray()));
    }

    private static async Task<NinePFSDispatcher> OpenRootAsync()
    {
        var root = new NinePDir("/");
        root.AddChild(new NinePFile("notes", Encoding.UTF8.GetBytes("hello")));
        root.AddChild(new NinePDir("docs"));
        NinePFSDispatcher dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new FileSystemBackend(root));
        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 1);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 1, 2, []);
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 2, NinePConstants.OREAD);
        return dispatcher;
    }

    private static string[] Names(byte[] records)
    {
        var names = new System.Collections.Generic.List<string>();
        for (int offset = 0; offset < records.Length;)
        {
            names.Add(new Stat(records, ref offset).Name);
        }

        return [.. names];
    }
}
