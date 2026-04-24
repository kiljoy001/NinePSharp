using NinePSharp.Constants;
using System.Runtime.CompilerServices;
using NinePSharp.Server;

namespace NinePSharp.Tests;

internal static class TestAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        NinePServerBootstrap.Initialize();
    }
}
