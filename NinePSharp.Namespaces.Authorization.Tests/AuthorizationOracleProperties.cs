using FsCheck.Xunit;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Namespaces.Authorization.Tests;

/// <summary>Runs the shared fuzz model (NinePSharp.Fuzzer/AuthorizationFuzz.cs) as properties.</summary>
public sealed class AuthorizationOracleProperties
{
    [Property(MaxTest = 500)]
    public void TheLayerAgreesWithAnIndependentModel(int seed) => AuthorizationFuzz.RunCase(new Random(seed).Next);

    [Property(MaxTest = 300)]
    public void ArbitraryBytesDecodeToCasesThatAgree(byte[] bytes) => AuthorizationFuzz.Run(bytes);

    [Fact]
    public void SeedCorpusAndOversizedInputsAgree()
    {
        string corpus = Path.Combine(RepositoryRoot(), "corpus", "authorization");
        Assert.NotEmpty(Directory.GetFiles(corpus));
        foreach (string seed in Directory.GetFiles(corpus))
        {
            using FileStream stream = File.OpenRead(seed);
            AuthorizationFuzz.Run(stream);
        }

        using var oversized = new MemoryStream(Enumerable.Range(0, 9000).Select(index => (byte)index).ToArray());
        AuthorizationFuzz.Run(oversized);
        AuthorizationFuzz.Run(ReadOnlySpan<byte>.Empty);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "NinePSharp.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("NinePSharp.sln");
    }
}
