---
name: pawndiary-prompt-workflow
description: Maintain Pawn Diary event capture, prompt planning, XML-driven policy, and generation boundaries. Use when changing event signals, event groups, prompt templates/enchantments, generation planning, event windows, observed conditions, or their tests and documentation.
---

# Pawn Diary prompt workflow

Use this skill for changes that connect a RimWorld event to a diary prompt.

## Workflow

1. Locate the producer, signal/DTO, catalog decision, prompt planner, and XML policy before editing. Start with [Repository Map & Runtime Flow](../../repowiki/en/content/Core%20Architecture/Repository%20Map%20%26%20Runtime%20Flow.md) and [Event-to-Prompt Map](../../repowiki/en/content/Event%20System/Event-to-Prompt%20Map.md).
2. Keep the boundary explicit: read live RimWorld state at capture time, then pass plain typed data into pure classification/planning/parsing/formatting helpers. Keep pawns, DefDatabase, settings, Unity UI, Scribe, and HTTP at adapters.
3. Route new producers through `DiaryEvents.Submit`; do not write storage or queue generation directly.
4. Prefer XML matchers, groups, prompt Defs, windows, observed conditions, and tuning over new hardcoded policy. Preserve safe code fallbacks.
5. Add or extend a standalone pure test when the changed behavior can be tested without RimWorld.
6. Update the relevant human wiki page, concise root pointer, and `CHANGELOG.md` when structure or behavior changes. Build with `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug` after code changes.

## Policy invariants

- Ordinary event flow is capture -> submit -> recordable/dedup -> `DiaryEventCatalog.Decide` -> repository -> prompt plan -> LLM -> main-thread apply/archive/UI.
- Solo, pair, batch, death, arrival, and reflection events use different template keys; do not infer shape in the transport layer.
- Event windows and observed conditions reuse the normal event/generation path. Correlation observers must not emit duplicate events.
- External prompt fragments and context lines are untrusted input: keep them bounded, sanitized, and inside Pawn Diary’s prompt wrapper.
- DLC behavior must no-op cleanly without the DLC. Prefer string matchers in XML; live DLC reads belong behind the established guarded context accessors.
- Prompt text, thresholds, weights, caps, and style rules belong in XML or localization, not arbitrary C# constants.

## XML ownership

Use the smallest relevant Def file: `DiaryInteractionGroupDefs.xml` for event groups, `DiaryEventPromptDefs.xml` for source/classifier choices, `DiaryPromptTemplateDefs.xml` for structure, `DiaryPromptEnchantmentDefs.xml` for candidates, `DiaryEventWindowDefs.xml` and `DiaryObservedConditionDefs.xml` for live conditions, and `DiaryTuningDef.xml` for caps/weights/budgets.

## Completion checklist

- [ ] Signal is plain and submitted through `DiaryEvents.Submit`.
- [ ] Classification/dedup/batching behavior is covered by a pure test where practical.
- [ ] XML/localization policy is updated and no-DLC loading remains safe.
- [ ] Wiki map and root pointer remain accurate.
- [ ] `CHANGELOG.md` has a dated entry and the Debug build passes.
