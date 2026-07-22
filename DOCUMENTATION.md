# Pawn Diary — Maintainer Guide

This file is the compact root index for maintainers. Human-facing architecture, event, and API explanations live in the [repository wiki](repowiki/README.md); implementation-specific rules live in the committed [agent skills](skills/).

## 1. Purpose

Pawn Diary is a RimWorld library DLL. RimWorld discovers its classes by reflection and calls lifecycle hooks, patches, tick work, save/load, and UI callbacks; there is no application `main()`.

## 2. Repository Map

| Area | Responsibility |
|---|---|
| `About/`, `LoadFolders.xml` | Mod metadata and load folders |
| `1.6/Defs/` | XML-driven groups, prompts, styles, windows, tuning, and policies |
| `Languages/` | Keyed and DefInjected localization |
| `Source/Capture/`, `Source/Ingestion/` | Plain event facts and signal submission |
| `Source/Core/` | Lifecycle, dispatch, storage, retention, queues |
| `Source/Generation/`, `Source/Pipeline/` | Context, prompt planning, LLM, parsing, and decoration |
| `Source/Integration/` | Public `PawnDiary.Integration` API |
| `Source/Patches/`, `Source/Settings/`, `Source/UI/` | Hooks, settings, and player UI |
| `tests/`, `integrations/` | Tests and buildable adapter example |

Expanded human map: [Repository Map & Runtime Flow](repowiki/en/content/Core%20Architecture/Repository%20Map%20%26%20Runtime%20Flow.md).

## 3. Runtime Flow

`startup → hook/scan/API → DiaryEvents.Submit → Dispatch → recordable/dedup → catalog decision → repository → prompt plan → LLM → main-thread apply/archive/UI`

Event windows and observed conditions use the same event path. New producers should submit plain data through `DiaryEvents.Submit`; they should not write storage or generation directly.

See the [Event-to-Prompt Map](repowiki/en/content/Event%20System/Event-to-Prompt%20Map.md) for templates, XML ownership, and schema tables.

## 4. Event Sources

Capture is impure; classification, planning, parsing, formatting, and policy helpers should be pure. Keep the live RimWorld boundary at the edge and pass typed DTOs inward.

### 4.1 Exact Ideology food evidence

Food belief enrichment follows the same barrier without owning a page. While Ideology is active,
`Thing.Ingested` opens a short-lived scope only when the direct food or a bounded
`CompIngredients` row has vanilla's exact `Humanlike` or `Insect` meat category. Capture freezes a
primitive `ingredientKind`/DefName/localized-label fact and the exact ThoughtDefs returned by that
same `FoodUtility.ThoughtsFromIngesting` call. `ThoughtSignal` then asks the pure XML-driven food
policy to enrich its already-authorized evidence before the scope closes. The existing thought page,
dedup key, RNG behavior, and event-time saved belief context remain the owners of the result.

XML maps `humanlike_meat` to `cannibal_meal` and `insect_meat` to `insect_meal`; localized semantic
aliases let the shared stance resolver compare those facts with live visible doctrine without a
precept-name catalog. A mixed modded meal keeps humanlike-meat precedence so insect support cannot
change the shipped humanlike result. Corpses, ordinary meals, unknown categories, ambiguous or
malformed XML, inactive Ideology, and adapter failures leave the ordinary page unchanged. Every
thought returned for the same exact meal intentionally may receive the same factual ingredient row;
the resolver still emits doctrine only when it finds one relevant live stance. Loaded active-Ideology
coverage is 397/398; all food cases pass, and the sole remaining failure is the separate Odyssey
fixture's parked-player-gravship prerequisite.

## 5. XML Policy

Put tunable prompt text, groups, thresholds, weights, caps, and style rules in `1.6/Defs/`. Use safe code fallbacks only for stable schema tokens, defensive limits, parser sentinels, and defaults.

## 6. Prompts And Writing Styles

Prompt framing and safety text belong to Pawn Diary. External adapters provide bounded facts or instructions; they do not replace the normal wrapper. Localize UI and prompt text according to [Localization](#12-localization).

## 7. Settings And UI

Settings and UI are outer adapters. Do not pass Unity/UI/settings objects into pure planners or parsers. Persist player settings through the existing settings/Scribe patterns.

## 8. API And Reliability

The supported public surface is `PawnDiary.Integration`. Keep it additive, main-thread-gated where required, no-throw to callers, eligibility-aware, bounded, and safe when integrations are disabled. Read the [External API Quickstart](repowiki/en/content/Integration%20Framework/Public%20API%20Reference/External%20API%20Quickstart.md) and [Adapter Contract](repowiki/en/content/Integration%20Framework/Public%20API%20Reference/Adapter%20Contract.md).

## 9. Save Data And Compatibility

Treat shipped event keys, DefNames, save tokens, and public API members as compatibility contracts. Add migration logic before changing serialized meaning. Transient queues and budgets must not be assumed to survive a save/load boundary unless explicitly serialized.

## 10. Runtime And DLC Constraints

The base game must work without paid DLC. Prefer string matchers in XML for optional content. Guard live DLC pawn reads and use the existing DLC-safe context boundary. Never require a DLC merely for one optional reaction.

## 11. Testing And Build

Keep pure logic covered by standalone tests and run the relevant RimWorld tests for lifecycle/API changes. Build the committed DLL after source changes:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

## 12. Localization

Never hardcode English that reaches the UI or LLM prompt. Add player-facing strings to `Languages/English/Keyed/PawnDiary.xml` and use `.Translate()` on the main thread. Localize Def text through `DefInjected`. Structured prompt schema labels and background-thread `LlmClient` diagnostics are the documented exceptions.

## 13. When Changing The Mod

1. Read the relevant wiki page and agent skill.
2. Make the smallest change at the correct architecture boundary.
3. Update affected docs and add a dated `CHANGELOG.md` entry.
4. Build and run the relevant pure/RimWorld tests.
5. Review the diff and stage only files belonging to the requested change.

Debug build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

## 14. Repository Wiki

- [Wiki home](repowiki/README.md)
- [Repository Map & Runtime Flow](repowiki/en/content/Core%20Architecture/Repository%20Map%20%26%20Runtime%20Flow.md)
- [Event-to-Prompt Map](repowiki/en/content/Event%20System/Event-to-Prompt%20Map.md)
- [External API Quickstart](repowiki/en/content/Integration%20Framework/Public%20API%20Reference/External%20API%20Quickstart.md)
- [Adapter Contract](repowiki/en/content/Integration%20Framework/Public%20API%20Reference/Adapter%20Contract.md)

Agent workflow files:

- [`skills/pawndiary-engineering/SKILL.md`](skills/pawndiary-engineering/SKILL.md) — local engineering workflow when present.
- [`skills/pawndiary-prompt-workflow/SKILL.md`](skills/pawndiary-prompt-workflow/SKILL.md) — event and generation changes.
- [`skills/pawndiary-integration-contract/SKILL.md`](skills/pawndiary-integration-contract/SKILL.md) — public API and adapter changes.
- [`skills/repo-wiki-maintenance/SKILL.md`](skills/repo-wiki-maintenance/SKILL.md) — wiki upkeep.
