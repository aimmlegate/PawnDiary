# Body-Part Change Diary Events — Implementation Plan

Status: **implemented on 2026-07-03**. This document is the hand-off brief that was used for the
implementation.
Follow `skills/pawndiary-engineering/SKILL.md` and `AGENTS.md` (architecture barriers, DLC safety,
localization, docs-as-done). Every "verify" note below is a real checkpoint, not boilerplate.

---

## 1. Goal

Give pawns diary entries for the two body-change moments the diary currently misses or under-serves:

1. **Gaining an artificial body part** — surgery install, Anomaly mutation, any other
   `Hediff_AddedPart` addition (peg leg, prosthetic, bionic, archotech, fleshmass mutation).
2. **Losing a natural body part** — a `Hediff_MissingPart` appearing on a living colonist
   (combat destruction, amputation/harvest surgery).

Both must be written **through the pawn's own perception** of body modification:

- **Transhumanists crave augmentation** — a new bionic is a triumph; a lost limb is half grief,
  half "now I finally get the upgrade".
- **Body purists despise it** — an installed part is a violation of the flesh; losing a part is
  devastating *and* they dread the prosthetic that follows.
- **Everyone else is neutral-negative** — uneasy acceptance, adjustment, phantom-limb strangeness.
- **Ideology (when owned)** shifts the default: body-mod-approving ideoligions lean positive,
  disapproving/abhorrent ones lean negative. **Traits beat ideology** when both apply.

And through the **nature of the part**:

- crude prosthetic (peg leg, wooden hand, denture) → clumsy, humbling, "better than nothing"
- industrial prosthetic → functional, mechanical, a tool bolted to the body
- bionic → superior to flesh, powerful, faintly alien hum
- archotech → a miracle beyond understanding, near-magical
- **anomalous flesh** (Anomaly mutations: tentacle, flesh whip, fleshmass organs) →
  **horrifying for any pawn who is not inhumanized**; merely *passable* (uneasy fascination,
  tolerable) for transhumanists / body-mod-approving pawns; **indifferent for inhumanized pawns**
  (and ghouls, if they ever write).

## 2. Current behavior (the gap) — verified in code

- Capture chain that already exists: `Pawn_HealthTracker.AddHediff` Harmony postfix
  ([DiaryHealthPatches.cs](Source/Patches/DiaryHealthPatches.cs)) → `HediffSignal`
  ([HediffSignal.cs](Source/Ingestion/Sources/HediffSignal.cs)) → XML classification
  (`InteractionGroups.ClassifyHediff`, Hediff-domain `DiaryInteractionGroupDef` with a nested
  `HediffSignalPolicy`) → pure `Decide` → `Emit` → immediate solo entry or day-reflection line
  ([DiaryGameComponent.Hediffs.cs](Source/Core/DiaryGameComponent.Hediffs.cs)).
- The only Hediff-domain groups today are `hediffPregnancy`, `hediffLabor`,
  `hediffAnomalyCompulsion`, and the catch-all `hediffMajorHealth`
  ([DiaryInteractionGroupDefs.xml:1971](1.6/Defs/DiaryInteractionGroupDefs.xml)).
- **Artificial part installs are dropped entirely**: `Hediff_AddedPart` defs have `isBad=false`,
  and the catch-all has `<badOnly>true</badOnly>` → `HediffPassesBasicPolicy` rejects them.
- **Natural part loss is only a day-reflection footnote**: `Hediff_MissingPart` passes the
  catch-all (`missingPartAlways=true`) but the catch-all's mode is `DayReflection`, so the pawn
  never gets a dedicated page for losing an arm.

## 3. Verified RimWorld facts (1.6, checked against the local install)

Class hierarchy (verified by compile-time conversion checks against
`RimWorldWin64_Data/Managed/Assembly-CSharp.dll`):

- `Verse.Hediff_AddedPart : Verse.Hediff_Implant : Verse.HediffWithComps` — TRUE.
- `Verse.Hediff_MissingPart : Verse.HediffWithComps` — it does **NOT** derive from
  `Hediff_Injury` in 1.6. So `excludeInjuries=true` does *not* filter missing parts, and the
  existing `excludeInjuries + missingPartAlways` XML combination is coherent. Do not "fix" it.

