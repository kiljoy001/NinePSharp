using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Represents a protocol translation backend that exposes a service via 9P.
/// </summary>
public interface IProtocolBackend
{
    /// <summary>
    /// The unique name of this backend instance.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The mount path in the 9P namespace where this service will be exposed.
    /// </summary>
    string MountPath { get; }

    /// <summary>
    /// Initializes the backend using the provided configuration.
    /// </summary>
    /// <param name="configuration">The configuration section specifically for this backend.</param>
    Task InitializeAsync(IConfiguration configuration);

    /// <summary>
    /// Returns the 9P file system implementation for this backend.
    /// </summary>
    INinePFileSystem GetFileSystem();
}
