# Existing C# 9P Libraries

## TL;DR: Yes, there's one. But it's old and basic.

---

## Sharp9P

**GitHub:** https://github.com/dave-tucker/Sharp9P
**NuGet:** `Sharp9P` (latest: 0.1.6.29 from 2016)
**Author:** Dave Tucker (worked at Docker)
**License:** Apache 2.0

### Status:
- ✓ Exists and is on NuGet
- ✓ Basic 9P2000 protocol implementation
- ⚠️ Last NuGet publish: 2016 (8+ years old)
- ⚠️ GitHub shows recent activity (Aug 2025?), but NuGet is stale
- ⚠️ Only 10 stars on GitHub
- ⚠️ Targets older .NET Framework (likely needs porting to .NET 8/10)

### What It Provides:
```bash
dotnet add package Sharp9P --version 0.1.6.29
```

**Likely includes:**
- 9P2000 message parsing/serialization
- Basic client implementation
- Maybe basic server framework

**Probably does NOT include:**
- 9P2000.L support (Linux extensions)
- Modern async/await patterns (written in 2016)
- Your specific use cases (Emercoin, LuxVault, etc.)

---

## The Big Question: Should You Use It?

### Pros:
1. **Solves protocol parsing** - Message serialization is tedious and error-prone
2. **Working reference** - You can see how someone else structured it
3. **Battle-tested** - Used by Docker for something (probably Docker for Windows file sharing)
4. **Saves time** - Don't reimplement message framing

### Cons:
1. **Abandoned?** - 8 years without NuGet update suggests low maintenance
2. **Old patterns** - Likely uses synchronous I/O, no modern C# features
3. **Missing features** - Probably doesn't have session management, backend abstraction, etc.
4. **Unknown quality** - Only 10 stars suggests limited adoption

---

## The Hybrid Approach (Recommended)

**Use Sharp9P for the protocol, build your stack on top:**

### Phase 1: Evaluate Sharp9P
```bash
# Clone and read the code
git clone https://github.com/dave-tucker/Sharp9P
cd Sharp9P

# Check what's there
ls -la
cat README.md
```

**Look for:**
- Message definitions (Tversion, Tattach, Twalk, etc.)
- Serialization code (wire format parsing)
- Client example
- Server example (if any)

### Phase 2: Decision Tree

**If Sharp9P has good message definitions:**
- ✓ Use their message structs (saves weeks of work)
- ✓ Use their serialization code
- ✗ Don't use their server architecture (if any)
- → Build your own Session/Server on top

**If Sharp9P is too basic/broken:**
- ✓ Use it as a reference (copy their message layout)
- ✓ Steal their constants (message types, flags, etc.)
- ✗ Don't use the library directly
- → Write your own simplified version

### Phase 3: Your Custom Stack

```
Your Architecture:
┌─────────────────────────────────────┐
│ Your Server (Session Management)    │ ← You write this
├─────────────────────────────────────┤
│ Sharp9P (Message Parsing)           │ ← Use this if good
├─────────────────────────────────────┤
│ Your Backends:                      │ ← You write this
│  - EmercoinAuth                     │
│  - SimpleSecureStorage              │
│  - MockFileSystem (testing)         │
└─────────────────────────────────────┘
```

**This gives you:**
- ✓ Working protocol layer (Sharp9P)
- ✓ Control over architecture (your Session management)
- ✓ Your unique features (Emercoin, deniability, etc.)

---

## What This Means For Your Project

### Good News:
**You don't have to implement message parsing from scratch.** That's the most tedious part.

### Reality Check:
**Sharp9P won't give you:**
- Session isolation (you need to build this)
- Backend abstraction (you need to build this)
- Emercoin integration (you need to build this)
- Modern async patterns (might need to wrap/update)

**It's a foundation, not a complete solution.**

---

## Action Plan

### Option A: Try Sharp9P First (Recommended)
1. Install Sharp9P from NuGet
2. Read their examples/tests
3. Try to build a simple echo server with it
4. If it works → Great! Use it for protocol layer
5. If it's broken → At least you learned their message structure

### Option B: Use As Reference Only
1. Clone the repo
2. Read their message definitions
3. Copy their approach (not their code)
4. Write your own simplified version
5. Focus on basic 9P2000 only

### Option C: Ignore It Completely
1. Your message definitions are probably fine
2. Just simplify what you have (remove .L messages)
3. Build the Session layer properly
4. Test with real 9P clients

---

## My Recommendation

**Do Option A for 1-2 hours:**

```bash
# Quick evaluation
dotnet new console -n Sharp9PTest
cd Sharp9PTest
dotnet add package Sharp9P

# Write a 20-line test
# See if it works with modern .NET
# Check if message parsing is sane
```

**If Sharp9P works:**
- Use it for message layer
- Build your Session/Backend layer on top
- Save weeks of debugging wire format issues

**If Sharp9P is broken/outdated:**
- Your current message definitions are probably better
- Just simplify (remove .L stuff)
- Focus on Session isolation and architecture

---

## The Bottom Line

**Yes, a C# 9P library exists. But it's not a magic bullet.**

You still need to:
- Design session management ✗ (library doesn't do this)
- Handle FID isolation ✗ (library doesn't do this)
- Build backends ✗ (library doesn't do this)
- Integrate Emercoin ✗ (library doesn't do this)

**What the library MIGHT give you:**
- Message parsing ✓ (saves weeks)
- Wire format handling ✓ (saves weeks)
- Protocol constants ✓ (saves hours)

**So it's worth 2 hours of evaluation to see if you can use it.**

---

## Next Steps

Want me to:
1. **Clone Sharp9P and analyze it** - I'll tell you if it's usable
2. **Write a test program** - Try to use Sharp9P with modern .NET
3. **Compare your messages to theirs** - See if yours are better
4. **Just focus on your architecture** - Ignore external libraries

Which one?
