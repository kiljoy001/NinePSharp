namespace NinePSharp.Constants;

/// <summary>
/// Represents a unique identifier for a file or directory in 9P.
/// </summary>
public readonly struct Qid
{
    /// <summary>The type of the file (directory, append-only, etc.).</summary>
    public readonly QidType Type;

    /// <summary>The version of the file, incremented every time it is modified.</summary>
    public readonly uint Version;

    /// <summary>A unique path identifier for the file.</summary>
    public readonly ulong Path;

    /// <summary>
    /// Initializes a new instance of the <see cref="Qid"/> struct.
    /// Initializes a new Qid with the specified type, version, and path.
    /// </summary>
    /// <param name="type">The file type.</param>
    /// <param name="version">The version number.</param>
    /// <param name="path">The unique path identifier.</param>
    public Qid(QidType type, uint version, ulong path)
    {
        Type = type;
        Version = version;
        Path = path;
    }
}
