# Direct math-worker milestone

Fog can now submit an AngouriMath expression to another Linux machine through
9P files over pinned mutual TLS, disconnect, and retrieve the retained result
through a fresh connection. The demo provider is `angouri-2.4.0-demo1`.

The first physical two-machine run used this workstation as the client and Edward
as the worker. Submitting `x^2-4` and reconnecting returned the exact root rows
`-2` and `2`. The worker runs in a fresh process for each job; the retained files
belong to the long-lived worker host.

## Build and start

Requirements: Linux x64, .NET 10 SDK for building, cgroup v2 with user systemd
resource control, unprivileged user/mount/PID/network namespaces, bubblewrap
with `--size` support, and libseccomp.so.2. Published executables include .NET
10.0.9. The AngouriMath package and transitive dependencies are locked; the build
script writes a SHA-256 manifest for the actual deployed files.

```sh
bash scripts/build-fog-demo.sh
.artifacts/fog-demo/host/NinePSharp.Fog.Demo init .artifacts/fog-demo/credentials
```

`init` requires a new directory. It creates private, seven-day demo certificates
and enrollment data. Copy `host/`, `math/`, `credentials/worker.pfx`,
`credentials/client.cer`, and `credentials/client.boot` to a private directory on
the worker. Keep `client.pfx` on the submitting machine. Preserve the manifest
alongside the deployment. These are explicit development credentials, not
automatic production enrollment.

On the worker, from its deployment directory:

```sh
./host/NinePSharp.Fog.Demo serve ./credentials 19568 ./math /usr/bin/bwrap
```

The listener binds to loopback. Forward it from the client machine:

```sh
ssh -N -L 127.0.0.1:19569:127.0.0.1:19568 scott@edward.rentonsoftworks.coin
```

In another client terminal:

```sh
.artifacts/fog-demo/host/NinePSharp.Fog.Demo submit .artifacts/fog-demo/credentials 127.0.0.1 19569 'x^2-4'
# Copy the printed job ID. The submitting process exits after start is acknowledged.
.artifacts/fog-demo/host/NinePSharp.Fog.Demo result .artifacts/fog-demo/credentials 127.0.0.1 19569 JOB_ID
.artifacts/fog-demo/host/NinePSharp.Fog.Demo start .artifacts/fog-demo/credentials 127.0.0.1 19569 JOB_ID
.artifacts/fog-demo/host/NinePSharp.Fog.Demo result .artifacts/fog-demo/credentials 127.0.0.1 19569 JOB_ID
```

Replaying `start` uses the same job. It does not execute again. `status`, `cancel`
and `release` use the same final argument. Release after saving the result.
The client prints and flushes the ID before uploading or starting, so an ambiguous
start failure can be inspected using that ID. A failed staging upload requires
re-upload before start; the demo CLI does not automatically recover uploads.

The submit command defaults to `solve` in `x`. Other examples:

```sh
.artifacts/fog-demo/host/NinePSharp.Fog.Demo submit .artifacts/fog-demo/credentials 127.0.0.1 19569 '1/2+1/3' evaluate
.artifacts/fog-demo/host/NinePSharp.Fog.Demo submit .artifacts/fog-demo/credentials 127.0.0.1 19569 '1+2*x+3*x^2' differentiate x
.artifacts/fog-demo/host/NinePSharp.Fog.Demo submit .artifacts/fog-demo/credentials 127.0.0.1 19569 'x+x' simplify
```

## Files and limits

`/compute/clone` allocates on open. A job has `spec`, `input`, `ctl`, `status`
and `result`. One upload writer is allowed at a time. Upload offsets must be
contiguous and a successful clunk seals the replacement; disconnect discards an
unsealed replacement. `start\n`, `cancel\n` and `release\n` are whole writes.
Status and result reads are immutable snapshots per open. Job ownership includes
the enrolled node identity, boot and policy epoch.

The demo host retains at most four jobs, including staging and completed jobs.
Staging reservations and terminal results expire after ten minutes; restarting the
host loses all jobs. Job IDs include a random host boot identifier. There is no
restart recovery or automatic resubmission. Release frees the reservation and
invalidates old job fids. Job IDs are not listed in the directory.

The CLI requests 5 seconds of cumulative job CPU, a 15-second wall deadline,
256 MiB memory and 64 KiB output. The server accepts CPU budgets of 1–30000 ms,
wall deadlines of 100–60000 ms, memory of 128 MiB–1 GiB, and output of 256–65536
bytes. Input is limited to 1 MiB; spec to 8 KiB. Only the job's sealed input can
be the expression source. The closed schemas are defined in `FogMathJob.cs`;
the demo status fields are `job,state,error,result_bytes,p_cpu_ms_used`.

## Isolation and scope

The host launches each job through a transient systemd user service, which owns
the cgroup memory, swap, process-count, CPU-rate and wall-time limits. Bubblewrap
creates private namespaces, a read-only runtime bundle and `/usr`, private `/proc`
and `/dev`, and a 64 MiB temporary filesystem. The environment is cleared and
credentials, home directories, host sockets and host cgroups are absent.

Before reading source, the runner applies a thread-synchronized seccomp denylist
covering sockets, process execution, non-thread clones, namespace changes and
privileged kernel interfaces. AngouriMath receives a restricted arithmetic grammar
and its parsed AST is checked again. Numerical Newton solving is disabled; output
expressions retain exact arithmetic. Numerical evaluation is used only to classify
solver candidates as real or complex, never to print approximate replacements.

The host samples cgroup CPU every 5 ms while execution is active. Scheduling can
cause overshoot. Final CPU accounting is checked before publication, and success
also requires a reaped child and empty cgroup. Cleanup failure quarantines that
runner instance. Cancellation and deadlines discard candidate output.

This is an experimental direct worker, not conformance with the full
`fog-math-v1`/`linux-process-v1` proposals. It uses bounded private standard-I/O
messages rather than the proposed supervisor/runner/capability 9P descriptors;
it has a syscall denylist rather than the final allowlist, and exposes read-only
`/usr` rather than a minimized verified runtime root. It lacks independent
supervisor restart reconciliation, pidfd ownership, CLOCK_BOOTTIME deadlines,
capability brokers, scheduler/worker leases, WASM execution and durable results.
The proposed profile remains unchanged; this demo does not register under its name.

## Validation

```sh
FOG_MATH_BUNDLE="$PWD/.artifacts/fog-demo/math" dotnet test NinePSharp.Fog.Server.Tests/NinePSharp.Fog.Server.Tests.csproj
FOG_MATH_BUNDLE="$PWD/.artifacts/fog-demo/math" bash scripts/run-quality.sh
```

The native worker test is explicitly skipped when `FOG_MATH_BUNDLE` is absent.
With it set, it checks actual roots, CPU exhaustion, malformed source,
cancellation and reuse after failure. Unit tests cover engine vectors, input
restrictions, sealed uploads, cancellation, retention, ownership and start replay.
The TLS test submits, disconnects, reconnects and checks one execution and retained
output. Full mutation and AFL campaigns remain separate from the standard gate.
