using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces;
using NinePSharp.Namespaces.Orleans;
using NinePSharp.Namespaces.Orleans.Server;
using Orleans;
using Orleans.Hosting;

namespace NinePSharp.Examples;

public interface IExampleResourceGrain : IMountableResourceGrain;
