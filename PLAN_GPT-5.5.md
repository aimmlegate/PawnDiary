# Compatibility Integration Plan

This plan covers future compatibility support for popular RimWorld mods such as Pawn Social Overhaul, RimWorld 1-2-3 Personalities, Psychology, RimPsyche, RimTalk, RimJobWorld, and other external mods.

## Goal

Pawn Diary should support other mods without making the main library depend on them. The safest shape is:

- XML compatibility packs when a target mod already emits vanilla-visible Defs or signals.
- Optional adapter mods when the target mod stores important state behind custom C# APIs, private fields, custom logs, or its own chat system.
- A small, stable Pawn Diary public API so adapter mods feed data into Pawn Diary without touching internal save models, generation queues, or pipeline classes.

Initial data flow should be one-directional into Pawn Diary. The API should also expose read-only context snapshots so external mods can use Pawn Diary history and pawn context later.

## Current Architecture Anchors

- `PawnDiary.Ingestion.DiaryEvents.Submit(...)` is the current internal event front door.
- `DiaryGameComponent.Dispatch(...)` owns guard, dedup, decision, and emit routing.
- `Source/Pipeline/DiaryPipelineContracts.cs` already defines primitive-only DTOs between impure RimWorld adapters and pure prompt planning.
- `DiaryInteractionGroupDef` already supports XML classification by domain, defName, package id, exact/prefix/suffix/segment/token matchers, and ordered first-match policy.
- `DiaryEventPromptDef` already supports compatibility XML adding prompt rows, with lookup order: exact source defName, matched group defName, classifier key, then broad domain.

The public compatibility API should terminate at this existing boundary. It should not expose `DiaryEvent`, `DiaryGameComponent`, `DiarySignal`, or current capture/spec internals.

## Compatibility Rule

Use XML-only support when the target mod can be represented through existing visible game signals:

- `InteractionDef` / social log rows
- `ThoughtDef`
- `HediffDef`
- `MentalStateDef`
- `TaleDef`
- incidents, quests, letters, spawned things, abilities, game conditions
- lasting map or pawn conditions observable by existing scanners

Use a C# adapter mod when:

- the target mod stores important state in custom components or private fields;
- the event does not reach a vanilla choke point Pawn Diary already patches;
- the event needs custom eligibility, dedup, or pairing logic;
- the target mod has its own LLM/chat flow and needs diary context;
- rendering vanilla text causes side effects and a safe fallback text path is needed.

## Public API Shape

Add a new stable namespace, for example:

```csharp
namespace PawnDiary.Api
```

Expose simple DTOs with public fields, compatible with RimWorld's Mono runtime and C# 7.3. Avoid external dependencies, JSON libraries, async callbacks, and mutable internal references.

Suggested facade:

```csharp
public static class PawnDiaryApi
{
    public static int ApiVersion { get; }
    public static PawnDiaryApiCapabilities Capabilities { get; }
}

public static class PawnDiaryEvents
{
    public static PawnDiaryRecordResult TryRecordSolo(PawnDiaryExternalSoloEvent request);
    public static PawnDiaryRecordResult TryRecordPair(PawnDiaryExternalPairEvent request);
}

public static class PawnDiaryContext
{
    public static bool TryGetPawnContext(Pawn pawn, out PawnDiaryPawnContextSnapshot snapshot);
    public static bool TryGetRecentEntries(Pawn pawn, int maxEntries, out List<PawnDiaryEntrySnapshot> entries);
}
```

Result codes should be explicit and non-throwing for normal rejection:

- `Queued`
- `NotPlaying`
- `InvalidRequest`
- `InvalidPawn`
- `NotEligible`
- `Disabled`
- `Duplicate`
- `Rejected`
- `ApiUnavailable`

## External Event DTO

Base fields for external event requests:

```csharp
public string sourcePackageId;
public string adapterPackageId;
public string eventKey;
public string label;
public string kind;
public string initiatorText;
public string recipientText;
public string neutralText;
public string externalContext;
public string dedupKey;
public int dedupTicks;
public bool important;
public string colorCue;
```

Solo request adds:

```csharp
public Pawn pawn;
public Pawn otherPawn;
```

