namespace NinePSharp.Server.Configuration.Models;

public class ComputeBackendConfig : BackendConfigBase
{
    /// <summary>
    /// Maximum number of concurrent jobs allowed on this node.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 10;

    /// <summary>
    /// Whether to allow arbitrary shell execution (DANGEROUS).
    /// If false, only WASM or pre-defined scripts can be run.
    /// </summary>
    public bool AllowShellExecution { get; set; } = false;

    /// <summary>
    /// Working directory for jobs.
    /// </summary>
    public string SandboxDirectory { get; set; } = "/tmp/ninep-compute";
}
