using System.Collections.Generic;
using System.Linq;

namespace NinePSharp.Server.Identity;

public static class IdentityProvider
{
    private static readonly Dictionary<string, IUser> Users = new();
    private static readonly Dictionary<string, IGroup> Groups = new();

    static IdentityProvider()
    {
        var rootGroup = new Group("root", 0);
        var rootUser = new User("root", 0, new[] { rootGroup });
        Users["root"] = rootUser;
        Groups["root"] = rootGroup;
    }

    public static IUser GetUser(string name) => Users.TryGetValue(name, out var user) ? user : Users["root"];

    public static IGroup GetGroup(string name) => Groups.TryGetValue(name, out var group) ? group : Groups["root"];

    public static void AddUser(string name, int id, string groupName)
    {
        var group = GetGroup(groupName);
        Users[name] = new User(name, id, new[] { group });
    }
}
