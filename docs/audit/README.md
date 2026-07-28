# Audit set — AnkleBreaker fork of Facepunch.Steamworks

A full-codebase audit run on 2026-07-28 against `master`. Six workstreams, each producing
an evidence-backed report. **Every finding is verified** — against the SDK headers in
`Generator/steam_sdk/`, against the export tables of the shipped native binaries, or by a
harness that reproduces the behaviour offline. Suspicions that did not survive
verification are recorded as such rather than quietly dropped.

**Steam was not installed and no Steam account was used.** That constraint shaped the
method: it forced everything onto static analysis and pure-managed harnesses, which turned
out to be a strength — those techniques are reproducible in CI, and they found real bugs.

---

## The reports

| # | Report | Scope |
|---|---|---|
| 00 | [Native conformance](00-native-conformance.md) | Every P/Invoke entry point vs. the shipped `steam_api` binaries, on all three platforms |
| 01 | [Marshaling & ABI](01-marshaling-abi.md) | Struct layout, packing, string and array marshaling vs. the C++ headers |
| 02 | [Dispatch, memory & threading](02-dispatch-memory-threading.md) | The callback pump, call-result lifetimes, allocation, and the real threading contract |
| 03 | [API coverage & missing features](03-api-coverage-gaps.md) | What the SDK offers vs. what the public API exposes, per interface |
| 04 | [GameServer / dedicated server](04-gameserver.md) | The fork's flagship area: server interface routing, auth, server browser |
| 05 | [Tests & documentation coverage](05-tests-and-docs.md) | Test suite state, offline test strategy, measured doc coverage |

---

## What was found, in one page

### Fixed during this pass

| Defect | Severity | Report |
|---|---|---|
| Solution did not build on a clean machine (`net46` refs + three projects sharing one `obj/`) | Blocker | — |
| `ISteamAppList` — 6 bindings to functions Valve deleted; threw `EntryPointNotFoundException` | High | 00 |
| 8 committed native binaries ~11 SDK releases stale, incl. **every Linux and macOS library** — missing `SteamInternal_GameServer_Init_V2`, so a Linux dedicated server could not initialise | **Critical** | 00 |
| `SteamUtils.CurrentBatteryPower` returned `0` for every charge level below 100% (integer division) | High | 05 |
| `Utility.ToType<T>` boxed every callback struct — 32–320 bytes per handler per delivery, forever | Medium | 02 |
| `Newtonsoft.Json 9.0.2-beta1` (GHSA-5crp-9r3c-p9vr) referenced but unused by both test projects | Medium | 05 |
| PS5 DualSense adaptive triggers unsupported — exported and declared, but the generator cannot emit its C `union` | Medium | 00 |

### Open, ranked by severity

1. **`SteamApps` is registered on dedicated servers but has no game-server accessor** —
   `Self` stays `IntPtr.Zero` and `SteamServer.AddInterface<T>` ignores the failure return
   (unlike `SteamClient`'s). Any `SteamApps.*` call on a pure dedicated server passes a
   NULL `this` to the flat API: an access violation, not a catchable exception. (04)
2. **Shutdown races the async callback pump** — `Dispatch.LoopClientAsync` is `async void`
   with no cancellation or join, so an in-flight frame can call into a destroyed pipe.
   Crash-on-exit for headless servers. (02)
3. **Pending call results are silently abandoned on shutdown** — proven: 5000 registered
   continuations, 0 invoked. Every awaiting `Task` hangs forever. (02)
4. **`Dispatch.runningFrame` is a non-volatile check-then-set** — 872,129 simultaneous
   entries measured in a 2M-iteration race. Concurrent `FreeLastCallback` is something
   Valve explicitly forbids. (02)
5. **Server auth tickets cannot be cancelled** — `AuthTicket.Cancel()` hard-codes
   `SteamUser.Internal`, which is never registered server-side, so `Dispose()` throws. (04)
6. **`SteamServer.Shutdown()` does not reset cached statics**, so a second `Init` silently
   skips `ModDir`/`GameDescription`/`MaxPlayers`. (04)
7. **Callback exceptions are swallowed** and permanently drop the remaining handlers for
   that callback. (02)
8. **`Achievement.GlobalUnlocked` always returns `-1`** — its prerequisite call is never
   made. (03)
9. **`SteamNetworkingSockets.CreateFakeUDPPort` returns an unusable handle** — the backing
   class is empty and its `Self` is permanently zero. (03)
10. **`GSStatsUnloaded_t` is unreachable** — a genuine upstream callback-ID collision at
    1108 that the generator resolves inconsistently with the identical one at 1112. (05)

### The headline numbers

- **1019** P/Invoke entry points, **100%** resolving against **all 17** committed native
  binaries across Windows x86/x64, Linux x86/x64 and macOS. Was 8 of 17 failing.
- **906 of 946** flat-API functions bound (95.8%). Of the 40 unbound, **38 are correctly
  omitted** (Valve-superseded legacy) and 2 were genuine gaps — one of which (DualSense)
  is now closed.
- **52.9%** of bound internal methods are reachable from the public API. **11 interfaces
  have zero public reachability.**
- **36.2%** of the public API has any XML documentation. **Zero `<example>` and zero
  `<remarks>` in the entire library.** `<param>` sits at 11.8% — and for a P/Invoke
  binding, the arguments are exactly where the danger is.
- **118 of 118 tests fail offline** in 227 ms, all on one `[AssemblyInitialize]`. Separately,
  **53 of 118 contain no assertions at all**.
- Valve's own headers leave **30.9%** of methods with no comment, and **91.7%** of
  `const char*` returns document no pointer lifetime — so our docs must state assumptions
  rather than cite Valve.

---

## Method notes

**Verification over assertion.** Findings cite a header quote, a measured number, or a
reproduction. Several plausible-sounding hypotheses were investigated and *disproved*;
those are recorded in each report's "verified-correct" section so the next audit does not
re-litigate them. Notable examples: there is not a single `GCHandle` in the assembly, so
the classic `ISteamMatchmakingServers` delegate-collection crash is structurally
impossible; `FreeLastCallback` genuinely is called on every path including exceptions; and
`CallbackMsg_t` is byte-exact against native.

**Offline testing is viable and valuable.** Six techniques were prototyped and run, not
merely proposed — export conformance, struct layout, callback IDs, enum conformance,
pure-managed logic, and reflection-based API invariants. Between them they found the dead
`ISteamAppList` bindings, the callback-ID collision, a missing `ToString()` override, and
contributed to the battery bug. None require Steam. Report 05 details the recommended
test-project layout.

**Already in CI.** `verify-native-conformance.ps1` runs the full 17-binary export sweep on
every push, so the two most severe defects found here cannot silently reappear.
