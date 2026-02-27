# NinePSharp: Emercoin-Focused Development Plan

## Your Actual Use Case

You're building a **9P server with Emercoin NVS** (Name-Value Storage) as a distributed certificate authority. This is actually a really interesting architecture:
- Emercoin blockchain = distributed auth via NVS lookups
- 9P = universal filesystem interface
- LuxVault/Elligator = deniable storage layer

This is **way more focused** than "support every cloud/blockchain/protocol."

---

## Active Backends (Keep These 3)

### 1. MockFileSystem ⭐ Your Learning Tool
**Path:** `NinePSharp.Server/backends/MockFileSystem.cs`
**Purpose:** Learn and test 9P protocol here first
**Status:** Working, use as reference

### 2. SecretBackend (LuxVault Integration)
**Path:** `NinePSharp.Server/backends/SecretBackend.cs`
**Purpose:** Your core "Zero-Exposure" storage with Elligator
**Status:** Has hex string leaks, needs fixing
**What it does:** Uses LuxVault to store secrets in RAM-locked memory with Elligator obfuscation

### 3. Emercoin Integration (Already Exists!)
**Components:**
- `NinePSharp.Server/utils/EmercoinClient.cs` - JSON-RPC client for Emercoin daemon
- `NinePSharp.Server/utils/EmercoinAuthService.cs` - Certificate verification via NVS

**How it works:**
```
Client connects with TLS certificate
  ↓
Server extracts certificate thumbprint/serial
  ↓
Looks up "ssl:<thumbprint>" in Emercoin NVS
  ↓
If found → Authorized ✓
If not found → Rejected ✗
```

**Example Emercoin NVS record:**
```bash
# On Emercoin blockchain:
name_new ssl:a1b2c3d4e5f6...  # Certificate thumbprint
value: {"authorized":true,"user":"alice","permissions":["read","write"]}
```

**Status:** Already implemented, just needs the session isolation fixes

---

## Archive Everything Else

Move to `/archived-backends/`:
- ✗ All cloud backends (AWS, Azure, GCP) - You said you don't use them
- ✗ All other blockchain backends (Ethereum, Bitcoin, Solana, etc.) - Not needed for Emercoin focus
- ✗ All protocol backends (REST, SOAP, gRPC, MQTT, WebSocket) - Not your use case
- ✗ Compute grid backend - Too ambitious for now
- ✗ Database backend - Not needed
- ✗ PowerShell backend - Not needed

**Your real architecture is:**
```
9P Client (v9fs, Plan 9, etc.)
    ↓
NinePSharp Server (session-isolated)
    ↓
┌─────────────┬──────────────┬────────────────┐
│MockFileSystem│SecretBackend │Emercoin Auth  │
│(testing)     │(storage)     │(authorization)│
└─────────────┴──────────────┴────────────────┘
```

---

## The Critical Fixes (In Priority Order)

### 1. Fix Session Isolation (HIGHEST PRIORITY)
**Problem:** Singleton NinePFSDispatcher = all clients share FID maps
**Impact:** Client A's `ls` breaks Client B's `open`
**Fix:** Create `SessionContext` per TCP connection

**Why this matters for Emercoin:**
If you're using Emercoin auth, each client has their own certificate/identity. They MUST have isolated FID namespaces.

---

### 2. Fix Tversion/Tattach Ordering
**Problem:** Fire-and-forget Task.Run allows Tattach before Tversion
**Impact:** Session state corruption, wrong msize/DotU settings
**Fix:** Use Coyote to enforce message ordering

**Why this matters for Emercoin:**
Auth check happens during Tattach. If version negotiation isn't complete, client might bypass auth.

---

### 3. Fix Readdir Pagination
**Problem:** Only serves first page (offset=0), rest return empty
**Impact:** Can't list more than ~10-15 entries
**Fix:** Implement proper offset tracking

**Why this matters:**
If you have many authorized users in Emercoin NVS, you need pagination to list them all.

---

### 4. Fix Elligator Hex Leaks
**Problem:** Converting Elligator output to hex string defeats the purpose
**Impact:** "Deniable" keys are detectable in memory
**Fix:** Keep everything as `Span<byte>`, use constant-time comparison

**Why this matters:**
Your "Zero-Exposure" branding depends on this. If keys leak to managed heap, game over.

---

### 5. Fix 64-bit Master Entropy
**Problem:** Only 8 bytes of seed (crackable in 21 days with 100 GPUs)
**Impact:** All LuxVault keys derivable from brute-force
**Fix:** Change `byte[8]` to `byte[32]`

**Why this matters:**
If you're storing secrets in SecretBackend, weak entropy undermines everything.

---

## The Development Workflow

### Phase 1: Clean Up (This Week)
1. Archive unused backends (move to `/archived-backends/`)
2. Remove them from solution file
3. Rebuild with only 3 backends active
4. Run tests - should be much faster

### Phase 2: Learn 9P (Next 2 Weeks)
1. Read MockFileSystem.cs line-by-line
2. Read the 9P man pages (one per day)
3. Test against a real 9P client (Linux v9fs or Plan 9)
4. Understand: Tversion, Tattach, Twalk, Tread, Treaddir

### Phase 3: Fix Core Issues (Next 4 Weeks)
Week 1: Session isolation (Singleton → SessionContext)
Week 2: Tversion/Tattach ordering (Coyote test)
Week 3: Readdir pagination
Week 4: Elligator leaks + entropy fix

### Phase 4: Test Emercoin Integration (Week 9)
1. Set up local Emercoin node
2. Add test certificates to NVS
3. Test auth flow end-to-end
4. Write integration tests

---

## Your Unique Value Proposition

Once fixed, this is actually **really interesting**:

**"A 9P filesystem server with distributed certificate authority via Emercoin blockchain and deniable storage via Elligator."**

That's:
- ✓ Decentralized auth (no central CA)
- ✓ Immutable audit log (blockchain)
- ✓ Universal interface (9P works everywhere)
- ✓ Deniable storage (Elligator)
- ✓ Zero-exposure memory (RAM-locked, wiped on close)

**Nobody else has this combination.** But you need to fix the foundation first.

---

## Next Steps

### Immediate (Today):
1. Archive the unused backends
2. Remove them from NinePSharp.sln
3. Rebuild - should be much faster

### This Week:
1. Read 9P_LEARNING_PATH.md
2. Pick ONE message to understand deeply (I recommend Tversion or Treaddir)
3. Write a Coyote or Stryker test for that message

### Want Help?

I can help you:
- **Option A:** Archive the backends (I'll write the script)
- **Option B:** Write a Coyote test for Tversion/Tattach ordering
- **Option C:** Fix the session isolation (refactor Singleton → SessionContext)
- **Option D:** Fix the Elligator hex leaks in SecretBackend

Which one do you want to tackle first?
