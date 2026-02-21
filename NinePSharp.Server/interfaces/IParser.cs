using Microsoft.Extensions.Configuration;

namespace NinePSharp.Server.Interfaces;

public interface IParser
{
    T Bind<T>(IConfiguration configuration, string sectionName) where T : new();
}
