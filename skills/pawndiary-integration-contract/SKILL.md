---
name: pawndiary-integration-contract
description: Work safely on Pawn Diary’s public integration API and adapter examples. Use when changing Source/Integration, PawnDiaryApi DTOs, external event submission, context providers, overrides, one-shot completions, event filters, capability registration, or integrations/.
---

# Pawn Diary integration contract

Treat `PawnDiary.Integration` as a versioned contract for other mods. Read the [External API Quickstart](../../repowiki/en/content/Integration%20Framework/Public%20API%20Reference/External%20API%20Quickstart.md) and [Adapter Contract](../../repowiki/en/content/Integration%20Framework/Public%20API%20Reference/Adapter%20Contract.md) before editing.

## Contract rules

- Keep public evolution additive. Never rename, remove, or repurpose a shipped member or `eventKey`; bump `ApiVersion` only for additions.
- Keep public DTOs plain, copied, and prompt-free. Do not expose live internals, raw provider responses, in-flight state, or mutable game collections.
- Main-thread gate gameplay reads/writes. Calls should return documented safe values rather than throw into an adapter. The v8 capability registry is the deliberate thread-safe exception.
- Respect `IsExternalApiEnabled` and `IsReady`; registration may remain accepted while the master switch is off so providers/listeners recover later.
- Enforce subject eligibility, group claiming, sanitization, caps, deduplication, and transient rolling budget at the API boundary.
- Keep prompt ownership in Pawn Diary: adapters supply facts/instructions, while Pawn Diary owns framing, safety, localization of its strings, model routing, parsing, persistence, and UI.
- Never log or forward API keys returned by `GetApiSetup`.

## Submission semantics

| Path | Contract |
|---|---|
| `SubmitEvent` | Facts for normal LLM-generated diary prose; ordinary requests require an External XML group claiming `eventKey`. |
| `SubmitEventWithHandle` | Same path with opaque lifecycle handles. |
| `SubmitPromptEntry` | Adapter-owned idea/instruction inside the normal prompt wrapper; External group optional. |
| `SubmitDirectEntry` | Adapter-owned final prose; no rewrite, except optional title generation. |
| `RequestLlmCompletion` | Separate one-shot, budgeted, bounded lane request; poll and cancel explicitly. |

## When adding a member

1. Define failure values for no game, off-thread, disabled integration, invalid request, ineligible pawn, and budget pressure.
2. Sanitize and cap all adapter-controlled strings before they enter save data or prompts.
3. Add pure tests for DTO mapping/outcome logic and RimWorld tests for lifecycle behavior where needed.
4. Update the adapter example and curated wiki tables; keep `INTEGRATIONS.md` as a short pointer only.
5. Update `CHANGELOG.md`, build, and verify the public assembly/API version.

## XML and no-DLC safety

Adapters should prefer XML event matchers when possible. Match optional DLC content by string in XML, do not directly reference missing DLC Defs, and keep all live DLC reads behind existing guards. The base mod must still load and operate with no paid DLC.
