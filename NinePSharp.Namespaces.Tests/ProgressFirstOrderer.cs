using System.Reflection;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace NinePSharp.Namespaces.Tests;

/// <summary>Check release/progress invariants before tests which wait for subsequent operations.</summary>
public sealed class ProgressFirstOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> collections)
        => collections.OrderBy(c => c.DisplayName == "Namespace progress contracts" ? 0 : 1);
}
