namespace NinePSharp.Constants;

/// <summary>
/// Constants used throughout the 9P protocol implementation.
/// </summary>
public static class NinePConstants
{
    /// <summary>Special tag value indicating no tag</summary>
    public const ushort NoTag = 0xFFFF;
    /// <summary>Special fid value indicating no fid</summary>
    public const uint NoFid = uint.MaxValue; // ~0
    /// <summary>Default maximum message size (8KB)</summary>
    public const uint DefaultMSize = 8192; // 8K as a standard default

    /// <summary>Open for reading</summary>
    public const byte OREAD = 0x00;
    /// <summary>Open for writing</summary>
    public const byte OWRITE = 0x01;
    /// <summary>Open for reading and writing</summary>
    public const byte ORDWR = 0x02;
    /// <summary>Open for execution</summary>
    public const byte OEXEC = 0x03;

    /// <summary>Empty directory length</summary>
    public const uint DirLength = 0; // Empty directory length
    /// <summary>9P2000 protocol version string</summary>
    public const string VersionString_9p = "9P2000";
    /// <summary>9P2000.L protocol version string</summary>
    public const string VersionString_9pl = "9P2000.L";
    /// <summary>9P2000.u protocol version string</summary>
    public const string VersionString_9pu = "9P2000.u";
    /// <summary>Size of the 9P message header in bytes</summary>
    public const int HeaderSize = 7;

    /// <summary>
    /// Bitmask constants for 9P2000.L getattr requests
    /// </summary>
    public enum GetAttrMask : ulong
    {
        /// <summary>Get file mode</summary>
        P9_GETATTR_MODE = 0x00000001,
        /// <summary>Get number of hard links</summary>
        P9_GETATTR_NLINK = 0x00000002,
        /// <summary>Get user ID</summary>
        P9_GETATTR_UID = 0x00000004,
        /// <summary>Get group ID</summary>
        P9_GETATTR_GID = 0x00000008,
        /// <summary>Get device ID</summary>
        P9_GETATTR_RDEV = 0x00000010,
        /// <summary>Get access time</summary>
        P9_GETATTR_ATIME = 0x00000020,
        /// <summary>Get modification time</summary>
        P9_GETATTR_MTIME = 0x00000040,
        /// <summary>Get status change time</summary>
        P9_GETATTR_CTIME = 0x00000080,
        /// <summary>Get inode number</summary>
        P9_GETATTR_INO = 0x00000100,
        /// <summary>Get file size</summary>
        P9_GETATTR_SIZE = 0x00000200,
        /// <summary>Get number of blocks</summary>
        P9_GETATTR_BLOCKS = 0x00000400,
        /// <summary>Get birth/creation time</summary>
        P9_GETATTR_BTIME = 0x00000800,
        /// <summary>Get generation number</summary>
        P9_GETATTR_GEN = 0x00001000,
        /// <summary>Get data version</summary>
        P9_GETATTR_DATA_VERSION = 0x00002000,
        /// <summary>Get basic attributes</summary>
        P9_GETATTR_BASIC = 0x000007ff,
        /// <summary>Get all attributes</summary>
        P9_GETATTR_ALL = 0x00003fff
    }

    /// <summary>
    /// File mode flags for 9P
    /// </summary>
    public enum FileMode9P : uint
    {
        /// <summary>Directory</summary>
        DMDIR = 0x80000000,
        /// <summary>Append only</summary>
        DMAPPEND = 0x40000000,
        /// <summary>Exclusive use</summary>
        DMEXCL = 0x20000000,
        /// <summary>Mounted channel</summary>
        DMMOUNT = 0x10000000,
        /// <summary>Authentication file</summary>
        DMAUTH = 0x08000000,
        /// <summary>Temporary file</summary>
        DMTMP = 0x04000000,
        /// <summary>Read permission</summary>
        DMREAD = 0x4,
        /// <summary>Write permission</summary>
        DMWRITE = 0x2,
        /// <summary>Execute permission</summary>
        DMEXEC = 0x1
    }
}
