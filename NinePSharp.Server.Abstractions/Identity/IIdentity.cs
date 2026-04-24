using System.Collections.Generic;

namespace NinePSharp.Server.Identity;

public interface IUser
{
    string Name { get; }
    int Id { get; }
    IEnumerable<IGroup> Groups { get; }
}

public interface IGroup
{
    string Name { get; }
    int Id { get; }
    IEnumerable<IUser> Members { get; }
}
