# Documentation

Steam's own documentation is thin and, in several places, wrong or unfollowable. These
docs aim to be the opposite: explain the concept, show a worked example, and be explicit
about what we had to infer rather than read stated.

**Where we assume rather than know, we say so.** Valve leaves 30.9% of SDK methods with no
comment at all, and 91.7% of functions returning `const char*` document no pointer
lifetime. Silence in Valve's headers is not a reason for silence here — it is a reason to
state the assumption we are relying on, so it can be challenged.

---

## Guides

Task-oriented, written for someone who has not shipped a Steam game before.

| Guide | Covers |
|---|---|
| [Getting Started](guides/01-getting-started.md) | The IPC mental model, why `Init` throwing is normal, `steam_appid.txt` traps, reading init failures, the `asyncCallbacks` decision, shutdown, dedicated servers, which native binary to ship |

## Steam Datagram Relay (SDR)

Valve's relay network — routing game traffic through Valve's backbone for lower latency,
DDoS protection and IP privacy.

| Document | Covers |
|---|---|
| [Poll groups & hosted servers](sdr/poll-groups-and-hosted-servers.md) | Draining many connections in one call; running a dedicated server inside the relay network |
| [Ping locations & certificates](sdr/ping-locations-and-certificates.md) | Latency estimation without pinging, POP enumeration, running SDR without a Steam login |

## Audits

Evidence-backed analysis of the codebase. Every finding cites a header quote, a
measurement, or a reproduction.

Start with the [audit index](audit/README.md) — it ranks all open findings and summarises
what has already been fixed.

| # | Report |
|---|---|
| 00 | [Native conformance](audit/00-native-conformance.md) — P/Invoke entry points vs. the shipped binaries |
| 01 | [Marshaling & ABI](audit/01-marshaling-abi.md) — struct layout and marshalling vs. the C++ headers |
| 02 | [Dispatch, memory & threading](audit/02-dispatch-memory-threading.md) — the callback pump and lifetimes |
| 03 | [API coverage & missing features](audit/03-api-coverage-gaps.md) — what the SDK offers vs. what we expose |
| 04 | [GameServer](audit/04-gameserver.md) — the dedicated-server path |
| 05 | [Tests & documentation coverage](audit/05-tests-and-docs.md) — measured coverage and the offline test strategy |
| 06 | [Performance](audit/06-performance.md) — measured allocation and interop costs |

---

## Verifying the library without Steam

Steam does not need to be installed to check a great deal of this library's correctness.
**Run this locally before every commit** — it is the primary gate, and it runs all of the
checks below:

```bash
powershell -ExecutionPolicy Bypass -File verify.ps1
```

CI runs the same scripts, but treat CI as a backstop rather than the source of truth: if
`verify.ps1` passes on your machine the change is good, and if it fails, CI will not save
you.

The individual gates, if you want to run one on its own:

```bash
powershell -ExecutionPolicy Bypass -File verify-native-conformance.ps1
```

Asserts every `DllImport` entry point exists in all 17 committed native binaries, across
Windows x86/x64, Linux x86/x64 and macOS. Entry points are plain strings the compiler
never checks — this is what stops a symbol Valve removed from reaching a player's machine.

```bash
powershell -ExecutionPolicy Bypass -File verify-struct-layout.ps1
```

Pins the marshalled size, pack and field offsets of all 339 structs. Steam writes these
directly into memory we read back, so a mismatch silently returns wrong data instead of
throwing. Pass `-Record` to deliberately accept an intended layout change.
