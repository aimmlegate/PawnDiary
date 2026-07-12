# Implementation plan — Vanilla Ideology Expanded: Memes and Structures compat (pure XML, core mod)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #4 (jointly
> with Alpha Memes) in [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Pure-XML pack in
> the core mod: **no C#, no adapter, no API change.** Sibling plan:
> `ALPHA_MEMES_COMPAT_PLAN.md` — read its "How the Ritual domain matches" section first; the
> same precept-vs-pattern validation applies here and only needs doing once.

## Target mod facts (verified from source 2026-07-12 — re-verify against installed 1.6 Defs)

- **Vanilla Ideology Expanded – Memes and Structures**, packageId `VanillaExpanded.VMemesE`,
  Workshop 2636329500, source `github.com/Vanilla-Expanded/VanillaIdeologyExpanded-Memes`.
  1.4/1.5/1.6; maintained (v4.07, 2026-03-19). Requires Ideology.
- 13 RitualPatternDefs (`VME_` prefix): `VME_BonfireRitual`, `VME_CeremonialSuicideRitual`,
  `VME_DivineStarsRitual`, `VME_IncantationPattern`, `VME_InsectoidHymnRitual`,
  `VME_LeaderConversionPattern`, `VME_LeadershipChallengeRitual`, `VME_OrgyRitual`,
  `VME_PlagueFestivalRitual`, `VME_SlaveEmancipationPattern`, `VME_TradingFairRitual`,
  `VME_ViolentConversionRitual`, `VME_WickerManBurningRitual`. **Audit the installed build for
  the matching ritual PreceptDef names** — the Ritual domain matches precept defNames (see the
  Alpha Memes plan); expect them to share the `VME_` prefix.
- 49 ThoughtDefs — mostly ritual-outcome tiers (`VME_GloriousWickerManBurning`,
  `VME_ExhilaratingOrgy` / `VME_AwkwardOrgy`, `VME_SpectacularPlagueFestival`,
  `VME_HonorableCeremonialSuicide`, `VME_UnshacklingSlaveEmancipation`, …) plus
  `VME_AttendedParty`, `VME_GotSomeLovin`, `VME_NeedAnonymity`.
- InteractionDefs on a **second prefix**: `VFEA_InterrogatePrisoner`,
  `VFEA_InterrogationSuccess`, `VFEA_InterrogationRefused`, `VFEA_Intimidate`.
- Also: 46 MemeDefs (no event surface), 1 IncidentDef (`VME_JunkChunkDrop` — junk, skip),
  fleshcrafted-part HediffDefs (`VME_Fleshcrafted*`), `VME_MedicalEmergencyHediff`.
  No TaleDefs / MentalStateDefs / GameConditionDefs.
- The other six VIE modules (Dryads, Icons and Symbols, Hats and Rags, Relics and Artifacts,
  Anima Theme, Sophian Style) were audited in `MOD_SUPPORT_RESEARCH.md` and add **nothing
  capturable** — this plan deliberately covers only Memes and Structures.

## Ground truth to read first

Same list as `ALPHA_MEMES_COMPAT_PLAN.md` (RitualSignal.cs; core group orders; VSIE compat
idiom). Additionally read the core `slavery` Interaction group (order 40) and `trial` group
(order 60) — the interrogation groups below sit in the same tonal family and must not blur
into them.

## Deliverables (core mod)

```
1.6/Defs/Compat/DiaryCompat_VIEMemes.xml       all groups below
Languages/Russian (Русский)/DefInjected/…      RU for the new groups
```

All groups gated `enableWhenPackageIdsLoaded: VanillaExpanded.VMemesE`.

## Groups

| Group | Domain | Order | Matchers | Shape / instruction intent |
|---|---|---|---|---|
| `viememes_darkrites` | Ritual | 764 (before the Alpha Memes groups at 766–767 — orders across compat files must be re-checked together at build time) | `matchDefNames`: precepts for `VME_CeremonialSuicideRitual`, `VME_WickerManBurningRitual`, `VME_PlagueFestivalRitual`, `VME_ViolentConversionRitual` (installed names) | The heavy rites get their own voice, separate from festivals: awe, dread, complicity. `important=true`, dark `colorCue` (reuse an existing cue key — check `colorCue` values used by core anomaly groups; never invent a new key without checking the UI style map). |
| `viememes_rituals` | Ritual | 765 | `matchPrefixes: VME_` | Everything else: bonfires, star-gazing, hymns, trading fairs, leadership challenges, emancipations, orgies. Instruction: a communal rite — celebration, politics, or release; the pawn's role and what the colony looked like mid-ceremony. Variant pools (3 instructions / 2 tones) are mandatory here — this group spans very different moods, so write variants that flex rather than one flavor. |
| `viememes_thoughts` | Thought | 487 | `matchPrefixes: VME_` | Theming only, no batch; global mood-offset policy filters. Instruction: the after-image of a rite or festival — glory, awkwardness, shame, or relief settling in. `VME_NeedAnonymity` and other ambient situationals will rarely clear the threshold; if one proves noisy, intercept it with a lower-order disabled group (the `MOD_COMPAT_PLAN.md` interceptor pattern) rather than narrowing the prefix. |
| `viememes_interrogation` | Interaction | 22 (verify free) | `matchDefNames: VFEA_InterrogatePrisoner, VFEA_InterrogationSuccess, VFEA_InterrogationRefused, VFEA_Intimidate` | Not batched — each interrogation beat is notable. Instruction: pressure applied across a table — what the pawn did to another person, or held out against; power, guilt, or cold procedure. Distinct from core `trial`/`slavery` wording (read them first). **Verify prisoner-recipient capture works** (non-colonist recipient — same blocking check as `HOSPITALITY_COMPAT_PLAN.md`; prisoners may already be diary-eligible, colonist-interrogators definitely are). |

Deliberately skipped (record in the file header): `VME_JunkChunkDrop` (junk drop, no story),
MemeDefs (static config), `VME_Fleshcrafted*` hediffs (the core
`hediffPartGainedAnomalous`/`hediffPartGainedArtificial` groups already classify gained parts —
**verify** one fleshcrafted part in-game classifies acceptably before deciding it needs its own
group), `VME_MedicalEmergencyHediff` (mechanics).

## Wording note

Two registers in one mod: festival warmth (bonfire, trading fair, divine stars) and genuine
darkness (wicker man, ceremonial suicide, plague festival). That is exactly why the dark rites
are split into their own group instead of stretching one instruction across both. Keep
`viememes_rituals` warm-to-neutral and let `viememes_darkrites` carry the weight.

## Localization

English inline; Russian DefInjected for all four groups, core RU register per `GLOSSARY.md`
(match the core ritual group's RU terminology; «обряд»/«ритуал» consistent with existing
usage — check first).

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. With VIE Memes + Ideology: dev-complete a bonfire ritual → `viememes_rituals`; a wicker-man
   burning → `viememes_darkrites`; confirm neither falls to core `ritualFinished`.
2. One outcome thought (e.g. `VME_GloriousWickerManBurning`) writes via `viememes_thoughts`.
3. Interrogate a prisoner → colonist entry via `viememes_interrogation` (this is also the
   non-colonist-recipient capture check).
4. Fleshcrafted part installed → classifies to an existing core hediff group acceptably
   (decision point: own group or not).
5. Without VIE Memes: inert, no warnings.
6. With Alpha Memes AND VIE Memes together (common combo): both packs classify to their own
   groups; no order collisions (766/767 vs 764/765), no cross-claiming.

## Risks / open questions

- Same precept-vs-pattern matching assumption as Alpha Memes — validate once, apply to both.
- `VFEA_` prefix implies these interactions may originate from a shared VE-framework module;
  confirm the defs live in **this** package (matchers are exact-name so behavior is right
  either way, but the `enableWhenPackageIdsLoaded` gate must name whichever package actually
  ships them — check the installed def's `modContentPack`).
- Orgy/lovin' content: instructions must stay suggestive-not-explicit, consistent with how the
  core handles vanilla lovin' thoughts (read the core `thoughtPositive` handling of lovin'
  before writing).
