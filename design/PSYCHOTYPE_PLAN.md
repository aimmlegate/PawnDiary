# Psychotype layer — detailed implementation plan

> Status: planned 2026-07-08; not yet implemented. This is a working plan, not a contract —
> shipped behavior lands in `../DOCUMENTATION.md`, the public API contract in `../EXTERNAL_API.md`.
> Written to be executable step by step: follow phases in order, don't improvise beyond marked
> decision points.

Add a second per-pawn voice layer to diary generation: the **psychotype** — a short semantic lens
describing what a pawn notices, values, and fears, and how they judge events. It is independent of
the existing writing-style layer (which controls sentence mechanics only) so the two multiply into
many distinct diary voices. Target model range stays 8b–70b: every psychotype rule is 1–2
sentences, behavioral, and mechanics-free.

## Glossary (required reading — the codebase has a false friend)

| Term | Meaning | Where |
|---|---|---|
| **Persona** (legacy, frozen) | The *writing style* layer. `DiaryPersonaDef`, `personaDefName`, `PersonaAffinity`, `PersonaStudio`. The name is kept for save/Def compat only. Never use "persona" for anything new. | existing |
| **Psychotype** (new) | This layer: the pawn's outlook/temperament lens. All new identifiers use this word. | this plan |
| **RimTalk persona** | The RimTalk mod's character text, read (never written) by the bridge. | `integrations/` |

## Recorded user decisions

- Psychotype is a **separate layer from writing style**; the two roll **independently** so
  combinations stay diverse. Writing style keeps rolling from traits/backstory themes as today.
- Psychotype rolls from the pawn's **skill passions** (minor/burning), including passion
  **combinations** and profile shape, with deliberate extra randomness (wildcard + jitter).
- Labels are **plain English adjectives** (RimWorld-trait register: "Paranoid", "Volatile"),
  not invented compound names.
- The psychotype **label is never injected into the prompt** (unlike styles, which prefix
  `label: rule`). Rule text only.
- **Children** (below the crystallization age, default 13) use a separate small child catalog for
  BOTH layers, and both layers re-roll ("crystallize") when the pawn crosses the age threshold.
  This allows lowering the first-person diary minimum age below 13.
- Player-made picks are **pinned** and never auto-re-rolled.
- Pre-existing saves: records that already have generated entries keep a **Neutral** (empty-rule)
  psychotype so established voices do not shift; records with no entries yet roll normally.
- RimTalk bridge Tier B ("persona-led diary voice") is **repointed** from the writing-style
  override to the psychotype override: RimTalk supplies who the pawn is, Pawn Diary keeps how
  they write.

## Verified facts (code ground truth; do not re-research)

- Writing-style initial roll: `DiaryPersonas.WeightedStartingPersona`
  ([Source/Defs/DiaryPersonaDef.cs](../Source/Defs/DiaryPersonaDef.cs)) — base weight 1,
  +3/matched theme via `PersonaAffinity`, ×0.25 per duplicate. Called once at record creation in
  `DiaryGameComponent.Lookup.cs` (`personaDefName = ...WeightedStartingPersona(...)`, ~line 837),
  next to `BuildUsedPersonaCounts`.
- Style runtime resolution: `WritingStyleResolutionPolicy` (pure) picks External > Hediff >
  Custom > Base; `HediffPersonaOverrides.ResolveWritingStyle` is the impure adapter. The final
  rule reaches the system prompt as `"PawnDiary.Prompt.PersonaVoice".Translate(rule)` in
  [Source/Generation/DiaryPipelineAdapters.cs](../Source/Generation/DiaryPipelineAdapters.cs)
  (`PersonaVoiceBlock`, line ~271), merged with the humor block by `CombinedVoiceBlock` and
  appended by `PromptAssembler.ComposeSystem` when `template.includePersona` is true.
- **`PromptAssembler` is mirrored byte-identically by the prompt-lab harness.** Therefore this
  plan does NOT change `ComposeSystem` or `PromptAssemblerInput`; the psychotype block is merged
  upstream inside `CombinedVoiceBlock` (adapters layer), which is not mirrored.
- First-person diary entries are already age-gated: `minimumFirstPersonAgeYears` = 13 in
  [1.6/Defs/DiaryTuningDef.xml](../1.6/Defs/DiaryTuningDef.xml) (checked in
  `DiaryGameComponent.Lookup.cs` ~line 180). 13 is also the final vanilla growth moment, i.e. the
  age at which a pawn's passion set is complete.
