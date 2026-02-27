# Sharp9P Library Analysis

## Summary: Client-Only, But Very Useful

I cloned and analyzed Sharp9P. Here's what you need to know:

---

## What Sharp9P Is

### ✓ Clean Protocol Implementation
- All basic 9P2000 messages (Tversion, Tattach, Twalk, Topen, Tread, Twrite, etc.)
- Clean message serialization (ToBytes/FromBytes)
- Protocol helpers (ReadUInt, WriteString, etc.)
- Proper wire format handling

### ✓ Working Client Library
```csharp
var client = Client.FromStream(networkStream);
client.Version(Constants.DefaultMsize, Constants.DefaultVersion);
client.Attach(Constants.RootFid, Constants.NoFid, "user", "/");
client.Walk(fid, fid, new[] {"path", "to", "file"});
client.Read(fid, offset, count);
```

### ✗ NO Server Implementation
- No server framework
- No session management
- No backend abstraction
- Client-only library

---

## What You Can Use From Sharp9P

### 1. Message Definitions (VERY USEFUL)

**Example (Tversion.cs):**
```csharp
public sealed class Tversion : Message
{
    public Tversion(uint msize, string version)
    {
        Type = (byte) MessageType.Tversion;
        Msize = msize;
        Version = version;
        Length += Constants.Bit32Sz + Protocol.GetStringLength(version);
    }

    public Tversion(byte[] bytes) : base(bytes)
    {
        var offset = Constants.HeaderOffset;
        Msize = Protocol.ReadUInt(bytes, offset);
        offset += Constants.Bit32Sz;
        Version = Protocol.ReadString(bytes, offset);
        // ...
    }

    public override byte[] ToBytes()
    {
        var bytes = new byte[Length];
        // Serialize to wire format
        return bytes;
    }
}
```

**This is gold** - Clean message parsing/serialization that you can reference or use directly.

### 2. Protocol Helpers (VERY USEFUL)

```csharp
// Reading from wire format
uint value = Protocol.ReadUInt(bytes, offset);
string text = Protocol.ReadString(bytes, offset);
Qid qid = Protocol.ReadQid(bytes, offset);

// Writing to wire format
offset += Protocol.WriteUint(bytes, value, offset);
offset += Protocol.WriteString(bytes, text, offset);
```

**This saves weeks** of debugging byte-order issues and string encoding.

### 3. Constants (USEFUL)

```csharp
Constants.DefaultMsize = 8192
Constants.DefaultVersion = "9P2000"
Constants.RootFid = 0
Constants.NoFid = ~0u

// File modes
Constants.Dmread = 0x4
Constants.Dmwrite = 0x2
Constants.Dmexec = 0x1
Constants.Dmdir = 0x80000000

// Open modes
Constants.Oread = 0x0
Constants.Owrite = 0x1
Constants.Ordwr = 0x2
```

### 4. Client for Testing (VERY USEFUL)

You can use Sharp9P's **client** to test YOUR **server**:

```csharp
// Your server running on port 564
var tcpClient = new TcpClient("localhost", 564);
var client = Client.FromStream(tcpClient.GetStream());

// Test your server
client.Version(8192, "9P2000");
client.Attach(0, ~0u, "testuser", "/");
client.Walk(0, 1, new[] {"test", "path"});
// If this works, your server is correct!
```

---

## What You CANNOT Use

