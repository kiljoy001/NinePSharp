namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class StreamingDirectoryFixture : IAsyncDisposable, IDirectoryStatOperations
{
    internal StreamingDirectoryFixture()
    {
        Codec = new DirectoryStatOperations(
            Files.Resources,
            new[] { "root", "A", "B", "C", "X", "replacement" }
                .Select((name, i) => new DirectoryDeviceBinding(7, (uint)i, "memory-data", name)));
        Calls = ForProcess(Files.Process);
        Files.Plane.OpenOverride = async (channel, mode, context, token) =>
        {
            if (BeforeOpen is not null)
            {
                await BeforeOpen(channel.Current);
            }

            ResourceOpenHandle opened = await Files.Local.OpenAsync(channel, mode, context, token);
            Opens.Add(opened);
            return opened;
        };
        Files.Plane.ClunkOverride = async (handle, context, token) =>
        {
            Closes.Add(handle);
            await Files.Local.ClunkAsync(handle, context, token);
        };
        Files.Plane.ReadOverride = async (handle, offset, count, _) =>
        {
            Reads.Add((handle, offset, count));
            if (ReadOverride is not null)
            {
                return await ReadOverride(handle, offset, count);
            }

            return ReadRecords(handle.Resource, offset, count);
        };
        Records[Root.Identity] = new[] { Record("first", 64, 100), Record("second", 72, 101) };
    }

    internal FileSyscallFixture Files { get; } = new();

    internal ResourceHandle Root => Files.Process.Root.Current;

    internal MountTable Mounts => Files.Process.ProcessGroup.MountTable;

    internal Plan9FileSyscalls Calls { get; }

    internal DirectoryStatOperations Codec { get; }

    internal Dictionary<ResourceIdentity, byte[][]> Records { get; } = new();

    internal List<(ResourceOpenHandle Handle, ulong Offset, uint Count)> Reads { get; } = new();

    internal List<ResourceOpenHandle> Opens { get; } = new();

    internal List<ResourceOpenHandle> Closes { get; } = new();

    internal Func<ResourceOpenHandle, ulong, uint, ValueTask<ReadOnlyMemory<byte>>>? ReadOverride { get; set; }

    internal Func<ResourceHandle, ValueTask>? BeforeOpen { get; set; }

    internal Func<ResourceHandle, uint, ValueTask<ReadOnlyMemory<byte>>>? StatOverride { get; set; }

    internal List<(ResourceHandle Resource, uint Count)> Stats { get; } = new();

    public ResourceIdentity ResolveIdentity(ushort type, uint device, ulong path) => Codec.ResolveIdentity(type, device, path);

    public async ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken token)
    {
        Stats.Add((resource, count));
        return StatOverride is null ? await Codec.StatAsync(resource, count, token) : await StatOverride(resource, count);
    }

    public ValueTask DisposeAsync() => Files.DisposeAsync();

    internal Plan9FileSyscalls ForProcess(VProcess process) => new(
        process,
        Files.Plane,
        Files.Context,
        DirectoryReadMode.ProviderStream,
        this);

    internal byte[] Record(string name, int size, ulong path = 100)
        => Codec.Encode(new ResourceStat(
            new ResourceHandle(new("memory-data", "root", path), 0),
            name,
            0x180,
            2,
            3,
            4,
            new string('u', size - 49 - System.Text.Encoding.UTF8.GetByteCount(name)),
            string.Empty,
            string.Empty));

    internal ResourceHandle Directory(string device, params byte[][] records)
    {
        ResourceHandle directory = Files.Resources.Directory(device);
        Records[directory.Identity] = records;
        return directory;
    }

    internal void Union(params ResourceHandle[] members)
    {
        Mounts.Mount(members[0], Root);
        foreach (ResourceHandle member in members.Skip(1))
        {
            Mounts.Mount(member, Root, MountFlags.After);
        }
    }

    internal ReadOnlyMemory<byte> ReadRecords(ResourceHandle resource, ulong offset, uint count)
    {
        if (count == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        byte[][] records = Records[resource.Identity];
        ulong position = 0;
        var selected = new List<byte>();
        foreach (byte[] record in records)
        {
            if (position < offset)
            {
                position += (uint)record.Length;
                if (position > offset)
                {
                    throw new ResourceDirectoryRejectedException("offset splits a record");
                }

                continue;
            }

            if ((long)selected.Count + record.Length > count)
            {
                if (selected.Count == 0)
                {
                    throw new ResourceDirectoryRejectedException("small buffer");
                }

                break;
            }

            selected.AddRange(record);
            position += (uint)record.Length;
        }

        return selected.ToArray();
    }

    internal void ReplaceRecord(ulong path, ResourceHandle target)
        => Mounts.Mount(
            NamespaceChannel.Restore(new[] { new ChannelFrame("/", target) }),
            new ResourceHandle(new("memory-data", "root", path), target.Type));
}