- Per-pawn saved state lives in `PawnDiaryRecord` (`personaDefName`,
  `customWritingStyleRule`, `externalWritingStyleOverride*`) with defaults backfilled in
  `ExposeData`/`EnsurePawnDiaryDefaults`.
- Tests are console assert harnesses (`tests/*/Program.cs`, `Main()` calls test methods and
  counts assertions); pure `Source/Pipeline/*.cs` files compile into them.
- External API override pattern to clone: `PawnDiaryApi.SetWritingStyleOverride` /
  `ResetWritingStyleOverride` ([Source/Integration/PawnDiaryApi.cs](../Source/Integration/PawnDiaryApi.cs)
  ~line 782), sanitizers in `ExternalWritingStyleOverrideText`, bridge usage in
  `integrations/PawnDiary.RimTalkBridge/Source/PersonaSync.cs`.

## Design summary

### Layer contract

| | Writing style (existing) | Psychotype (new) |
|---|---|---|
| Answers | how sentences are shaped | who is looking; what they notice/value/fear |
| Rule form | mechanical signature + example | 1–2 semantic sentences, no example, no mechanics |
| Rolled from | traits + backstory themes | skill passions (profile + combos) |
| Label in prompt | yes (`label: rule`) | **no** |

### Prompt block

New keyed string `PawnDiary.Prompt.PsychotypeLens`:

> How this pawn tends to see things: {0} Let this color what the entry notices, what it worries
> over, and how it judges — never what happened. Build only from the supplied facts, and never
> describe or judge the pawn's personality; let it show through the entry.

Placed BEFORE the style block (style stays last: it is the harder mechanical constraint and small
models weight the final instruction most). Order inside `CombinedVoiceBlock`:
psychotype → writing style → humor cue. Templates with `includePersona=false` (neutral
chronicles, titles) drop the whole combined block, psychotype included.

### Two-stage roll (adult)

Stage 0 — profile signals from the 12 skills (minor passion = 1 pt, burning = 2 pts):
domains **Violence** (Shooting, Melee), **Making** (Construction, Mining, Crafting),
**Nurture** (Cooking, Plants, Animals, Medicine), **Mind** (Intellectual, Artistic),
**People** (Social); plus total points, burning count, and focus (share of points in the top
domain).

Stage 1 — roll a **family**:

| Family | Members | Base | Signals |
|---|---|---|---|
| grounded | Content, Ambitious, Dutiful, Nostalgic, Pragmatic, Wry | 6 | +Nurture pts; +1 if passions exist but none burning |
| inward | Detached, Superstitious, Paranoid | 2 | +Mind pts; **+4 if zero passions**; +creepjoiner bonus |
| intense | Volatile, Ruthless, Theatrical, Narcissistic | 2 | +Violence pts, +People pts; +2 if burning ≥ 3 |
| anxious | Perfectionist, Avoidant, Dependent, Resentful | 2 | +Making pts; +2 if focus ≥ 2/3 with ≥ 3 total pts |

Stage 2 — roll the member within the family: flat base + per-skill nudges (data on the def XML)
+ combo signatures (constants) + child-continuity nudge + duplicate penalty (×0.25 per living
same-band colonist already holding it) + jitter (×[0.8, 1.3] per candidate).

Combo signatures (checked as sets, +2 each): Artistic+Social → Theatrical;
Intellectual+Artistic without Social → Superstitious; Shooting+Melee with no other passions →
Ruthless; Medicine+Social → Dependent; ≥2 of Cooking/Plants/Animals → Content; exactly one
passion and it is burning → Perfectionist and Ambitious +1 each; ≥4 passions across ≥3 domains →
Ambitious.

Randomness knobs: **wildcard** — with probability 0.12 skip all profile logic and roll flat over
stage-appropriate candidates (base grounded/skewed weights and duplicate penalty only); jitter as
above. Vetoes (candidate removed before the roll): a pawn with the Psychopath trait never rolls
Dependent; a pawn with the Kind trait never rolls Ruthless. No other trait input — independence
is the point.

### Children and crystallization

- Records created for pawns below `psychotypeCrystallizationAgeYears` (new tuning field, default
  13) roll from the **child catalogs** of both layers (flat + duplicate penalty + jitter; no
  profile signals at that age).
