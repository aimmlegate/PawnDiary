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
     or DefInjected text, following `AGENTS.md` and `DOCUMENTATION.md §12`.

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

8. Document as part of done
   - Update `DOCUMENTATION.md` whenever behavior/structure changes.
   - Add a dated entry to `CHANGELOG.md`.

## Red flags
- Skipping documentation updates after structural/behavior changes.
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
- `DOCUMENTATION.md` and `CHANGELOG.md` updated when required.
- No unrelated files modified.
