using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

[GenerateSerializer]
public sealed class TestCompletedOperation
{
    [Id(0)]
    public string Fingerprint { get; set; } = string.Empty;

    [Id(1)]
    public ulong ResourcePath { get; set; }

    [Id(2)]
    public string HandleId { get; set; } = string.Empty;

    [Id(3)]
    public byte Mode { get; set; }

    [Id(4)]
    public uint Count { get; set; }

    [Id(5)]
    public bool Rejected { get; set; }

    [Id(6)]
    public string Error { get; set; } = string.Empty;
}
