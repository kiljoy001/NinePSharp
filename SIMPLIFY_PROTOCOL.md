# Simplify to Basic 9P2000

## The Problem

You have **67 message types** implemented. This is insane for a learning project.

### What You Have:
- 9P2000 (original ~14 messages)
- 9P2000.u (Unix extensions)
- 9P2000.L (Linux extensions ~40+ messages!)

**This is why AI keeps generating garbage** - you're trying to support every version of the protocol at once.

---

## The Smart Approach: Start with 9P2000 Only

### Core 9P2000 Messages (KEEP THESE)

**Session Management:**
- ✓ Tversion / Rversion
- ✓ Tattach / Rattach
- ✓ Tauth / Rauth (optional, can skip for now)

**File Navigation:**
- ✓ Twalk / Rwalk
- ✓ Tstat / Rstat

**File Operations:**
- ✓ Topen / Ropen
- ✓ Tcreate / Rcreate
- ✓ Tread / Rread
- ✓ Twrite / Rwrite

**Cleanup:**
- ✓ Tclunk / Rclunk
- ✓ Tremove / Rremove

**Flow Control:**
- ✓ Tflush / Rflush (optional, can skip initially)

**Errors:**
- ✓ Rerror

**Total: 14 messages (7 request/response pairs)**

This is **manageable** and **well-documented**.

---

## Messages to DELETE (Move to archived/)

### 9P2000.L Messages (DELETE ALL OF THESE FOR NOW):
- ✗ Treaddir / Rreaddir (use Tread on directories in basic 9P)
- ✗ Tfsync / Rfsync
- ✗ Tgetattr / Rgetattr (use Tstat instead)
- ✗ Tsetattr / Rsetattr (use Twstat instead)
- ✗ Tstatfs / Rstatfs
- ✗ Tlopen / Rlopen (use Topen instead)
- ✗ Tlcreate / Rlcreate (use Tcreate instead)
- ✗ Tmkdir / Rmkdir (use Tcreate with DMDIR flag)
- ✗ Tmknod / Rmknod
- ✗ Tsymlink / Rsymlink
- ✗ Treadlink / Rreadlink
- ✗ Tlink / Rlink
- ✗ Trename / Rrename
- ✗ Trenameat / Rrenameat
- ✗ Tunlinkat / Runlinkat
- ✗ Tgetlock / Rgetlock
- ✗ Tsetlock / Rsetlock (shown as Tlock/Rlock in your code)
- ✗ Tlock / Rlock
- ✗ Rlerror (use Rerror instead)

**That's ~20 message types you can DELETE.**

### Why Delete .L Messages?

1. **Complexity:** .L messages have different wire formats, semantics, and edge cases
2. **Linux-specific:** Only needed if you want full Linux kernel v9fs compatibility
3. **Not portable:** Plan 9, Inferno, and other clients don't support .L
4. **Poorly documented:** Much less documentation than basic 9P2000
5. **Overkill:** You don't need symlinks, locks, and xattrs for your use case

---

## The Twstat Message (Keep or Delete?)

**Twstat / Rwstat** is technically part of basic 9P2000, but it's complex and rarely used.

**My recommendation:** Delete it for now, add back later if needed.

Most clients only use:
- Tstat (read metadata)
- NOT Twstat (write metadata)

You can build a read-only filesystem without Twstat.

---

## What About DotU (9P2000.u)?

The `DotU` flag you see everywhere adds:
- Numeric UIDs/GIDs in Stat
- Extension strings
- A few extra fields

**My recommendation:**
1. Keep the `DotU` flag in your Stat message
2. Ignore it for now (just set it to false)
3. Add proper .u support AFTER basic 9P works

Setting `DotU = false` tells clients: "I'm a basic 9P2000 server."

---

## The Simplified Message List

### Phase 1: Minimum Viable 9P (Start Here)

**Session (3 messages):**
1. Tversion / Rversion - Negotiate protocol
2. Tattach / Rattach - Authenticate and get root FID
3. Rerror - Error responses

**Navigation (2 messages):**
4. Twalk / Rwalk - Navigate directories
5. Tstat / Rstat - Get file info

