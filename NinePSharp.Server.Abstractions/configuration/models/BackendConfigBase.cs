namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Base class for all backend configurations.
/// </summary>
public abstract class BackendConfigBase
{
    /// <summary>
    /// Gets or sets the path where this backend is mounted in the 9P hierarchy.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;
}
