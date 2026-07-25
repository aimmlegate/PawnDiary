# Tuning Definitions

Pawn Diary keeps defaults and feature policy in RimWorld Defs. XML is the shipped source of truth;
the C# declarations provide safe fallback values and typed accessors when a Def is missing or
partially authored.

For the exact current fields and instances, use the generated pages:

- [DiaryTuningDef](Generated%20Def%20Reference/DiaryTuningDef.md) — broad generation, pacing,
  retention, UI, and defensive-cap values.
- [DiaryKnowledgeTuningDef](Generated%20Def%20Reference/DiaryKnowledgeTuningDef.md) — bounded
  important-memory and culture-knowledge policy.
- [DiaryNarrativeContinuityDef](Generated%20Def%20Reference/DiaryNarrativeContinuityDef.md) —
  narrative selection and reflection continuity.
- [DiaryUiStyleDef](Generated%20Def%20Reference/DiaryUiStyleDef.md) — visual layout and color
  policy.
- [DiarySignalPolicyDef](Generated%20Def%20Reference/DiarySignalPolicyDef.md) — signal-specific
  capture thresholds and cooldowns.

The [Generated Def Reference](Generated%20Def%20Reference/Generated%20Def%20Reference.md) also lists
the singleton DLC and feature policy Defs.

## Ownership and runtime behavior

RimWorld loads the XML once during startup and registers typed objects in `DefDatabase<T>`.
Pawn Diary's Def accessors then expose the loaded values to capture, planning, prompt construction,
persistence, and UI adapters. Pure policies receive copied primitive values or DTOs rather than live
Defs.

The Advanced settings editor can overlay selected fields for the current settings profile. Those
overrides are runtime/user configuration; they do not rewrite the shipped XML defaults.

## Authoring rules

- Put thresholds, odds, weights, cooldowns, prompt policy, and UI style values in XML.
- Keep stable schema/save tokens and defensive caps in C#.
- Give code a safe fallback for a missing singleton Def.
- Use `GetNamedSilentFail` for optional lookups.
- Keep DLC-aware matchers as strings where possible. Direct DLC Def references need `MayRequire`.
- Localize Def prose with DefInjected rather than Keyed strings.
- Validate XML, then run the standalone tests that consume the changed policy.

## Regenerate the reference

```powershell
powershell -ExecutionPolicy Bypass -File tools/generate-xml-def-wiki.ps1
powershell -ExecutionPolicy Bypass -File tools/generate-xml-def-wiki.ps1 -Check
```

The generator inventories the XML actually present under `1.6/Defs`; it does not guess undocumented
ranges or infer behavior from field names.

[Back to XML Definition System](XML%20Definition%20System.md)
