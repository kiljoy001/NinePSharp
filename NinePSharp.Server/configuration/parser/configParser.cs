using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Configuration.Parser;

public class ConfigParser : IParser
{
    public T Bind<T>(IConfiguration configuration, string sectionName) where T : new()
    {
        var section = configuration.GetSection(sectionName);
        var settings = new T();
        section.Bind(settings);
        return settings;
    }
}
