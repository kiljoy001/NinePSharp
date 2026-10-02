using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

[GenerateSerializer]
public sealed class TestResourceNode
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;

    [Id(1)]
    public bool Directory { get; set; }

    [Id(2)]
    public uint Version { get; set; }

    [Id(3)]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    [Id(4)]
    public Dictionary<string, ulong> Children { get; set; } = new(StringComparer.Ordinal);

    [Id(5)]
    public uint Mode { get; set; }

    [Id(6)]
    public uint ModificationTime { get; set; }
}
