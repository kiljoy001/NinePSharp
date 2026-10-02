using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>Adapts typed resource metadata to the native directory stat boundary.</summary>
public sealed class DirectoryStatOperations : IDirectoryStatOperations
{
    private readonly IResourceDataOperations resources;
    private readonly IReadOnlyList<DirectoryDeviceBinding> devices;

    /// <summary>Initializes a new instance of the <see cref="DirectoryStatOperations"/> class. The wire device mapping is explicit and unambiguous.</summary>
    public DirectoryStatOperations(IResourceDataOperations resources, IEnumerable<DirectoryDeviceBinding> devices)
    {
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        this.devices = devices.ToArray();
        if (this.devices.Select(d => (d.Type, d.Device)).Distinct().Count() != this.devices.Count
            || this.devices.Select(d => (d.Provider, d.ResourceDevice)).Distinct().Count() != this.devices.Count)
        {
            throw new ArgumentException("directory device mappings must be unique", nameof(devices));
        }
    }

    /// <inheritdoc/>
    public ResourceIdentity ResolveIdentity(ushort type, uint device, ulong path)
    {
        DirectoryDeviceBinding binding = devices.SingleOrDefault(d => d.Type == type && d.Device == device)
            ?? throw new IOException("unmapped directory device identity");
        return new ResourceIdentity(binding.Provider, binding.ResourceDevice, path);
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken)
    {
        ResourceStat stat = await resources.StatAsync(resource, cancellationToken);
        byte[] record = Encode(stat);
        return record.Length <= count ? record : record.AsMemory(0, Math.Min(2, (int)count));
    }

    /// <summary>Encodes provider metadata using the configured wire identity.</summary>
    public byte[] Encode(ResourceStat stat)
    {
        DirectoryDeviceBinding binding = devices.SingleOrDefault(d =>
            d.Provider == stat.Resource.Identity.Provider && d.ResourceDevice == stat.Resource.Identity.Device)
            ?? throw new IOException("unmapped resource device identity");
        byte[][] strings = new[] { stat.Name, stat.User, stat.Group, stat.LastModifier }
            .Select(Encoding.UTF8.GetBytes).ToArray();
        int size = 49 + strings.Sum(s => s.Length);
        if (size - 2 > ushort.MaxValue)
        {
            throw new IOException("directory stat exceeds the wire record limit");
        }

        using var buffer = new MemoryStream(size);
        using var writer = new BinaryWriter(buffer, Encoding.UTF8);
        writer.Write((ushort)(size - 2));
        writer.Write(binding.Type);
        writer.Write(binding.Device);
        writer.Write((byte)stat.Resource.Qid.Type);
        writer.Write(stat.Resource.Qid.Version);
        writer.Write(stat.Resource.Qid.Path);
        writer.Write(stat.Mode);
        writer.Write(stat.AccessTime);
        writer.Write(stat.ModificationTime);
        writer.Write(stat.Length);
        foreach (byte[] value in strings)
        {
            writer.Write((ushort)value.Length);
            writer.Write(value);
        }

        return buffer.ToArray();
    }
}
