using System.Collections.Generic;
using System.Linq;

namespace NinePSharp.Server.Identity;

public class Group : IGroup
{
    public Group(string name, int id, IEnumerable<IUser>? members = null)
    {
        Name = name;
        Id = id;
        Members = members ?? Enumerable.Empty<IUser>();
    }

    public string Name { get; }

    public int Id { get; }

    public IEnumerable<IUser> Members { get; }
}
