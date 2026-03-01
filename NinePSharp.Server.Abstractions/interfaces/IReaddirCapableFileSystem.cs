using System.Threading.Tasks;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

public interface IReaddirCapableFileSystem
{
    Task<Rreaddir> ReaddirAsync(Treaddir treaddir);
}
