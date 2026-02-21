namespace NinePSharp.Constants;

public static class NinePConstants
{
    public const ushort NoTag = 0xFFFF;
    public const uint NoFid = uint.MaxValue; // ~0
    public const uint DefaultMSize = 8192; // 8K as a standard default

    // Open/Create Modes
    public const byte OREAD = 0x00;
    public const byte OWRITE = 0x01;
    public const byte ORDWR = 0x02;
    public const byte OEXEC = 0x03;
    
    public const uint DirLength = 0; // Empty directory length
    public const string VersionString_9p = "9P2000";
    public const string VersionString_9pl = "9P2000.L";
    public const string VersionString_9pu = "9P2000.u";
    public const int HeaderSize = 7;

    // File Modes
    public enum FileMode9P : uint
    {
        DMDIR = 0x80000000,
        DMAPPEND = 0x40000000,
        DMEXCL = 0x20000000,
        DMMOUNT = 0x10000000,
        DMAUTH = 0x08000000,
        DMTMP = 0x04000000,
        DMREAD = 0x4,
        DMWRITE = 0x2,
        DMEXEC = 0x1
    }
}