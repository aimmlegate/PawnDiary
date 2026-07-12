# Implementation plan — Vanilla Traits Expanded compat (pure XML, core mod)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #6 in
> [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Pure-XML pack in the core mod: **no C#,
> no adapter, no API change.** The research doc floated a context-provider adapter for VTE's
> traits; this plan replaces that with something better and cheaper: **XML-patching the
> psychotype trait-affinity table**, which feeds VTE traits into the outlook system Pawn Diary
> already has — plus the thought/mental-break matchers.

## Target mod facts (verified from source 2026-07-12 — re-verify against installed 1.6 Defs)

- **Vanilla Traits Expanded**, packageId `VanillaExpanded.VanillaTraitsExpanded`, Workshop
  2296404655, source `github.com/Vanilla-Expanded/VanillaTraitsExpanded`. 1.4/1.5/1.6;
  maintained (2026-04-12). One stale search snippet claimed the Workshop item was removed —
  almost certainly erroneous, but do the two-minute Steam check before investing.
- 53 TraitDefs, all `VTE_`-prefixed: `VTE_Kleptomaniac`, `VTE_MadSurgeon`, `VTE_Vengeful`,
  `VTE_Wanderlust`, `VTE_Technophobe`, `VTE_DrunkenMaster`, `VTE_ChildOfSea`,
  `VTE_ChildOfMountain`, `VTE_AnimalHater`, `VTE_AnimalLover`, `VTE_Perfectionist`,
  `VTE_Rebel`, `VTE_Submissive`, `VTE_Insomniac`, `VTE_Coward`, `VTE_Brave`, `VTE_Stoner`,
  `VTE_Workaholic`, `VTE_WorldWeary`, … (dump the full 53 from the installed XML).
- 27 ThoughtDefs (trait-conditional): event-like memories (`VTE_HarvestedOrgans`,
  `VTE_MyRivalsAreAlive`, `VTE_CreatedLowQualityItem`, `VTE_CouldNotFinishItem`,
  `VTE_BondedAnimalDiedHater`, `VTE_MechanoidIsKilled`, `VTE_ObservedManyBlood`,
  `VTE_SoakingWetChildOfTheSea`, `VTE_HaventExitedColonyForLongTime`) and ambient situationals
  (`VTE_AnimalsInColony`, `VTE_EnvironmentDark`, …) — the split matters below.
- 3 MentalStateDefs (+3 MentalBreakDefs): `VTE_Kleptomaniac` (stealing spree),
  `VTE_TechnophobeTantrum`, `VTE_PanicFreezing`.
- 4 HediffDefs (minor), 1 JobDef. **No InteractionDefs, no TaleDefs.**

## Ground truth to read first

1. `1.6/Defs/DiaryPsychotypeTraitPolicyDefs.xml` — the single
   `PawnDiary.DiaryPsychotypeTraitPolicyDef` (`Diary_PsychotypeTraitPolicy`) whose `rules` list
   maps trait defNames → psychotype family/member bonuses. **This is the extension point**:
   vanilla traits like Psychopath/Kind/TooSmart already steer psychotype rolls; VTE traits can
   too, purely via a PatchOperation.
2. `Source/Generation/PsychotypeRolls.cs` — how rules are consumed (matchDegree semantics,
   `gatedTakeoverChance`), and confirm unknown trait defNames in rules are simply never hit
   (they are — rules match against the pawn's actual traits — so VTE-less players are safe
   even without a gate; add the gate anyway, see below).
3. `1.6/Defs/DiaryPsychotypeDefs.xml` — the family names (`intense`, `anxious`, `inward`,
   `grounded`, …) and member defNames the bonuses may target. Use only existing targets.
4. `1.6/Defs/DiaryInteractionGroupDefs.xml` — MentalState groups 200–210 (catch-all
   `mentalbreak` 210); Thought groups 490–520; the `mentalbreakViolent`/`mentalbreakEscape`
   register for wording.
5. `integrations/PawnDiary.Vsie/1.6/Patches/AddDiscordToInsults.xml` — the
   `PatchOperationFindMod`-gated patch idiom this plan reuses.

## Deliverables (core mod)

```
1.6/Defs/Compat/DiaryCompat_VTE.xml            thought + mental-state groups
1.6/Patches/VtePsychotypeAffinities.xml        PatchOperationFindMod-gated rules additions
Languages/Russian (Русский)/DefInjected/…      RU for the new groups
```

## Piece 1 — psychotype affinities for VTE traits (`1.6/Patches/VtePsychotypeAffinities.xml`)

A `PatchOperationFindMod` (match "Vanilla Traits Expanded" — verify the exact `<name>` the
mod declares; FindMod matches names, not packageIds) wrapping one `PatchOperationAdd` that
appends rules to `Defs/PawnDiary.DiaryPsychotypeTraitPolicyDef[defName="Diary_PsychotypeTraitPolicy"]/rules`.

Author rules **only for traits with a clear outlook pull** — target 15–20 of the 53, not all.
Seed table (agent: review each against the trait's actual description text, adjust
families/members to what exists in `DiaryPsychotypeDefs.xml`, keep bonuses in the 2–6 range
the vanilla rules use):

| traitDefName | familyBonuses | memberBonuses (examples — validate defNames) |
|---|---|---|
| `VTE_Vengeful` | intense 4 | Resentful 6, Ruthless 2 |
| `VTE_Kleptomaniac` | intense 2 | Ruthless 2, Wry 2 |
| `VTE_MadSurgeon` | intense 4 | Hollow 4, Detached 2 |
| `VTE_Perfectionist` | anxious 4 | Dutiful 4 |
| `VTE_Coward` | anxious 4 | (fear-flavored member if present) |
| `VTE_Brave` | grounded 2 | (steady member) |
| `VTE_Wanderlust` | inward 2 | Restless/Nostalgic-type members |
| `VTE_Insomniac` | anxious 2 | Detached 2 |
| `VTE_WorldWeary` | inward 4 | Nostalgic 4, Wry 2 |
| `VTE_Rebel` | intense 2 | Ambitious/defiant member |
| `VTE_Submissive` | grounded 2 | Dutiful 4 |
| `VTE_AnimalLover` / `VTE_AnimalHater` | grounded 2 / intense 2 | fitting members |
| `VTE_Technophobe` | anxious 2 | fitting member |
| `VTE_Stoner` | inward 2 | Content 4 |
| `VTE_Workaholic` | grounded 2 | Dutiful 6 |

This gives VTE players trait-appropriate diary *outlooks* with zero C# and zero prompt-space
cost — strictly better than the context-line idea for static traits. Skip traits that are
purely mechanical (`VTE_DrunkenMaster`, biome-childhood traits unless a fitting member
exists). Header comment lists skipped traits as considered-skipped.

## Piece 2 — groups (`DiaryCompat_VTE.xml`)

Both gated `enableWhenPackageIdsLoaded: VanillaExpanded.VanillaTraitsExpanded`.

| Group | Domain | Order | Matchers | Shape / instruction intent |
|---|---|---|---|---|
| `vte_thoughts` | Thought | 489 (last compat slot before core 490 — verify free) | `matchDefNames`: the **event-like** memories only: `VTE_HarvestedOrgans`, `VTE_MyRivalsAreAlive`, `VTE_CreatedLowQualityItem`, `VTE_CouldNotFinishItem`, `VTE_BondedAnimalDiedHater`, `VTE_MechanoidIsKilled`, `VTE_ObservedManyBlood`, `VTE_SoakingWetChildOfTheSea`, `VTE_HaventExitedColonyForLongTime` (+ any other memory-class thought found in the dump). **Deliberately NOT matchPackageIds** — the ambient situationals (`VTE_AnimalsInColony`, `VTE_EnvironmentDark`, …) must fall through to core handling untouched, and an explicit allowlist is the guardrail. | Theming only, no batch. Instruction: a trait pressing on the day — the pawn's particular wiring turning an ordinary event personal (the perfectionist's ruined piece, the vengeful pawn's living rival, cabin fever). Keep it need-of-the-trait-flavored without naming trait mechanics. |
| `vte_mentalbreaks` | MentalState | 198 (before core `socialfight` 200) | `matchDefNames: VTE_Kleptomaniac, VTE_TechnophobeTantrum, VTE_PanicFreezing` — confirm the MentalState domain matches the **MentalStateDef** defNames (not MentalBreakDef) in `Source/Ingestion/Sources/MentalStateSignal.cs`, and dump the actual state defNames if they differ from the break defNames | Not batched; rare, high drama. Instruction: losing the thread in a very personal way — a spree of pockets filled, machines smashed, or freezing when it mattered; write from inside the compulsion, shame optional, judgment never. Register: match `mentalbreakIndulgent`/`mentalbreakViolent` (read both first). |

## Localization

English inline; Russian DefInjected for both groups (label/instruction/instructions/tone/
tones), core RU register. The affinity patch has no player-facing strings.

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. With VTE: create a pawn with `VTE_Vengeful` → new-pawn psychotype roll shows the affinity
   pull (use the dev psychotype tools / re-roll and inspect; exact assertion per how the
   Personalities smoketest checks rolls).
2. Trigger `VTE_MyRivalsAreAlive` (rival alive) → writes via `vte_thoughts` with the themed
   voice once past the global mood threshold.
3. Dev-trigger the kleptomaniac break → entry via `vte_mentalbreaks`, not core catch-all.
4. Ambient check: `VTE_AnimalsInColony` present all day → no VTE-voiced entry (falls to core
   policy) — this is the allowlist working.
5. Without VTE: groups inert; **the patch file must no-op cleanly** (PatchOperationFindMod
   succeeds as "not found" without error), no rules added — inspect the policy def in-game.
6. Save/load with affinities applied: psychotypes persist; removing VTE later leaves rolled
   psychotypes valid (they're core defs — nothing dangles).

## Risks / open questions

- **PatchOperationFindMod name-matching**: brittle if VTE renames its About name (rare for VE
  team). Alternative if it bites: gate by loading the rules through a separate
  `DiaryPsychotypeTraitPolicyDef`-shaped mechanism — but that would extend core code; prefer
  living with FindMod.
- `matchDegree` semantics: VTE traits are mostly non-spectrum; if any allowlisted trait is a
  spectrum trait, add one rule per degree per the policy-def comment.
- The memory/situational thought split is from source reading; re-classify each thought from
  the installed XML (`thoughtClass`, durationDays) — anything permanent-while-condition-holds
  is situational and stays off the allowlist.
- MentalStateDef defNames sharing the trait's name (`VTE_Kleptomaniac` both trait and state)
  is fine — domains scope matchers, no cross-match (see `GroupDomain` doc comment).
