# Implementation plan — Alpha Memes compat (pure XML, core mod)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #4 (jointly
> with VIE Memes) in [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Pure-XML pack in the
> core mod: **no C#, no adapter, no API change.** Sibling plan: `VIE_MEMES_COMPAT_PLAN.md` —
> implement both against the same Ritual-domain reading of the code, and keep their group
> wording distinct (Alpha Memes leans macabre-ceremonial; VIE leans festival/political).

## Target mod facts (verified from source 2026-07-12 — re-verify against installed 1.6 Defs)

- **Alpha Memes**, packageId `Sarg.AlphaMemes`, Workshop 2661356814, source
  `github.com/juanosarg/AlphaMemes`. 1.5/1.6; requires Harmony + VE Framework + **Ideology**.
  Extremely active (v4.1201 committed 2026-07-12).
- Ritual PreceptDefs (what the Ritual domain actually matches — see below): `AM_SkyBurial`,
  `AM_PyramidBurial`, `AM_Mummification`, `AM_RumBurial`, `AM_InsectoidBurial`,
  `AM_BlastOffFuneral`, `AM_CremateFuneral` (+ NoCorpse variant), `AM_PastedFuneral`,
  `AM_OcularFuneral`, `AM_ScrapRitual`, `AM_TantrumRitual`, plus baptism/rodeo/sparring/
  maddening-chant/ocular-warping/relic-destruction precepts. All `AM_`-prefixed.
- ThoughtDefs (39): ritual-outcome quality tiers (`AM_TerribleRodeo`…`AM_UnforgettableRodeo`,
  `AM_*Baptism`, `AM_*Sparring`, `AM_RationalMaddeningChant`…`AM_InsaneMaddeningChant`,
  funeral outcomes, `AM_CorpseRumThought`, `AM_EntertainingRelicDestruction`) + monastic/
  barracks sleeping thoughts (ambient — see noise note).
- InteractionDefs: `AM_Speech_Baptism`. HediffDefs: `AM_CatharsisHediff`,
  `AM_IconoclastHediff`, `AM_ToxicBuildup`, `AM_Kamikaze`, dryad hediffs.
  AbilityDefs: `AM_Ocular*`, `AM_AnalyzeCreature`, `AM_ArchitectStone`, ….
  No TaleDefs / MentalStateDefs / GameConditionDefs.

## How the Ritual domain matches (read the code, then trust this summary)

`Source/Ingestion/Sources/RitualSignal.cs`: on `LordJob_Ritual.ApplyOutcome`, the classifier
key is built from **`Precept_Ritual.def.defName`** (the PreceptDef, *not* the
RitualPatternDef) plus a behavior-class token. Core groups `ritualRoyal`(770)…
`ritualFinished`(780, catch-all). So matchers below target **precept defNames**; the
`AM_` prefix covers them, but confirm by dev-triggering one Alpha Memes ritual and logging the
classifier key before finalizing.

## Ground truth to read first

1. `Source/Ingestion/Sources/RitualSignal.cs` (matching key, above).
2. `1.6/Defs/DiaryInteractionGroupDefs.xml` — Ritual groups 770–780; Thought groups 490–520;
   Interaction compat orders in use 11–19 (check every shipped compat file); Ability groups
   790+.
3. `integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml` — group XML idiom, and the
   Thought-domain "theming only, no batch" rule.

## Deliverables (core mod)

```
1.6/Defs/Compat/DiaryCompat_AlphaMemes.xml     all groups below
Languages/Russian (Русский)/DefInjected/…      RU for the new groups
```

All groups gated `enableWhenPackageIdsLoaded: Sarg.AlphaMemes`.

## Groups

| Group | Domain | Order | Matchers | Shape / instruction intent |
|---|---|---|---|---|
| `alphamemes_funeral` | Ritual | 766 (before core `ritualRoyal` 770) | `matchDefNames`: the funeral precepts (`AM_SkyBurial`, `AM_PyramidBurial`, `AM_Mummification`, `AM_RumBurial`, `AM_InsectoidBurial`, `AM_BlastOffFuneral`, `AM_CremateFuneral`, `AM_CremateFuneralNoCorpse`, `AM_PastedFuneral`, `AM_OcularFuneral`, `AM_FleshCraftingFuneral` — audit the installed list; enumerate exactly, do not prefix-match) | The centerpiece. Instruction: a themed farewell — grief filtered through the ideology's chosen shape (a body given to the sky, to fire, to the launch pad); the pawn's own goodbye against the ceremony's strangeness or rightness. `important=true`, `colorCue` matching what core death/ritual groups use. |
| `alphamemes_rituals` | Ritual | 767 | `matchPrefixes: AM_` | Catch-the-rest for rodeo/sparring/baptism/maddening chant/relic destruction/scrap/tantrum. Ordered **after** the funeral group so funerals classify there first. Instruction: an unusual rite — spectacle, catharsis, or communal madness, written from the pawn's role in it. |
| `alphamemes_thoughts` | Thought | 486 | `matchPrefixes: AM_` **minus** ambience: add explicit low-value defNames to a *disabled* lower-order interceptor group if the monastic/barracks sleeping thoughts prove noisy (the `MOD_COMPAT_PLAN.md` interceptor pattern), rather than enumerating the ~30 outcome thoughts | Theming only, no batch. Instruction: how the rite settled in — pride, unease, or the hangover of collective fervor. The global mood-offset policy filters the rest. |
| `alphamemes_baptism` | Interaction | 19 (verify free) | `matchDefNames: AM_Speech_Baptism` | Not batched; a naming/initiation speech is a legitimate solo beat. Instruction: standing in front of everyone while words are said over you — or saying them. |
| `alphamemes_hediffs` | Hediff | 656 (verify free vs. `VEE_COMPAT_PLAN.md`'s 655) | `matchDefNames: AM_CatharsisHediff, AM_IconoclastHediff` | `HediffSignalPolicy`: `mode=DayReflection`, `badOnly=false`, `recordOnAdd=true`, `dayReflectionWeight=0.6`. Catharsis (post-tantrum-ritual relief) and iconoclast fervor are the only two with narrative weight; `AM_ToxicBuildup`/`AM_Kamikaze`/dryad hediffs are mechanics — skip, list as considered-skipped in the header comment. |

Deliberately skipped (record in file header): AbilityDefs (`AM_Ocular*` etc. would need
Ability-domain groups but fire as combat spam — the core `abilityHostile`/catch-all coverage
is enough); MemeDefs (static ideology configuration, no event); gauranlen/dryad content.

## Wording note

Alpha Memes rituals get **dark-leaning but not uniformly grim** language: sky burial is solemn,
blast-off funerals are absurd-and-moving, corpse rum is macabre comedy. Use the
`instructions`/`tones` variant pools (2–3 entries each) so repeated funerals don't read
identically; follow the no-blank-`<li>` rule documented on `DiaryInteractionGroupDef.instructions`.

## Localization

English inline; Russian DefInjected for all five groups (label/instruction/instructions/
tone/tones), core RU register per `GLOSSARY.md` (ритуал terminology consistent with the core
`ritualFinished` RU strings — read them first).

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. With Alpha Memes + Ideology: adopt a funeral precept, dev-complete the funeral ritual →
   participants' entries classify to `alphamemes_funeral` (not core `ritualFinished`), with
   the themed voice.
2. Dev-trigger a rodeo/sparring ritual → `alphamemes_rituals`.
3. Confirm one outcome thought (e.g. `AM_UnforgettableRodeo`) writes via `alphamemes_thoughts`
   when its mood offset clears the global threshold.
4. `AM_Speech_Baptism` → solo baptism entry.
5. Without Alpha Memes: file inert, no warnings, groups absent from settings.
6. Regression: vanilla funerals still classify to their existing group (order check).

## Risks / open questions

- **Precept-vs-pattern matching** is the single assumption to validate first (one dev ritual +
  log line). If the classifier key turns out to carry the pattern defName in some path, the
  `AM_` prefix still matches — but the funeral enumeration would need the pattern names
  (`AM_SkyBurialPattern`, …) added alongside.
- Alpha Memes updates fast (multiple releases/month): enumerate-and-gate means a *new* funeral
  type lands in `alphamemes_rituals` (safe default) until the list is refreshed — acceptable;
  note it in the header.
- Funeral Framework may also fire its own C#-driven sequences; if a funeral produces both a
  ritual signal and gathering-like behavior, ensure no double entry (dedup check in testing).
