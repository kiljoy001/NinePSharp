namespace NinePSharp.Generators

open FsCheck
open FsCheck.FSharp
open NinePSharp.Constants
open NinePSharp.Messages
open NinePSharp.Parser
open System
open System.Linq

module Generators =
    
    let gen = GenBuilder.gen

    // ─── Primitive Generators ────────────────────────────────────────────

    type Valid9PString = { Value: string }
    
    let genValidString = 
        Gen.sized (fun size ->
            Gen.choose (1, max 1 size) |> Gen.bind (fun len ->
                Gen.arrayOfLength len (Gen.choose(32, 126) |> Gen.map char)
                |> Gen.map (fun cs -> { Value = new string(cs) })))

    let genTag : Gen<uint16> = Gen.choose(0, 65535) |> Gen.map uint16
    let genFid : Gen<uint32> = Gen.choose(0, System.Int32.MaxValue) |> Gen.map uint32
    let genOffset : Gen<uint64> = Gen.choose(0, System.Int32.MaxValue) |> Gen.map uint64
    let genCount : Gen<uint32> = Gen.choose(1, 8192) |> Gen.map uint32
    let genByte' : Gen<byte> = Gen.choose(0, 255) |> Gen.map byte
    let genU32 : Gen<uint32> = Gen.choose(0, System.Int32.MaxValue) |> Gen.map uint32
    let genU64 : Gen<uint64> = Gen.choose(0, System.Int32.MaxValue) |> Gen.map uint64
    
    let genQid : Gen<Qid> = 
        Gen.map3 (fun qType version path -> Qid(qType, version, path))
            (Gen.elements [QidType.QTDIR; QidType.QTFILE; QidType.QTAPPEND; QidType.QTEXCL])
            genU32
            genU64

    let genStat (dotu: bool) =
        gen {
            let! name = genValidString
            let! uid = genValidString
            let! gid = genValidString
            let! muid = genValidString
            let! qid = genQid
            let! mode = genU32
            let! atime = genU32
            let! mtime = genU32
            let! length = genU64
            
            return Stat(0us, 0us, 0u, qid, mode, atime, mtime, length, name.Value, uid.Value, gid.Value, muid.Value, dotu)
        }

    /// Helper to compute 9P string wire size: 2-byte length prefix + UTF-8 bytes
    let strWireSize (s: string) = 2 + System.Text.Encoding.UTF8.GetByteCount(s)

    /// Header size constant (size[4] + type[1] + tag[2] = 7)
    let H = 7u

    let genSmallData : Gen<byte[]> =
        Gen.choose(0, 128) |> Gen.bind (fun len ->
            Gen.arrayOfLength len genByte')

    /// Write a 9P string (2-byte length prefix + UTF-8 bytes) to a span
    let private writeStr (span: Span<byte>) (offset: byref<int>) (s: string) =
        let bytes = System.Text.Encoding.UTF8.GetBytes(s)
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), uint16 bytes.Length)
        offset <- offset + 2
        bytes.CopyTo(span.Slice(offset, bytes.Length))
        offset <- offset + bytes.Length

    /// Write full 9P header (size[4] + type[1] + tag[2])
    let private writeHdr (span: Span<byte>) (size: uint32) (msgType: byte) (tag: uint16) =
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), size)
        span.[4] <- msgType
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(5, 2), tag)

    let inline private wr32 (span: Span<byte>) (offset: byref<int>) (v: uint32) =
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), v)
        offset <- offset + 4

    let inline private wr64 (span: Span<byte>) (offset: byref<int>) (v: uint64) =
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), v)
        offset <- offset + 8

    // ─── Classic 9P2000 T-messages ───────────────────────────────────────

    let genTversion = 
        Gen.map2 (fun tag msize -> MsgTversion(Tversion(tag, msize, "9P2000.L"))) 
            genTag (Gen.elements [8192u; 16384u; 65536u])

    let genTauth =
        gen {
            let! tag = genTag
            let! afid = genFid
            let! uname = genValidString
            let! aname = genValidString
            return MsgTauth(Tauth(tag, afid, uname.Value, aname.Value))
        }

    let genTflush = Gen.map (fun tag -> MsgTflush(Tflush(tag, 0us))) genTag

    let genTattach =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! afid = genFid
            let! uname = genValidString
            let! aname = genValidString
            return MsgTattach(Tattach(tag, fid, afid, uname.Value, aname.Value))
        }

    let genTwalk =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! newFid = genFid
            let! count = Gen.choose(0, 16)
            let! names = Gen.arrayOfLength count (genValidString |> Gen.map (fun v -> v.Value))
            return MsgTwalk(Twalk(tag, fid, newFid, names))
        }

    let genTopen =
        Gen.map2 (fun tag fid -> MsgTopen(Topen(tag, fid, 0uy))) genTag genFid

    /// Build a Tcreate from components (it only has a ReadOnlySpan constructor)
    let private buildTcreate (tag: uint16) (fid: uint32) (name: string) (perm: uint32) (mode: byte) =
        let size = H + 4u + uint32 (strWireSize name) + 4u + 1u
        let buf = Array.zeroCreate<byte> (int size)
        let span = buf.AsSpan()
        writeHdr span size (byte MessageTypes.Tcreate) tag
        let mutable offset = int NinePConstants.HeaderSize
        wr32 span &offset fid
        writeStr span &offset name
        wr32 span &offset perm
        span.[offset] <- mode
        Tcreate(ReadOnlySpan<byte>(buf))

    let genTcreate =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! name = genValidString
            let! perm = genU32
            let! mode = genByte'
            return MsgTcreate(buildTcreate tag fid name.Value perm mode)
        }

    let genTread =
        Gen.map4 (fun tag fid offset count -> MsgTread(Tread(tag, fid, offset, count)))
            genTag genFid genOffset genCount

    let genTwrite =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! offset = genOffset
            let! data = genSmallData
            return MsgTwrite(Twrite(tag, fid, offset, data))
        }

    let genTclunk = Gen.map2 (fun tag fid -> MsgTclunk(Tclunk(tag, fid))) genTag genFid
    let genTremove = Gen.map2 (fun tag fid -> MsgTremove(Tremove(tag, fid))) genTag genFid
    let genTstat = Gen.map2 (fun tag fid -> MsgTstat(Tstat(tag, fid))) genTag genFid
    
    let genTwstat =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! stat = genStat false
            return MsgTwstat(Twstat(tag, fid, stat))
        }

    // ─── Classic 9P2000 R-messages ───────────────────────────────────────

    let genRversion =
        Gen.map2 (fun tag msize -> MsgRversion(Rversion(tag, msize, "9P2000.L")))
            genTag (Gen.elements [8192u; 16384u; 65536u])

    let genRauth =
        Gen.map2 (fun tag qid -> MsgRauth(Rauth(tag, qid))) genTag genQid

    let genRattach =
        Gen.map2 (fun tag qid -> MsgRattach(Rattach(tag, qid))) genTag genQid

    let genRerror =
        gen {
            let! tag = genTag
            let! ename = genValidString
            return MsgRerror(Rerror(tag, ename.Value))
        }

    let genRopen =
        Gen.map3 (fun tag qid iounit -> MsgRopen(Ropen(tag, qid, iounit)))
            genTag genQid genU32

    let genRcreate =
        Gen.map3 (fun tag qid iounit -> MsgRcreate(Rcreate(tag, qid, iounit)))
            genTag genQid genU32

    let genRread =
        gen {
            let! tag = genTag
            let! data = genSmallData
            return MsgRread(Rread(tag, ReadOnlyMemory<byte>(data)))
        }

    let genRwrite =
        Gen.map2 (fun tag count -> MsgRwrite(Rwrite(tag, count))) genTag genCount

    let genRclunk = Gen.map (fun (tag: uint16) -> MsgRclunk(Rclunk(tag))) genTag
    let genRflush = Gen.map (fun (tag: uint16) -> MsgRflush(Rflush(tag))) genTag
    let genRremove = Gen.map (fun (tag: uint16) -> MsgRremove(Rremove(tag))) genTag
    let genRwstat = Gen.map (fun (tag: uint16) -> MsgRwstat(Rwstat(tag))) genTag

    let genRwalk =
        gen {
            let! count = Gen.choose(0, 16)
            let! tag = genTag
            let! qids = Gen.arrayOfLength count genQid
            return MsgRwalk(Rwalk(tag, qids))
        }

    let genRstat =
        gen {
            let! tag = genTag
            let! stat = genStat false
            return MsgRstat(Rstat(tag, stat))
        }

    // ─── 9P2000.L T-messages ─────────────────────────────────────────────

    let genTstatfs =
        gen {
            let! tag = genTag
            let! fid = genFid
            let size = H + 4u
            return MsgTstatfs(Tstatfs(size, tag, fid))
        }

    let genTlopen =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! flags = genU32
            let size = H + 4u + 4u
            return MsgTlopen(Tlopen(size, tag, fid, flags))
        }

    let genTlcreate =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! name = genValidString
            let! flags = genU32
            let! mode = genU32
            let! gid = genU32
            let size = H + 4u + uint32 (strWireSize name.Value) + 4u + 4u + 4u
            return MsgTlcreate(Tlcreate(size, tag, fid, name.Value, flags, mode, gid))
        }

    let genTsymlink =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! name = genValidString
            let! symtgt = genValidString
            let! gid = genU32
            let size = H + 4u + uint32 (strWireSize name.Value) + uint32 (strWireSize symtgt.Value) + 4u
            return MsgTsymlink(Tsymlink(size, tag, fid, name.Value, symtgt.Value, gid))
        }

    let genTmknod =
        gen {
            let! tag = genTag
            let! dfid = genFid
            let! name = genValidString
            let! mode = genU32
            let! major = genU32
            let! minor = genU32
            let! gid = genU32
            let size = H + 4u + uint32 (strWireSize name.Value) + 4u + 4u + 4u + 4u
            return MsgTmknod(Tmknod(size, tag, dfid, name.Value, mode, major, minor, gid))
        }

    let genTrename =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! dfid = genFid
            let! name = genValidString
            let size = H + 4u + 4u + uint32 (strWireSize name.Value)
            return MsgTrename(Trename(size, tag, fid, dfid, name.Value))
        }

    let genTreadlink =
        gen {
            let! tag = genTag
            let! fid = genFid
            let size = H + 4u
            return MsgTreadlink(Treadlink(size, tag, fid))
        }

    let genTgetattr =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! requestMask = genU64
            let size = H + 4u + 8u
            return MsgTgetattr(Tgetattr(size, tag, fid, requestMask))
        }

    let genTsetattr =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! valid = genU32
            let! mode = genU32
            let! uid = genU32
            let! gid = genU32
            let! fileSize = genU64
            let! atimeSec = genU64
            let! atimeNsec = genU64
            let! mtimeSec = genU64
            let! mtimeNsec = genU64
            let size = H + 4u + 4u + 4u + 4u + 4u + 8u + 8u + 8u + 8u + 8u
            return MsgTsetattr(Tsetattr(size, tag, fid, valid, mode, uid, gid, fileSize, atimeSec, atimeNsec, mtimeSec, mtimeNsec))
        }

    let genTxattrwalk =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! newFid = genFid
            let! name = genValidString
            let size = H + 4u + 4u + uint32 (strWireSize name.Value)
            return MsgTxattrwalk(Txattrwalk(size, tag, fid, newFid, name.Value))
        }

    let genTxattrcreate =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! name = genValidString
            let! attrSize = genU64
            let! flags = genU32
            let size = H + 4u + uint32 (strWireSize name.Value) + 8u + 4u
            return MsgTxattrcreate(Txattrcreate(size, tag, fid, name.Value, attrSize, flags))
        }

    let genTreaddir =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! offset = genOffset
            let! count = genCount
            let size = H + 4u + 8u + 4u
            return MsgTreaddir(Treaddir(size, tag, fid, offset, count))
        }

    let genTfsync =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! datasync = genU32
            let size = H + 4u + 4u
            return MsgTfsync(Tfsync(size, tag, fid, datasync))
        }

    let genTlock =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! lockType = genByte'
            let! flags = genU32
            let! start = genU64
            let! length = genU64
            let! procId = genU32
            let! clientId = genValidString
            let size = H + 4u + 1u + 4u + 8u + 8u + 4u + uint32 (strWireSize clientId.Value)
            return MsgTlock(Tlock(size, tag, fid, lockType, flags, start, length, procId, clientId.Value))
        }

    let genTgetlock =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! lockType = genByte'
            let! start = genU64
            let! length = genU64
            let! procId = genU32
            let! clientId = genValidString
            let size = H + 4u + 1u + 8u + 8u + 4u + uint32 (strWireSize clientId.Value)
            return MsgTgetlock(Tgetlock(size, tag, fid, lockType, start, length, procId, clientId.Value))
        }

    let genTlink =
        gen {
            let! tag = genTag
            let! dfid = genFid
            let! fid = genFid
            let! name = genValidString
            let size = H + 4u + 4u + uint32 (strWireSize name.Value)
            return MsgTlink(Tlink(size, tag, dfid, fid, name.Value))
        }

    let genTmkdir =
        gen {
            let! tag = genTag
            let! dfid = genFid
            let! name = genValidString
            let! mode = genU32
            let! gid = genU32
            let size = H + 4u + uint32 (strWireSize name.Value) + 4u + 4u
            return MsgTmkdir(Tmkdir(size, tag, dfid, name.Value, mode, gid))
        }

    let genTrenameat =
        gen {
            let! tag = genTag
            let! oldDirFid = genFid
            let! oldName = genValidString
            let! newDirFid = genFid
            let! newName = genValidString
            let size = H + 4u + uint32 (strWireSize oldName.Value) + 4u + uint32 (strWireSize newName.Value)
            return MsgTrenameat(Trenameat(size, tag, oldDirFid, oldName.Value, newDirFid, newName.Value))
        }

    let genTunlinkat =
        gen {
            let! tag = genTag
            let! dirFd = genFid
            let! name = genValidString
            let! flags = genU32
            let size = H + 4u + uint32 (strWireSize name.Value) + 4u
            return MsgTunlinkat(Tunlinkat(size, tag, dirFd, name.Value, flags))
        }

    // ─── 9P2000.L R-messages ─────────────────────────────────────────────

    /// Build a Rlerror from components (it only has a ReadOnlySpan constructor)
    let private buildRlerror (tag: uint16) (ecode: uint32) =
        let size = H + 4u
        let buf = Array.zeroCreate<byte> (int size)
        let span = buf.AsSpan()
        writeHdr span size (byte MessageTypes.Rlerror) tag
        let mutable offset = int NinePConstants.HeaderSize
        wr32 span &offset ecode
        Rlerror(ReadOnlySpan<byte>(buf))

    let genRlerror =
        gen {
            let! tag = genTag
            let! ecode = genU32
            return MsgRlerror(buildRlerror tag ecode)
        }

    let genRstatfs =
        gen {
            let! tag = genTag
            let! fsType = genU32
            let! bSize = genU32
            let! blocks = genU64
            let! bFree = genU64
            let! bAvail = genU64
            let! files = genU64
            let! fFree = genU64
            let! fsId = genU64
            let! nameLen = genU32
            return MsgRstatfs(Rstatfs(tag, fsType, bSize, blocks, bFree, bAvail, files, fFree, fsId, nameLen))
        }

    let genRlopen =
        gen {
            let! tag = genTag
            let! qid = genQid
            let! iounit = genU32
            let size = H + 13u + 4u
            return MsgRlopen(Rlopen(size, tag, qid, iounit))
        }

    let genRlcreate =
        gen {
            let! tag = genTag
            let! qid = genQid
            let! iounit = genU32
            let size = H + 13u + 4u
            return MsgRlcreate(Rlcreate(size, tag, qid, iounit))
        }

    let genRsymlink =
        gen {
            let! tag = genTag
            let! qid = genQid
            let size = H + 13u
            return MsgRsymlink(Rsymlink(size, tag, qid))
        }

    let genRmknod =
        gen {
            let! tag = genTag
            let! qid = genQid
            let size = H + 13u
            return MsgRmknod(Rmknod(size, tag, qid))
        }

    let genRrename =
        gen {
            let! tag = genTag
            let size = H
            return MsgRrename(Rrename(size, tag))
        }

    let genRreadlink =
        gen {
            let! tag = genTag
            let! target = genValidString
            let size = H + uint32 (strWireSize target.Value)
            return MsgRreadlink(Rreadlink(size, tag, target.Value))
        }

    let genRgetattr =
        gen {
            let! tag = genTag
            let! valid = genU64
            let! qid = genQid
            let! mode = genU32
            let! uid = genU32
            let! gid = genU32
            let! nlink = genU64
            let! rdev = genU64
            let! dataSize = genU64
            let! blkSize = genU64
            let! blocks = genU64
            let! atimeSec = genU64
            let! atimeNsec = genU64
            let! mtimeSec = genU64
            let! mtimeNsec = genU64
            let! ctimeSec = genU64
            let! ctimeNsec = genU64
            let! btimeSec = genU64
            let! btimeNsec = genU64
            let! genVal = genU64
            let! dataVersion = genU64
            return MsgRgetattr(Rgetattr(tag, valid, qid, mode, uid, gid, nlink, rdev, dataSize, blkSize, blocks, atimeSec, atimeNsec, mtimeSec, mtimeNsec, ctimeSec, ctimeNsec, btimeSec, btimeNsec, genVal, dataVersion))
        }

    let genRsetattr =
        gen {
            let! tag = genTag
            return MsgRsetattr(Rsetattr(tag))
        }

    let genRxattrwalk =
        gen {
            let! tag = genTag
            let! xattrSize = genU64
            let size = H + 8u
            return MsgRxattrwalk(Rxattrwalk(size, tag, xattrSize))
        }

    let genRxattrcreate =
        gen {
            let! tag = genTag
            let size = H
            return MsgRxattrcreate(Rxattrcreate(size, tag))
        }

    let genRreaddir =
        gen {
            let! tag = genTag
            let! data = genSmallData
            let count = uint32 data.Length
            let size = H + 4u + count
            return MsgRreaddir(Rreaddir(size, tag, count, ReadOnlyMemory<byte>(data)))
        }

    let genRfsync =
        gen {
            let! tag = genTag
            let size = H
            return MsgRfsync(Rfsync(size, tag))
        }

    let genRlock =
        gen {
            let! tag = genTag
            let! status = genByte'
            let size = H + 1u
            return MsgRlock(Rlock(size, tag, status))
        }

    let genRgetlock =
        gen {
            let! tag = genTag
            let! lockType = genByte'
            let! start = genU64
            let! length = genU64
            let! procId = genU32
            let! clientId = genValidString
            let size = H + 1u + 8u + 8u + 4u + uint32 (strWireSize clientId.Value)
            return MsgRgetlock(Rgetlock(size, tag, lockType, start, length, procId, clientId.Value))
        }

    let genRlink =
        gen {
            let! tag = genTag
            let size = H
            return MsgRlink(Rlink(size, tag))
        }

    let genRmkdir =
        gen {
            let! tag = genTag
            let! qid = genQid
            let size = H + 13u
            return MsgRmkdir(Rmkdir(size, tag, qid))
        }

    let genRrenameat =
        gen {
            let! tag = genTag
            let size = H
            return MsgRrenameat(Rrenameat(size, tag))
        }

    let genRunlinkat =
        gen {
            let! tag = genTag
            let size = H
            return MsgRunlinkat(Runlinkat(size, tag))
        }

    // ─── The Master Generator ────────────────────────────────────────────

    let genNinePMessage = 
        Gen.oneof [
            // Classic 9P2000 T-messages
            genTversion
            genTauth
            genTflush
            genTattach
            genTwalk
            genTopen
            genTcreate
            genTread
            genTwrite
            genTclunk
            genTremove
            genTstat
            genTwstat
            // Classic 9P2000 R-messages
            genRversion
            genRauth
            genRattach
            genRerror
            genRopen
            genRcreate
            genRread
            genRwrite
            genRclunk
            genRflush
            genRremove
            genRwstat
            genRwalk
            genRstat
            // 9P2000.L T-messages
            genTstatfs
            genTlopen
            genTlcreate
            genTsymlink
            genTmknod
            genTrename
            genTreadlink
            genTgetattr
            genTsetattr
            genTxattrwalk
            genTxattrcreate
            genTreaddir
            genTfsync
            genTlock
            genTgetlock
            genTlink
            genTmkdir
            genTrenameat
            genTunlinkat
            // 9P2000.L R-messages
            genRlerror
            genRstatfs
            genRlopen
            genRlcreate
            genRsymlink
            genRmknod
            genRrename
            genRreadlink
            genRgetattr
            genRsetattr
            genRxattrwalk
            genRxattrcreate
            genRreaddir
            genRfsync
            genRlock
            genRgetlock
            genRlink
            genRmkdir
            genRrenameat
            genRunlinkat
        ]

    type NinePArb =
        static member NinePMessage() = Arb.fromGen genNinePMessage
        static member Qid() = Arb.fromGen genQid
        static member Stat() = Arb.fromGen (genStat true)