- The record stores which **stage band** ("child"/"adult") its current rolls were made for. A
  lazy check in the generation path compares the band to the pawn's current age; on mismatch it
  re-rolls: psychotype via the full adult roll (with a +1 continuity nudge from the child
  psychotype), writing style via the existing `WeightedStartingPersona` restricted to adult-stage
  styles. Pinned picks are skipped (band is stamped without re-rolling). Vat-grown pawns skip the
  child band entirely and land on the adult roll with a thin passion profile — the zero-passion
  inward lean is intended for them.
- Child continuity nudges: WideEyed → Superstitious, Content; BraveFront → Dutiful, Resentful;
  ShyWatcher → Avoidant, Detached; WildThing → Volatile, Ambitious; LittleAdult → Perfectionist,
  Dutiful.
- This unlocks lowering `minimumFirstPersonAgeYears` (new default 7 — decision point: keep 13 if
  child entries underwhelm in playtesting); children below the writing age still write nothing.

### Runtime resolution

`PsychotypeResolutionPolicy` (pure, mirrors `WritingStyleResolutionPolicy`):
**External API override > pawn custom rule > base def**. No hediff layer in v1 (hediff *style*
overrides already cover altered-state prose; a hediff psychotype layer is a possible follow-up).
Master settings toggle `enablePsychotypes` (default on): when off, resolution returns an empty
rule (block omitted) and pending rolls stay deferred.

---

## Phase 1 — Pure policy + tests (no Verse; land first)

**New file `Source/Pipeline/PsychotypeRollPolicy.cs`** — pure, test-covered:

- DTOs: `PsychotypeCandidate { defName, family, isGrounded, stage }`,
  `PsychotypeRollInput { List<PsychotypeSkillPassion> passions (skillDefName, level 0/1/2),
  bool isCreepJoiner, bool blockDependent, bool blockRuthless, string childPsychotypeDefName,
  Dictionary<string,int> usedCounts, string stageBand }`. Randomness injected as
  `Func<float> rand01` so tests seed it.
- Constants at top of file (all tunable): family bases (6/2/2/2), zero-passion inward bonus 4,
  burning-3 intense bonus 2, focus anxious bonus 2, creepjoiner inward bonus 4, combo bonus 2,
  skill-nudge scale (minor 1 / burning 2), continuity bonus 1, wildcard chance 0.12,
  jitter range [0.8, 1.3], duplicate penalty 0.25, weight floor 0.0001.
- Public: `Roll(input, candidates, rand01)` → defName; internal helpers exposed for tests:
  `BuildProfile`, `FamilyWeights`, `MemberWeights`.
- Child roll path: when `stageBand == "child"`, flat over child-stage candidates + duplicate
  penalty + jitter only.

**New file `Source/Pipeline/PsychotypeResolutionPolicy.cs`** — clone the shape of
`WritingStyleResolutionPolicy` (enum `PsychotypeRuleSource { BaseType, PawnCustom,
ExternalApiOverride }`, resolution DTO with candidates + winner + `CustomSuppressedByOverride`).

**New file `Source/Pipeline/PsychotypeText.cs`** — sanitizers: `CleanRule` (multiline,
player-authored custom rules, clone `PlayerWritingStyleText` caps) and
`CleanExternalRule`/`CleanSourceId` (one-line, clone `ExternalWritingStyleOverrideText`).

**Tests (extend `tests/DiaryPipelineTests/Program.cs`)**, seeded rand:
profile extraction (domains/burning/focus); family weights incl. zero-passion, burning≥3, focus,
creepjoiner; every combo signature fires and non-matching sets don't; vetoes remove candidates;
duplicate penalty compounds; wildcard path ignores signals; child path ignores signals;
continuity nudge applies; resolution precedence + suppression flag; sanitizer caps.
Distribution smoke: 10k seeded rolls over synthetic pawns — assert grounded share lands in
45–70%, no adult candidate below 1%, wildcard branch taken 10–14%.

## Phase 2 — Def type + XML catalogs + strings

**New file `Source/Defs/DiaryPsychotypeDef.cs`**: fields `rule`, `family`,
`lifeStage` ("adult" default / "child"), `skillAffinities` (list of `{ skill, points }`,
model-facing data for stage-2 nudges), initialized-null-safe like `DiaryPersonaDef`. Helper
`DiaryPsychotypes`: `All` (with hardcoded Neutral fallback), `Neutral`, `ForDefName`, `Resolve`,
`CandidatesFor(stageBand)`, and `RuleFor(defName)` that returns the rule **without** a label
prefix — comment states the divergence from `DiaryPersonas.RuleFor` is deliberate (labels are
picker text, not prompt text).

