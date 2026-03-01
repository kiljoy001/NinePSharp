using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Abstractions.Utils;

public static class BackendRuntimeReaddirExtensions
{
    public static async Task<Rreaddir> ReaddirCompatAsync(this IBackendRuntime runtime, string[] relativePath, Treaddir treaddir, NinePDialect dialect)
    {
        if (runtime is IReaddirCapableBackendRuntime readdirRuntime)
        {
            return await readdirRuntime.ReaddirAsync(relativePath, treaddir, dialect);
        }

        var read = await runtime.ReadAsync(relativePath, new Tread(treaddir.Tag, treaddir.Fid, treaddir.Offset, treaddir.Count), dialect);
        return new Rreaddir((uint)(NinePConstants.HeaderSize + 4 + read.Data.Length), treaddir.Tag, read.Count, read.Data);
    }
}