Pair request adds:

```csharp
public Pawn initiator;
public Pawn recipient;
```

The API should sanitize text before saving it into `DiaryEvent.gameContext` or prompt-visible fields. Stable saved context markers should look like:

```text
external_event=true; external_mod=<packageId>; external_adapter=<packageId>; external_key=<eventKey>; external_context=<summary>
```

Do not save live object references, raw JSON blobs, or target-mod object string dumps.

## Internal Implementation

Add a new internal event route instead of letting adapter mods construct existing ingestion classes:

- `DiaryEventType.External`
- `GroupDomain.External`
- `ExternalEventData`
- `ExternalEventSpec`
- `ExternalDiarySignal`

`PawnDiary.Api.PawnDiaryEvents` should convert public requests into `ExternalDiarySignal` and then route through the existing dispatcher. That keeps guard, dedup, eligibility, event registration, prompt planning, and LLM queueing inside Pawn Diary.

Add XML defaults:

- broad `DiaryEventPrompt_External`;
- external catch-all `DiaryInteractionGroupDef`;
- optional groups for known compatible mods;
- optional prompt fields that read specific `GameContext` keys such as `external_context`.

## Read-Only Context API

For RimTalk and future integrations, expose read-only snapshots:

```csharp
public class PawnDiaryPawnContextSnapshot
{
    public string pawnId;
    public string pawnName;
    public string pawnSummary;
    public string surroundings;
    public string continuity;
    public string lastOpener;
    public string previousEntryEnding;
}

public class PawnDiaryEntrySnapshot
{
    public string eventId;
    public int tick;
    public string date;
    public string title;
    public string text;
    public string sourceDomain;
    public string sourceKey;
}
```

These snapshots must be copies. External mods should never receive mutable `DiaryEvent` or repository references.

## Adapter Mod Structure

Each non-XML target should get its own optional mod:

```text
PawnDiary.Compat.Psychology
PawnDiary.Compat.RimPsyche
PawnDiary.Compat.RimTalk
PawnDiary.Compat.RJW
```

Each adapter mod should:

- depend on Pawn Diary and the target mod;
- load after both;
- own its own Harmony patches;
- guard target methods with reflection or target-version checks;
- call only `PawnDiary.Api`;
- no-op if the API version/capabilities are missing;
- never mutate Pawn Diary internals directly.

Adapters can reference `PawnDiary.dll` directly if they declare Pawn Diary as a dependency. Reflection is useful only for soft-dependency helper libraries.

## XML Compatibility Packs

An XML-only compat pack should usually contain:

- `DiaryInteractionGroupDef` rows for new event classification;
- `DiaryEventPromptDef` rows for source-specific prompt guidance;
- optional `DiaryEventWindowDef` rows for one-shot or bounded story windows;
- optional `DiaryObservedConditionDef` rows for lasting states;
- DefInjected translations for all XML prompt text;
- Keyed strings only when code-owned strings are introduced.

Prefer exact `matchDefNames`, `matchPrefixes`, `matchSuffixes`, and `matchSegments`. Use broad `matchTokens` only when false positives are acceptable. Use `matchPackageIds` when a whole mod's Def family should be grouped together.

## Target Mod Triage

### Pawn Social Overhaul

Start with XML. If it emits normal `InteractionDef` social log rows, support can likely be `DiaryInteractionGroupDef` plus `DiaryEventPromptDef`.

If rendering social text triggers side effects or schedules follow-up dialogue, use `captureRenderedGameText=false` for affected groups or add a small adapter that submits safe fallback text.

### RimWorld 1-2-3 Personalities

Likely mixed support.

Use XML for any thoughts, traits, interactions, hediffs, or mental states. Use API context extensions only if personality state should appear in prompts and cannot be represented through vanilla traits or thoughts.

### Psychology

Likely needs an adapter for custom psychology/personality state, plus XML for thoughts and social interactions.

Adapter should submit concise context such as personality axes, known quirks, or relationship modifiers. It should not send large raw psychology dumps.

### RimPsyche

Likely similar to Psychology. Start by identifying whether it uses hediffs/thoughts/traits that XML can classify. Use adapter support for custom psyche state and major psyche events.

### RimTalk

