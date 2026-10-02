using System.Collections.Generic;

namespace NinePSharp.Server.Identity;

public interface IUser
{
    string Name { get; }

    int Id { get; }

    IEnumerable<IGroup> Groups { get; }
}