HediffDef fields usable for classification (all base-game fields — safe without any DLC):

- `hediffClass` — `Hediff_AddedPart` for real replacement parts; `Hediff_Implant` for implants
  (joywire, painstopper, ghoul plating). Scope of this plan is **AddedPart only** (see §11).
- `organicAddedBodypart` (bool) — set `true` by Anomaly's `AddedMutationBase`
  (`Data/Anomaly/Defs/HediffDefs/Hediffs_BodyParts_Prosthetic.xml`): **`FleshmassStomach`,
  `FleshmassLung`, `Tentacle`, `FleshWhip`**. This is the primary "anomalous mutated flesh"
  marker and it is mod-friendly (any mod's organic added part classifies the same way).
- `spawnThingOnRemoved` (ThingDef) — the removal item. Its `techLevel` gives the tech tier
  (`Industrial` prosthetics, `Spacer` bionics, `Archotech` archotech). **Trap:** the medieval set
  (`PegLeg`, `WoodenHand`, `WoodenFoot`) returns **`WoodLog`**, and `Denture` and the Anomaly
  mutations have **no** `spawnThingOnRemoved` at all — tier detection needs fallbacks.
- `addedPartProps.partEfficiency` (float) — reliable fallback signal: peg leg/wooden hand 0.60,
  wooden foot/denture 0.80, simple prosthetics ~0.85–1.0, bionics 1.25, archotech 1.50,
  Anomaly tentacle 1.20.
- `addedPartProps.betterThanNatural` — set only by archotech parts in Core; optional extra signal.

Vanilla content inventory (for seed lists and testing):

- Core crude: `PegLeg`, `WoodenHand`, `WoodenFoot`, `Denture`.
- Core industrial: `SimpleProstheticLeg/Arm/Heart`, `CochlearImplant`, `PowerClaw`.
- Core bionic (Spacer): `BionicEye/Arm/Leg/Spine/Heart/Stomach/Ear/Tongue/Jaw`.
- Core archotech: `ArchotechEye`, `ArchotechArm`, `ArchotechLeg`.
- Royalty/Biotech/Odyssey parts (`Hediffs_BodyParts_*_Empire.xml`, `Detoxifier*`, `BloodWarmer`,
  `PilotAssistant`, `VacskinGland`, `SentienceCatalyst`) all follow the same base defs and
  classify via techLevel/efficiency automatically — no per-DLC code, no defName references.
- Anomaly ghoul hearts are `Hediff_AddedPart` with Industrial tech (`AdrenalHeart`,
  `CorrosiveHeart`, `MetalbloodHeart`) plus `RevenantVertebrae` — bioferrite horror-tech; ship
  them in the anomalous defName override list (strings only, inert without Anomaly).
- Missing part is a **single def**: `MissingBodyPart` (class `Hediff_MissingPart`,
  `Data/Core/Defs/HediffDefs/Hediffs_Local_Injuries.xml:12`). The hediff's `Part` gives the body
  part; `lastInjury` + `IsFresh` give the cause (`SurgicalCut` ⇒ surgery; anything else ⇒
  violence/accident).

Perception sources (all read as strings, all optional-content-safe):

- Base-game traits (no DLC guard needed): `Transhumanist`, `BodyPurist`
  (`Data/Core/Defs/TraitDefs/Traits_Singular.xml:462,476`).
- Ideology precepts, issue `BodyModification` (`Data/Ideology/Defs/PreceptDefs/Precepts_BodyMod.xml`):
  `BodyMod_Approved`, `BodyMod_Disapproved`, `BodyMod_Abhorrent`. Read via `pawn.Ideo`
  (Ideology-guarded, see §6).
- Anomaly inhumanized state: hediff defName `Inhumanized` (already string-matched by the existing
  `hediffAnomalyCompulsion` group — reuse the same string). Ghouls: guarded `pawn.IsGhoul`.
- Ideology's own event thoughts for installs are named `InstalledProsthetic_*` — relevant for the
  double-capture checkpoint in §10.

## 4. Design overview

Reuse the existing Hediff signal pipeline end to end. No new Harmony patch is required
(`AddHediff` postfix already fires for installs, mutations, and part destruction). The work is:

```
AddHediff postfix (exists)
  → HediffSignal (exists; add 2 small guards)
    → NEW: kind-token classifier key  ("BionicArm_addedpart", "Tentacle_addedpart_organicpart",
                                       "MissingBodyPart_missingpart"; ordinary hediffs unchanged)
    → 3 NEW Hediff-domain groups claim the keys by suffix/segment matchers
    → HediffEventData payload (extend with part fields)
    → pure Decide (existing policy flags suffice)
    → RecordImmediateHediffEvent (extend: stance/tier cue + richer gameContext)
      → solo diary page, LLM rewrite queued
```

Three new groups (three separate settings toggles, three tones):

| group defName | claims | order | mode | colorCue |
|---|---|---|---|---|
| `hediffPartGainedAnomalous` | added parts with `organicpart` token, plus XML defName list | 660 | Immediate | `extremeDark` |
| `hediffPartGainedArtificial` | remaining `addedpart` tokens | 665 | Immediate | — |
| `hediffPartLostNatural` | `missingpart` token | 670 | Immediate | — |

All three sit below the `hediffMajorHealth` catch-all (order 700) so they claim first; ordinary
hediffs keep flowing exactly as before.

Two per-event enrichment axes, computed at capture time and carried as **plain payload tokens**
(English schema tokens per the AGENTS.md localization carve-out):

- `part_tier` = `crude | prosthetic | bionic | archotech | anomalous`
- `body_attitude` = gain: `craves | approves | uneasy | despises | detached`,
  anomalous gain: `fascinated_uneasy | horrified | detached`,
  loss: `opportunity | grieving | violated | detached`
- plus `part_cause` = `surgery | violence | unknown` (loss only, from `lastInjury`/`IsFresh`).

The tokens select localized **attitude/tier cue sentences** (Keyed strings) appended to the
group instruction, and are also embedded in `gameContext` so the LLM prompt and future policy can
see them.

## 5. Classification: kind-token classifier key

Precedent: Raid/Ritual/Ability domains already classify by a **synthetic key** (defName plus
tokens), and `DiaryEventDomainClassifier.GroupClassifierKey`
([DiaryEventDomainClassifier.cs:60](Source/Pipeline/DiaryEventDomainClassifier.cs)) already
rebuilds such keys for saved events from `gameContext` markers. Mirror that pattern for the
Hediff domain:

1. **Pure helper** (new, e.g. `Source/Capture/BodyPartEventPolicy.cs`):
   `BuildHediffClassifierKey(defName, isAddedPart, isMissingPart, isOrganicAddedPart)`.
   - No part flags → return `defName` unchanged. **This keeps every existing exact
     `matchDefNames` group (pregnancy, labor, anomaly compulsions) working — do not append
     tokens unconditionally.**
   - Otherwise append lowercase tokens in a fixed order, e.g.
     `BionicArm_addedpart`, `Tentacle_addedpart_organicpart`, `MissingBodyPart_missingpart`.
   - **Checkpoint:** pick the separator so `GroupNameMatcher.MatchesSuffix` / `MatchesSegment`
     treat each token as a whole suffix/segment (`_` is expected to work — the segment matcher
     splits on CamelCase/underscore/digit). Pin this with a pure test before writing the XML.
2. **Edge computation** in `DiaryGameComponent.TryGetHediffPolicy`
   ([DiaryGameComponent.Hediffs.cs:288](Source/Core/DiaryGameComponent.Hediffs.cs)): derive the
   flags from the live def — `typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass)`,
   `typeof(Hediff_MissingPart).IsAssignableFrom(def.hediffClass)`, `def.organicAddedBodypart` —
   and classify with `InteractionGroups.ClassifyHediff(classifierKey)`. Add an overload; keep the
   old `ClassifyHediff(HediffDef)` signature delegating with a def-derived key so the settings-UI
   caller (`PawnDiarySettings`) classifies identically.
   - The existing string memo (`classifyByDomainName`) keys on domain+name — the classifier key
     is a string, so memoization works unchanged. **Do not** memoize part hediffs under the bare
     defName in the `classifyByDef` map, or reuse one map keyed consistently by the key string.
3. **Saved-event display recovery**: append a `part_kind=` marker to the hediff `gameContext`
   (see §7) and extend `GroupClassifierKey` with a Hediff branch that rebuilds
   `savedDefName + tokens` from that marker — same shape as the existing Ability branch. Without
   this, old body-part pages would display-classify into the catch-all after reload.
4. Group XML matchers: anomalous group `matchSuffixes: [organicpart]` (order 660 wins first);
   artificial group `matchSuffixes: [addedpart]` (suffix, so it never claims `..._organicpart`
   keys); loss group `matchSuffixes: [missingpart]`. The anomalous group ALSO lists the ghoul
   hearts + `RevenantVertebrae` via a tuning-driven defName override (see §6 tier overrides) —
   implement the override by letting the tier policy force `anomalous`, and route the *group*
   via a `matchDefNames` entry `AdrenalHeart_addedpart` etc. (exact match against the built key —
   cheap and explicit).

## 6. Perception and tier: new context reader + pure policy

**Impure edge reader** — new file `Source/Generation/BodyModContext.cs`, DlcContext-style
(this is pawn-state reading, but body-mod stance spans base game + 2 DLCs; keep it its own small
file with the same double-guard discipline; reference [DlcContext.cs](Source/Generation/DlcContext.cs)):

- `BodyModStanceFacts FactsFor(Pawn pawn)` returning a plain DTO:
  - `hasCravesTrait` / `hasDespisesTrait` — iterate `pawn.story?.traits?.allTraits`, compare
    defNames against XML-tuned lists (defaults `[Transhumanist]` / `[BodyPurist]`).
  - `ideologyStance` token (`approves | despises | none`) — guarded
    `ModsConfig.IdeologyActive && pawn?.Ideo != null`, then scan
    `pawn.Ideo.PreceptsListForReading` defNames against tuned lists
    (defaults approve `[BodyMod_Approved]`, despise `[BodyMod_Disapproved, BodyMod_Abhorrent]`).
  - `isInhumanized` — hediffSet contains a defName from a tuned list (default `[Inhumanized]`);
    plain string scan, no Anomaly guard needed (absent def simply never appears).
  - `isGhoul` — `ModsConfig.AnomalyActive && pawn.IsGhoul` (**checkpoint:** confirm the exact
    property name on 1.6 `Pawn`; if absent use `pawn.mutant?.Def?.defName == "Ghoul"` with
    null-guards).

**Pure policy** — new file `Source/Capture/BodyPartEventPolicy.cs` (plain C#, no Verse types;
DTO in/tokens out; unit-tested):

Tier resolution, first match wins (all inputs snapshotted at the edge):

| # | rule | result |
|---|---|---|
| 1 | defName in tuned `bodyPartTierOverrideAnomalous/Crude/Prosthetic/Bionic/Archotech` lists | that tier |
| 2 | `organicAddedBodypart` | `anomalous` |
| 3 | `spawnThingOnRemoved.techLevel` is Neolithic/Medieval | `crude` |
| 4 | … Industrial | `prosthetic` |
| 5 | … Spacer | `bionic` |
| 6 | … Ultra/Archotech | `archotech` |
| 7 | `partEfficiency` < 0.9 | `crude` |
| 8 | ≤ 1.0 | `prosthetic` |
| 9 | ≤ 1.3 | `bionic` |
| 10 | > 1.3 | `archotech` |
| 11 | default | `prosthetic` |

Ship `bodyPartTierOverrideAnomalous` with defaults
`[AdrenalHeart, CorrosiveHeart, MetalbloodHeart, RevenantVertebrae]`. Note rule 3 makes the
`WoodLog` quirk land correctly (crude), but keep the efficiency fallback anyway for `Denture`
and modded parts. Thresholds and lists live in `DiaryTuningDef` (pattern:
`psylinkHediffDefNames` already exists there) and register in
[AdvancedFieldCatalog.cs](Source/Settings/AdvancedFieldCatalog.cs) near the existing hediff
tuning rows (~line 1271).

Attitude resolution (precedence top→bottom):

| pawn state | artificial gain | anomalous gain | natural loss |
|---|---|---|---|
| inhumanized or ghoul | `detached` | `detached` | `detached` |
| despises trait (BodyPurist) | `despises` | `horrified` | `violated` |
| craves trait (Transhumanist) | `craves` | `fascinated_uneasy` | `opportunity` |
| ideology despises | `despises` | `horrified` | `violated` |
| ideology approves | `approves` | `fascinated_uneasy` | `grieving` |
| default | `uneasy` | `horrified` | `grieving` |

This encodes the user's rules exactly: anomalous flesh is horrifying for everyone who is not
inhumanized, and merely passable (`fascinated_uneasy`) for transhumanist-leaning pawns; artificial
parts are craved / despised / neutral-negative by stance; traits outrank ideology.

The policy also returns the **Keyed cue-key names** for attitude and tier (see §8), so key
selection itself is pure and testable.

## 7. Payload, capture guards, and emit changes

`HediffEventData` ([HediffEventData.cs](Source/Capture/Events/HediffEventData.cs)) — add optional
string fields `PartKindToken`, `PartTierToken`, `AttitudeToken`, `CauseToken` (empty for ordinary
hediffs). Extend `BuildGameContext` to append, **after all existing fields** (field order is
locked by tests — extend the locked test, don't reorder):
`part_kind=…; part_tier=…; body_attitude=…; part_cause=…` (each omitted when empty).
`part_kind` is the marker `GroupClassifierKey` uses for display recovery (§5.3).

`HediffSignal` / `DiaryGameComponent.BuildHediffEventData` — populate the new fields when the
matched group has a part kind (pass the kind flags + `BodyModContext.FactsFor(pawn)` +
tier snapshot through; keep all Verse reads on the impure side, tokens computed by the pure policy).

Capture guards (in `HediffSignal` constructor or `TryGetHediffPolicy`):

- **Skip `Hediff_MissingPart` when `pawn.Dead`** — a killing blow that destroys a vital part must
  not race the death-description page.
- Starting-pawn safety already exists (`!GamePlaying` skip + first-scan baselining) — no change.
- Recruits/NPCs never generate entries for pre-existing parts because `AddHediff` only fires for
  colonists during play — no change needed, but assert this in manual testing.

Emit (`RecordImmediateHediffEvent`,
[DiaryGameComponent.Hediffs.cs:209](Source/Core/DiaryGameComponent.Hediffs.cs)) — when the payload
carries a `PartKindToken`:

- Resolve the attitude cue + tier cue Keyed strings chosen by the pure policy and append them to
  the group instruction (one combined instruction string; `.Translate()` on the main thread).
- Fallback text comes from the group's `appearedTextKey` as usual (`{0}` pawn, `{1}` part label —
  the mechanism already exists in `ImmediateHediffText`).
- **Checkpoint:** confirm the group's `colorCue` reaches the created `DiaryEvent` through
  `AddSoloEvent` (check `DiaryGameComponent.EventFactory.cs`); the anomalous group must render
  with the `extremeDark` cue like the Anomaly observed conditions do.

## 8. XML deliverables

**`1.6/Defs/DiaryInteractionGroupDefs.xml`** — three groups in the Hediff section. Sketch (final
wording at implementer's discretion; instructions/tones are DefInjected-localized prose):

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>hediffPartGainedAnomalous</defName>
  <label>Anomalous body changes</label>
  <order>660</order>
  <domain>Hediff</domain>
  <important>true</important>
  <colorCue>extremeDark</colorCue>
  <instruction>the pawn's body has grown or received a living, wrong-feeling flesh part; write the physical reality of it — movement, warmth, sound — filtered through the attitude cue, and never explain its biology</instruction>
  <tones>
    <li>visceral and intimate, the body no longer fully one's own</li>
    <li>quiet dread anchored in physical sensation</li>
  </tones>
  <hediff>
    <mode>Immediate</mode>
    <badOnly>false</badOnly>
    <excludeInjuries>true</excludeInjuries>
    <minSeverity>0</minSeverity>
    <recordOnAdd>true</recordOnAdd>
    <recordOnSeverityIncrease>false</recordOnSeverityIncrease>
    <dedupTicks>60000</dedupTicks>
    <appearedTextKey>PawnDiary.Event.BodyPartGainedAnomalous</appearedTextKey>
  </hediff>
  <matchSuffixes><li>organicpart</li></matchSuffixes>
  <matchDefNames>
    <li>AdrenalHeart_addedpart</li>
    <li>CorrosiveHeart_addedpart</li>
    <li>MetalbloodHeart_addedpart</li>
    <li>RevenantVertebrae_addedpart</li>
  </matchDefNames>
</PawnDiary.DiaryInteractionGroupDef>
```

`hediffPartGainedArtificial` (order 665): same shape, `matchSuffixes: [addedpart]`, no colorCue,
instruction about a made thing joining the body, `appearedTextKey PawnDiary.Event.BodyPartGained`.
`hediffPartLostNatural` (order 670): `matchSuffixes: [missingpart]`,
`missingPartAlways true`, instruction about losing a part of oneself,
`appearedTextKey PawnDiary.Event.BodyPartLost`. Keep `<visibleOnly>false</visibleOnly>` everywhere.

**`1.6/Defs/DiaryTuningDef.xml` + [DiaryTuningDef.cs](Source/Defs/DiaryTuningDef.cs)** — new
fields with code fallbacks: the five tier-override string lists, the four stance string lists
(craves traits, despises traits, approve precepts, despise precepts), the inhumanized hediff list,
and the two efficiency thresholds if not hardcoded as parser sentinels. Register all of them in
`AdvancedFieldCatalog`.

**Keyed strings** — `Languages/English/Keyed/PawnDiary.xml` **and**
`Languages/Russian (Русский)/Keyed/PawnDiary.xml` (both are mandatory in the same change; follow
`Languages/Russian (Русский)/GLOSSARY.md` for terminology):

- Fallback texts: `PawnDiary.Event.BodyPartGained`, `…BodyPartGainedAnomalous`,
  `…BodyPartLost` (`{0}` pawn, `{1}` part label).
- Attitude cues (one sentence each, instruction-register prose):
  `PawnDiary.Prompt.BodyPart.Attitude.Craves / Approves / Uneasy / Despises / Detached /
  FascinatedUneasy / Horrified / Opportunity / Grieving / Violated`.
- Tier cues: `PawnDiary.Prompt.BodyPart.Tier.Crude / Prosthetic / Bionic / Archotech / Anomalous`.
- Cause fragments (loss only, optional): `PawnDiary.Prompt.BodyPart.Cause.Surgery / Violence`.

**DefInjected** — add the three groups' `label`, `instruction`, `instructions`, `tone`, `tones`
to `Languages/Russian (Русский)/DefInjected/PawnDiary.DiaryInteractionGroupDef/DiaryInteractionGroupDefs.xml`
(and English DefInjected only if that file mirrors other groups — follow the existing file's
convention). New tuning fields likewise follow the existing DefInjected DiaryTuningDef convention.

## 9. Tests (extend `tests/DiaryCapturePolicyTests`)

Pure-project rules apply: no Verse/Unity types. Cover at minimum:

- **Classifier key builder**: token order and separator; ordinary defNames pass through
  *unchanged* (`PregnantHuman` → `PregnantHuman`); missing+added never co-occur;
  key segments/suffixes actually match via `GroupNameMatcher` (pin the separator behavior).
- **Tier policy**: every rule row in the §6 table, including the `WoodLog`/Neolithic case,
  `Denture` (no spawn thing, efficiency 0.8 → crude), tentacle (organic wins over its 1.2
  efficiency), archotech 1.5 fallback path, override-list precedence.
- **Attitude policy**: full precedence matrix from §6 for all three event kinds — especially
  inhumanized-beats-everything, trait-beats-ideology, anomalous default = horrified,
  transhumanist anomalous = fascinated_uneasy.
- **Cue-key selection**: token → key-name mapping is total (no token silently maps to nothing).
- **GameContext**: extend the locked field-order test with the four new trailing fields, and the
  `GroupClassifierKey` Hediff branch (context with `part_kind=` → rebuilt key; without → defName).

## 10. Implementation checkpoints (verify during coding, not after)

1. `GroupNameMatcher` separator handling for the classifier key (§5.1) — pure test first.
2. `classifyByDef` memo in `InteractionGroups` — must not cache a part hediff under a key that
   the new path would compute differently (§5.2).
3. `colorCue` propagation to `DiaryEvent` for Hediff-domain immediate events (§7).
4. `pawn.IsGhoul` API shape on 1.6 (§6).
5. **Double capture via Ideology thoughts**: with Ideology active, installs also fire
   `InstalledProsthetic_*` memory thoughts, which the Thought-domain groups may claim. Grep the
   Thought groups; if any full-page group would claim them, add `InstalledProsthetic` as a
   `matchPrefixes` entry to the ignored/low-value thought group (or share an
   `EventTypeDedupKey` the way death descriptions do). Day-reflection-only claims are acceptable.
6. `Hediff_MissingPart` severity: confirm `minSeverity 0` + `missingPartAlways true` records it;
   confirm no progression baseline is created (`recordOnSeverityIncrease false`).
7. Confirm settings UI shows three new toggle rows automatically and
   `AdvancedFieldCatalog.RegisterInteractionHediffPolicy` picks up the new groups' policies.

## 11. Non-goals / follow-ups (do not implement now; note in DOCUMENTATION.md if useful)

- **Implants** (`Hediff_Implant` without added part: joywire, painstopper, ghoul plating) — same
  machinery would extend naturally (an `implant` kind token); out of scope.
- **Part restored** (healer mech serum, natural-organ install removing a MissingPart) — removal
  hooks don't exist; would need a `RestorePart` patch. Follow-up.
- **"What was lost was artificial"** — when a bionic is destroyed in combat, the MissingPart event
  reads as flesh loss. A Harmony *prefix* capturing whether the destroyed part carried an
  AddedPart (via `__state`) would let the attitude policy mourn the lost bionic (a great
  transhumanist beat). Optional P2, only if cheap.
- **Lasting stance coloring** (a body purist brooding for days over their prosthetic) — belongs
  to the observed-conditions/prompt-enchantment layer, not one-shot events.

## 12. Definition of done

- `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug` passes; rebuilt
  `1.6/Assemblies/PawnDiary.dll` staged.
- `dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj` passes
  (plus `DiaryPipelineTests` if `DiaryEventDomainClassifier` changed).
- Touched XML validated with an XML parser.
- English + Russian Keyed and DefInjected strings added together.
- `DOCUMENTATION.md` updated: §4 Event Sources (hediff part events), §5 XML Policy (kind tokens,
  new tuning fields), and `EVENT_PROMPT_MAP.md` rows for the three groups.
- `CHANGELOG.md` dated entry.
- Manual smoke pass (dev mode): install a bionic arm on a Transhumanist, a BodyPurist, and a
  plain pawn (three distinct attitudes in the entries); shoot off a pawn's arm (loss entry,
  `part_cause=violence`); Anomaly: mutate a pawn via the twisted obelisk (horrified entry,
  extremeDark), same on an inhumanized pawn (detached); verify a pregnancy still classifies to
  `hediffPregnancy` (no regression) and that a new colonist with pre-existing bionics writes
  nothing.
