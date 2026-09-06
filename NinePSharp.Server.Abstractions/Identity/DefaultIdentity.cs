using System.Collections.Generic;
using System.Linq;

namespace NinePSharp.Server.Identity;

public class User : IUser
{
    public string Name { get; }
    public int Id { get; }
    public IEnumerable<IGroup> Groups { get; }

    public User(string name, int id, IEnumerable<IGroup>? groups = null)
    {
        Name = name;
        Id = id;
        Groups = groups ?? Enumerable.Empty<IGroup>();
    }
}

public class Group : IGroup
{
    public string Name { get; }
    public int Id { get; }
    public IEnumerable<IUser> Members { get; }

    public Group(string name, int id, IEnumerable<IUser>? members = null)
    {
        Name = name;
        Id = id;
        Members = members ?? Enumerable.Empty<IUser>();
    }
}

public static class IdentityProvider
{
    private static readonly Dictionary<string, IUser> _users = new();
    private static readonly Dictionary<string, IGroup> _groups = new();

    static IdentityProvider()
    {
        var rootGroup = new Group("root", 0);
        var rootUser = new User("root", 0, new[] { rootGroup });
        _users["root"] = rootUser;
        _groups["root"] = rootGroup;
    }

    public static IUser GetUser(string name) => _users.TryGetValue(name, out var user) ? user : _users["root"];
    public static IGroup GetGroup(string name) => _groups.TryGetValue(name, out var group) ? group : _groups["root"];

    public static void AddUser(string name, int id, string groupName)
    {
        var group = GetGroup(groupName);
        _users[name] = new User(name, id, new[] { group });
    }
}
