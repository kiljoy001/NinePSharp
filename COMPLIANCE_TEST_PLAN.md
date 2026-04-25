# NinePSharp Compliance Test Plan

This document outlines the strategy for ensuring NinePSharp is fully compliant with the **strictly 9P2000** protocol as defined by Plan 9 and 9front.

## 1. Reference Sources (Source of Truth)

All compliance testing is measured against the following references in the `9front` repository:

- **Protocol Specification:** `sys/man/5/*` (Manual pages for version, auth, attach, walk, open, read, write, clunk, remove, stat, wstat, flush, error).
- **Message Structures:** `sys/include/fcall.h`. **Note: We target the base 9P2000 structures only.**
- **Kernel Semantics:** `sys/src/9/port/chan.c` (The `namec` implementation and channel lifecycle).
- **Directory Format:** `sys/include/libc.h` (The `Dir` structure and its encoding).

## 2. Test Layers

### 2.1 Protocol Conformance Suite (Unit & Integration)
- **Goal:** Verify each 9P2000 message type behaves exactly as documented.
- **Strictly No Extensions:** Any messages or fields related to 9P2000.u or 9P2000.L are out of scope and should be removed or ignored.
- **Key Tests:**
    - `Tversion`: msize negotiation, version string matching ("9P2000").
    - `Tattach`: root QID retrieval, aname validation.
    - `Twalk`: path element limits (MAXWELEM=16), partial walk failure semantics, clone vs walk.
    - `Topen/Tcreate`: mode validation, iounit calculation, permissions.
    - `Tread/Twrite`: offset/count validation, readdir encoding.
    - `Twstat`: metadata updates (mode, name, etc.) and permission checks.

### 2.2 Stateful Model-Based Testing
- **Goal:** Catch protocol state machine drifts.
- **Approach:** Use a randomized sequence generator to compare NinePSharp behavior against a simplified abstract model of a 9P server.

### 2.3 Path Walk Semantics (Property-Based)
- **Goal:** Ensure path traversal and FID evolution stay consistent across equivalent walk sequences.
- **Key Invariants:**
    - `walk(A/B/C)` must be equivalent to `walk(A) -> walk(B) -> walk(C)`.
    - `walk(..)` and `walk(.)` must behave consistently with the active backend path.
    - Partial walk failure must preserve the exact number of successfully resolved elements.

### 2.4 Race Condition & Concurrency Testing
- **Goal:** Ensure thread safety and protocol atomicity.
- **Tool:** Microsoft Coyote.

### 2.5 Real Client Interoperability
- **Goal:** End-to-end validation with actual 9P clients.
- **Target Clients:**
    - `9p` (Plan 9 / 9front user-space client).
    - `mount` (using `-o proto=9p2000`).

### 2.6 Fuzzing (Robustness)
- **Goal:** Detect crashes or memory corruption from malformed inputs.
- **Targets:**
    - Message parsing (F# Parser).
    - Path resolution and traversal state.

## 3. Compliance Matrix (9P2000 Core)

| Message | Status | Spec Reference | Key Compliance Rule |
| :--- | :--- | :--- | :--- |
| `Tversion` | 🟡 | `man 5 version` | Must negotiate msize; version must be "9P2000". |
| `Tauth` | ⚪ | `man 5 attach` | Must return `Rerror` if auth not required. |
| `Tattach` | 🟢 | `man 5 attach` | Must return root QID for the requested `aname`. |
| `Tflush` | ⚪ | `man 5 flush` | Must abort the operation associated with `oldtag`. |
| `Twalk` | 🟡 | `man 5 walk` | `newfid` only bound on success; handle `..` and `.`. |
| `Topen` | 🟡 | `man 5 open` | Establish I/O unit; check permissions. |
| `Tcreate` | 🟡 | `man 5 open` | Atomic creation in directory; permissions. |
| `Tread` | 🟡 | `man 5 read` | Handle directory entries; offset-based paging. |
| `Twrite` | 🟡 | `man 5 read` | Return actual count written. |
| `Tclunk` | 🟢 | `man 5 clunk` | Forget FID regardless of previous errors. |
| `Tremove` | 🟡 | `man 5 remove` | File removed and FID clunked; atomic. |
| `Tstat` | 🟡 | `man 5 stat` | Return machine-independent directory entry. |
| `Twstat` | 🟡 | `man 5 stat` | Permission checks; partial success is forbidden. |

*Legend: 🟢 Complete, 🟡 Partial/WIP, ⚪ Not Started*

## 4. Automation & CI

- **`run_all_tests.sh`**: Executes the full suite.
- **Coyote CI**: Runs stateful race checks.

## 5. Definition of Done (Compliance)

A feature is considered compliant when:
1. It passes all relevant Conformance Suite tests.
2. It passes Property-Based invariant checks.
3. It has been verified against a real 9front client.
4. It does not introduce any regressions in Coyote/Fuzzing runs.
