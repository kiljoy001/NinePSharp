using Orleans.Hosting;
using Orleans.TestingHost;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

internal static class OrleansTestEnvironment
{
    private static readonly object Gate = new();
    private static TestCluster? cluster;

    internal static TestCluster Cluster
        => cluster ?? throw new InvalidOperationException("The Orleans test cluster has not been started.");

    internal static void Start()
    {
        lock (Gate)
        {
            if (cluster is not null)
            {
                return;
            }

            var builder = new TestClusterBuilder(2);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            cluster = builder.Build();
            cluster.Deploy();
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            cluster?.StopAllSilos();
            cluster?.Dispose();
            cluster = null;
        }
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
            => siloBuilder.AddMemoryGrainStorageAsDefault();
    }
}
