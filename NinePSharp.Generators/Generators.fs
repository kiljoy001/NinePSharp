namespace NinePSharp.Generators

open FsCheck
open FsCheck.FSharp
open NinePSharp.Constants
open NinePSharp.Messages
open NinePSharp.Parser
open System
open System.Buffers.Binary
open System.Linq
open System.Text

module Generators =
    let gen = GenBuilder.gen
    
    // ─── Primitive Generators ────────────────────────────────────────────

    type Valid9PString = { Value: string }
    
    let genValidString =
        gen {
            let! size = Gen.sized Gen.constant
            let len = max 1 size
            let! chars = Gen.arrayOfLength len (Gen.choose(32, 126) |> Gen.map char)
            return { Value = new string(chars) }
        }

    /// Strings with nulls, invalid UTF-8, or extreme lengths
    let genInvalidString =
        Gen.oneof [
            Gen.constant null
            Gen.constant ""
            Gen.arrayOfLength 100 (Gen.choose(0, 255) |> Gen.map char) |> Gen.map (fun cs -> new string(cs))
            Gen.constant "\u0000\u0001\u0002"
            Gen.arrayOfLength 10000 (Gen.constant 'A') |> Gen.map (fun cs -> new string(cs))
        ]

    let genTag : Gen<uint16> = Gen.choose(0, 65535) |> Gen.map uint16
    let genFid : Gen<uint32> = Gen.choose(0, Int32.MaxValue) |> Gen.map uint32
    let genOffset : Gen<uint64> = Gen.choose(0, Int32.MaxValue) |> Gen.map uint64
    let genCount : Gen<uint32> = Gen.choose(1, 8192) |> Gen.map uint32
    let genByte' : Gen<byte> = Gen.choose(0, 255) |> Gen.map byte
    let genU32 : Gen<uint32> = Gen.choose(0, Int32.MaxValue) |> Gen.map uint32
    let genU64 : Gen<uint64> = Gen.choose(0, Int32.MaxValue) |> Gen.map uint64
    
    let genBoundaryU32 = Gen.elements [0u; 1u; UInt32.MaxValue; UInt32.MaxValue - 1u]
    let genBoundaryU64 = Gen.elements [0uL; 1uL; UInt64.MaxValue; UInt64.MaxValue - 1uL]

    let genQid : Gen<Qid> = 
        Gen.map3 (fun qType version path -> Qid(qType, version, path))
            (Gen.elements [QidType.QTDIR; QidType.QTFILE; QidType.QTAPPEND; QidType.QTEXCL])
            genU32
            genU64

    let genStat (dialect: NinePDialect) =
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
            
            return Stat(0us, 0us, 0u, qid, mode, atime, mtime, length, name.Value, uid.Value, gid.Value, muid.Value, dialect)
        }

    /// Helper to compute 9P string wire size: 2-byte length prefix + UTF-8 bytes
    let strWireSize (s: string) = 
        if isNull s then 2 
        else 2 + Encoding.UTF8.GetByteCount(s)

    /// Header size constant (size[4] + type[1] + tag[2] = 7)
    let H = 7u

    let genSmallData : Gen<byte[]> =
        gen {
            let! len = Gen.choose(0, 128)
            let! data = Gen.arrayOfLength len genByte'
            return data
        }

    /// Write a 9P string (2-byte length prefix + UTF-8 bytes) to a span
    let private writeStr (span: Span<byte>) (offset: byref<int>) (s: string) =
        if isNull s then
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), 0us)
            offset <- offset + 2
        else
            let bytes = Encoding.UTF8.GetBytes(s)
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
            let! stat = genStat NinePDialect.NineP2000
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
            let! stat = genStat NinePDialect.NineP2000
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

    // ─── Edge Case Generators ────────────────────────────────────────────

    /// Generate strings with control characters, null bytes, and invalid UTF-8
    let genMalformedString =
        Gen.oneof [
            Gen.constant null
            Gen.constant ""
            Gen.constant "\x00"  // Null byte
            Gen.constant "\x00\x01\x02\x03"  // Control characters
            Gen.constant (String('A', 100000))  // Huge string
            Gen.arrayOfLength 100 (Gen.choose(0, 255) |> Gen.map char) |> Gen.map (fun cs -> new string(cs))  // Invalid UTF-8
        ]

    /// Generate malformed 9P headers (size mismatch, invalid types, etc.)
    let genMalformedHeader =
        gen {
            let! invalidSize = Gen.elements [0u; 1u; 6u; UInt32.MaxValue]
            let! invalidType = Gen.elements [0uy; 99uy; 200uy; 255uy]  // Not in MessageTypes
            let! tag = genTag
            let buf = Array.zeroCreate 7
            let span = buf.AsSpan()
            writeHdr span invalidSize invalidType tag
            return buf
        }

    /// Generate messages with boundary FID values
    let genBoundaryFids : Gen<uint32[]> =
        Gen.constant [| 0u; 1u; UInt32.MaxValue - 1u; UInt32.MaxValue; 0xDEADBEEFu |]

    /// Generate path components known to cause issues
    let genMaliciousPaths =
        Gen.elements [
            [| ".."; ".."; ".." |]  // Parent traversal
            [| "/"; "etc"; "passwd" |]  // Absolute path
            [| ""; ""; "" |]  // Empty components
            [| "."; ".." |]  // Special dirs
            [| String('A', 10000) |]  // Huge component
            [| "\x00"; "\x01" |]  // Null bytes
        ]

    /// Generate Twalk messages with malicious paths
    let genMaliciousTwalk =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! newFid = genFid
            let! path = genMaliciousPaths
            return MsgTwalk(Twalk(tag, fid, newFid, path))
        }

    /// Generate Tread messages with boundary values
    let genBoundaryTread =
        gen {
            let! tag = genTag
            let! fid = Gen.elements [0u; UInt32.MaxValue]
            let! offset = Gen.elements [0uL; UInt64.MaxValue - 1uL; UInt64.MaxValue]
            let! count = Gen.elements [0u; 1u; UInt32.MaxValue]
            return MsgTread(Tread(tag, fid, offset, count))
        }

    /// Generate Twrite messages with boundary values and malformed data
    let genBoundaryTwrite =
        gen {
            let! tag = genTag
            let! fid = Gen.elements [0u; UInt32.MaxValue]
            let! offset = Gen.elements [0uL; UInt64.MaxValue]
            let! dataSize = Gen.elements [0; 1; 100000]
            let! data = Gen.arrayOfLength dataSize genByte'
            return MsgTwrite(Twrite(tag, fid, offset, ReadOnlyMemory<byte>(data)))
        }

    /// Generate Tlcreate messages with malicious filenames
    let genMaliciousTlcreate =
        gen {
            let! tag = genTag
            let! fid = genFid
            let! name = Gen.elements [""; "/"; ".."; "\x00"; String('X', 10000)]
            let! flags = Gen.elements [0u; UInt32.MaxValue]
            let! mode = Gen.elements [0u; 0777u; UInt32.MaxValue]
            let! gid = genU32
            let size = H + 4u + uint32 (strWireSize name) + 4u + 4u + 4u
            return MsgTlcreate(Tlcreate(size, tag, fid, name, flags, mode, gid))
        }

    /// Generate complete attack sequences (multi-message)
    let genAttackSequence =
        gen {
            let! attack = Gen.elements ["traversal"; "overflow"; "injection"]
            match attack with
            | "traversal" ->
                return [
                    MsgTwalk(Twalk(100us, 1u, 2u, [| ".."; ".."; ".." |]))
                    MsgTread(Tread(101us, 2u, 0uL, 8192u))
                ]
            | "overflow" ->
                return [
                    MsgTread(Tread(100us, UInt32.MaxValue, UInt64.MaxValue, UInt32.MaxValue))
                    MsgTwrite(Twrite(101us, UInt32.MaxValue, UInt64.MaxValue, ReadOnlyMemory<byte>(Array.zeroCreate 100000)))
                ]
            | "injection" ->
                let name = "/etc/passwd"
                let size = 7u + 4u + 2u + uint32 (Text.Encoding.UTF8.GetByteCount(name)) + 4u + 4u + 4u
                return [
                    MsgTlcreate(Tlcreate(size, 100us, 1u, name, 0u, 0777u, 0u))
                    MsgTwrite(Twrite(101us, 1u, 0uL, ReadOnlyMemory<byte>(Text.Encoding.UTF8.GetBytes("malicious content"))))
                ]
            | _ -> return []
        }

    // ─── Targeted Generators for Low Mutation Coverage ──────────────────

    /// Aggressive Stat generator testing variable-length fields and 9P2000.u extensions
    let genAggressiveStat : Gen<Stat> =
        gen {
            let! dialect = Gen.elements [NinePDialect.NineP2000; NinePDialect.NineP2000U; NinePDialect.NineP2000L]
            let! testCase = Gen.elements [
                "empty_strings"
                "long_strings"
                "max_length_strings"
                "null_strings"
                "unicode_strings"
                "size_mismatch"
                "boundary_values"
                "extension_edge_cases"
            ]

            let! name, uid, gid, muid, extension =
                match testCase with
                | "empty_strings" -> Gen.constant ("", "", "", "", "")
                | "long_strings" ->
                    let long = String.replicate 1000 "A"
                    Gen.constant (long, long, long, long, long)
                | "max_length_strings" ->
                    let max = String.replicate 65535 "X"
                    Gen.constant (max, "u", "g", "m", "e")
                | "null_strings" -> Gen.constant ("", "", "", "", "")
                | "unicode_strings" ->
                    Gen.constant ("日本語ファイル", "用户", "组", "修改者", "扩展")
                | "size_mismatch" ->
                    Gen.constant ("file.txt", "user123", "group456", "modifier789", "ext")
                | "boundary_values" ->
                    Gen.constant ("a", "b", "c", "d", "e")
                | "extension_edge_cases" ->
                    Gen.constant ("file", "user", "group", "muid", String.replicate 10000 "E")
                | _ -> Gen.constant ("default", "user", "group", "muid", "ext")

            let! typ = Gen.choose(0, 65535) |> Gen.map uint16
            let! dev = Gen.elements [0u; 1u; UInt32.MaxValue]
            let! qid = genQid
            let! mode = Gen.elements [0u; 0o755u; 0o644u; 0o777u; UInt32.MaxValue]
            let! atime = Gen.elements [0u; 1u; UInt32.MaxValue]
            let! mtime = Gen.elements [0u; 1u; UInt32.MaxValue]
            let! length = Gen.elements [0uL; 1uL; UInt64.MaxValue]

            let! nuid = Gen.elements [Nullable 0u; Nullable UInt32.MaxValue; Nullable 1000u; Nullable()]
            let! ngid = Gen.elements [Nullable 0u; Nullable UInt32.MaxValue; Nullable 1000u; Nullable()]
            let! nmuid = Gen.elements [Nullable 0u; Nullable UInt32.MaxValue; Nullable 1000u; Nullable()]

            // Test both correct and incorrect size calculations
            let! useCorrectSize = Gen.elements [true; false]
            let is9u = dialect = NinePDialect.NineP2000U || dialect = NinePDialect.NineP2000L
            let extStr = if is9u then extension else null
            let correctSize = Stat.CalculateSize(name, uid, gid, muid, dialect, extStr)
            let! sizeOffset = Gen.choose(-10, 10) |> Gen.map uint16
            let size = if useCorrectSize then correctSize else (uint16)(int correctSize + int sizeOffset)

            return Stat(size, typ, dev, qid, mode, atime, mtime, length, name, uid, gid, muid, dialect,
                       extStr, nuid, ngid, nmuid)
        }

    /// Aggressive Tattach generator testing string boundaries and 9P2000.u extension
    let genAggressiveTattach : Gen<Tattach> =
        gen {
            let! tag = genTag
            let! fid = Gen.elements [0u; 1u; 100u; UInt32.MaxValue]
            let! afid = Gen.elements [0u; NinePConstants.NoFid; UInt32.MaxValue]

            let! testCase = Gen.elements [
                "empty_strings"
                "null_strings"
                "long_strings"
                "max_length_strings"
                "unicode_strings"
                "special_chars"
                "whitespace"
                "mixed_case"
            ]

            let! uname, aname =
                match testCase with
                | "empty_strings" -> Gen.constant ("", "")
                | "null_strings" -> Gen.constant ("", "")
                | "long_strings" ->
                    let long = String.replicate 10000 "U"
                    Gen.constant (long, long)
                | "max_length_strings" ->
                    let max = String.replicate 65535 "X"
                    Gen.constant (max, "aname")
                | "unicode_strings" ->
                    Gen.constant ("用户名", "文件系统名称")
                | "special_chars" ->
                    Gen.constant ("user!@#$%^&*()", "aname'\"<>?")
                | "whitespace" ->
                    Gen.constant (" \t\n\r", " \t\n\r")
                | "mixed_case" ->
                    Gen.constant ("UsErNaMe", "AnAmE")
                | _ -> Gen.constant ("user", "aname")

            return Tattach(tag, fid, afid, uname, aname)
        }

    /// Aggressive Tauth generator testing string boundaries
    let genAggressiveTauth : Gen<Tauth> =
        gen {
            let! tag = genTag
            let! afid = Gen.elements [0u; 1u; NinePConstants.NoFid; UInt32.MaxValue]

            let! testCase = Gen.elements [
                "empty_strings"
                "null_strings"
                "long_strings"
                "max_length_strings"
                "unicode_strings"
                "special_chars"
                "whitespace"
                "boundary_lengths"
            ]

            let! uname, aname =
                match testCase with
                | "empty_strings" -> Gen.constant ("", "")
                | "null_strings" -> Gen.constant ("", "")
                | "long_strings" ->
                    let long = String.replicate 10000 "A"
                    Gen.constant (long, long)
                | "max_length_strings" ->
                    let max = String.replicate 65535 "M"
                    Gen.constant (max, "auth")
                | "unicode_strings" ->
                    Gen.constant ("认证用户", "认证名称")
                | "special_chars" ->
                    Gen.constant ("auth!@#$%", "name'\"<>")
                | "whitespace" ->
                    Gen.constant (" \t\r\n", "  ")
                | "boundary_lengths" ->
                    // Test exact boundary: 65535 UTF8 bytes
                    let boundary = String.replicate 65535 "B"
                    Gen.constant (boundary, "x")
                | _ -> Gen.constant ("auth_user", "auth_name")

            let! nuname = Gen.elements [Nullable 0u; Nullable 1000u; Nullable UInt32.MaxValue; Nullable()]

            return Tauth(tag, afid, uname, aname, nuname)
        }

    /// Aggressive Rstatfs generator testing all numeric field combinations
    let genAggressiveRstatfs : Gen<Rstatfs> =
        gen {
            let! tag = genTag
            let! testCase = Gen.elements [
                "all_zero"
                "all_max"
                "invalid_free_greater_than_total"
                "boundary_values"
                "realistic_full_disk"
                "realistic_empty_disk"
                "edge_case_one_block"
                "inconsistent_values"
            ]

            let! fsType, bSize, blocks, bFree, bAvail, files, fFree, fsId, nameLen =
                match testCase with
                | "all_zero" ->
                    Gen.constant (0u, 0u, 0uL, 0uL, 0uL, 0uL, 0uL, 0uL, 0u)
                | "all_max" ->
                    Gen.constant (UInt32.MaxValue, UInt32.MaxValue, UInt64.MaxValue, UInt64.MaxValue,
                                 UInt64.MaxValue, UInt64.MaxValue, UInt64.MaxValue, UInt64.MaxValue, UInt32.MaxValue)
                | "invalid_free_greater_than_total" ->
                    // BFree > Blocks (invalid), FFree > Files (invalid)
                    Gen.constant (1u, 4096u, 1000uL, 2000uL, 2000uL, 500uL, 1000uL, 12345uL, 255u)
                | "boundary_values" ->
                    Gen.constant (1u, 1u, 1uL, 1uL, 1uL, 1uL, 1uL, 1uL, 1u)
                | "realistic_full_disk" ->
                    // Realistic: 1TB disk, 4KB blocks, 95% full
                    let totalBlocks = 268435456uL // 1TB / 4KB
                    let freeBlocks = 13421772uL   // 5% free
                    Gen.constant (0x9123u, 4096u, totalBlocks, freeBlocks, freeBlocks, 10000000uL, 500000uL, 0xABCDEF123456uL, 255u)
                | "realistic_empty_disk" ->
                    // Realistic: 500GB disk, all free
                    let totalBlocks = 134217728uL
                    Gen.constant (0x53464846u, 4096u, totalBlocks, totalBlocks, totalBlocks, 50000000uL, 50000000uL, 0x123456uL, 255u)
                | "edge_case_one_block" ->
                    Gen.constant (1u, 512u, 1uL, 0uL, 0uL, 1uL, 0uL, 1uL, 1u)
                | "inconsistent_values" ->
                    // BAvail != BFree (sometimes valid in some filesystems)
                    Gen.constant (2u, 8192u, 100000uL, 50000uL, 45000uL, 1000000uL, 800000uL, 0xDEADBEEFuL, 128u)
                | _ ->
                    Gen.constant (0x9123u, 4096u, 1000uL, 500uL, 500uL, 10000uL, 5000uL, 123456uL, 255u)

            return Rstatfs(tag, fsType, bSize, blocks, bFree, bAvail, files, fFree, fsId, nameLen)
        }

    // ─── FID State Machine Violation Generators ─────────────────────────

    /// Generator for FID state machine violations (double clunk, use after clunk, etc.)
    let genFidStateViolation : Gen<NinePMessage list> =
        gen {
            let! violationType = Gen.elements [
                "double_clunk"
                "use_after_clunk"
                "invalid_fid"
                "walk_existing_newfid"
                "clunk_twice_different_tags"
            ]

            let fid = 100u
            let tag = 1us

            match violationType with
            | "double_clunk" ->
                // Attach, then clunk twice on same FID
                return [
                    MsgTattach(Tattach(tag, fid, NinePConstants.NoFid, "user", ""))
                    MsgTclunk(Tclunk((uint16)(tag + 1us), fid))
                    MsgTclunk(Tclunk((uint16)(tag + 2us), fid)) // INVALID: FID already clunked
                ]
            | "use_after_clunk" ->
                // Attach, clunk, then try to use
                return [
                    MsgTattach(Tattach(tag, fid, NinePConstants.NoFid, "user", ""))
                    MsgTclunk(Tclunk((uint16)(tag + 1us), fid))
                    MsgTstat(Tstat((uint16)(tag + 2us), fid)) // INVALID: FID was clunked
                ]
            | "invalid_fid" ->
                // Try to use FID that was never attached
                return [
                    MsgTstat(Tstat(tag, 9999u)) // INVALID: FID never attached
                ]
            | "walk_existing_newfid" ->
                // Attach, walk to create newfid, walk again with same newfid
                return [
                    MsgTattach(Tattach(tag, fid, NinePConstants.NoFid, "user", ""))
                    MsgTwalk(Twalk((uint16)(tag + 1us), fid, fid + 1u, [| "test" |]))
                    MsgTwalk(Twalk((uint16)(tag + 2us), fid, fid + 1u, [| "test2" |])) // INVALID: newfid already exists
                ]
            | "clunk_twice_different_tags" ->
                // Clunk with different tags (still invalid)
                return [
                    MsgTattach(Tattach(tag, fid, NinePConstants.NoFid, "user", ""))
                    MsgTclunk(Tclunk((uint16)(tag + 1us), fid))
                    MsgTclunk(Tclunk((uint16)(tag + 99us), fid)) // INVALID: different tag, same FID
                ]
            | _ -> return []
        }

    /// Generator for complex FID lifecycle sequences with violations
    let genFidLifecycleViolation : Gen<NinePMessage list> =
        gen {
            let baseFid = 100u
            let! violationPoint = Gen.elements ["after_attach"; "after_walk"; "after_open"; "after_read"; "after_clunk"]

            let baseSequence = [
                MsgTattach(Tattach(1us, baseFid, NinePConstants.NoFid, "user", ""))
                MsgTwalk(Twalk(2us, baseFid, baseFid + 1u, [| "file.txt" |]))
                MsgTopen(Topen(3us, baseFid + 1u, 0uy))
                MsgTread(Tread(4us, baseFid + 1u, 0uL, 100u))
                MsgTclunk(Tclunk(5us, baseFid + 1u))
            ]

            let violation =
                match violationPoint with
                | "after_clunk" ->
                    // Try to read after clunk
                    MsgTread(Tread(6us, baseFid + 1u, 0uL, 100u))
                | "after_attach" ->
                    // Try to read before walk/open
                    MsgTread(Tread(6us, baseFid, 0uL, 100u))
                | "after_walk" ->
                    // Try to read before open
                    MsgTread(Tread(6us, baseFid + 1u, 0uL, 100u))
                | "after_open" ->
                    // Double open
                    MsgTopen(Topen(6us, baseFid + 1u, 0uy))
                | "after_read" ->
                    // Double clunk
                    MsgTclunk(Tclunk(6us, baseFid + 1u))
                | _ ->
                    MsgTstat(Tstat(6us, 9999u))

            return baseSequence @ [violation]
        }

    // ─── String Boundary Generators (null vs empty) ─────────────────────

    /// Generator specifically for null vs empty string edge cases
    let genNullVsEmptyStrings : Gen<Stat> =
        gen {
            let! nameCase = Gen.elements ["null"; "empty"; "whitespace"; "normal"]
            let! uidCase = Gen.elements ["null"; "empty"; "whitespace"; "normal"]
            let! gidCase = Gen.elements ["null"; "empty"; "whitespace"; "normal"]
            let! muidCase = Gen.elements ["null"; "empty"; "whitespace"; "normal"]

            let toStr case_ =
                match case_ with
                | "null" -> null
                | "empty" -> ""
                | "whitespace" -> " "
                | "normal" -> "user"
                | _ -> ""

            let name = toStr nameCase
            let uid = toStr uidCase
            let gid = toStr gidCase
            let muid = toStr muidCase

            let! qid = genQid

            // Create Stat with potentially null strings
            return Stat(0us, 0us, 0u, qid, 0u, 0u, 0u, 0uL,
                       name, uid, gid, muid,
                       NinePDialect.NineP2000, null, Nullable(), Nullable(), Nullable())
        }

    /// Generator for Tattach with null vs empty string variations
    let genNullVsEmptyTattach : Gen<Tattach> =
        gen {
            let! unameCase = Gen.elements ["null"; "empty"; "whitespace"]
            let! anameCase = Gen.elements ["null"; "empty"; "whitespace"]

            let toStr case_ =
                match case_ with
                | "null" -> null
                | "empty" -> ""
                | "whitespace" -> " "
                | _ -> ""

            let uname = toStr unameCase
            let aname = toStr anameCase

            return Tattach(1us, 100u, NinePConstants.NoFid, uname, aname)
        }

    /// Generator for Tauth with null vs empty string variations
    let genNullVsEmptyTauth : Gen<Tauth> =
        gen {
            let! unameCase = Gen.elements ["null"; "empty"; "whitespace"]
            let! anameCase = Gen.elements ["null"; "empty"; "whitespace"]

            let toStr case_ =
                match case_ with
                | "null" -> null
                | "empty" -> ""
                | "whitespace" -> " "
                | _ -> ""

            let uname = toStr unameCase
            let aname = toStr anameCase

            return Tauth(1us, 1u, uname, aname, Nullable())
        }

    // ─── Server Startup and Configuration Generators ────────────────────

    /// Generator for malformed TCP message streams (for fuzzing server input)
    let genMalformedTcpStream : Gen<byte[]> =
        gen {
            let! streamType = Gen.elements [
                "empty"
                "partial_header"
                "invalid_size"
                "oversized_claim"
                "random_junk"
                "valid_then_junk"
            ]

            match streamType with
            | "empty" ->
                return Array.empty
            | "partial_header" ->
                let! partialSize = Gen.choose(1, 6)
                let! bytes = Gen.arrayOfLength partialSize genByte'
                return bytes
            | "invalid_size" ->
                // Size field claims message is smaller than header
                let buffer = Array.zeroCreate 7
                BinaryPrimitives.WriteUInt32LittleEndian(Span<byte>(buffer, 0, 4), 3u) // Invalid: < 7
                buffer.[4] <- byte MessageTypes.Tversion
                return buffer
            | "oversized_claim" ->
                // Valid message but size field claims much larger
                let tversion = Tversion(100us, 8192u, "9P2000")
                let realSize = int tversion.Size
                let buffer = Array.zeroCreate realSize
                tversion.WriteTo(Span<byte>(buffer))
                // Tamper with size
                BinaryPrimitives.WriteUInt32LittleEndian(Span<byte>(buffer, 0, 4), uint32 realSize + 10000u)
                return buffer
            | "random_junk" ->
                let! len = Gen.choose(1, 512)
                let! bytes = Gen.arrayOfLength len genByte'
                return bytes
            | "valid_then_junk" ->
                // Start with valid Tversion, then append junk
                let tversion = Tversion(100us, 8192u, "9P2000")
                let realSize = int tversion.Size
                let! junkSize = Gen.choose(1, 100)
                let buffer = Array.zeroCreate (realSize + junkSize)
                tversion.WriteTo(Span<byte>(buffer))
                let! junk = Gen.arrayOfLength junkSize genByte'
                Array.Copy(junk, 0, buffer, realSize, junkSize)
                return buffer
            | _ ->
                return Array.empty
        }

    /// Generator for server configuration edge cases
    let genServerConfig : Gen<(string * int * string)> =
        gen {
            let! addressType = Gen.elements [
                "localhost"
                "ipv4_loopback"
                "ipv4_any"
                "ipv6_loopback"
                "invalid_ip"
            ]

            let address =
                match addressType with
                | "localhost" -> "localhost"
                | "ipv4_loopback" -> "127.0.0.1"
                | "ipv4_any" -> "0.0.0.0"
                | "ipv6_loopback" -> "::1"
                | "invalid_ip" -> "999.999.999.999"
                | _ -> "127.0.0.1"

            let! portType = Gen.elements ["zero"; "low"; "high"; "max"; "invalid"]
            let port =
                match portType with
                | "zero" -> 0 // Auto-assign
                | "low" -> 1024
                | "high" -> 9564
                | "max" -> 65535
                | "invalid" -> 70000 // Invalid port
                | _ -> 9564

            let! protocol = Gen.elements ["tcp"; "tls"; "invalid"]

            return (address, port, protocol)
        }

    /// Generator for client conversation sequences (for integration testing)
    let genClientConversation : Gen<NinePMessage list> =
        gen {
            let! conversationType = Gen.elements [
                "minimal"           // Just Tversion
                "attach_only"       // Tversion -> Tattach
                "full_walk"         // Tversion -> Tattach -> Twalk
                "read_sequence"     // ... -> Open -> Read
                "write_sequence"    // ... -> Open -> Write
                "lifecycle"         // Full lifecycle with Clunk
            ]

            let baseTag = 1us
            let baseFid = 100u

            match conversationType with
            | "minimal" ->
                return [
                    MsgTversion(Tversion(baseTag, 8192u, "9P2000"))
                ]
            | "attach_only" ->
                return [
                    MsgTversion(Tversion(baseTag, 8192u, "9P2000"))
                    MsgTattach(Tattach((uint16)(baseTag + 1us), baseFid, NinePConstants.NoFid, "user", ""))
                ]
            | "full_walk" ->
                return [
                    MsgTversion(Tversion(baseTag, 8192u, "9P2000"))
                    MsgTattach(Tattach((uint16)(baseTag + 1us), baseFid, NinePConstants.NoFid, "user", ""))
                    MsgTwalk(Twalk((uint16)(baseTag + 2us), baseFid, baseFid + 1u, [| "test.txt" |]))
                ]
            | "read_sequence" ->
                return [
                    MsgTversion(Tversion(baseTag, 8192u, "9P2000"))
                    MsgTattach(Tattach((uint16)(baseTag + 1us), baseFid, NinePConstants.NoFid, "user", ""))
                    MsgTwalk(Twalk((uint16)(baseTag + 2us), baseFid, baseFid + 1u, [| "test.txt" |]))
                    MsgTopen(Topen((uint16)(baseTag + 3us), baseFid + 1u, 0uy))
                    MsgTread(Tread((uint16)(baseTag + 4us), baseFid + 1u, 0uL, 100u))
                ]
            | "write_sequence" ->
                return [
                    MsgTversion(Tversion(baseTag, 8192u, "9P2000"))
                    MsgTattach(Tattach((uint16)(baseTag + 1us), baseFid, NinePConstants.NoFid, "user", ""))
                    MsgTwalk(Twalk((uint16)(baseTag + 2us), baseFid, baseFid + 1u, [| "test.txt" |]))
                    MsgTopen(Topen((uint16)(baseTag + 3us), baseFid + 1u, 1uy))
                    MsgTwrite(Twrite((uint16)(baseTag + 4us), baseFid + 1u, 0uL, ReadOnlyMemory<byte>(Text.Encoding.UTF8.GetBytes("test data"))))
                ]
            | "lifecycle" ->
                return [
                    MsgTversion(Tversion(baseTag, 8192u, "9P2000"))
                    MsgTattach(Tattach((uint16)(baseTag + 1us), baseFid, NinePConstants.NoFid, "user", ""))
                    MsgTwalk(Twalk((uint16)(baseTag + 2us), baseFid, baseFid + 1u, [| "test.txt" |]))
                    MsgTopen(Topen((uint16)(baseTag + 3us), baseFid + 1u, 0uy))
                    MsgTread(Tread((uint16)(baseTag + 4us), baseFid + 1u, 0uL, 100u))
                    MsgTclunk(Tclunk((uint16)(baseTag + 5us), baseFid + 1u))
                ]
            | _ ->
                return []
        }

    // ─── LuxVault Generators ─────────────────────────────────────────────────

    /// Generator for passwords with edge cases (empty, whitespace, unicode, huge)
    let genVaultPassword : Gen<string> =
        Gen.oneof [
            Gen.constant ""
            Gen.constant " "
            Gen.constant "\t\n\r"
            genValidString |> Gen.map (fun s -> s.Value)
            Gen.constant "café"           // UTF-8 2-byte chars
            Gen.constant "密码"           // UTF-8 3-byte chars
            Gen.constant "🔐"             // UTF-8 4-byte chars (emoji)
            Gen.constant "pa$$w0rd!@#$%"  // Special chars
            Gen.arrayOfLength 1000 (Gen.constant 'A') |> Gen.map (fun cs -> new string(cs))  // Large password
        ]

    /// Generator for vault data (0-256 bytes, including edge cases)
    let genVaultData : Gen<byte[]> =
        gen {
            let! dataType = Gen.elements [
                "empty"
                "single"
                "small"
                "medium"
                "large"
            ]

            match dataType with
            | "empty" -> return Array.empty
            | "single" -> return [| 0x42uy |]
            | "small" ->
                let! len = Gen.choose(2, 32)
                let! data = Gen.arrayOfLength len genByte'
                return data
            | "medium" ->
                let! len = Gen.choose(33, 128)
                let! data = Gen.arrayOfLength len genByte'
                return data
            | "large" ->
                let! len = Gen.choose(129, 256)
                let! data = Gen.arrayOfLength len genByte'
                return data
            | _ -> return Array.empty
        }

    /// Generator for tampered vault payloads (valid payload with bit flips)
    let genTamperedPayload : Gen<byte[] * int> =
        gen {
            let! data = genVaultData
            let! password = genVaultPassword

            // This would require calling LuxVault.Encrypt, which we can't do in F#
            // Instead, just generate random payloads with bit flip locations
            let! payloadSize = Gen.choose(60, 200)
            let! payload = Gen.arrayOfLength payloadSize genByte'
            let! bitToFlip = Gen.choose(0, payloadSize - 1)

            return (payload, bitToFlip)
        }

    /// Generator for truncated vault payloads (< minimum size)
    let genTruncatedPayload : Gen<byte[]> =
        gen {
            // Valid vault needs: 16 salt + 24 nonce + 16 MAC = 56 bytes minimum
            let! size = Gen.choose(0, 55)
            let! data = Gen.arrayOfLength size genByte'
            return data
        }

    /// Generator for vault lifecycle operations
    type VaultOperation =
        | Store of string * byte[] * string
        | Load of string * string
        | Delete of string
        | CorruptFile of string

    let genVaultLifecycle : Gen<VaultOperation list> =
        gen {
            let secretName = "test-secret"
            let! password = genVaultPassword
            let! data = genVaultData

            let! operationType = Gen.elements [
                "normal_lifecycle"
                "double_load"
                "load_before_store"
                "delete_then_load"
                "corrupt_then_load"
            ]

            match operationType with
            | "normal_lifecycle" ->
                return [
                    Store(secretName, data, password)
                    Load(secretName, password)
                    Delete(secretName)
                ]
            | "double_load" ->
                return [
                    Store(secretName, data, password)
                    Load(secretName, password)
                    Load(secretName, password)
                ]
            | "load_before_store" ->
                return [
                    Load(secretName, password)
                ]
            | "delete_then_load" ->
                return [
                    Store(secretName, data, password)
                    Delete(secretName)
                    Load(secretName, password)
                ]
            | "corrupt_then_load" ->
                return [
                    Store(secretName, data, password)
                    CorruptFile(secretName)
                    Load(secretName, password)
                ]
            | _ ->
                return []
        }

    type NinePArb =
        static member Qid() = Arb.fromGen genQid
        static member Stat() = Arb.fromGen (genStat NinePDialect.NineP2000U)
        static member MaliciousTwalk() = Arb.fromGen genMaliciousTwalk
        static member BoundaryTread() = Arb.fromGen genBoundaryTread
        static member BoundaryTwrite() = Arb.fromGen genBoundaryTwrite
        static member MaliciousTlcreate() = Arb.fromGen genMaliciousTlcreate
        static member AggressiveStat() = Arb.fromGen genAggressiveStat
        static member AggressiveTattach() = Arb.fromGen genAggressiveTattach
        static member AggressiveTauth() = Arb.fromGen genAggressiveTauth
        static member AggressiveRstatfs() = Arb.fromGen genAggressiveRstatfs
        static member FidStateViolation() = Arb.fromGen genFidStateViolation
        static member FidLifecycleViolation() = Arb.fromGen genFidLifecycleViolation
        static member NullVsEmptyStrings() = Arb.fromGen genNullVsEmptyStrings
        static member NullVsEmptyTattach() = Arb.fromGen genNullVsEmptyTattach
        static member NullVsEmptyTauth() = Arb.fromGen genNullVsEmptyTauth
        static member MalformedTcpStream() = Arb.fromGen genMalformedTcpStream
        static member ServerConfig() = Arb.fromGen genServerConfig
        static member ClientConversation() = Arb.fromGen genClientConversation
        static member VaultPassword() = Arb.fromGen genVaultPassword
        static member VaultData() = Arb.fromGen genVaultData
        static member TamperedPayload() = Arb.fromGen genTamperedPayload
        static member TruncatedPayload() = Arb.fromGen genTruncatedPayload
        static member VaultLifecycle() = Arb.fromGen genVaultLifecycle
