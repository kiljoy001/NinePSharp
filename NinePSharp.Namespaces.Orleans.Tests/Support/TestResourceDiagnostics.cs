using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

[GenerateSerializer]
public sealed record TestResourceDiagnostics(
    [property: Id(0)] int Mutations,
    [property: Id(1)] int Clunks,
    [property: Id(2)] string[] Children,
    [property: Id(3)] int Activations,
    [property: Id(4)] string RuntimeIdentity);
