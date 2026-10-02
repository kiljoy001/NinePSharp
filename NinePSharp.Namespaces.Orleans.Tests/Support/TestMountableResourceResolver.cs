using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

internal sealed class TestMountableResourceResolver : IMountableResourceResolver
{
    private readonly IGrainFactory grainFactory;

    internal TestMountableResourceResolver(IGrainFactory grainFactory)
    {
        this.grainFactory = grainFactory;
    }

    public IMountableResourceGrain Resolve(ResourceIdentityModel identity)
        => grainFactory.GetGrain<IMountableResourceGrain>(identity.Device);
}