### ✗ Server Architecture
Sharp9P has no:
- Session management (you need to build this)
- FID tracking per connection (you need to build this)
- Backend abstraction (you need to build this)
- Async/await patterns (it's synchronous 2016 code)

### ✗ Modern Patterns
- Uses synchronous I/O (Stream.Read/Write, not async)
- No dependency injection
- No modern C# features (spans, async streams, etc.)

---

## The Hybrid Approach: Best of Both Worlds

### Option 1: Use Sharp9P Messages, Build Your Server

```
Your Architecture:
┌─────────────────────────────────────────┐
│ Your Server (async, session-per-conn)   │ ← You write
├─────────────────────────────────────────┤
│ Sharp9P.Protocol.Messages (serialization)│ ← Use directly
├─────────────────────────────────────────┤
│ Your Backends (Emercoin, secrets, etc.) │ ← You write
└─────────────────────────────────────────┘
```

**Advantages:**
- ✓ Battle-tested message parsing (saves weeks)
- ✓ You control architecture (session management)
- ✓ Use Sharp9P client to test your server
- ✓ Focus on your unique features (Emercoin)

**You write:**
```csharp
public class NinePServer
{
    private readonly ConcurrentDictionary<Socket, Session> _sessions = new();

    public async Task HandleConnectionAsync(Socket socket)
    {
        var session = new Session();
        _sessions[socket] = session;

        var protocol = new Sharp9P.Protocol.Protocol(new NetworkStream(socket));

        while (socket.Connected)
        {
            // Use Sharp9P to read message
            var message = protocol.Read();

            // Your logic to handle it
            var response = await session.HandleAsync(message);

            // Use Sharp9P to write response
            protocol.Write(response);
        }
    }
}
```

### Option 2: Reference Sharp9P, Write Your Own

**Use Sharp9P as a reference:**
- Copy their message structure
- Copy their serialization approach
- But write your own modern async version

**Advantages:**
- ✓ Modern async/await patterns
- ✓ Use Span<byte> instead of byte[]
- ✓ Full control over everything

**Disadvantages:**
- ✗ More work (but your messages are already done)
- ✗ More bugs to fix yourself

---

## My Recommendation

### Phase 1: Quick Validation (1 hour)

```bash
# Add Sharp9P to a test project
dotnet new console -n TestSharp9P
cd TestSharp9P
dotnet add package Sharp9P --version 0.1.6.29

# Write a tiny client that connects to your server
# See if Sharp9P messages work with your current implementation
```

### Phase 2: Decision

**If Sharp9P messages are compatible with your code:**
- ✓ Use Sharp9P for serialization (saves weeks)
- ✓ Keep your message definitions OR switch to theirs
- ✓ Build your Session/Server layer on top

**If incompatible or too old:**
- ✓ Keep your message definitions (they're probably fine)
- ✓ Just simplify (remove .L messages)
- ✓ Reference Sharp9P for tricky serialization

### Phase 3: Testing

**Use Sharp9P client to test YOUR server:**
```csharp
// This is HUGE - you get a working client for free
var client = Sharp9P.Client.FromStream(tcpStream);

// Test every message your server implements
client.Version(8192, "9P2000");
client.Attach(0, ~0u, "test", "/");
client.Walk(0, 1, new[] {"foo"});
client.Open(1, Constants.Oread);
var data = client.Read(1, 0, 1024);

// If this works, your server is spec-compliant!
```

---

## What This Changes For You

### Before:
"I have to implement 9P from scratch, I don't know if my messages are correct, I have no way to test."

### After:
"I can use Sharp9P messages for serialization, use Sharp9P client for testing, and focus on building my Session/Backend layer."

### Concrete Benefits:

1. **Message Parsing** - Sharp9P has this working (saves 2-3 weeks)
2. **Testing** - Sharp9P client can test your server (saves 1-2 weeks)
3. **Reference** - When confused, read Sharp9P code (saves countless hours)
4. **Validation** - If Sharp9P client works with your server, you're spec-compliant

---

## The Comparison: Sharp9P vs Your Code

### Message Count:
- **Sharp9P:** ~14 messages (basic 9P2000 only)
- **Your code:** 67 messages (9P2000 + .u + .L)

**Takeaway:** Sharp9P confirms you should focus on basic 9P2000.

### Architecture:
- **Sharp9P:** Client-only, synchronous
- **Your code:** Server with Singleton bugs, but more ambitious

**Takeaway:** You need to build the server yourself, but can use their messages.

### Code Quality:
- **Sharp9P:** Clean, simple, working
- **Your code:** Complex, buggy, but has features Sharp9P doesn't

**Takeaway:** Simplify to Sharp9P's level, THEN add your features.

---

## Action Items

### Immediate (Tonight):
1. Install Sharp9P: `dotnet add package Sharp9P`
2. Write 20-line test client
3. Try to connect to your server
4. See if it works

### This Week:
**If Sharp9P client works with your server:**
- Great! Your protocol layer is correct
- Focus on fixing Session isolation
- Use Sharp9P client for all testing

**If Sharp9P client FAILS:**
- Your message serialization might be wrong
- Compare your messages to Sharp9P's
- Fix differences
- Get Sharp9P client working

### This Month:
1. Build Session-per-connection (your architecture)
2. Use Sharp9P messages for protocol (their code)
3. Test with Sharp9P client (free testing)
4. Add Emercoin integration (your unique feature)

---

## Bottom Line

**Sharp9P is not a magic bullet, but it's VERY useful:**

- ✓ Gives you working message definitions
- ✓ Gives you a client for testing
- ✓ Proves basic 9P2000 is enough
- ✓ Shows clean architecture (simple is better)

**You still need to build:**
- Session management (per-connection state)
- Backend abstraction (Emercoin, secrets, etc.)
- Your unique features

**But you DON'T need to build:**
- Message parsing (use Sharp9P)
- Wire format serialization (use Sharp9P)
- Test client (use Sharp9P)

**This is a massive time saver.**

---

## Want Me To:

1. **Test Sharp9P against your server** - I can try to connect Sharp9P client to your current server and see what breaks

2. **Write integration guide** - Show exactly how to use Sharp9P messages in your server

3. **Compare messages** - Check if your messages match Sharp9P format

4. **Just simplify your code** - Remove complexity, focus on architecture

Which one?
