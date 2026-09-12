# NinePSharp.Namespaces.Orleans.Server

An embeddable 9P gateway for Orleans-backed namespaces and resource grains on .NET 10.

Configure an Orleans silo or connected client, then register:

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

`IMyResourceGrain` extends `IMountableResourceGrain`; provider `world` selects that
interface and each resource's device is its string grain key. Enable the Orleans
SDK in hosts defining grain interfaces or implementations. Initialize process-group
namespace state before accepting clients.

`MyAttachResolver` implements `IDistributedNamespaceAttachResolver` and authorizes
each attach, returning the permitted process group, process ID, user, and root.
There is no default anonymous policy. Keep TCP on loopback/trusted networks or
configure mutually authenticated TLS and an appropriate attach policy.

The gateway supports classic file operations and a maintained 9P2000.L subset.
Unsupported operations return errors. Fids are connection-local; disconnect and
clunk release handles. Mutation IDs support provider-owned replay validation.
Flush cancels the gateway wait, not an executing grain mutation; providers own
idempotency, storage, workload limits, and recovery of orphaned handles.

The repository includes a runnable read-only Orleans example and a full hosting
guide in `docs/orleans-integration.md`.