**Modify `Source/Defs/DiaryPersonaDef.cs`**: add `lifeStage` field (default adult);
`WeightedStartingPersona`/`RandomStartingPersona` gain a stage filter; existing callers pass
adult (behavior unchanged for adults).

**New file `1.6/Defs/DiaryPsychotypeDefs.xml`**: `DiaryPsychotype_Neutral` (empty rule) + 17
adult + 5 child defs — final rule texts in the appendix below, labels: Content, Ambitious,
Dutiful, Nostalgic, Pragmatic, Wry; Paranoid, Detached, Superstitious, Ruthless, Volatile,
Theatrical, Narcissistic, Resentful, Avoidant, Dependent, Perfectionist; Wide-eyed, Brave-front,
Shy-watcher, Wild-thing, Little-adult. Audit labels against vanilla trait names before commit and
rename any exact collision.

**Modify `1.6/Defs/DiaryPersonaDefs.xml`**: add ~5 child writing styles (naive-concrete
mechanics: present tense, literal observation, things in the order they happened, big plain
feelings; one mechanical signature each, no deliberate misspellings), tagged `lifeStage=child`.

**Strings**: English `DefInjected` for the new def type + `Keyed` additions
(`PawnDiary.Prompt.PsychotypeLens`, UI labels/tooltips/reroll/pin strings); Russian mirrors
(translate the interiority, not word-for-word). Existing style picker strings untouched.

## Phase 3 — Record state, rolls, crystallization

**Modify `Source/Models/PawnDiaryRecord.cs`**: add `psychotypeDefName`, `customPsychotypeRule`,
`externalPsychotypeOverrideRule`, `externalPsychotypeOverrideSourceId`, `voiceStageBand`
(""/"child"/"adult"), `psychotypePinned`, `writingStylePinned`. Scribe all; in the load-time
defaults pass: legacy records (field missing) with ≥1 generated entry → `Neutral` + band
"adult"; legacy records with no entries → clear so the lazy path rolls fresh. Sanitize custom/
external rules like the style fields.

**Modify `DiaryGameComponent.Lookup.cs`**: record creation rolls psychotype (band-appropriate)
next to the existing style roll; style roll now stage-filtered. Add
`BuildUsedPsychotypeCounts` (band-aware) beside `BuildUsedPersonaCounts`, and make the style
counts band-aware too. The impure adapter (new `Source/Generation/PsychotypeRolls.cs`) snapshots
`pawn.skills`, creepjoiner flag (`DlcContext.IsCreepJoiner`), and veto traits into
`PsychotypeRollInput` on the main thread, then calls the pure policy with `Rand.Value`.

**Crystallization**: new `DiaryTuningDef` field `psychotypeCrystallizationAgeYears` (default 13).
In the generation path (same main-thread spot where writing style is resolved per entry), call
`EnsureVoiceStage(pawn, record)`: compute band from biological age; on mismatch re-roll unpinned
layers as per the design summary and stamp the band. Lower `minimumFirstPersonAgeYears` default
to 7 in `DiaryTuningDef.xml` (decision point).

## Phase 4 — Prompt wiring

**Modify `Source/Generation/DiaryPipelineAdapters.cs`**: add `PsychotypeLensBlock(rule)`
(translate + trim, empty-safe); extend `CombinedVoiceBlock` to three parts ordered
psychotype → style → humor. `PromptAssembler`/`ComposeSystem` untouched (prompt-lab parity).
**Modify the generation dispatch** (where `HediffPersonaOverrides.RuleFor` is called) to also
resolve the psychotype rule via the resolution policy (inputs snapshotted main-thread) and pass
it through `DiaryPromptBuilder` → planner request alongside `personaRule`.
**Tests**: pipeline tests assert block presence/order, empty-psychotype omission, and that
`includePersona=false` templates drop the whole combined block.

## Phase 5 — UI

**Modify `Source/UI/Dialog_PawnWritingStyle.cs`** (single dialog, two sections — decision point:
split into a second dialog only if the layout fights): psychotype section with current
label+rule, stage-filtered picker, re-roll button, custom-rule text area, override-source
explanation row (mirrors the style section). Manual pick/custom edit sets the pin; show a small
"pinned" marker with an unpin control. **Modify `ITab_Pawn_Diary.Controls.cs`** to surface the
psychotype next to the style.
**Settings**: `enablePsychotypes` toggle (default on) in `PawnDiarySettings` + settings window.

## Phase 6 — External API + RimTalk bridge

