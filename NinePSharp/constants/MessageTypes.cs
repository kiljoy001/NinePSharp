namespace NinePSharp.Constants;

//Copied from https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/tree/include/net/9p/9p.h,
//updated with explict R type numbers

/// <summary>
/// Defines the various 9P message types for both 9P2000 and 9P2000.L.
/// </summary>
public enum MessageTypes : byte
{
	/// <summary>9P2000.L error request (illegal)</summary>
	Tlerror = 6,
	/// <summary>9P2000.L error response</summary>
	Rlerror = 7,
	/// <summary>9P2000.L statfs request</summary>
	Tstatfs = 8,
	/// <summary>9P2000.L statfs response</summary>
	Rstatsfs = 9,
	/// <summary>9P2000.L open request</summary>
	Tlopen = 12,
	/// <summary>9P2000.L open response</summary>
	RLopen = 13,
	/// <summary>9P2000.L create request</summary>
	Tlcreate = 14,
	/// <summary>9P2000.L create response</summary>
	Rlcreate = 15,
	/// <summary>9P2000.L symlink request</summary>
	Tsymlink = 16,
	/// <summary>9P2000.L symlink response</summary>
	Rsymlink = 17,
	/// <summary>9P2000.L mknod request</summary>
	Tmknod = 18,
	/// <summary>9P2000.L mknod response</summary>
	Rmknod = 19,
	/// <summary>9P2000.L rename request</summary>
	Trename = 20,
	/// <summary>9P2000.L rename response</summary>
	Rrename = 21,
	/// <summary>9P2000.L readlink request</summary>
	Treadlink = 22,
	/// <summary>9P2000.L readlink response</summary>
	Rreadlink = 23,
	/// <summary>9P2000.L getattr request</summary>
	Tgetattr = 24,
	/// <summary>9P2000.L getattr response</summary>
	Rgetattr = 25,
	/// <summary>9P2000.L setattr request</summary>
	Tsetattr = 26,
	/// <summary>9P2000.L setattr response</summary>
	Rsetattr = 27,
	/// <summary>9P2000.L xattr walk request</summary>
	Txattrwalk = 30,
	/// <summary>9P2000.L xattr walk response</summary>
	Rxattrwalk = 31,
	/// <summary>9P2000.L xattr create request</summary>
	Txattrcreate = 32,
	/// <summary>9P2000.L xattr create response</summary>
	Rxattrcreate = 33,
	/// <summary>9P2000.L readdir request</summary>
	Treaddir = 40,
	/// <summary>9P2000.L readdir response</summary>
	Rreaddir = 41,
	/// <summary>9P2000.L fsync request</summary>
	Tfsync =50,
	/// <summary>9P2000.L fsync response</summary>
	Rfsync = 51,
	/// <summary>9P2000.L lock request</summary>
	Tlock = 52,
	/// <summary>9P2000.L lock response</summary>
	Rlock = 53,
	/// <summary>9P2000.L getlock request</summary>
	Tgetlock = 54,
	/// <summary>9P2000.L getlock response</summary>
	Rgetlock = 55,
	/// <summary>9P2000.L link request</summary>
	Tlink = 70,
	/// <summary>9P2000.L link response</summary>
	Rlink = 71,
	/// <summary>9P2000.L mkdir request</summary>
	Tmkdir = 72,
	/// <summary>9P2000.L mkdir response</summary>
	Rmkdir = 73,
	/// <summary>9P2000.L renameat request</summary>
	Trenameat = 74,
	/// <summary>9P2000.L renameat response</summary>
	Rrenameat = 75,
	/// <summary>9P2000.L unlinkat request</summary>
	Tunlinkat = 76,
	/// <summary>9P2000.L unlinkat response</summary>
	Runlinkat = 77,
	/// <summary>9P2000 version negotiation request</summary>
    	Tversion = 100,
	/// <summary>9P2000 version negotiation response</summary>
	Rversion = 101,
	/// <summary>9P2000 authentication request</summary>
	Tauth = 102,
	/// <summary>9P2000 authentication response</summary>
	Rauth = 103,
	/// <summary>9P2000 attach request</summary>
	Tattach = 104,
	/// <summary>9P2000 attach response</summary>
	Rattach = 105,
	/// <summary>9P2000 error request (illegal)</summary>
	Terror = 106,
	/// <summary>9P2000 error response</summary>
	Rerror = 107,
	/// <summary>9P2000 flush request</summary>
	Tflush = 108,
	/// <summary>9P2000 flush response</summary>
	Rflush =109,
	/// <summary>9P2000 walk request</summary>
	Twalk = 110,
	/// <summary>9P2000 walk response</summary>
	Rwalk = 111,
	/// <summary>9P2000 open request</summary>
	Topen = 112,
	/// <summary>9P2000 open response</summary>
	Ropen = 113,
	/// <summary>9P2000 create request</summary>
	Tcreate = 114,
	/// <summary>9P2000 create response</summary>
	Rcreate = 115,
	/// <summary>9P2000 read request</summary>
	Tread = 116,
	/// <summary>9P2000 read response</summary>
	Rread = 117,
	/// <summary>9P2000 write request</summary>
	Twrite = 118,
	/// <summary>9P2000 write response</summary>
	Rwrite =119,
	/// <summary>9P2000 clunk request</summary>
	Tclunk = 120,
	/// <summary>9P2000 clunk response</summary>
	Rclunk = 121,
	/// <summary>9P2000 remove request</summary>
	Tremove = 122,
	/// <summary>9P2000 remove response</summary>
	Rremove = 123,
	/// <summary>9P2000 stat request</summary>
	Tstat = 124,
	/// <summary>9P2000 stat response</summary>
	Rstat = 125,
	/// <summary>9P2000 wstat request</summary>
	Twstat = 126,
	/// <summary>9P2000 wstat response</summary>
	Rwstat = 127
}

