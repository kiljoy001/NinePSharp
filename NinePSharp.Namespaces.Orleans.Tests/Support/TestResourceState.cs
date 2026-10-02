using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

[GenerateSerializer]
public sealed class TestResourceState
{
    [Id(0)]
    public bool Initialized { get; set; }

    [Id(1)]
    public ulong NextPath { get; set; }

    [Id(2)]
    public Dictionary<ulong, TestResourceNode> Nodes { get; set; } = new();

    [Id(3)]
    public Dictionary<string, TestCompletedOperation> Completed { get; set; } = new(StringComparer.Ordinal);

    [Id(4)]
    public int Mutations { get; set; }

    [Id(5)]
    public int Clunks { get; set; }

    [Id(6)]
    public int Activations { get; set; }
}