**Modify `Source/Integration/PawnDiaryApi.cs`**: `SetPsychotypeOverride(pawn, sourceId, rule)` /
`ResetPsychotypeOverride(pawn, sourceId)` cloned from the style pair (main-thread guard,
source-ownership, sanitizers); extend `DiaryPawnSummarySnapshot`/style snapshot surface with
psychotype fields. Document in `EXTERNAL_API.md`.
**Modify `integrations/PawnDiary.RimTalkBridge/Source/PersonaSync.cs`**: Tier B repoints to the
psychotype override (keyed rule text reworded to a lens, not a writing voice); reset paths clear
the psychotype override; Tier A `chat_persona=` context line unchanged. On bridge load, clear any
stale style overrides the old Tier B placed (one-time migration sweep using the existing broad
pawn walk).

## Phase 7 (optional, can ship after v1) — Psychotype Studio

Clone `PersonaPresetStore`/`PawnDiaryMod.PersonaStudio` as `PsychotypePresetStore`/
`PawnDiaryMod.PsychotypeStudio` (override/custom rows, fixed family vocabulary, cache
invalidation via a `DiaryPsychotypes.InvalidateCache` mirroring styles' merge-with-settings).

## Phase 8 — Docs, save smoketest, version

`DOCUMENTATION.md` (player-facing layer description + settings), `EXTERNAL_API.md` (overrides),
`CHANGELOG.md`, `README.md` feature list. Extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`:
old save loads → adults with entries read Neutral + unchanged voice; entry-less records roll;
child crosses 13 → both layers re-roll and band stamps; pinned picks survive; RimTalk Tier B
toggle on/off places/clears psychotype (not style) overrides; `enablePsychotypes` off removes
the block and defers rolls. Version bump last.

## Suggested commit slicing

One commit per phase (Phase 1 may split policy/tests). Each commit compiles and its harness
passes; XML catalog (Phase 2) lands with English strings, RU strings may follow in the same
phase as a second commit.

## Tuning knobs (all constants, one place each)

wildcard 0.12 · jitter [0.8, 1.3] · family bases 6/2/2/2 · zero-passion inward +4 ·
burning≥3 intense +2 · focus anxious +2 · creepjoiner inward +4 · combo +2 · continuity +1 ·
duplicate penalty 0.25 · grounded/skewed base 2/1 (wildcard path) · crystallization age 13 ·
first-person minimum age 7.

## Risks / watchpoints

- **Prompt budget**: +~40–70 tokens per entry. Mitigations: master toggle, short rules, Neutral
  contributes zero text.
- **Layer fighting on small models**: eyeball list for the prompt-lab/manual pass — Theatrical ×
  dramatic styles, Wry × humor cues, Ruthless/Narcissistic editorializing (wrapper's final
  clause guards this), Paranoid vs Superstitious and Avoidant vs Resentful staying distinct.
- **Save churn**: the legacy-record rule (entries → Neutral, no entries → roll) must be covered
  by `DiarySaveNormalizationTests`-style cases before release.
- **Duplicate-penalty interplay**: band-aware counts keep child catalogs from starving adult
  ones and vice versa.

## Out of scope (recorded for later)

Hediff-driven psychotype overrides; psychotype drift on progression milestones; ideology/gene
inputs to the roll; a one-time "first adult diary page" entry at crystallization; moving combo
signatures from constants into XML.

---

## Appendix — final catalog texts (v1)

Wrapper (`PawnDiary.Prompt.PsychotypeLens`): see Design summary above.

### Grounded (adult)

- **Content** `DiaryPsychotype_Content` — This pawn measures a day by warmth, food, and who was
  nearby. Small comforts weigh more than great events, and losing a small comfort stings more
  than any grand setback.
- **Ambitious** `DiaryPsychotype_Ambitious` — This pawn measures everything against the larger
  life they are building. Each event registers as a step forward, a delay, or a lesson, and a
  day with no progress feels like a quiet defeat.
- **Dutiful** `DiaryPsychotype_Dutiful` — This pawn weighs each day by what they owed and
  whether they delivered. Other people's reliance on them is always in view; their own comfort
  comes up last, briefly, almost as an afterthought.
- **Nostalgic** `DiaryPsychotype_Nostalgic` — This pawn holds today against somewhere they lost.
  The present is always being measured next to a remembered before, tenderly rather than
  bitterly, and small things keep reminding them of it.
- **Pragmatic** `DiaryPsychotype_Pragmatic` — This pawn takes events at face value and sizes
  them by practical consequence. Feelings get acknowledged, given their brief moment, and set
  down next to the work that still needs doing.
- **Wry** `DiaryPsychotype_Wry` — This pawn notices the small absurdity inside serious moments
  and keeps their balance by naming it. The darker the day, the drier the observation, but the
  real feeling still shows underneath.

### Skewed (adult)

- **Paranoid** `DiaryPsychotype_Paranoid` (inward) — This pawn assumes nothing is as harmless as
  it looks. Kindness gets weighed for motive, coincidence gets counted, and a quiet, safe day
  feels like the part they cannot explain yet.
- **Detached** `DiaryPsychotype_Detached` (inward) — This pawn watches their own life as if from
  across the room. Praise and blame land with the same quiet, company costs effort, and the
  richest hours of the day happen inside their own head.
- **Superstitious** `DiaryPsychotype_Superstitious` (inward) — This pawn finds meaning in
  coincidence. Weather, dreams, and odd timing read as signs meant for them, and every event
  carries a second, hidden significance worth puzzling over.
- **Ruthless** `DiaryPsychotype_Ruthless` (intense) — This pawn sizes every event by what it
  cost them or gained them. Rules are things other people believe in, and other people's
  feelings are weather: noted, worked around, not felt.
- **Volatile** `DiaryPsychotype_Volatile` (intense) — This pawn feels people as either wonderful
  or unbearable, sometimes the same person in the same day. Distance from someone they love
  reads as the start of being left, and a quiet day feels hollow rather than peaceful.
- **Theatrical** `DiaryPsychotype_Theatrical` (intense) — This pawn recounts every event by who
  was watching and what impression they made. Being overlooked stings worse than being hurt,
  and any feeling becomes more real once it has an audience.
- **Narcissistic** `DiaryPsychotype_Narcissistic` (intense) — This pawn reads every event as
  chiefly about them. Their own part grows in the telling, other people's successes register as
  slights, and criticism keeps echoing days after everyone else forgot it.
- **Resentful** `DiaryPsychotype_Resentful` (anxious) — This pawn privately believes they
  deserve better than they get. Every slight is filed away, other people's luck feels quietly
  unfair, and criticism replays for days beneath a composed surface.
- **Avoidant** `DiaryPsychotype_Avoidant` (anxious) — This pawn reads rejection into neutral
  faces and expects judgment before anyone speaks. They want closeness badly, keep their
  distance to stay safe, and count that distance afterward as one more loss.
- **Dependent** `DiaryPsychotype_Dependent` (anxious) — This pawn feels no decision is safe
  until someone they trust approves it. Being needed is a comfort, being the one who must
  decide is frightening, and others' opinions of their choices outweigh their own.
- **Perfectionist** `DiaryPsychotype_Perfectionist` (anxious) — This pawn feels safe when things
  are counted, finished, and in their place. Disorder gnaws harder than danger, an interrupted
  task hurts more than an injury, and a properly done job is the day's real reward.

### Child catalog

- **Wide-eyed** `DiaryPsychotype_WideEyed` — This pawn finds everything enormous and
  interesting. New things are the best part of any day, questions matter more than answers, and
  even scary events are also, secretly, a little bit exciting.
- **Brave-front** `DiaryPsychotype_BraveFront` — This pawn is scared more often than they let
  on, even to their own diary. Danger gets played down and courage played up, but the fear
  still peeks through in the details they linger on.
- **Shy-watcher** `DiaryPsychotype_ShyWatcher` — This pawn watches from the edge of things and
  notices more than anyone guesses. Other people are the main subject — what they did, who was
  kind — while their own part stays small and carefully mentioned.
- **Wild-thing** `DiaryPsychotype_WildThing` — This pawn runs at the world headfirst. Rules
  exist to be tested, sitting still is the real punishment, and the best moments of any day are
  the loud, fast, slightly forbidden ones.
- **Little-adult** `DiaryPsychotype_LittleAdult` — This pawn works hard at being taken
  seriously. Grown-up concerns get solemnly borrowed — supplies, safety, whether people are
  doing things properly — and childish delights are enjoyed quickly and reported with mild
  embarrassment.

### Skill nudges (stage-2, on the defs)

Shooting → Paranoid · Melee → Volatile · Construction → Dutiful · Mining → Pragmatic ·
Cooking → Content · Plants → Nostalgic · Animals → Avoidant · Crafting → Perfectionist ·
Artistic → Theatrical, Superstitious · Medicine → Dependent · Social → Narcissistic ·
Intellectual → Detached. (Minor passion = 1 pt, burning = 2 pts, applied as weight bonus.)
