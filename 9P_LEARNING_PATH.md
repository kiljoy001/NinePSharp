# 9P Protocol Learning Path (Bit by Bit)

## Your Situation
You don't need to know the entire 9P protocol upfront. You can build and fix it **one message at a time**, learning as you go. Here's the incremental path:

---

## Phase 1: The "Read-Only" Messages (Start Here)
**Goal:** Get basic directory listing and file reading working perfectly.

### 1. Tversion / Rversion ⭐ START HERE
**What it does:** Negotiate protocol version and max message size.
**Complexity:** Easy (just exchange two numbers)

```csharp
// Client sends: "9P2000" or "9P2000.u", msize
// Server responds: Same version (or subset), actual msize
```

**Test it:**
```bash
# Use existing 9P clients to connect
9pfuse 'tcp!localhost!564' /tmp/testmount
```

**Your current bug:** Version/Attach race condition (we found this!)

**Learn by fixing:**
1. Read the 9P man page for Tversion: https://man.cat-v.org/plan_9/5/intro
2. Write a Coyote test that ensures Tversion completes before Tattach
3. Fix the fire-and-forget Task.Run() to ensure ordering

---

### 2. Tattach / Rattach
**What it does:** Authenticate and attach to the root of the filesystem.
**Complexity:** Easy

```csharp
// Client sends: FID to use, username, filesystem name
// Server responds: Qid of the root directory
```

**Your current bug:** Tattach can run before Tversion (race condition)

**Learn by fixing:** Same Coyote test as above.

---

### 3. Tstat / Rstat ⭐ FUNDAMENTAL
**What it does:** Get file metadata (like `stat` in Unix).
**Complexity:** Medium (need to understand Qid and Stat structure)

```csharp
// Client sends: FID
// Server responds: Stat struct (size, type, permissions, timestamps, etc.)
```

**Your implementation:** Already exists in MockFileSystem.cs:147

**Learn by doing:**
1. Read one file from your MockFileSystem using a 9P client
2. Add a `Console.WriteLine` in StatAsync to see what's being requested
3. Compare your Stat output to what Linux v9fs expects

---

### 4. Twalk / Rwalk ⭐ FUNDAMENTAL
**What it does:** Navigate the directory tree (like `cd` + `ls`).
**Complexity:** Medium (handles ".." and multi-component paths)

```csharp
// Client sends: Starting FID, new FID, array of path components ["foo", "bar"]
// Server responds: Array of Qids for each component successfully walked
```

