using NinePSharp.Parser;
using System.Threading.Tasks;

namespace NinePSharp.Server;

public interface INinePFSDispatcher
{
    Task<object> DispatchAsync(NinePMessage message, bool dotu);
}
