# Implementation plan — Vanilla Events Expanded compat (pure XML, core mod)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #2 in
> [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Pure-XML pack: **no C#, no adapter, no
> API change.** Ships inside the core mod like `1.6/Defs/Compat/DiaryCompat_RimTalk.xml` — the
> `enableWhenPackageIdsLoaded` doc comment in `Source/Defs/InteractionGroups.cs` explicitly
> blesses core-side compat groups that sit fully inert without the target mod.

## Target mod facts (verified from source 2026-07-12 — re-verify defNames against the installed 1.6 Defs before shipping)

- **Vanilla Events Expanded**, packageId `VanillaExpanded.VEE`, Workshop 1938420742, source
  `github.com/Vanilla-Expanded/VanillaEventsExpanded`. 1.4/1.5/1.6; active (v2.01, 2026-07-11).
  Has a `1.6/Mods/NoOdyssey` conditional folder — def availability can differ with/without the
  Odyssey DLC; matchers are plain strings so both configurations are safe.
- GameConditionDefs (15): `VEE_Drought`, `VEE_LongNight`, `VEE_Scorch`, `VEE_Whiteout`,
  `VEE_PsychicBloom`, `VEE_PsychicHum`, `VEE_PsychicOverdrive`, `VEE_PsychicStimulation`,
  `PsychicRain`, `SpaceBattle` (note: two are **unprefixed**).
- IncidentDefs (30) incl. purple raid variants `RaidEnemyPurple`, `ManhunterPackPurple`,
  `InfestationPurple`, `AnimalInsanityMassPurple`; one-shot events `VEE_Earthquake`,
  `VEE_MeteoriteShower`, `VEE_ShuttleCrash`, `VEE_SpaceBattle`, `VEE_HuntingParty`,
  `VEE_WandererJoinsTraitor`, `VEE_VisitorGroupRaid`, `VEE_WildMenWanderIn`,
  `VEE_WhiteoutRefugees`.
- HediffDefs (8): `VEE_SunSickness`, `VEE_Aurora`, `VEE_LongNight`, `VEE_PsychicHumHediff`,
  `VEE_PsychicOverdriveHediff`, `VEE_PsychicRelaxationHediff`, `VEE_BloomPsychicSensitivity`,
  `VEE_SuperbloomPsychicDrone`.
- ThoughtDefs (2, **unprefixed**): `MightJoin`, `Traitor` (the traitor-wanderer arc).

## Ground truth to read first

1. `1.6/Defs/DiaryInteractionGroupDefs.xml` — MoodEvent groups (orders 400–420, catch-all
   `moodeventOther` 420), Raid groups (705–710, catch-all `raid` 710), Hediff groups (660–700,
   catch-all `hediffMajorHealth` 700 with embedded `HediffSignalPolicy`), Thought groups
   (490–520).
2. `1.6/Defs/DiaryObservedConditionDefs.xml` — the `GameCondition` observer idiom
   (`ToxicFalloutActive` is the exact template) including the Keyed prompt-key set every
   condition needs.
3. `1.6/Defs/DiaryEventWindowDefs.xml` — the `source=Incident, signal=executed` idiom, and the
   paired display groups (`eventWindowPrisonBreak`/`eventWindowMechCluster` in the groups file,
   orders 137–144) that give window pages a proper label instead of the catch-all.
4. `Source/Patches/DiaryEventSignalPatches.cs` `IsRaidLikeIncident` — the Raid domain only
   fires for `IncidentWorker_Raid` subclasses or workers whose type name contains
   "Infestation". This decides which VEE incidents go to the Raid domain vs. event windows.
5. Shipped compat idiom: `integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml`.

## Deliverables (all in the core mod)

```
1.6/Defs/Compat/DiaryCompat_VEE.xml                    interaction-group matchers (Raid/Thought/Hediff)
1.6/Defs/Compat/DiaryObservedConditions_VEE.xml        tone tints for the lasting purple conditions
1.6/Defs/Compat/DiaryEventWindows_VEE.xml              one-shot incident windows + display groups
Languages/English/Keyed/…                              prompt keys for the observed conditions + window texts
Languages/Russian (Русский)/…                          DefInjected for new groups/windows, Keyed for prompt keys
```

Every def gated `enableWhenPackageIdsLoaded: VanillaExpanded.VEE` (interaction groups) or
plain-string matched (observed conditions / event windows are string matchers by design — they
simply never match without VEE, but still add the package gate where the def supports it to
keep settings lists clean).

## Piece 1 — tone tints for lasting conditions (`DiaryObservedConditions_VEE.xml`)

Clone `ToxicFalloutActive` per condition (`observerType=GameCondition`, `scope=Map`,
`includeHomeMapsOnly=true`, `recordStartEvent=false`, `recordEndEvent=false`,
`promptEnabled=true`). One def per condition below — these persist 15–120 days and should
color every entry written under them, which is exactly this def's purpose:

| defName | matchDefNames | promptWeight | Cue intent |
|---|---|---|---|
| `VeeDroughtActive` | `VEE_Drought` | 4 | dust, rationed water, brittle crops |
| `VeeLongNightActive` | `VEE_LongNight` | 5 | no dawn, lamps burning, time unmoored |
| `VeeScorchActive` | `VEE_Scorch` | 4 | punishing heat, work at night |
| `VeeWhiteoutActive` | `VEE_Whiteout` | 5 | walls of snow, buried world, cabin pressure |
| `VeePsychicBloomActive` | `VEE_PsychicBloom` | 3 | strange flowering, heads too light |
| `VeePsychicHumActive` | `VEE_PsychicHum`, `VEE_PsychicOverdrive`, `VEE_PsychicStimulation`, `PsychicRain` | 3 | a pressure in the mind everyone shares |

Each def needs its four Keyed prompt keys (`…Priority` / `…Condition` / `…Description` /
`…Cue.*`) in English + Russian, following the `MapThreatActive` key naming convention.
`promptDecayTicks`: use 120000 like `ToxicFalloutActive` (long states, slow decay).
Do not add page recording (`recordStartEvent`) — the game already letters these; the diary
gets the arrival via mood-event capture below and the *ongoing* mood via the tint.

Deliberately skipped: no observed condition for `SpaceBattle` (short, covered by the event
window) and none per-hediff (the psychic hediffs shadow their conditions; a hediff tint would
double-tint).

## Piece 2 — interaction-group matchers (`DiaryCompat_VEE.xml`)

| Group | Domain | Order | Matchers | Shape / instruction intent |
|---|---|---|---|---|
| `vee_raids` | Raid | 704 (before core `raidFriendly` 705) | `matchDefNames: RaidEnemyPurple, InfestationPurple, VEE_VisitorGroupRaid` | Not batched (Raid groups aren't). Instruction: a raid with something *wrong* about it — scale, timing, or betrayal (VisitorGroupRaid = guests turned hostile). Verify each def actually routes through `IsRaidLikeIncident` (RaidEnemyPurple/VEE_VisitorGroupRaid use raid workers; InfestationPurple matches the "Infestation" name rule). `ManhunterPackPurple`/`AnimalInsanityMassPurple` do **not** pass the raid filter — they are covered by Piece 3. |
| `vee_thoughts` | Thought | 484 | `matchDefNames: MightJoin, Traitor` — **defNames are unprefixed vanilla-ish words; do NOT use matchPackageIds here** (VEE's package also owns the condition thoughts we want left to the tint) — exact names only | Theming only, no batch. Instruction: the traitor-wanderer arc — hope about a stranger, then the sting of betrayal. |
| `vee_hediffs` | Hediff | 655 (before core 660) | `matchDefNames:` the 8 `VEE_*` hediffs | Embedded `HediffSignalPolicy`: `mode=DayReflection`, `visibleOnly=true`, `badOnly=false` (Aurora/Relaxation are positive), `recordOnAdd=true`, `dedupTicks=60000`, `dayReflectionWeight=0.6`. These are ambient colony-wide states — fold into the end-of-day reflection, never Immediate (a colony-wide psychic hediff would write N identical solo pages). |

## Piece 3 — one-shot incident windows (`DiaryEventWindows_VEE.xml`)

For dramatic instants with no Raid/condition signal. Clone the `source=Incident,
signal=executed` idiom; each window gets `recordStartEvent=true`, `recordScope` per row,
`timeoutTicks=-1`, `promptEnabled=false`, plus a **display group** (Interaction domain, orders
145–147, before the `other` catch-all at 150) matching the incident defName so pages get a
proper label — exactly the `eventWindowMechCluster` pattern.

| Window | matchDefNames | recordScope | Instruction intent |
|---|---|---|---|
| `VeeEarthquake` | `VEE_Earthquake` | SubjectPawn | the ground itself turning traitor; damage tallied after the shaking |
| `VeeMeteoriteShower` | `VEE_MeteoriteShower` | SubjectPawn | fire from the sky, awe and danger mixed |
| `VeeSpaceBattle` | `VEE_SpaceBattle`, `VEE_ShuttleCrash` | SubjectPawn | a war overhead that has nothing and everything to do with us |
| `VeeStampede` | `ManhunterPackPurple`, `AnimalInsanityMassPurple` | SubjectPawn | animals gone wrong at impossible scale |

`dedupTicks=2500`; verify `recordScope=SubjectPawn` picks one witness (match how existing
windows choose the subject) rather than fanning out to every colonist.
Deliberately skipped: `VEE_HuntingParty`, `VEE_WildMenWanderIn`, `VEE_WhiteoutRefugees`,
`VEE_Drought`-style condition *starter* incidents (the condition tint + game letters cover
them), and all quest/chain plumbing incidents. List these in the file's header comment as
"considered, skipped" so nobody re-adds them blind.

## Localization

English inline + Keyed; Russian DefInjected for every new group/window
(label/instruction/instructions/tone/tones) and Keyed for the observed-condition prompt keys.
Follow `Languages/Russian (Русский)/GLOSSARY.md` and the core RU register rules (see
`MOD_COMPAT_PLAN.md` localization notes).

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. With VEE: dev-trigger `VEE_Drought` → no page, but `PreviewPrompt` (API explorer) shows the
   tint cue while active, and it ends after the condition clears + debounce.
2. Dev-trigger `RaidEnemyPurple` → one raid entry classified to `vee_raids` (not core `raid`).
3. Dev-trigger `VEE_Earthquake` and `ManhunterPackPurple` → one window page each, labeled by
   the display groups, deduped on re-trigger inside the window.
4. Traitor arc: confirm `Traitor` thought writes with the themed voice.
5. Without VEE: file loads inert — no warnings, groups not listed as capturable, observed
   conditions never activate.
6. No-Odyssey configuration: repeat 1–3 (the NoOdyssey folder may alter which defs exist —
   matchers must silently skip missing ones).

## Risks / open questions

- **v2.0 remake churn**: VEE's purple system was reworked into event chains; the def list
  above was read from master post-2.0 but **must be re-diffed against the installed build**
  (especially incident defNames) right before shipping.
- The two unprefixed GameConditions (`PsychicRain`, `SpaceBattle`) and thoughts
  (`MightJoin`, `Traitor`) are collision-prone names — exact-match only, and re-check no other
  popular mod defines the same defNames with different meaning (accepted residual risk; the
  package gate on the groups limits blast radius to VEE-active mod lists).
- Colony-wide hediff fan-out volume: if DayReflection candidates flood with a large colony,
  raise the policy's `minSeverity` or drop `recordOnAdd` for the mild psychic hediffs.