/// <summary>
/// Numerical error codes used by 9P2000.L.
/// </summary>
public enum LinuxErrorCode : uint
{
	/// <summary>Success (no error)</summary>
	ESUCCESS = 0,
	/// <summary>Operation not permitted</summary>
	EPERM = 1,
	/// <summary>No such file or directory</summary>
	ENOENT = 2,
	/// <summary>I/O error</summary>
	EIO = 5,
	/// <summary>Permission denied</summary>
	EACCESS = 13,
	/// <summary>File exists</summary>
	EEXIST = 17,
	/// <summary>Not a directory</summary>
	ENOTDIR = 20,
	/// <summary>Is a directory</summary>
	EISDIR = 21,
	/// <summary>Invalid argument</summary>
	EINVAL = 22,
	/// <summary>No space left on device</summary>
	ENOSPC = 28,
	/// <summary>Read-only file system</summary>
	EROFS = 30,
	/// <summary>Directory not empty</summary>
	ENOTEMPTY = 39,
	/// <summary>Bad file descriptor</summary>
	EBADF = 9,
	/// <summary>Resource temporarily unavailable</summary>
	EAGAIN = 11,
	/// <summary>Out of memory</summary>
	ENOMEM = 12,
	/// <summary>Bad address</summary>
	EFAULT = 14,
	/// <summary>Device or resource busy</summary>
	EBUSY = 16,
	/// <summary>Operation not supported</summary>
	EOPNOTSUPP = 95,
	/// <summary>Function not implemented</summary>
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

	/// <summary>
	/// Initializes a new Qid with the specified type, version, and path.
	/// </summary>
	/// <param name="type">The file type</param>
	/// <param name="version">The version number</param>
	/// <param name="path">The unique path identifier</param>
	public Qid(QidType type, uint version, ulong path)
	{
		Type = type;
		Version = version;
		Path = path;
	}
}