**Reading (2 messages):**
6. Topen / Ropen - Open file
7. Tread / Rread - Read data

**Cleanup (1 message):**
8. Tclunk / Rclunk - Close file

**Total: 8 message types (4 pairs)**

With these 8, you can:
- Connect to server
- Navigate directories
- Read files
- Mount with Linux v9fs or Plan 9 client

**This is enough to be useful.**

---

### Phase 2: Add Writing (Later)

9. Tcreate / Rcreate - Create files
10. Twrite / Rwrite - Write data
11. Tremove / Rremove - Delete files

**Total: 11 message types**

Now you have a read-write filesystem.

---

### Phase 3: Add Advanced (Much Later)

12. Tflush / Rflush - Cancel operations
13. Twstat / Rwstat - Modify metadata
14. Tauth / Rauth - Explicit authentication

**Total: 14 message types (full 9P2000)**

---

## The Refactor Plan

### Step 1: Archive Complex Messages

```bash
cd /home/scott/Repo/NinePSharp
mkdir -p NinePSharp/messages/archived-linux-extensions

# Move all the .L messages
mv NinePSharp/messages/Treaddir.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rreaddir.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tfsync.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rfsync.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tgetattr.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rgetattr.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tsetattr.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rsetattr.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tstatfs.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rstatfs.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tlopen.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rlopen.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tlcreate.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rlcreate.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tmkdir.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rmkdir.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tmknod.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rmknod.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tsymlink.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rsymlink.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Treadlink.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rreadlink.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tlink.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rlink.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Trename.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rrename.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Trenameat.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rrenameat.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tunlinkat.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Runlinkat.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tgetlock.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rgetlock.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Tlock.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rlock.cs messages/archived-linux-extensions/
mv NinePSharp/messages/Rlerror.cs messages/archived-linux-extensions/

echo "Archived Linux-specific 9P2000.L messages" > messages/archived-linux-extensions/README.md
```

### Step 2: Update MessageTypes Enum

Find the MessageTypes enum and comment out or remove the .L message types.

### Step 3: Simplify INinePFileSystem Interface

Remove methods you don't need:
- ReaddirAsync (use ReadAsync on directories)
- GetAttrAsync (use StatAsync)
- SetAttrAsync (use WstatAsync or skip)
- All the .L-specific methods

### Step 4: Update Your Backends

Remove the .L method implementations from:
- MockFileSystem.cs
- SecretBackend.cs

---

## What You'll Have After Simplification

### Before:
- 67 message types
- 127 references to DotU/.L
- Complex protocol negotiation
- Impossible to understand

### After:
- 11 message types (8 for read-only, +3 for read-write)
- Simple protocol (well-documented)
- Easy to test
- Easy to understand

**You just reduced complexity by 80%.**

---

## How to Test with Real Clients

### Linux v9fs (works with basic 9P2000):
```bash
# Mount your server
sudo mount -t 9p -o version=9p2000,trans=tcp,port=564 localhost /mnt/test

# List files
ls /mnt/test

# Read a file
cat /mnt/test/somefile
```

### Plan 9 client (works with basic 9P2000):
```bash
# Mount the server
srv tcp!localhost!564 ninep
mount /srv/ninep /n/remote

# Access files
ls /n/remote
```

**You don't need 9P2000.L to be useful.**

---

## The Bottom Line

**Delete the .L stuff. You don't need it.**

Basic 9P2000 is:
- ✓ Well-documented
- ✓ Works with all clients
- ✓ Simple enough to learn in 2 weeks
- ✓ Sufficient for your use case

9P2000.L is:
- ✗ Poorly documented
- ✗ Linux-specific
- ✗ 3× more complex
- ✗ Not needed for Emercoin auth + secret storage

**Focus on getting 8-11 messages working perfectly. Then decide if you need more.**

---

## Next Steps

Want me to:
1. **Write the archive script** (move .L messages to archived/)
2. **Create a simple INinePFileSystem interface** (8 methods instead of 20+)
3. **Show you how to respond to version negotiation** (reject .L requests gracefully)

Which one?
