---
name: pawndiary-engineering
description: PawnDiary repository workflow for Claude Code, Codex, and OpenCode. Use when implementing or changing behavior in this repo.
---

# PawnDiary Engineering Skill

## When to use
- Any feature, fix, refactor, or settings/behavior change in this repository.
- Any change that touches RimWorld runtime code, prompts, defs, or agent instructions.

## Core process
1. Understand scope first
   - Read relevant files before editing.
   - Keep changes surgical and tied to the requested outcome.
   - Identify the layer you are touching before coding: event capture, adapter, pure pipeline,
     transport, persistence, UI, XML Def, or tests.
   - Check the **RimWorld gotchas map** (below) for that layer before writing — it points at the
     local `docs/` lore that has already bitten this mod.

2. Respect runtime constraints
   - Target RimWorld Unity Mono runtime.
   - Do not add unsupported JSON/runtime dependencies; use existing `MiniJson.cs`.

3. Preserve architecture boundaries
   - New behavior should follow the established flow whenever possible:
     game event/listener -> plain payload/context snapshot -> pure selection/planning/parsing/
     formatting -> impure transport/persistence/UI adapter.
   - Keep impure code at the edges. RimWorld/Verse/Unity objects, `DefDatabase`, `.Translate()`,
     settings reads, IO, networking, randomness, and saved-game mutation belong in adapters,
     components, transport, Def accessors, or UI code.
   - Pure logic belongs in plain C# helpers/contracts, usually under `Source/Pipeline` or a nearby
     pure helper. Pure code accepts primitive/DTO inputs and returns primitive/DTO outputs.
   - If a feature needs new cross-layer data, extend an explicit typed contract instead of passing
     live `Pawn`, `Def`, GUI, HTTP, settings, or save-model objects through pure functions.
   - If a function can be pure, make it pure. If it cannot, isolate the messy state collection and
     call a pure helper with a snapshot.

4. Prefer XML-owned policy and constants
   - Prompt text, template fields, event classifiers, thresholds, odds/cooldowns, UI visualization
     values, text decorations, tags, colors, labels/instructions, and other tunable policy should
     live in XML Defs under `1.6/Defs/` with code fallbacks where runtime safety requires them.
   - Hardcoded C# values are acceptable for stable schema tokens, save keys, role/status constants,
     parser sentinels, defensive caps, and fallback defaults that keep the mod running if XML is
     absent. Treat every other new constant as suspect until you have considered XML.
   - New player-facing UI strings and prompt prose must remain localization-friendly: Keyed strings
     or DefInjected text, following `AGENTS.md` and the localization rules above.

5. Implement with minimum surface area
   - Prefer existing patterns/files over new abstractions.
   - Avoid unrelated cleanup.

6. Add or update pure tests
   - Any new or changed pure function should have focused tests in a standalone console harness under
     `tests/`, or an existing pure test project should be extended.
   - Pure test projects must compile without RimWorld/Verse/Unity assemblies. If they need those
     assemblies, split the code again until the testable core is plain C#.
   - Cover selection/matching/order/edge cases for XML-driven policy and parser/formatter changes.

7. Validate
   - Build command:
     - `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`
   - Run the relevant pure test projects after pure logic changes:
     - `dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj`
     - `dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj`
     - `dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj`
   - Validate touched XML with an XML parser when adding or editing Def files.
   - If `MSBuild` is unavailable in the environment, run the closest available build command and report the environment limitation.

8. Never launch the live game
   - Agents must not start `RimWorldWin64.exe` or Steam, use `-quicktest`, create or rename
     `Autostart.rws`, or otherwise run loaded-game/live-game validation.
   - Do not change the user's saves, `Prefs.xml`, RimTest configuration, or Steam launch options for
     testing.
   - Run builds and standalone automated checks only. When a change needs in-game verification,
     give the user exact manual test steps and the expected evidence, then let the user run the game
     and report the results.

9. Preserve reusable agent knowledge
   - Do not update root documentation or event maps as routine bookkeeping.
   - Update a committed skill only when the change introduces important reusable knowledge for
     future agents.
   - For notable changes, add one short compact dated line to `CHANGELOG.md`.

## RimWorld gotchas map (local `docs/`)
`docs/lore/*.md` (one trap + *why it's tricky* per heading) and `docs/cookbook/**` (Harmony recipe
reference) are **local-only** modding lore — gitignored, so this map lives here to route you to the
entries that bite *this* mod. Rule: before writing in a layer, open the file(s) listed for it; skim
the whole lore file when the work there is new. Keep any additions to `docs/` local — not committed.

