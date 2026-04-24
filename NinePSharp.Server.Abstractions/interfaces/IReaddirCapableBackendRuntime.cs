using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

public interface IReaddirCapableBackendRuntime
{
    Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default);
    Task<Rreaddir> ReaddirCompatAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default);
}
