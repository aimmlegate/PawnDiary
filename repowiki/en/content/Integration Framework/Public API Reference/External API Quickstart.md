# External API Quickstart

Pawn Diary exposes a supported adapter surface in the `PawnDiary.Integration` namespace. The facade is `PawnDiaryApi`; request and result objects are plain DTOs. Current contract: `PawnDiaryApi.ApiVersion == 8`.

## Minimal adapter

1. Reference `1.6/Assemblies/PawnDiary.dll` with `<Private>false</Private>`; do not bundle it.
2. Load after Pawn Diary.
3. Call from a main-thread gameplay hook and check readiness.

```csharp
using PawnDiary.Integration;

if (!PawnDiaryApi.IsReady || !PawnDiaryApi.IsExternalApiEnabled) return;

PawnDiaryApi.SubmitEvent(new ExternalEventRequest
{
    sourceId = "yourname.youradapter",
    eventKey = "youradapter_campfire_story",
    subject = pawn,
    partner = otherPawn,
    summaryText = "A campfire story became a confession.".Translate(),
});
```

For a complete buildable example, copy [`integrations/PawnDiary.ExampleAdapter/`](../../../../../integrations/PawnDiary.ExampleAdapter/) and start at `Source/PawnDiaryExampleApi.cs`.

## Claim the event key in XML

Ordinary `SubmitEvent` requests require an External interaction group. This keeps prompt policy in XML and makes an unclaimed adapter key harmless.

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>youradapterCampfireStory</defName>
  <label>campfire story</label>
  <domain>External</domain>
  <order>1000</order>
  <instruction>a campfire story shared tonight; write what it stirred up, not a transcript</instruction>
  <matchDefNames><li>youradapter_campfire_story</li></matchDefNames>
</PawnDiary.DiaryInteractionGroupDef>
```

## Choose a write path

| Need | Call | LLM? | External group? |
|---|---|---:|---:|
| Submit facts for normal Pawn Diary writing | `SubmitEvent` / `SubmitEventWithHandle` | yes | yes |
| Submit an idea/instruction for Pawn Diary to write | `SubmitPromptEntry` | yes | optional |
| Submit final prose owned by your mod | `SubmitDirectEntry` | no, except optional title generation | optional |
| Inspect a prompt without saving or spending tokens | `PreviewPrompt` | no | depends on request |

## Request and result shape

| Field | Meaning |
|---|---|
| `eventKey` | Stable lowercase save-data identifier; prefix it with your mod name and never rename it. |
| `subject` | Eligible pawn whose diary receives the entry. |
| `partner` | Optional eligible second pawn; creates a paired POV when valid. |
| `summaryText` | One localized factual line; Pawn Diary sanitizes and caps it. |
| `extraContext` | Bounded `key=value` facts; reserved keys are rejected. |
| `forceRecord` | Rare escape hatch for must-record adapter events; it does not bypass readiness or eligibility. |
| outcome/handle | Use `SubmitEventOutcome` or an opaque handle to distinguish drop reasons and poll status. |

## Read-only context

Snapshots such as `GetEntrySnapshot`, `GetContextSnapshot`, `GetPawnSummary`, `GetPromptEnchantments`, and `GetContextBundle` contain copied DTO data—not live pawns, prompts, raw provider responses, or in-flight pages. `GetApiSetup` is the deliberate security exception: it exposes configured API keys, so never log or forward them.

## Rules adapters must follow

- Main-thread by default; off-thread reads return safe empty values and submissions do not throw into the caller.
- Respect the player’s **Allow external mod integrations** switch.
- Treat `eventKey` as permanent save data and evolve the API additively using `ApiVersion` feature checks.
- Pawn Diary owns prompt framing, safety text, localization of its own strings, token budget, parsing, persistence, and UI.
- Localize adapter-owned `summaryText`, labels, and XML text in the adapter.

For the full adapter contract, see [Adapter Contract](Adapter%20Contract.md). For architecture, see [Repository Map & Runtime Flow](../../Core%20Architecture/Repository%20Map%20%26%20Runtime%20Flow.md).