- **Event capture / ingestion** — `Source/Ingestion`, `Source/Capture`, `Source/Patches` →
  `lore/harmony.md` + `lore/performance.md`. We patch base-game choke points (`PlayLog.Add`,
  `TaleRecorder.RecordTale`, `MentalStateHandler.TryStartMentalState`, `GameConditionManager.RegisterCondition`).
  A hot-path patch must early-return on a settings bool **then** type-narrow before any real work.
  Pin `Lib.Harmony` to brrainz's exact shipped `0Harmony` version — a floating `2.*` silently makes
  every Def fail to load. Also see `lore/world-incidents.md`: leader death / conditions fire once and
  vanilla regenerates synchronously, so hook the notify method, never poll.
- **Optional-mod hooks** — `Source/Patches/DiaryPatchRegistrar.cs`; RimTalk / SpeakUp, future RJW →
  `lore/harmony.md` + `lore/build.md`. Never `[HarmonyPatch(typeof(OtherMod.Type))]` — it throws
  `TypeLoadException` when the mod is absent. Resolve with `AccessTools.TypeByName` behind a
  `ModLister.HasActiveModWithName` guard, and cache the lookup behind a `_searched` bool (it is a full
  type scan). Referencing RimTalk's `net4.8` DLL from our `net472` build needs
  `ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch`.
- **Transport / LLM** — `Source/Generation/LlmClient.cs` → `lore/ui.md` + `lore/localization.md`.
  It runs on a **background thread**: `.Translate()` is main-thread-only (that is why prompt-schema
  words stay English), and `LongEventHandler.ExecuteWhenFinished` is *not* a safe background→main
  marshal — hand results back through a locked queue drained from a main-thread / OnGUI hook.
- **Pawn & context snapshots** — `Source/Generation/DiaryContextBuilder.cs`, `Source/Integration/*` →
  `lore/pawns.md`. Emit `pawn.LabelShort` / `Name.ToStringShort` (the **Nick**), never
  `NameTriple.First`, or the diary names colonists the player never sees. Trait labels like "nervous"
  are spectrum *degree* labels, not defNames. `PawnsFinder` uses British **Travelling** (two L's).
- **Persistence & ticking** — `Source/Core/DiaryGameComponent*.cs` → `lore/scribe-saving.md` +
  `lore/performance.md` + `lore/test-loop.md`. Reset every `static` cache (e.g. `DeathContextCache`)
  in `GameComponent.FinalizeInit` — statics leak across exit-to-menu + load. Schedule day-cadence work
  by **elapsed time**, not `TicksGame % N` (modulo dies on dev time-skip and save/load); add a bounded
  catch-up loop so a `DebugSetTicksGame` jump does not skip days. Name the static accessor `Instance`,
  never `Current` (it shadows `Verse.Current`).
- **UI — diary tab** — `Source/UI/ITab_Pawn_Diary*.cs` → `lore/ui.md`. `DoWindowContents` postfixes
  fire several times per frame — never mutate persistent state there. Size `Widgets.Label` rects to the
  font's true line height (Tiny 18 / Small 22 / Medium 28) and don't hardcode Tiny heights (the "tiny
  text" accessibility toggle changes them). `BeginScrollView` silently clips content past `viewRect.height`.
- **XML Defs** — `1.6/Defs/*`, `Source/Defs/*.cs` → `lore/defs.md` + `lore/patches.md`. A Def
  `Class` / `Type` field needs our **fully-qualified** type name (bare names resolve only for vanilla
  namespaces). No `{}` / `[]` in a `<label>`. Files under `Patches/` use root tag `<Patch>` (singular).
- **Dev / iteration** → `lore/test-loop.md` + `lore/debugging.md`. Fire events from the dev console
  instead of waiting on the storyteller; when triaging Player.log, grep our own source for the named
  defName first — most cross-reference errors belong to other loaded mods.

Cookbook: `docs/cookbook/harmony/*` is the technique reference (prefix/postfix, `__instance` /
`__result` / private-field injection, transpilers) behind the harmony lore. `docs/cookbook/ce-compat/*`
is Combat-Extended-only and does not apply here. **Skip entirely** (no custom art/audio/assets in a
text mod): `lore/animation.md`, `lore/textures.md`, `lore/sounds.md`, `lore/assets.md`.

## Red flags
- Turning routine implementation details into broad agent documentation.
- Adding dependencies incompatible with RimWorld Mono runtime.
- Making broad edits unrelated to the task.
- Letting RimWorld/Unity/Verse types leak into pure pipeline helpers or pure test projects.
- Adding hardcoded tunable constants instead of XML-backed policy/style values.
- Adding feature logic that cannot be tested without the game when the decision/formatting part could
  have been a pure function.

## Verification
- Build attempted and result reported.
- Relevant pure tests run and result reported, or a clear reason given when none apply.
- XML parser check run for touched XML Def files.
- A short changelog note added only when the change is notable.
- No unrelated files modified.
