# Hosting the Orleans 9P gateway

The gateway exposes Orleans-backed resources over TCP or mutually authenticated
TLS. A connection owns its fids; a virtual process group owns its mount table;
resource grains own their workload or data. Clients speak 9P. Inside the cluster,
the adapter uses typed Orleans grain calls, not a replacement Orleans wire transport.

The swarm-wide 9P transport with built-in AAN, LibTab compute jobs, workload providers and
the single-operator foundation with its v1 profiles build on this gateway and are specified
with Fog, in its own repository. The gateway implements none of them: it has no AAN and
releases connection-owned fids on disconnect as described below.

## Run the example

```sh
dotnet run --project NinePSharp.Examples -- --orleans 5640
```

This starts a localhost silo and a 9P listener at `127.0.0.1:5640`. Attach as
`guest` to `/`; `/hello` contains `Hello from an Orleans grain over 9P!`.
The example rejects writes and other users. It uses in-memory grain storage and
localhost clustering: it is a development example, not a durable deployment.
Orleans also uses its standard local silo/gateway ports, 11111 and 30000.

For example, using `NinePSharp.Client`:

```csharp
using var client = new NinePClient("127.0.0.1", 5640);
await client.VersionAsync(8192, "9P2000");
await client.AttachAsync(1, NinePConstants.NoFid, "guest", "/");
await client.WalkAsync(1, 2, new[] { "hello" });
await client.OpenAsync(2, NinePConstants.OREAD);
var reply = await client.ReadAsync(2, 0, 1024);
Console.Write(Encoding.UTF8.GetString(reply.Data.Span));
await client.ClunkAsync(2);
await client.ClunkAsync(1);
```

## Embed in a host

Reference `NinePSharp.Namespaces.Orleans.Server` and use an Orleans host with the
Orleans SDK enabled (the example uses `Microsoft.Orleans.Sdk` 10.3.1). Hosts defining
grain interfaces/implementations need code generation and the relevant grain
assemblies in their application manifest. Register these services after configuring
the silo, or after configuring an Orleans client connected to a separate cluster:

```csharp
services.AddNinePOrleans<MyAttachResolver>();
services.AddNinePResource<IMyResourceGrain>("world");
services.AddNinePOrleansListener(options =>
{
    options.Endpoint.Address = "127.0.0.1";
    options.Endpoint.Port = 5640;
    options.MaxConnections = 256;
});
```

`IMyResourceGrain` extends `IMountableResourceGrain`. A resource identity consists
of provider, device, and object path: provider `world` selects that registered
interface, device is the grain's string key, and path identifies an object inside
the grain. Unknown providers fail closed; provider names are case-sensitive and
duplicate registrations are rejected. Application registrations made before
`AddNinePOrleans` can override the resolver or data operations.

`ResourceIdentity.Path` is also the wire Qid path. Providers combined in one export
must allocate non-colliding, stable Qid paths across their devices (for example,
with assigned device/object ID ranges); the gateway does not remap local per-grain
object numbers. Increment Qid versions when content changes to support client caches.

`MyAttachResolver` implements `IDistributedNamespaceAttachResolver`. It must
authenticate/authorize each attach and return the permitted process-group ID,
process ID, authenticated user, and root handle. Do not trust `Tattach.Uname` as
authentication. Initialize the corresponding `IVProcessGroupGrain` and, if used,
`IVProcessGrain` before accepting clients; the example includes a bootstrap hosted
service registered before the listener. There is deliberately no default allow-all
attach policy. Authentication via `Tauth`/factotum is not implemented here.

For TLS, set `Endpoint.Protocol` to `tls` and configure
`ServerCertificatePath` / `ServerCertificatePassword` securely outside source
control. The existing transport validates client certificates and passes the
certificate to the attach policy. Applications can register `INinePTransportSecurity`
to supply another transport authentication policy. Plain TCP should stay on a
trusted network or loopback. Changing options after listener construction does not
reconfigure a bound endpoint.

## Lifetime and delivery semantics

Fids are isolated by connection. Clunk, disconnect, host shutdown, and successful
version renegotiation release session-owned open handles. Renegotiation creates a
new operation epoch so reused tags and fids cannot collide with earlier mutations.
Mount snapshots are fetched for namespace traversal; file reads and writes go
directly to the selected resource grain. Open resource identity survives grain
migration; fids themselves do not survive gateway failure or reconnect.

Mutating provider calls receive `ResourceOperationContext`, including a stable
operation ID. Providers must implement idempotency/replay validation and any
necessary durable storage themselves. For native wstat/fwstat, wrap
`FileStatOperations` in `DurableFileStatOperations` with an
`OrleansWStatRecoveryStore`. Its session-keyed journal is written before provider
dispatch and stores the selected resource or retained open handle. A lost reply
raises `WStatRecoveryPendingException`; `RecoverAsync` replays the exact request
and operation identity, allowing the provider to return its durable original
result without applying the mutation again. `ResourceWStatRejectedGrainException`
is reserved for definite no-effect rejection and becomes a terminal rejected
journal entry. Other Orleans failures remain pending. This protocol depends on
provider idempotency and does not make an arbitrary grain method exactly-once.

`Tflush` cancels the gateway wait and orders replies so no old
reply follows `Rflush`; it is not rollback or preemption of a running grain call.
Providers owning handles need bounded lifetimes/leases as well as idempotent clunk
to recover from client/gateway failure or interrupted open/create calls.

The hosted listener limits concurrent connections, not CPU work inside grains.
Gas limits, deadlines inside workloads, and resource quotas belong to the provider.
Workload execution providers are outside the current Plan 9 namespace milestone.

## Protocol boundary and verification

The gateway supports classic attach/walk/open/read/write/create/stat/clunk/remove
and flush, plus the maintained 9P2000.L subset (`lopen`, `lcreate`, `readdir`,
`getattr`). Unsupported operations return protocol errors. Negotiated `msize`
is bounded (default server maximum 1 MiB, minimum 256); read replies respect it.
This is not a claim of complete Linux filesystem or 9front authentication support.
See [namespace semantics and limits](plan9-namespace-semantics.md).

Version, walk, and flush behavior was checked against the local 9front source:
`sys/man/5/version`, `sys/man/5/walk`, `sys/man/5/flush`, and `sys/src/lib9p/srv.c`.
The tests use real TCP framing and a two-silo Orleans cluster, including resource
migration, disconnect cleanup, isolation, and the Linux protocol subset.

```sh
dotnet test NinePSharp.Namespaces.Orleans.Tests
bash scripts/run-quality.sh --full
```

The full gate retains the existing unit/property/BDD, coverage and CRAP gates.
It additionally mutation-tests the Orleans adapters, gateway, and connection
processor at the same 90% threshold, and runs a bounded SharpFuzz/AFL++ gateway
request-sequence campaign alongside parser/filesystem/namespace campaigns. The
gateway fuzzer uses deterministic grain doubles; distributed behavior is exercised
by the real-cluster tests. Fuzzing requires installed tools and fails on zero
executions, crashes, or hangs instead of silently skipping a requested campaign.
Mutation runs stop if the baseline test suite fails. As in the existing scopes,
string mutations are excluded; the transport scope also excludes diagnostic logging
calls, while protocol decisions, wire barriers, and buffer cleanup remain in scope.
