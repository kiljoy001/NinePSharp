using System;
using NinePSharp.Messages;
using NinePSharp.Constants;

namespace NinePSharp.Server.Cluster.Messages;

// Serializable wrappers for 9P messages
[Serializable]
public class TWalkDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public uint NewFid { get; set; }
    public string[] Wname { get; set; } = Array.Empty<string>();

    public TWalkDto() {}
    public TWalkDto(Twalk t)
    {
        Tag = t.Tag; Fid = t.Fid; NewFid = t.NewFid; Wname = t.Wname;
    }
}

[Serializable]
public class TReadDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public ulong Offset { get; set; }
    public uint Count { get; set; }

    public TReadDto() {}
    public TReadDto(Tread t)
    {
        Tag = t.Tag; Fid = t.Fid; Offset = t.Offset; Count = t.Count;
    }
}

[Serializable]
public class TWriteDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public ulong Offset { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public TWriteDto() {}
    public TWriteDto(Twrite t)
    {
        Tag = t.Tag; Fid = t.Fid; Offset = t.Offset; Data = t.Data.ToArray();
    }
}

// Add others as needed (Open, Clunk, Stat)
[Serializable]
public class TOpenDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public byte Mode { get; set; }
    
    public TOpenDto() {}
    public TOpenDto(Topen t) { Tag = t.Tag; Fid = t.Fid; Mode = t.Mode; }
}

[Serializable]
public class TClunkDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public TClunkDto() {}
    public TClunkDto(Tclunk t) { Tag = t.Tag; Fid = t.Fid; }
}

[Serializable]
public class TStatDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public TStatDto() {}
    public TStatDto(Tstat t) { Tag = t.Tag; Fid = t.Fid; }
}

[Serializable]
public class TGetAttrDto
{
    public ushort Tag { get; set; }
    public uint Fid { get; set; }
    public ulong RequestMask { get; set; }
    public TGetAttrDto() {}
    public TGetAttrDto(Tgetattr t) { Tag = t.Tag; Fid = t.Fid; RequestMask = t.RequestMask; }
}

[Serializable]
public class RWalkDto
{
    public ushort Tag { get; set; }
    public Qid[] Wqid { get; set; } = Array.Empty<Qid>();
    public RWalkDto() {}
    public RWalkDto(Rwalk r) { Tag = r.Tag; Wqid = r.Wqid; }
}

[Serializable]
public class RReadDto
{
    public ushort Tag { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public RReadDto() {}
    public RReadDto(Rread r) { Tag = r.Tag; Data = r.Data.ToArray(); }
}

[Serializable]
public class RWriteDto
{
    public ushort Tag { get; set; }
    public uint Count { get; set; }
    public RWriteDto() {}
    public RWriteDto(Rwrite r) { Tag = r.Tag; Count = r.Count; }
}

[Serializable]
public class ROpenDto
{
    public ushort Tag { get; set; }
    public Qid Qid { get; set; }
    public uint Iounit { get; set; }
    public ROpenDto() {}
    public ROpenDto(Ropen r) { Tag = r.Tag; Qid = r.Qid; Iounit = r.Iounit; }
}

[Serializable]
public class RClunkDto
{
    public ushort Tag { get; set; }
    public RClunkDto() {}
    public RClunkDto(Rclunk r) { Tag = r.Tag; }
}

[Serializable]
public class RStatDto
{
    public ushort Tag { get; set; }
    public bool DotU { get; set; }
    public byte[] StatBytes { get; set; } = Array.Empty<byte>();
    public RStatDto() {}
    public RStatDto(Rstat r)
    {
        Tag = r.Tag;
        DotU = r.Stat.DotU;
        StatBytes = new byte[r.Stat.Size];
        int offset = 0;
        r.Stat.WriteTo(StatBytes, ref offset);
    }
}

[Serializable]
public class RGetAttrDto
{
    public ushort Tag { get; set; }
    public ulong Valid { get; set; }
    public Qid Qid { get; set; }
    public uint Mode { get; set; }
    public uint Uid { get; set; }
    public uint Gid { get; set; }
    public ulong Nlink { get; set; }
    public ulong Rdev { get; set; }
    public ulong DataSize { get; set; }
    public ulong BlkSize { get; set; }
    public ulong Blocks { get; set; }
    public ulong AtimeSec { get; set; }
    public ulong AtimeNsec { get; set; }
    public ulong MtimeSec { get; set; }
    public ulong MtimeNsec { get; set; }
    public ulong CtimeSec { get; set; }
    public ulong CtimeNsec { get; set; }
    public ulong BtimeSec { get; set; }
    public ulong BtimeNsec { get; set; }
    public ulong Gen { get; set; }
    public ulong DataVersion { get; set; }

    public RGetAttrDto() {}
    public RGetAttrDto(Rgetattr r)
    {
        Tag = r.Tag; Valid = r.Valid; Qid = r.Qid; Mode = r.Mode; Uid = r.Uid; Gid = r.Gid;
        Nlink = r.Nlink; Rdev = r.Rdev; DataSize = r.DataSize; BlkSize = r.BlkSize; Blocks = r.Blocks;
        AtimeSec = r.AtimeSec; AtimeNsec = r.AtimeNsec; MtimeSec = r.MtimeSec; MtimeNsec = r.MtimeNsec;
        CtimeSec = r.CtimeSec; CtimeNsec = r.CtimeNsec; BtimeSec = r.BtimeSec; BtimeNsec = r.BtimeNsec;
        Gen = r.Gen; DataVersion = r.DataVersion;
    }
}

[Serializable]
public class RErrorDto
{
    public ushort Tag { get; set; }
    public string Ename { get; set; } = "";
    public RErrorDto() {}
    public RErrorDto(Rerror r) { Tag = r.Tag; Ename = r.Ename; }
}
