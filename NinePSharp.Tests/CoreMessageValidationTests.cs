using System;
using System.Linq;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Generators;
using Xunit;

namespace NinePSharp.Tests;

public class CoreMessageValidationTests
{
    [Property]
    public void Twalk_Fields_Preserved(ushort tag, uint fid, uint newFid, string[] wname)
    {
        var names = wname ?? Array.Empty<string>();
        names = names.Select(s => s ?? "").ToArray();

        var twalk = new Twalk(tag, fid, newFid, names);

        Assert.Equal(tag, twalk.Tag);
        Assert.Equal(fid, twalk.Fid);
        Assert.Equal(newFid, twalk.NewFid);
        Assert.Equal(names, twalk.Wname);

        var buffer = new byte[twalk.Size];
        twalk.WriteTo(buffer);

        var reparsed = new Twalk(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.Equal(fid, reparsed.Fid);
        Assert.Equal(newFid, reparsed.NewFid);
        Assert.Equal(names, reparsed.Wname);
    }

    [Property]
    public void Rread_ReadOnlyMemory_Handling(ushort tag, byte[] data)
    {
        var mem = new ReadOnlyMemory<byte>(data ?? Array.Empty<byte>());
        var rread = new Rread(tag, mem);

        Assert.Equal(tag, rread.Tag);
        Assert.True(mem.Span.SequenceEqual(rread.Data.Span));

        var buffer = new byte[rread.Size];
        rread.WriteTo(buffer);

        var reparsed = new Rread(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.True(mem.Span.SequenceEqual(reparsed.Data.Span));
    }

    [Property]
    public void Twrite_ReadOnlyMemory_Handling(ushort tag, uint fid, ulong offset, byte[] data)
    {
        var mem = new ReadOnlyMemory<byte>(data ?? Array.Empty<byte>());
        var twrite = new Twrite(tag, fid, offset, mem);

        Assert.Equal(tag, twrite.Tag);
        Assert.Equal(fid, twrite.Fid);
        Assert.Equal(offset, twrite.Offset);
        Assert.True(mem.Span.SequenceEqual(twrite.Data.Span));

        var buffer = new byte[twrite.Size];
        twrite.WriteTo(buffer);

        var reparsed = new Twrite(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.Equal(fid, reparsed.Fid);
        Assert.Equal(offset, reparsed.Offset);
        Assert.True(mem.Span.SequenceEqual(reparsed.Data.Span));
    }

    [Property]
    public void Stat_Serialization_Consistency(bool dotu)
    {
        var gen = Generators.Generators.genStat(dotu);
        var stat = gen.Sample(10, 1).First();

        var size = stat.Size;
        var buffer = new byte[size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        Assert.Equal(size, (uint)offset);

        int reparseOffset = 0;
        var reparsed = new Stat(buffer, ref reparseOffset, dotu);

        Assert.Equal(stat.Name, reparsed.Name);
        Assert.Equal(stat.Uid, reparsed.Uid);
        Assert.Equal(stat.Gid, reparsed.Gid);
        Assert.Equal(stat.Muid, reparsed.Muid);
        Assert.Equal(stat.Mode, reparsed.Mode);
        Assert.Equal(stat.Length, reparsed.Length);
        Assert.Equal(stat.Qid.Path, reparsed.Qid.Path);
        
        if (dotu)
        {
            Assert.Equal(stat.Extension ?? "", reparsed.Extension ?? "");
            Assert.Equal(stat.NUid, reparsed.NUid);
        }
    }

    [Property]
    public void Rgetattr_AllFields_Preserved(ushort tag, ulong valid, uint mode, uint uid, uint gid, ulong nlink, ulong rdev, ulong dataSize, ulong blkSize, ulong blocks, ulong atimeSec, ulong atimeNsec, ulong mtimeSec, ulong mtimeNsec, ulong ctimeSec, ulong ctimeNsec, ulong btimeSec, ulong btimeNsec, ulong gen, ulong dataVersion)
    {
        var qid = new Qid(QidType.QTFILE, 1, 12345);
        var r = new Rgetattr(tag, valid, qid, mode, uid, gid, nlink, rdev, dataSize, blkSize, blocks, atimeSec, atimeNsec, mtimeSec, mtimeNsec, ctimeSec, ctimeNsec, btimeSec, btimeNsec, gen, dataVersion);

        var buffer = new byte[r.Size];
        r.WriteTo(buffer);

        var reparsed = new Rgetattr(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.Equal(valid, reparsed.Valid);
        Assert.Equal(qid.Path, reparsed.Qid.Path);
        Assert.Equal(mode, reparsed.Mode);
        Assert.Equal(uid, reparsed.Uid);
        Assert.Equal(gid, reparsed.Gid);
        Assert.Equal(nlink, reparsed.Nlink);
        Assert.Equal(rdev, reparsed.Rdev);
        Assert.Equal(dataSize, reparsed.DataSize);
        Assert.Equal(blkSize, reparsed.BlkSize);
        Assert.Equal(blocks, reparsed.Blocks);
        Assert.Equal(atimeSec, reparsed.AtimeSec);
        Assert.Equal(atimeNsec, reparsed.AtimeNsec);
        Assert.Equal(mtimeSec, reparsed.MtimeSec);
        Assert.Equal(mtimeNsec, reparsed.MtimeNsec);
        Assert.Equal(ctimeSec, reparsed.CtimeSec);
        Assert.Equal(ctimeNsec, reparsed.CtimeNsec);
        Assert.Equal(btimeSec, reparsed.BtimeSec);
        Assert.Equal(btimeNsec, reparsed.BtimeNsec);
        Assert.Equal(gen, reparsed.Gen);
        Assert.Equal(dataVersion, reparsed.DataVersion);
    }

    [Property]
    public void Tsetattr_AllFields_Preserved(ushort tag, uint fid, uint valid, uint mode, uint uid, uint gid, ulong fileSize, ulong atimeSec, ulong atimeNsec, ulong mtimeSec, ulong mtimeNsec)
    {
        uint size = 67; // Header(7) + 5*4 + 5*8
        var t = new Tsetattr(size, tag, fid, valid, mode, uid, gid, fileSize, atimeSec, atimeNsec, mtimeSec, mtimeNsec);
        
        var buffer = new byte[t.Size];
        t.WriteTo(buffer);

        var reparsed = new Tsetattr(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.Equal(fid, reparsed.Fid);
        Assert.Equal(valid, reparsed.Valid);
        Assert.Equal(mode, reparsed.Mode);
        Assert.Equal(uid, reparsed.Uid);
        Assert.Equal(gid, reparsed.Gid);
        Assert.Equal(fileSize, reparsed.FileSize);
        Assert.Equal(atimeSec, reparsed.AtimeSec);
        Assert.Equal(atimeNsec, reparsed.AtimeNsec);
        Assert.Equal(mtimeSec, reparsed.MtimeSec);
        Assert.Equal(mtimeNsec, reparsed.MtimeNsec);
    }

    [Property]
    public void Tversion_Fields_Preserved(ushort tag, uint msize, string version)
    {
        var v = version ?? "";
        var t = new Tversion(tag, msize, v);

        Assert.Equal(tag, t.Tag);
        Assert.Equal(msize, t.MSize);
        Assert.Equal(v, t.Version);

        var buffer = new byte[t.Size];
        t.WriteTo(buffer);

        var reparsed = new Tversion(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.Equal(msize, reparsed.MSize);
        Assert.Equal(v, reparsed.Version);
        Assert.Equal(t.Size, reparsed.Size);
    }

    [Property]
    public void Rversion_Fields_Preserved(ushort tag, uint msize, string version)
    {
        var v = version ?? "";
        var r = new Rversion(tag, msize, v);

        Assert.Equal(tag, r.Tag);
        Assert.Equal(msize, r.MSize);
        Assert.Equal(v, r.Version);

        var buffer = new byte[r.Size];
        r.WriteTo(buffer);

        var reparsed = new Rversion(buffer);
        Assert.Equal(tag, reparsed.Tag);
        Assert.Equal(msize, reparsed.MSize);
        Assert.Equal(v, reparsed.Version);
        Assert.Equal(r.Size, reparsed.Size);
    }
}
