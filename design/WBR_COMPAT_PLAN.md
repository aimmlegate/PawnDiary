# Implementation plan — Way Better Romance compat (pure XML, core mod)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #5 in
> [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Pure-XML pack in the core mod: **no C#,
> no adapter, no API change.** The optional orientation context line mentioned in the research
> doc is **deliberately dropped** — orientation is identity, not an event, and surfacing it in
> every prompt risks flattening pawns to a label; the core prompt already carries traits'
> effects via psychotype and thought capture.

## Target mod facts (verified from source 2026-07-12 — re-verify against installed Defs)

- **Way Better Romance**, author divineDerivative, packageId `divineDerivative.Romance`,
  Workshop 2877731755, source `github.com/divineDerivative/WayBetterRomance` (MIT).
  1.4/1.5/1.6; steady maintenance (commits through 2026-04).
- InteractionDefs (3): `TriedHookupWith`, `AskedForDate`, `AskedForHangout` — **unprefixed**;
  exact-name matching only.
- ThoughtDefs (14): `RebuffedMyHookupAttempt`, `RebuffedMyHookupAttemptMood`,
  `FailedHookupAttemptOnMe`, `RebuffedMyDateAttempt`, `RebuffedMyDateAttemptMood`,
  `FailedDateAttemptOnMe`, `RebuffedMyHangoutAttempt`, `RebuffedMyHangoutAttemptMood`,
  `FailedHangoutAttemptOnMe`, `GotSomeLovinAsexual`, `LovinAsexualPositive`,
  `LovinAsexualNegative`, `PassionateLovinAsexualPositive`, `PassionateLovinAsexualNegative` —
  re-dump the exact list from the installed `Defs/ThoughtDefs.xml`; several are social(+mood)
  twins.
- Also adds: orientation TraitDefs (slot-free) + Faithful/Philanderer, JoyDefs, date/hangout
  JobDefs (no def signal — the initiating interaction is the capture point), lover/spouse-count
  PreceptDefs (static ideology config — no event surface), StatDefs.
- WBR **modifies vanilla romance flow** (hookups/dates are its own systems, but vanilla
  `RomanceAttempt`/`MarriageProposal` interactions and Lover/Fiance/Spouse relations remain) —
  the core `romance` (Interaction, order 20) and `romance_relation` (Romance domain, order 20)
  groups keep working untouched. This pack only adds WBR's *new* defs.

## Ground truth to read first

1. `1.6/Defs/DiaryInteractionGroupDefs.xml` — the core `romance` group (order 20): its
   instruction register is the baseline these new groups must complement, not duplicate.
2. `integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml` — group idiom; `vsie_vent`'s
   promotion block (cloned below).
3. All shipped compat files — confirm order slots 15/16 and 488 are free.

## Deliverables (core mod)

```
1.6/Defs/Compat/DiaryCompat_WayBetterRomance.xml   three groups below
Languages/Russian (Русский)/DefInjected/…          RU for the new groups
```

All groups gated `enableWhenPackageIdsLoaded: divineDerivative.Romance`.

## Groups

| Group | Domain | Order | Matchers | Shape / instruction intent |
|---|---|---|---|---|
| `wbr_hookup` | Interaction | 15 | `matchDefNames: TriedHookupWith` | **Not batched.** A hookup attempt is rare enough per pair and emotionally loaded in either outcome — every attempt is its own pairwise entry, like the core `romance` group. Instruction: wanting someone and saying so with the whole colony ten meters away — nerve, the answer, and what it changes tomorrow. Must read as *carnal-adjacent but tasteful*; distinct from core `romance`'s courtship register. |
| `wbr_askedout` | Interaction | 16 | `matchDefNames: AskedForDate, AskedForHangout` | `AmbientDayNote` batch (windowTicks 60000, `minEventsToWrite` 2 — these are rarer than chatter; a lone ask should still write, `maxSampleLines` 3, `syntheticDefName` WbrAskedOutAmbientDay, shared `PawnDiary.Event.AmbientSocial*` keys) **plus** the `vsie_vent` promotion block with `baseChance` raised to 0.05 — a first date-ask between longtime friends deserves its own page reasonably often. Instruction: asking for someone's time — a date or just company; hope dressed up as casualness. Note for the agent: the *rejection* side arrives separately via the thoughts group; do not write rejection language into this instruction. |
| `wbr_thoughts` | Thought | 488 | `matchPackageIds: divineDerivative.Romance` | Theming only, no batch (Thought groups never batch — see `DiaryCompat_VSIE.xml` note). Package-id matching is correct here (unlike Hospitality): every WBR thought is romance-flavored and lands on a diary-eligible colonist. Instruction: the aftermath of romantic courage — rebuffed, relieved, or quietly glad; for the asexual-lovin' thoughts, intimacy negotiated on the pawn's own terms, dignity intact. |

Wording constraint for all three: WBR's headline feature is orientation diversity. The
instructions must handle any pairing without assuming gender or orientation — write in terms
of "someone", "the other person", attraction and nerve, never pronoun-dependent templates.
The asexual-lovin' thoughts especially: positive variants are *contentment with chosen
closeness*, negative are *discomfort despite affection* — check the thought stage labels in
the installed XML and mirror their nuance.

## Localization

English inline; Russian DefInjected for the three groups. RU register: follow the core
`romance` group's existing RU strings for terminology (свидание, флирт — check `GLOSSARY.md`
and the core RU file first); keep the same gender-neutral discipline in Russian, which is
harder — prefer constructions that avoid gendered past-tense verbs where the subject is the
partner.

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. With WBR: force a hookup attempt (dev or high-attraction pair) → pairwise entry via
   `wbr_hookup` for both pawns (paired POV).
2. A date-ask → batches into the ambient note; a second ask same day joins it; confirm a
   promoted one surfaces alone.
3. A rebuffed attempt → `RebuffedMy*` thought writes via `wbr_thoughts` when it clears the
   global mood threshold.
4. Vanilla regression: `RomanceAttempt` / `MarriageProposal` / new Lover relation still
   classify to core `romance` / `romance_relation` (WBR patches vanilla flow — this is the
   regression most worth checking).
5. Without WBR: inert, no warnings.
6. With WBR + Romance On The Rim both loaded (common combo; RotR compat ships later per its
   own plan): no cross-claiming — WBR matchers are exact-name so RotR defs are untouched.

## Risks / open questions

- WBR renames/retunes defs occasionally (it has a framework, DivineFramework, and active
  refactors): the 3+14 def list must be re-dumped at build time; exact-name matchers mean a
  renamed def silently drops out — acceptable, but note the list's verification date in the
  file header.
- `RebuffedMy*` vs `FailedAttemptOnMe` twins land on opposite pawns of the same moment —
  both are legitimately capturable (paired POV via two separate thoughts); watch in testing
  that the same rejection doesn't also produce a near-duplicate interaction entry from
  `wbr_hookup`/`wbr_askedout` (if it does, rely on the thoughts and consider dropping the
  interaction's promotion block — decide from observed output, and record the decision in the
  file header).
- Race-mod configurations (HAR) change WBR behavior but not defNames — no action.
