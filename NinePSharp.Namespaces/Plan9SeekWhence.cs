using System.Buffers.Binary;
using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Origin used by the Plan 9 seek syscall.</summary>
public enum Plan9SeekWhence
{
    /// <summary>Position relative to the start of the resource.</summary>
    Set,

    /// <summary>Position relative to the shared channel offset.</summary>
    Current,

    /// <summary>Position relative to the current file length.</summary>
    End,
}
