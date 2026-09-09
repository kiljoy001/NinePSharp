using NinePSharp.Namespaces.Orleans.Tests.Support;
using Reqnroll;

namespace NinePSharp.Namespaces.Orleans.Tests.Steps;

[Binding]
public static class OrleansHooks
{
    [BeforeTestRun]
    public static void StartCluster() => OrleansTestEnvironment.Start();

    [AfterTestRun]
    public static void StopCluster() => OrleansTestEnvironment.Stop();
}