This is the strongest candidate for two-way compatibility.

Inbound to Pawn Diary:

- notable generated chats can become external pair events;
- chat summaries can become solo or pair diary moments;
- adapter should dedup aggressively to avoid every chat line becoming a diary page.

Outbound from Pawn Diary:

- RimTalk can call `PawnDiaryContext.TryGetPawnContext`;
- RimTalk can read recent entries for memory/context;
- expose summaries, not raw prompt internals or LLM endpoint data.

### RimJobWorld

Keep this in a separate opt-in adapter mod, not in the main Pawn Diary mod.

Technical policy:

- no bundled dependency from main Pawn Diary;
- adapter must be explicitly installed by the player;
- prompts should be non-graphic by default;
- hard-exclude unsafe pawn categories;
- avoid storing explicit details in general diary context;
- provide a setting to disable generation while still allowing neutral event omission or sanitized summaries.

The adapter should call the same public API as every other compat mod. The main library should not become adult-content aware.

## Safety And Compatibility Requirements

- Missing target mods must no-op, never crash startup.
- Missing target methods should warn once or silently no-op depending on severity.
- Never `DefDatabase<T>.GetNamed(...)` for optional mod content; use string matching or `GetNamedSilentFail`.
- Do not add target mods to main `About.xml` dependencies.
- Do not reference external mod assemblies from the main Pawn Diary DLL.
- Keep DLC and mod content as string matches unless guarded.
- Avoid background-thread translation; `.Translate()` remains main-thread only.
- Keep player-facing UI/prompt strings localizable.

## Testing Plan

Pure tests:

- external event decision accepts valid solo/pair requests;
- invalid/missing pawn requests reject cleanly;
- dedup keys block repeats;
- external domain recovery works from saved context markers;
- prompt key resolution tries exact event key, external group, classifier key, and broad `External`;
- `GameContext` prompt fields can read `external_context`;
- sanitizer strips semicolons/newlines/control characters where needed.

Runtime smoke tests:

- no target mods installed: Pawn Diary loads cleanly;
- XML compat pack installed without target mod: loads cleanly and matches nothing;
- adapter installed without target mod should be impossible via dependencies, but if forced it no-ops;
- target mod installed with adapter: event records, queues generation, appears in diary tab;
- save/load after external event preserves display and prompt recovery;
- disabling the group prevents new records.

Build after each C# change:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Run relevant pure tests after pure logic changes.

## Phased Implementation

### Phase 1: Public API Skeleton

- Add `Source/Api/` namespace and DTOs.
- Add API version/capabilities.
- Add non-throwing result codes.
- Add read-only context snapshot types.
- Document API stability rules.

### Phase 2: External Ingestion

- Add `DiaryEventType.External`.
- Add `GroupDomain.External`.
- Add `ExternalEventData`, `ExternalEventSpec`, and `ExternalDiarySignal`.
- Route public API calls through the existing dispatcher.
- Add XML broad defaults.
- Add tests.

### Phase 3: XML Compat Authoring Guide

- Document XML-only support patterns.
- Add example XML for a social interaction mod, a hediff/thought mod, and an event-window-only mod.
- Explain localization requirements and DefInjected files.

### Phase 4: First Pilot Adapter

Recommended pilot: RimTalk or one psychology/personality mod.

RimTalk tests both directions:

- external chat-to-diary event submission;
- read-only diary context export.

Psychology/RimPsyche tests custom pawn context without needing LLM chat integration.

### Phase 5: Additional Compat Packs

After the pilot stabilizes:

- Pawn Social Overhaul XML pack;
- RimWorld 1-2-3 Personalities XML/adapter pack;
- Psychology adapter pack;
- RimPsyche adapter pack;
- RimTalk adapter pack;
- RimJobWorld opt-in adapter pack with strict safety and sanitization.

## Done Bar

The compatibility API is ready when:

- a simple external adapter can record a solo event without internal references;
- a simple external adapter can record a pair event without internal references;
- an external mod can read a pawn context snapshot;
- XML can classify external events and tune prompts;
- missing target mods do not affect main Pawn Diary;
- save/load keeps external entries stable;
- docs explain when to use XML vs adapter code.
