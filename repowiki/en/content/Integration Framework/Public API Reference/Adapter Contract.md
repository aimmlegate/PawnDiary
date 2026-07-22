# Adapter Contract

This is the human-readable contract for other RimWorld mods. The supported surface is only `PawnDiary.Integration`; all other assembly types are implementation details.

## Compatibility

| Rule | Contract |
|---|---|
| Version | `ApiVersion` starts at 1 and is currently 8; additions are additive. Use `>=` checks for version-gated members. |
| Failure | API calls return documented safe values and log diagnostics; `SubmitEvent` never throws into the caller. |
| Availability | Check `IsReady` and `IsExternalApiEnabled`; a disabled master switch suppresses reads, submissions, providers, and listeners. |
| Threading | Gameplay reads/writes are main-thread only. Queue work from worker threads and drain it from a game component/UI hook. |
| Eligibility | The subject must be a diary-eligible pawn. An invalid partner downgrades a pair request to solo. |
| Budget | Token-spending calls share Pawn Diary’s transient rolling external prompt budget and lane limits. Back off after `DroppedBudget`. |
| Ownership | Writing-style and psychotype overrides are source-owned; reset only your own source. |
| Data safety | Snapshots are copied DTOs. Never log `GetApiSetup().lanes[*].apiKey`. |

## Event key conventions

`eventKey` is stored with diary events like a DefName. Use a stable lowercase `snake_case` key prefixed by the adapter package, for example `youradapter_campfire_story`. Never rename or repurpose a shipped key; add a new key when the meaning changes.

## External event schema

| Input | Required | Notes |
|---|---:|---|
| `sourceId` | no | Use the package ID for attribution; blank becomes `unknown-source`. |
| `eventKey` | yes | Must be claimed by an External group for ordinary `SubmitEvent`. |
| `subject` | yes | Diary owner and first POV. |
| `partner` | no | Distinct eligible pawn creates the second POV. |
| `summaryText` | no | Factual event evidence, localized by the adapter, cleaned and capped by Pawn Diary. |
| `eventLabel` | no | UI label; group label is the fallback. |
| `extraContext` | no | Short factual `key=value` lines; protected/reserved keys are ignored. |
| `promptFragment` | no | Protected event-specific evidence inside the normal prompt wrapper. |
| `promptEnchantmentCandidates` | no | Optional candidate lines for the same planner used by live context. |
| `forceRecord` | no | Bypasses budget/dedup only; never bypasses readiness, toggle, group, or eligibility. |
| `dedupKey`, `dedupTicks` | no | Optional collapse window for related adapter submissions. |

## Lifecycle and handles

Use `SubmitEventWithHandle` when the adapter must track generation. Keep returned handles only when `recorded` is true; poll `GetEntryStatus`/`GetEntrySnapshot`, or register an entry-status listener for push-style updates. A pruned or invalid handle is not an error in the adapter—it is a terminal absence.

## Context providers and overrides

- `RegisterPawnContextProvider` contributes one compact `key=value` line to Pawn Diary’s structured pawn summary. Providers must be cheap, main-thread-safe, and must not throw; a throwing provider is disabled for the session.
- Writing-style and psychotype overrides are source-owned and cleaned/capped. Register once or when source data changes, not every tick.
- `SetCaptureCapabilityReady` / `IsCaptureCapabilityReady` are the v8 thread-safe exception for capability registration during background mod construction. XML can suppress a fallback only while the capability is ready.

## One-shot LLM completion

`RequestLlmCompletion` is a token-spending, main-thread, loaded-game operation with bounded input/output, normal lane concurrency, cooldown, and budget admission. Poll with `GetLlmCompletionResult`; cancel obsolete work with `CancelLlmCompletion`. Treat errors as redacted status data, not transport internals.

## XML-only compatibility

If an adapter can describe an event with an interaction-group matcher, prefer XML. A string matcher for a DLC defName is safe without that DLC; do not reference DLC defs directly without `MayRequire`. Keep C# integration code optional and no-DLC-safe.

## Related source

- [External API Quickstart](External%20API%20Quickstart.md)
- [`Source/Integration/`](../../../../../Source/Integration/)
- [`integrations/`](../../../../../integrations/)
