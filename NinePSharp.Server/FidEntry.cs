using System.Collections.Generic;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Server;

internal class FidEntry
{
    public uint Fid { get; }
    public string[] Path { get; set; }
    public Qid Qid { get; set; }
    public bool IsOpen { get; set; }
    public byte OpenMode { get; set; }

    public FidEntry(uint fid, string[] path, Qid qid)
    {
        Fid = fid;
        Path = path;
        Qid = qid;
    }
}
