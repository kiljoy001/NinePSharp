using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

public interface ITestMountableResourceGrain : IGrainWithStringKey
{
    Task<TestResourceDiagnostics> GetDiagnosticsAsync();

    Task DeactivateAsync();

    Task DelayNextReadAsync(int milliseconds);

    Task LoseNextWStatReplyAsync();
}