**Your current bugs:**
- Sticky delegation (once you walk into a backend, you're stuck)
- No FID cloning (Twalk should create a new FID)

**Learn by fixing:**
1. Read MockFileSystem.cs:59 (WalkAsync)
2. Understand the difference between "clone walk" (newfid != fid) and "regular walk"
3. Fix RootFileSystem to allow switching backends

---

### 5. Topen / Ropen
**What it does:** Open a file for reading/writing.
**Complexity:** Easy

```csharp
// Client sends: FID, mode (O_RDONLY, O_WRONLY, O_RDWR)
// Server responds: Qid, iounit (max read/write size)
```

**Your implementation:** MockFileSystem.cs:111

---

### 6. Tread / Rread
**What it does:** Read data from an open file.
**Complexity:** Easy

```csharp
// Client sends: FID, offset, count (how many bytes to read)
// Server responds: Data (up to count bytes)
```

**Your implementation:** MockFileSystem.cs:135

---

### 7. Tclunk / Rclunk
**What it does:** Close a FID (like `close()` in Unix).
**Complexity:** Easy

```csharp
// Client sends: FID to close
// Server responds: Success
```

**Your current bug:** Clunking doesn't reset sticky delegation in RootFileSystem

**Learn by fixing:**
1. Add a test: Open backend1, clunk, then open backend2
2. Verify backend2 works (it currently won't)
3. Fix RootFileSystem to reset _delegatedFs on clunk

---

### 8. Treaddir / Rreaddir (9P2000.L only)
**What it does:** List directory contents (modern replacement for read on directories).
**Complexity:** Medium (pagination, offset handling)

```csharp
// Client sends: FID, offset (for pagination), count (max bytes)
// Server responds: Array of directory entries
```

**Your current bug:** Offset > 0 returns empty (we found this!)

**Learn by fixing:**
1. Read RootFileSystem.cs:279 (ReaddirAsync)
2. Understand how offset works (it's NOT an array index, it's a cookie)
3. Implement proper pagination

---

## Phase 2: The "Write" Messages (After Phase 1 Works)

### 9. Twrite / Rwrite
**What it does:** Write data to an open file.
**Complexity:** Medium (need to handle append, truncate, etc.)

### 10. Tcreate / Rcreate
**What it does:** Create a new file or directory.
**Complexity:** Medium

### 11. Tremove / Rremove
**What it does:** Delete a file or directory.
**Complexity:** Easy

---

## Phase 3: The "Advanced" Messages (Optional)

### 12. Twstat / Rwstat
**What it does:** Modify file metadata (chmod, chown, etc.).
**Complexity:** Hard (lots of edge cases)

### 13. Tflush / Rflush
**What it does:** Cancel an in-flight request.
**Complexity:** Hard (requires async cancellation)

### 14. 9P2000.u Extensions
**What they do:** Unix-specific features (symlinks, permissions, etc.).
**Complexity:** Hard

---

## The Incremental Fix Strategy

### Week 1: Fix Session Isolation
**What to learn:** How 9P sessions work (one FID map per connection).

**Steps:**
1. Read about FIDs: https://man.cat-v.org/plan_9/5/intro
2. Understand: FID 0 is always root, FIDs are session-local
3. Refactor: Move FID map from Singleton to per-connection SessionContext
4. Test: Run two connections simultaneously, verify FID isolation

**You don't need to understand all of 9P for this.** Just understand: "Each TCP connection gets its own FID namespace."

---

### Week 2: Fix Tversion/Tattach Ordering
**What to learn:** 9P handshake sequence.

**Steps:**
1. Read: https://man.cat-v.org/plan_9/5/version
2. Understand: Tversion MUST complete before any other message
3. Write Coyote test: Ensure version-then-attach ordering
4. Fix: Replace fire-and-forget with proper async sequencing

---

### Week 3: Fix Twalk/Tclunk in RootFileSystem
**What to learn:** How delegation should work.

**Steps:**
1. Read your own RootFileSystem.cs (80 lines, readable)
2. Understand: _delegatedFs should reset on clunk
3. Add test: Walk into backend1, clunk, walk into backend2
4. Fix: Clear _delegatedFs on ClunkAsync

---

### Week 4: Fix Readdir Pagination
**What to learn:** How directory cookies work in 9P.

**Steps:**
1. Read: https://man.cat-v.org/plan_9_4th_ed/5/read
2. Understand: offset is an opaque server cookie, not an array index
3. Fix: Implement stateful pagination (store cursor in FID state)
4. Test: List a directory with 100 entries, verify all are visible

---

### Week 5: Fix Security Issues
**What to learn:** Nothing 9P-specific, just fix the crypto.

**Steps:**
1. Change `byte[8]` to `byte[32]` for master seed
2. Remove hex string conversions from Elligator
3. Implement SafeHandle for SecureBuffer (prevent double-free)

---

## Learning Resources (In Order of Usefulness)

### 1. The Manual Pages ⭐ BEST RESOURCE
Read these in order:
- https://man.cat-v.org/plan_9/5/intro (Overview)
- https://man.cat-v.org/plan_9/5/attach (Attach/Auth)
- https://man.cat-v.org/plan_9/5/walk (Walk)
- https://man.cat-v.org/plan_9/5/open (Open/Create)
- https://man.cat-v.org/plan_9/5/read (Read/Write)

Each page is ~1-2 screens of text. Read one per day.

### 2. The Linux 9P Client Source
**Why useful:** See how a real client uses the protocol.
- https://github.com/torvalds/linux/blob/master/net/9p/client.c

Search for `p9_client_version()`, `p9_client_attach()`, etc.

### 3. Your Own MockFileSystem.cs
**Why useful:** You already have a working implementation!

Read it line-by-line. It's only 200 lines. Every method corresponds to one 9P message.

### 4. The 9P Protocol Spec (If You Want Details)
- http://doc.cat-v.org/plan_9/4th_edition/papers/9p

Warning: This is dense. Only read this if you need to understand wire format.

---

## The Key Insight

**You don't need to learn all of 9P to fix your project.**

You need to learn:
1. **Sessions** (for fixing the Singleton bug)
2. **FIDs** (for understanding walk/clunk)
3. **Message ordering** (for fixing the race condition)
4. **Directory enumeration** (for fixing readdir)

That's ~20% of the protocol, but it fixes 80% of your bugs.

The rest (write, create, remove, wstat) you can learn **later**, after your read-only server works perfectly.

---

## Next Steps

**Pick ONE message to focus on this week.** I recommend:

### Option A: Start with Tversion (Fix the race)
**Why:** It's the simplest message, and fixing it forces you to fix the architecture.

### Option B: Start with Treaddir (Fix pagination)
**Why:** It's a visible bug, easy to test, and doesn't require architecture changes.

### Option C: Start with FID isolation (Fix the Singleton)
**Why:** It's the biggest architectural flaw, and everything else depends on it.

Which one do you want to tackle first? I'll walk you through it step by step.
