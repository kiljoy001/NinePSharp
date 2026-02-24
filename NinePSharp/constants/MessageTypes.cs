namespace NinePSharp.Constants;

//Copied from https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/tree/include/net/9p/9p.h,
//updated with explict R type numbers

/// <summary>
/// Defines the various 9P message types for both 9P2000 and 9P2000.L.
/// </summary>
public enum MessageTypes : byte
{
	Tlerror = 6, /* illegal */
	Rlerror = 7,
	Tstatfs = 8,
	Rstatsfs = 9,
	Tlopen = 12,
	RLopen = 13,
	Tlcreate = 14,
	Rlcreate = 15,
	Tsymlink = 16,
	Rsymlink = 17,
	Tmknod = 18,
	Rmknod = 19,
	Trename = 20,
	Rrename = 21,
	Treadlink = 22,
	Rreadlink = 23,
	Tgetattr = 24,
	Rgetattr = 25,
	Tsetattr = 26,
	Rsetattr = 27,
	Txattrwalk = 30,
	Rxattrwalk = 31,
	Txattrcreate = 32,
	Rxattrcreate = 33,
	Treaddir = 40,
	Rreaddir = 41,
	Tfsync =50,
	Rfsync = 51,
	Tlock = 52,
	Rlock = 53,
	Tgetlock = 54,
	Rgetlock = 55,
	Tlink = 70,
	Rlink = 71,
	Tmkdir = 72,
	Rmkdir = 73,
	Trenameat = 74,
	Rrenameat = 75,
	Tunlinkat = 76,
	Runlinkat = 77,
    	Tversion = 100,
	Rversion = 101,
	Tauth = 102,
	Rauth = 103,
	Tattach = 104,
	Rattach = 105,
	Terror = 106,	/* illegal */
	Rerror = 107,
	Tflush = 108,
	Rflush =109,
	Twalk = 110,
	Rwalk = 111,
	Topen = 112,
	Ropen = 113,
	Tcreate = 114,
	Rcreate = 115,
	Tread = 116,
	Rread = 117,
	Twrite = 118,
	Rwrite =119,
	Tclunk = 120,
	Rclunk = 121,
	Tremove = 122,
	Rremove = 123,
	Tstat = 124,
	Rstat = 125,
	Twstat = 126,
	Rwstat = 127
}

/// <summary>
/// Numerical error codes used by 9P2000.L.
/// </summary>
public enum LinuxErrorCode : uint
{
	ESUCCESS = 0,
	EPERM = 1,
	ENOENT = 2,
	EIO = 5,
	EACCESS = 13,
	EEXIST = 17,
	ENOTDIR = 20,
	EISDIR = 21,
	EINVAL = 22,
	ENOSPC = 28,
	EROFS = 30,
	ENOTEMPTY = 39,
	EBADF = 9,
	EAGAIN = 11,
	ENOMEM = 12,
	EFAULT = 14,
	EBUSY = 16,
	EOPNOTSUPP = 95,
	ENOSYS = 38
}

/// <summary>
/// Bitmask for QID types in 9P.
/// </summary>
public enum QidType : byte
{
	/// <summary>Directory</summary>
	QTDIR = 0x80,
	/// <summary>Append-only file</summary>
	QTAPPEND = 0x40,
	/// <summary>Exclusive-use file</summary>
	QTEXCL = 0x20,
	/// <summary>Mounted channel</summary>
	QTMOUNT = 0x10,
	/// <summary>Authentication file</summary>
	QTAUTH = 0x08,
	/// <summary>Temporary file</summary>
	QTTMP = 0x40,
	/// <summary>Plain file</summary>
	QTFILE = 0X00
}

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
	
	public Qid(QidType type, uint version, ulong path)
	{
		Type = type;
		Version = version;
		Path = path;
	}
}