using System.Collections.Generic;

namespace NinePSharp.Server.Identity;

public interface IGroup
{
    string Name { get; }

    int Id { get; }

    IEnumerable<IUser> Members { get; }
}
