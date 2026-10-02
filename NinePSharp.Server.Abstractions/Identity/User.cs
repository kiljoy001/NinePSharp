using System.Collections.Generic;
using System.Linq;

namespace NinePSharp.Server.Identity;

public class User : IUser
{
    public User(string name, int id, IEnumerable<IGroup>? groups = null)
    {
        Name = name;
        Id = id;
        Groups = groups ?? Enumerable.Empty<IGroup>();
    }

    public string Name { get; }

    public int Id { get; }

    public IEnumerable<IGroup> Groups { get; }
}
