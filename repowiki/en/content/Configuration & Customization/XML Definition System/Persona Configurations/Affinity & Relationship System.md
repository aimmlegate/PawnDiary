# Affinity & Relationship System

The current catalog has two distinct personality layers:

- a **writing style** (`DiaryPersonaDef`) controls how a pawn writes;
- a **psychotype** (`DiaryPsychotypeDef`) describes the pawn's internal profile and contributes
  structured voice guidance.

They are selected independently. Social relationships can be captured into prompt context, but
there is no generic XML table that assigns writing styles from lover/friend/rival relations.

## Writing-style affinity

[DiaryPersonaDef](../Generated%20Def%20Reference/DiaryPersonaDef.md) has three Pawn Diary fields:

- `rule` — the prose rule injected into the model-facing voice block;
- `themes` — internal affinity tags used only to bias the initial style roll;
- `lifeStage` — the child/adult catalog band.

The loaded XML catalog is merged with settings-backed edits and custom rows. If no Defs load,
`DiaryPersonas` exposes a hardcoded safe fallback. The legacy code/schema name “persona” remains for
save compatibility; the player-facing feature is writing styles.

The selector derives theme weights from pawn traits and backstory, then chooses from the eligible
life-stage band. XML owns the catalog and theme tags; the selection adapter owns live pawn reads.

## Psychotype trait affinity

[DiaryPsychotypeTraitPolicyDef](../Generated%20Def%20Reference/DiaryPsychotypeTraitPolicyDef.md)
maps supported RimWorld trait `defName`/degree pairs to canonical keys. Each rule can add:

- a family bonus during the first stage of the psychotype roll;
- a member bonus during the second stage;
- eligibility for a psychotype whose `requiredTraitKey` matches.

The Def is copied into plain policy DTOs before the pure roll runs. Unknown traits contribute
nothing. A trait-gated candidate is ineligible unless the canonical key is present; XML also owns
the gated-candidate takeover chance.

[DiaryPsychotypeDef](../Generated%20Def%20Reference/DiaryPsychotypeDef.md) contains the shipped
profiles, while
[DiaryPsychotypeRollPolicyDef](../Generated%20Def%20Reference/DiaryPsychotypeRollPolicyDef.md)
contains the broader roll weights and thresholds.

## External personality systems

External integrations should use the public psychotype-generator/adapter contracts rather than
mutating Defs during a roll. Integrations snapshot their result into Pawn Diary's plain contracts,
and must degrade cleanly when the other mod is absent.

Relevant implementation:

- [DiaryPersonaDef.cs](../../../../../../Source/Defs/DiaryPersonaDef.cs)
- [DiaryPsychotypeTraitPolicyDef.cs](../../../../../../Source/Defs/DiaryPsychotypeTraitPolicyDef.cs)
- [PsychotypeTraitAffinities.cs](../../../../../../Source/Pipeline/PsychotypeTraitAffinities.cs)
- [PsychotypeRollPolicy.cs](../../../../../../Source/Pipeline/PsychotypeRollPolicy.cs)
- [ExternalPsychotypeGenerators.cs](../../../../../../Source/Integration/ExternalPsychotypeGenerators.cs)

[Back to Persona Configurations](Persona%20Configurations.md) ·
[Generated Def Reference](../Generated%20Def%20Reference/Generated%20Def%20Reference.md)
