# RimWorld Event Coverage — Gap Analysis & XML-Only Suggestions

Status: **proposal for review — no behavior changed by this document.**

This document answers three questions:

1. What RimWorld moments does Pawn Diary react to today?
2. What does RimWorld (base game + all five DLC) throw at the player that we currently skip
   or handle with only a generic fallback?
3. What could we add to improve atmosphere — **using only the existing machinery, by extending
   XML defs, with zero C# changes** — without overwhelming the player with diary pages?

We explicitly do **not** aim to cover every possible event. The selection principle throughout:
prefer changes that add *tone and flavor to pages we already write* (zero new page volume) over
changes that create *new pages* (bounded, rare, one-shot only).

Sources: repo XML/C# under `1.6/Defs/` and `Source/`, `EVENT_PROMPT_MAP.md`, and the RimWorld
wiki event lists ([Events](https://rimworldwiki.com/wiki/Events),
[Events/Anomaly](https://rimworldwiki.com/wiki/Events/Anomaly),
[Events/Odyssey](https://rimworldwiki.com/wiki/Events/Odyssey)).

---

## 1. What the mod reacts to today

### 1.1 Hooked signal sources (C# — fixed; not extendable without code)

| Hook | Domain | What it captures |
|---|---|---|
| `PlayLog.Add` | Interaction | every social interaction (pair, batch, ambient) |
| `MemoryThoughtHandler.TryGainMemory` | Thought | memory thoughts |
| periodic thought-stage scan | Thought | escalating need/drug-desire stages |
| periodic progression scan | Progression | skill milestones, psylink, xenotype, royal title |
| `InspirationHandler.TryStartInspiration` | Inspiration | inspirations |
| `Ability.Activate` | Ability | sampled ability uses |
| `Pawn_RelationsTracker.AddDirectRelation` | Romance | lover/spouse/ex transitions |
| `MentalStateHandler.TryStartMentalState` | MentalState | every mental break / social fight |
| `TaleRecorder.RecordTale` | Tale | every recorded tale |
| `Pawn.Kill` fallback | Tale | death page when no death tale exists |
| `Pawn_HealthTracker.AddHediff` + severity scan | Hediff | health conditions (immediate or day-reflection) |
| periodic current-job scan | Work | sampled work pages |
| `IncidentWorker.TryExecute` | Raid | **raid-like incidents only** (`IncidentWorker_Raid` subclasses + infestation workers) fan out per colonist |
| `IncidentWorker.TryExecute` | (event windows) | **every successful incident** is offered to `DiaryEventWindowDef` matching |
| `GameConditionManager.RegisterCondition` | MoodEvent | **every registered game condition** fans out per colonist |
| `Quest.Accept` / `Quest.End` | Quest | accepted (bookkeeping only) / completed / failed |
| ritual + psychic-ritual completion | Ritual | role fan-out pages |
| starting scan + `Pawn.SetFaction` | Arrival | neutral arrival pages (covers wanderer joins, recruits, pod-crash rescues that join) |
| sleep/rest day flush | Reflection | day / quadrum / arc reflections |
| `PawnDiaryApi.SubmitEvent` | External | other mods |

Because the MoodEvent and event-window hooks are *universal* (every condition, every incident),
**most "skipped" vanilla events are actually captured but land in a generic catch-all group** with
a bland instruction ("a colony-wide event affecting the pawn's mood", "a raid approaching"). The
cheapest atmosphere win is therefore: give the interesting ones their own XML group with a
tailored instruction/tone — this changes *wording quality only*, never page volume.

### 1.2 XML-only extension points (the machinery we can extend without code)

| Def type | What a new row can do |
|---|---|
| `DiaryInteractionGroupDef` | claim defNames (exact/prefix/suffix/segment/token strings) in any existing domain and give them their own instruction, tone pool, importance, combat flag, color cue, batching, hediff routing |
| `DiaryEventWindowDef` | record a one-shot page (and/or bias prompts) when a signal fires: `Incident` (any incident defName), `Letter`, `ProximityLetter`, `ThingSpawned`, `Hediff` added, `PawnAge` birthday, `Quest`, `VoidMonolith`, `PrisonBreak` |
| `DiaryObservedConditionDef` | while live state persists, bias prompt tone (and optionally record start/end pages): `MapDanger`, `GameCondition`, `ThingPresent`, `PawnHediff`, `MapHiddenHediff` observers |
| `DiaryPromptEnchantmentDef` | inject "important context" lines into prompts from live hediffs / capacities / royal title / ideoligion role, weighted and chance-gated |
| `DiaryHediffPersonaOverrideDef` | force a writing style while a hediff is present |
| `DiaryEventPromptDef` | per-key event prompt / enhancement / forced model rows |

All matchers are plain strings, so DLC/modded defNames sit harmlessly inert when the content is
absent — the established DLC-safety pattern (`AGENTS.md`). An unmatched string is free; when a
defName is uncertain we can list every plausible spelling.

### 1.3 Current named coverage (what already has tailored flavor)

- **Interactions**: romance, recruitment/prison, slavery, conversion, trials, strange chat,
  anomaly dialogue, insults/fights, rituals/speeches, animal handling, heartfelt talk, teaching,
  small talk + catch-all.
- **Mental states**: social fights, insult sprees + generic catch-all for everything else.
- **Tales**: combat/death, health/medicine, life milestones, masterworks, work achievements,
  anomaly horror, raids/disasters, quiet moments + catch-all.
- **Mood events (game conditions)**: Aurora, Party, PsychicSoothe (positive); Eclipse,
  PsychicDrone, GrayPall, DeathPall, ToxicFallout (negative); PsychicSuppressor, UnnaturalDarkness
  (mixed) + catch-all.
- **Hediffs**: pregnancy, labor, anomaly compulsions + major-health day-reflection catch-all.
- **Raids**: friendly, drop-pod, infestation + important catch-all.
- **Quests**: completed, failed (accepted = bookkeeping only, by design).
- **Rituals**: royal, childbirth, gravship launch, psychic + catch-all.
- **Abilities**: psycasts, hostile + catch-all.
- **Progression**: skill passion, psylink, xenotype, royal title + catch-all.
- **Event windows**: void monolith discovery/activation, birthday, heart attack, prison break,
  ancient danger (all one-shot pages, no lasting prompt bias).
- **Observed conditions**: map threat, toxic fallout, solar flare (tone only); gray-flesh
  evidence (page + tone), metalhorror emergence/infection (tone only).
- **Prompt enchantments**: ~38 rows — drugs/highs/withdrawals, blood loss, infections/fevers,
  consciousness tiers, blindness, memory decay, pregnancy, Biotech hemogen/psychic bond,
  Anomaly compulsions/void states, royal title, ideoligion role.
- **Persona overrides**: trauma savant, inhumanized, crumbled mind, bliss lobotomy, mindscrew,
  joywire.

---

## 2. What RimWorld has that we skip (or only catch generically)

"Skip" below means: no tailored group/window/condition — the moment either produces a page with a
generic catch-all instruction, only surfaces indirectly (e.g. via a thought), or produces nothing.

### 2.1 Base game

| Event | Today | Notes |
|---|---|---|
| Cold snap / heat wave | generic mood-event page | multi-day hardship with strong diary potential (cold rooms, rationed showers, crops dying) |
| Flashstorm | generic mood-event page | fires, panic, dramatic sky |
| Volcanic winter | generic mood-event page | long, oppressive dimness |
| Crop blight | nothing (no condition, not a raid; tale-less) | colony-food dread; incident `CropBlight` |
| Alphabeavers | nothing | tree-eating swarm; incident `Alphabeavers` |
| Manhunter pack | raid catch-all ("raiders approaching" tone is wrong) + `ManhunterPack` tale | needs its own animal-fury flavor |
| Mech cluster (also Royalty-flavored) | nothing (not raid-like worker) | the XML comment in `DiaryEventWindowDefs.xml` even uses it as the canonical example |
| Ship chunk / cargo pod / resource pod drops | nothing | low stakes — deliberately skip |
| Thrumbo passes | nothing | rare, awe-inspiring visitor; incident `ThrumboPasses` |
| Self-tame, farm animals wander in, herd migration | nothing (self-tame produces a bond tale sometimes) | small warm moments; self-tame is the strongest |
| Ambrosia sprout | nothing | small temptation moment |
| Wanderer joins / transport pod crash survivor | arrival page already covers the join | covered — skip |
| Trader/visitor caravans, orbital traders | nothing | frequent; would spam — deliberately skip |
| Short circuit | nothing | minor and technical — deliberately skip |
| Disease outbreaks (plague, malaria, flu…) | hediff day-reflection + FeverishBody enchantment | covered; `SleepingSickness` missing from the enchantment matcher |
| Weather itself (thunderstorm, fog, blizzard…) | nothing | **needs code** (no weather observer) — out of scope |
| Caravan/world-map events | nothing (raid fan-out requires a home map) | **needs code** — out of scope |

### 2.2 Royalty

| Event | Today | Notes |
|---|---|---|
| Mech cluster | nothing | see above |
| Problem causers (psychic drone ship parts etc.) | drone/soothe conditions covered; `ProblemCauser` deliberately skipped (TODO comment in XML) | keep skipped |
| Bestowing ceremony, decrees, honors | thoughts + progression royal title + ritual groups | covered |
| Anima tree linking | ritual group | covered |

### 2.3 Ideology

Rituals, conversions, trials, terror, relics: all covered via ritual/interaction/thought groups.
No meaningful gap found. (Archonexus endgame letters could get an event window later; rare enough
to defer.)

### 2.4 Biotech

| Event | Today | Notes |
|---|---|---|
| Pregnancy/birth/children | pregnancy + labor hediff groups, thoughts, teaching | covered |
| Sanguophage attack / mech raids | raid catch-all | acceptable (they are raids) |
| Deathrest | nothing | distinctive vampire-sleep state; hediffs `Deathrest`, `DeathrestExhaustion` |
| Pollution / wastepacks | nothing directly; `ToxicBuildup` enchantment exists | `LungRot` hediff missing from enchantments |
| Mechanitor (mechlink, band nodes) | psylink-style progression not fired for mechlink | needs code — skip |

### 2.5 Anomaly

| Event | Today | Notes |
|---|---|---|
| Sightstealer / shambler / fleshbeast / gorehulk / devourer / chimera assaults | raid catch-all with **"raiders grabbing weapons" tone — wrong register for horror entities** | biggest tone bug found by this analysis; fix is pure XML (Raid-domain groups) |
| Blood rain | generic mood-event page; `BloodRage` hediff unhandled | strong horror weather |
| Pit gate / fleshmass heart | raid catch-all at eruption (infestation-like workers) but **no lasting dread while present on the map** | `ThingPresent` observed conditions fit exactly (proven by GrayFlesh/Metalhorror rows) |
| Obelisks (warped/twisted/corrupted) | nothing | lasting uncanny presence |
| Golden cube | `CubeInterest`/`CubeWithdrawal`/`CubeRage` hediffs covered | the cube *thing itself* on the map could add tone; hediff coverage may be enough |
| Unnatural corpse | nothing | slow-burn personal horror |
| Harbinger trees | nothing | flesh-eating trees; quiet dread |
| Nociosphere | nothing | ticking catastrophe on the map |
| Revenant | `RevenantHypnosis` hediff covered | acceptable |
| Void curiosity / monolith chain | monolith windows covered | covered |
| Death pall / unnatural darkness / gray pall / noxious haze | mood-event groups + anomaly tale group | covered for tone; death pall could also be an observed condition while it lasts |

### 2.6 Odyssey (1.6)

| Event | Today | Notes |
|---|---|---|
| Gravship launch/landing | ritual group `ritualGravship` | covered |
| Orbital debris | `OrbitalDebris` tale matcher in `taleincident` | covered |
| Lava flow | nothing | days-long visible danger |
| Volcanic ash | generic mood-event page (if implemented as a game condition — verify) | oppressive sky, burning air |
| Flooding | generic mood-event page (verify defName/mechanism) | rising water |
| Gill rot | nothing | economic fishing event — deliberately skip |
| Vacuum exposure | `VacuumExposureRevealed` tale covered | a vacuum-exposure hediff enchantment is missing |
| Mechhive / orbital threats | raid catch-all where raid-like | acceptable |

---

## 3. Suggestions

Ordered by (atmosphere gained) / (player-facing volume added). Tier 1 adds **zero** new pages.

### Tier 1 — retone what we already write (no new pages, highest priority)

**1a. Anomaly assault flavor — new Raid-domain groups.**
Anomaly entity assaults currently read like human raids ("checking a weapon, sandbags, first
sight of the enemy"). Add Raid-domain groups ordered before the `raid` catch-all, matched by
robust tokens so exact incident defNames don't matter:

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>raidAnomalyEntities</defName>
  <label>Entity attacks</label>
  <order>708</order>
  <domain>Raid</domain>
  <defaultEnabled>true</defaultEnabled>
  <important>true</important>
  <colorCue>extremeDark</colorCue>
  <instruction>inhuman things attacking the colony—write dread, wrongness, and survival, not a
    normal battle; never explain what the creatures are</instruction>
  <tones>
    <li>horrified and sensory, the attackers not registering as people</li>
    <li>tight and survival-focused, wrongness kept at the edge of the page</li>
  </tones>
  <matchTokens>
    <li>Sightstealer</li><li>Shambler</li><li>Fleshbeast</li><li>Gorehulk</li>
    <li>Devourer</li><li>Chimera</li><li>Noctol</li><li>Metalhorror</li>
  </matchTokens>
</PawnDiary.DiaryInteractionGroupDef>
```

Optionally a second group for shamblers alone (slow, rotting, almost pitiable) if one register
feels too flat. Cost: one Def + DefInjected strings. Volume: unchanged (these pages already
exist).

**1b. Weather-hardship flavor — new MoodEvent-domain groups.**
`ColdSnap`, `HeatWave`, `Flashstorm`, `VolcanicWinter` (+ Odyssey `VolcanicAsh`, `Flooding`;
Anomaly `BloodRain`) currently fall to `moodeventOther`. Add two groups:

- `moodeventWeatherHardship` (ColdSnap, HeatWave, VolcanicWinter, VolcanicAsh, Flooding) —
  "the climate itself turned hostile; write the body coping: cold fingers, sweat, gray sky,
  rationing," tone pool around endurance and smallness against weather.
- `moodeventStormDanger` (Flashstorm, BloodRain) — sudden sky-violence, `danger`/`extremeDark`
  color cues; blood rain instruction leans horror ("do not explain the rain").

Cost: two Defs + strings. Volume: unchanged.

**1c. Mental-break flavor — split the catch-all into three registers.**
All non-fight breaks share one instruction today. Three groups before the catch-all, no volume
change, big wording payoff:

- `mentalbreakViolent` — tokens/segments: Berserk, MurderousRage, Slaughterer, Tantrum,
  TargetedTantrum — outward destruction, adrenaline, shame afterwards.
- `mentalbreakEscape` — Wander_Sad, Wander_OwnRoom, GiveUpExit, RunWild, PanicFlee, Hide —
  inward collapse, flight, the world too loud.
- `mentalbreakIndulgent` — Binging_Food, Binging_DrugExtreme, Binging_DrugMajor,
  FireStartingSpree, CorpseObsession, Jailbreaker — compulsion with a guilty, hungry register.

**1d. Missing prompt enchantments (the "enchantments" ask).**
These decorate existing pages only. Suggested rows, following the existing weight/chance style:

| New def | Matches | Chance | Weight | Sev | Why |
|---|---|---:|---:|---:|---|
| `DiaryEnchant_Malnutrition` | `Malnutrition` | 0.75 | 1.3 | 1.4 | starvation colors everything; severity tiers like BloodLoss |
| `DiaryEnchant_HeatstrokeOrHypothermia` | `Heatstroke, Hypothermia` | 0.75 | 1.3 | 1.4 | pairs with 1b weather groups |
| `DiaryEnchant_AnestheticHaze` | `Anesthetic` | 0.6 | 1.0 | 1.1 | waking from surgery, cotton-headed |
| `DiaryEnchant_PsychicShock` | `PsychicShock` | 0.7 | 1.1 | 1.2 | |
| `DiaryEnchant_Carcinoma` | `Carcinoma` | 0.7 | 1.2 | 1.3 | slow illness dread |
| `DiaryEnchant_Mechanites` | `FibrousMechanites, SensoryMechanites` | 0.65 | 1.1 | 1.2 | buzzing, wrong-feeling body |
| `DiaryEnchant_WakeUpHigh` | `WakeUpHigh` | 0.55 | 0.9 | 1.0 | fills the one gap in the drug set |
| `DiaryEnchant_CryptosleepSickness` | `CryptosleepSickness` | 0.6 | 1.0 | 1.1 | disorientation after the casket |
| `DiaryEnchant_AgingBody` | `Frail, BadBack, Cataract, HearingLoss` | 0.15 | 0.5 | 0.8 | low-chance elder texture; keep rare so it doesn't dominate old pawns |
| `DiaryEnchant_SleepingSickness` | add `SleepingSickness` to `DiaryEnchant_FeverishBody` matchers | — | — | — | omission fix, not a new row |
| `DiaryEnchant_Deathrest` (Biotech) | `Deathrest, DeathrestExhaustion` | 0.6 | 1.0 | 1.1 | inert without Biotech |
| `DiaryEnchant_LungRot` (Biotech) | `LungRot, LungRotExposure` | 0.7 | 1.1 | 1.2 | pollution consequence |
| `DiaryEnchant_BloodRage` (Anomaly) | `BloodRage` | 0.85 | 1.4 | 1.5 | blood-rain hediff; pairs with 1b |
| `DiaryEnchant_VacuumExposure` (Odyssey) | `VacuumExposure, VacuumBurn` | 0.8 | 1.3 | 1.4 | verify exact defNames |

**1e. Two persona overrides (fun, bounded).**

- `HediffPersonaOverride_Drunk` → new `DiaryPersona_DrunkRambling`, match `AlcoholHigh`,
  priority ~15 (below all current overrides). Loose, meandering, overly sincere entries while
  drunk. The severity tiers already on `DiaryEnchant_AlcoholHigh` keep light buzzes from
  triggering it if we gate via a higher `minSeverity` on the override.
- `HediffPersonaOverride_MemoryDecay` → `DiaryPersona_FadingMemory`, match
  `Dementia, Alzheimers`, priority ~30. Entries that repeat themselves, lose the thread, reach
  for names. The enchantment exists; the persona completes it.

### Tier 2 — lasting dread/tone via observed conditions (tone-only, no pages)

All follow the proven `GameCondition`/`ThingPresent` patterns; `recordStartEvent=false` unless
noted, so they only bias prompts while the state persists.

| New def | Observer | Matches | Weight / normal-mult | Notes |
|---|---|---|---|---|
| `ColdSnapActive` | GameCondition | `ColdSnap` | 4 / 1 | cues: frozen crops, huddling indoors |
| `HeatWaveActive` | GameCondition | `HeatWave` | 4 / 1 | cues: heat, tempers, coolers straining |
| `VolcanicWinterActive` | GameCondition | `VolcanicWinter, VolcanicAsh` | 3 / 1 | long decay window (it lasts quadrums) |
| `BloodRainActive` (Anomaly) | GameCondition | `BloodRain` | 20 / 0.5 | `extremeDark`; short decay |
| `DeathPallActive` (Anomaly) | GameCondition | `DeathPall` | 20 / 0.5 | the dead walking outside |
| `UnnaturalDarknessActive` (Anomaly) | GameCondition | `UnnaturalDarkness` | 25 / 0.3 | strongest vanilla-horror override |
| `PitGatePresence` (Anomaly) | ThingPresent | `PitGate` | 30 / 0.3 | hole in the world; optional start page (`recordStartEvent=true`, MapColonists) since eruption is loud anyway |
| `FleshmassHeartPresence` (Anomaly) | ThingPresent | `FleshmassHeart` | 30 / 0.3 | growing flesh; optional start page |
| `ObeliskPresence` (Anomaly) | ThingPresent | list all candidate spellings: `WarpedObelisk, TwistedObelisk, CorruptedObelisk, AbductorObelisk, MutatorObelisk, DuplicatorObelisk` | 15 / 0.7 | unmatched spellings are inert, so listing candidates is safe; verify in dev mode |
| `HarbingerTreePresence` (Anomaly) | ThingPresent | `HarbingerTree` | 8 / 1 | quiet dread, low weight — trees can stay for a long time |
| `NociospherePresence` (Anomaly) | ThingPresent | `Nociosphere` | 25 / 0.5 | |
| `UnnaturalCorpsePresence` (Anomaly) | ThingPresent | `UnnaturalCorpse` | 20 / 0.7 | verify defName |

Guardrails: weights follow the existing ladder (ordinary weather ≈ SolarFlare's 4; horror set
pieces between GrayFlesh 40 and MapThreat 6); every def is a plain-string matcher (DLC-safe);
long-lived presences get `promptDecayTicks` so the dread fades into background instead of
repeating at full strength for quadrums.

### Tier 3 — a few one-shot pages for rare colony-shaking moments (bounded volume)

Event windows (`source=Incident`, `signal=executed`, one-shot, deduped) for incidents that are
rare, loud, and currently invisible to the diary:

| Window | matchDefNames | Scope | Register |
|---|---|---|---|
| `MechClusterArrival` | `MechCluster` | Map | cold machine dread on the horizon (the XML comment's own example) |
| `CropBlight` | `CropBlight` | SubjectPawn | quiet fear about winter and food |
| `Alphabeavers` | `Alphabeavers` | SubjectPawn | absurd-but-real threat to the treeline; allows light humor |
| `ThrumboPasses` | `ThrumboPasses` | SubjectPawn | awe; a legend walking past the fence |
| `SelfTame` | `SelfTame` | SubjectPawn | a small grace: something chose us |
| `AmbrosiaSprout` | `AmbrosiaSprout` | SubjectPawn | temptation with a wink |
| `LavaFlow` (Odyssey) | `LavaFlow` | Map | the ground itself moving in; verify defName |

That is ~7 windows, each firing at most once per incident occurrence with `dedupTicks`, mostly
`SubjectPawn` scope (a single pawn's page, not a colony fan-out) to keep volume low. Skip trader
arrivals, visitors, ship chunks, short circuits, and resource pods on purpose: frequent + low
emotional stakes = diary spam.

### Deliberately NOT suggested (would need C# — out of scope per request)

- Weather (rain, thunderstorm, fog, blizzard): no weather observer exists.
- Caravan / world-map events: raid fan-out and observed conditions require a home map.
- Trade deals, visitor-group social texture, prisoner recruitment arcs beyond current hooks.
- Mechanitor progression (mechlink) as a Progression signal.
- New per-event humor cues wired to specific groups (humor selection is stakes-based, not
  group-based, in C#).
- `questAccepted` pages: disabled by pure policy in code, not just XML — leave as designed.

---

## 4. Volume & atmosphere guardrails

- Tier 1 changes **zero** page counts — they only sharpen wording on pages already written.
- Tier 2 adds tone bias only (plus at most two optional Anomaly start pages tied to events that
  are already colony-defining); the enchantment planner still picks **one** context line per
  prompt, so more candidates ≠ longer prompts.
- Tier 3 adds at most one page per rare incident, single-pawn scope where possible.
- Anything frequent (traders, visitors, chunks, small drops) stays uncovered on purpose.
- All new defs are toggleable per group in mod settings like existing ones (`defaultEnabled`
  fields), so players can dial any of it off.

## 5. Verification checklist before implementation

1. Confirm exact defNames in dev mode (Debug Actions → incident/condition lists), especially:
   Odyssey `VolcanicAsh` / `Flooding` / `LavaFlow` / vacuum hediffs; Anomaly obelisk ThingDefs,
   `UnnaturalCorpse`, assault incident defNames (tokens above are chosen to survive naming
   differences, but verify anyway).
2. Confirm `BloodRain` registers through `GameConditionManager.RegisterCondition` (expected —
   it is condition-based weather).
3. XML-parse every touched Def file; run the pure test projects that cover XML-driven policy
   (`DiaryObservedConditionTests`, `DiaryPipelineTests`, `PromptVariantsTests`).
4. New DefInjected strings for both shipped languages (`Languages/English`, `Languages/Russian`);
   new Keyed strings for observed-condition prompt keys and event-window start texts
   (`DOCUMENTATION.md §12` rules).
5. Update `EVENT_PROMPT_MAP.md` tables, `DOCUMENTATION.md`, and `CHANGELOG.md` in the same
   change (AGENTS.md working rule 1).
6. DLC-safety bar: every new matcher is a plain string; no def references, no `MayRequire`
   needed; a no-DLC game must load with zero errors.

## 6. Suggested implementation order

1. **PR A (Tier 1a–1c)**: retone groups (anomaly raids, weather moods, mental-break split).
   Pure wording quality; easiest review.
2. **PR B (Tier 1d–1e)**: enchantments + two personas.
3. **PR C (Tier 2)**: observed conditions (weather tone first, then Anomaly presences).
4. **PR D (Tier 3)**: event windows, ideally after a playtest of A–C confirms volume feels right.
