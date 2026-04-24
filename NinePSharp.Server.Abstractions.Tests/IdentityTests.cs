using NinePSharp.Server.Identity;
using Xunit;

namespace NinePSharp.Server.Abstractions.Tests;

public class IdentityTests
{
    [Fact]
    public void DefaultIdentityProvider_ReturnsRootForUnknowns()
    {
        var user = IdentityProvider.GetUser("nonexistent");
        var group = IdentityProvider.GetGroup("nonexistent");

        Assert.Equal("root", user.Name);
        Assert.Equal("root", group.Name);
        Assert.Equal(0, user.Id);
        Assert.Equal(0, group.Id);
    }

    [Fact]
    public void AddUser_WorksAndCanBeRetrieved()
    {
        IdentityProvider.AddUser("scott", 1000, "root");
        var user = IdentityProvider.GetUser("scott");

        Assert.Equal("scott", user.Name);
        Assert.Equal(1000, user.Id);
        Assert.Contains(user.Groups, g => g.Name == "root");
    }
}
