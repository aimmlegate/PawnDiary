# Mod compatibility patches — 1-2-3 Personalities, Psychology, Vanilla Social Interactions Expanded

> Status: planned 2026-07-10; not yet implemented. This is a working plan, not a contract —
> shipped behavior lands in `../DOCUMENTATION.md`, the public API contract in `../EXTERNAL_API.md`.
> Research facts below were gathered from the target mods' published source (GitHub) on the plan
> date; re-verify anything marked as a risk before coding against it.

Add integration or compatibility patches for three popular personality/social mods, preferring
**minimal XML-only patches** and, where a deeper layer is worth it, **minimal glue through the
public integration API** (`PawnDiary.Integration.PawnDiaryApi`). Nothing here introduces a new
mechanism — every piece reuses a compat tool that already shipped for RimTalk/SpeakUp.

## Existing toolkit (code ground truth; do not re-research)

| Mechanism | Form | What it does | Existing example |
|---|---|---|---|
| `DiaryInteractionGroupDef` | pure XML, `1.6/Defs/Compat/` | Routes another mod's InteractionDef / ThoughtDef / PawnRelationDef / HediffDef / MentalStateDef into a diary capture bucket (instruction, tone, batching, promotion), gated by `matchPackageIds` / `enableWhenPackageIdsLoaded` so it is inert without the target mod | [1.6/Defs/Compat/DiaryCompat_RimTalk.xml](../1.6/Defs/Compat/DiaryCompat_RimTalk.xml), `speakup_chitchat` group in [1.6/Defs/DiaryInteractionGroupDefs.xml](../1.6/Defs/DiaryInteractionGroupDefs.xml) |
| `DiaryObservedConditionDef` | pure XML | Tints prompt tone while a lasting state (e.g. a hediff) is observably active; string matchers, DLC/mod-safe | [1.6/Defs/DiaryObservedConditionDefs.xml](../1.6/Defs/DiaryObservedConditionDefs.xml) |
| `RegisterPawnContextProvider` | minimal C# glue (1 registration + 1 pure function) | Contributes one `key=value` line to every prompt's pawn summary, read-only | `PawnDiaryExampleApi.TraitContextLine`, RimTalk bridge `PersonaSync.ProvidePersonaLine` (Tier A) |
| `SetPsychotypeOverride` / `ResetPsychotypeOverride` / `GetPsychotype` | C# glue via `PawnDiaryApi` (ApiVersion 3) | Source-owned override of the pawn's diary outlook lens — the deepest personality integration point | RimTalk bridge [PersonaSync.cs](../integrations/PawnDiary.RimTalkBridge/Source/PersonaSync.cs) Tier B — the exact template to clone |
| Full adapter mod | separate mod under `integrations/`, own `About.xml`, may hard-reference the target assembly | Two-way / event-capture integration beyond XML + context provider | [integrations/PawnDiary.RimTalkBridge/](../integrations/PawnDiary.RimTalkBridge/) |

Standing rule (matches the RimTalkBridge isolation): the **core** mod (`Source/`) only ever uses
string matchers; a real assembly reference to a target mod is allowed only inside a dedicated
adapter under `integrations/`.

Relevant `GroupDomain` values already supported by the classifier: `Interaction`, `Thought`,
`Romance` (matches any `PawnRelationDef` defName, not only romantic ones), `Hediff`,
`MentalState`, `External` (claims adapter `eventKey`s).

## Research findings (verified 2026-07-10)

### 1-2-3 Personalities

- **M1** "1-2-3 Personalities M1", author Hahkethomemah, packageId
  `hahkethomemah.simplepersonalities`, Workshop 2527258500, supports 1.2–1.6, Harmony only.
  Enneagram-style system: 9 `SPM1.PersonalityRoot`s (`SP_Root1..9`), 4 variants each
  (`SPM1.PersonalityVariant`), mod-specific `SPM1.PersonalityTrait`s — all separate from vanilla
  Traits. M1 itself adds **no** ThoughtDefs/InteractionDefs/NeedDefs (only some animal hediffs,
  `SP_Hediff_Animal*`).
- **M2** packageId `hahkethomemah.simplepersonalities.module2`, Workshop 2781023972, depends on M1.
  Adds `SP_`-prefixed ThoughtDefs and Harmonious/Diversive/Turmoil InteractionDefs (some behind
  `MayRequire` on VSIE). **Risk: M2's checked About.xml lists supportedVersions only through 1.5.**
- **Public API (key finding):** `SPM1.Comps.CompEnneagram` is injected at runtime; public extension
  methods in `SPM1.Extensions` exist explicitly "for external mod support":
  `pawn.TryGetEnneagramComp()`, `pawn.TryGetEnneagram()` → `Enneagram { Root, Variant, MainTrait,
  SecondaryTrait, OptionalTrait }`, plus `ExtractPersonality()` / `IntractPersonality(string)`
  string serialization. Reading personality data needs no Harmony.

### Psychology

- Original by The Word-Mule (Workshop 1552507180); actively used continuation is **"Psychology
  (unofficial) v1.1-1.6"**, Workshop 2016263135, packageId `Community.Psychology.UnofficialUpdate`
  (source: github.com/eebasso/Rimworld-Psychology-unofficial-v1.1-1.4 for 1.1–1.4; 1.5/1.6 source
  location unverified). Harmony required; HugsLib dropped from 1.3 on.
- Adds a "Psyche" node system summarized as a Big-Five radar, sexual/romantic drives +
  Kinsey-scale orientation, conversations/hangouts/dates, mayor elections, expanded mental breaks,
  anxiety/PTSD, treatable traits. Confirmed Def folders include `InteractionDefs`, `ThoughtDefs`,
  `HediffDefs`, `TraitDefs`, `MentalStateDefs`, `TaleDefs`; **no NeedDefs** (drives live in the
  psyche tracker, not the vanilla Need system).
- C# surface: `CompPsychology`, `Pawn_PsycheTracker` exist, but **exact public signatures are
  unverified** — no documented external API found. Its repo does contain a compat-subproject
  pattern (`PsychologyVPE` etc. via `LoadFolders.xml` `IfModActive`).
- **Risk: 1.6 maintenance status uncertain** (one community note: "only mildly functional at
  best"). Also note **"Rimpsyche – Personality Core"** as an actively-maintained, 1.6-native,
  Psychology-inspired alternative — likely a cleaner future Tier-1 target.

### Vanilla Social Interactions Expanded (VSIE)

- packageId `VanillaExpanded.VanillaSocialInteractionsExpanded`, Workshop 2439736083, authors
  Oskar Potocki / Taranchuk, versions 1.4–1.6, Harmony, soft `loadAfter` on VE Framework. Source:
  github.com/Vanilla-Expanded/VanillaSocialInteractionsExpanded.
- New InteractionDefs (**exhaustive**, confirmed from source): `VSIE_Discord`, `VSIE_Vent`,
  `VSIE_Teaching`, and `VSIE_Teaching_<Skill>` for Shooting, Melee, Construction, Mining, Cooking,
  Animals, Plants, Crafting, Medicine, Artistic, Social, Intellectual. It does **not** add
  Gossip/Roast-style defs; vanilla `Chitchat`/`DeepTalk` are only reweighted via Harmony (they keep
  their defNames, so existing vanilla capture already handles them — no patch needed).
- New `PawnRelationDef`: `VSIE_BestFriend`. ~20+ `VSIE_`-prefixed ThoughtDefs. No new
  TraitDefs/NeedDefs.
- **GatheringDefs** (group activities — birthdays, funerals, movie night, skygazing, …) are a
  separate system with **no InteractionDef/TaleDef signal**, hence no existing capture path.
- No public extension API found.

## Per-mod plan

### 1. 1-2-3 Personalities

**Tier 1 — XML only. New file `1.6/Defs/Compat/DiaryCompat_123Personalities.xml`**, everything
gated `enableWhenPackageIdsLoaded` on the M1/M2 packageIds:

- One `domain=Thought` group, `matchPackageIds` on both M1 and M2 packageIds, batched as an
  ambient day-note (clone the `speakup_chitchat` batch shape). Package-id matching is deliberate:
  no verified exhaustive ThoughtDef list exists and `matchPackageIds` was built for exactly that gap.
- One `domain=Interaction` group, `matchPackageIds` on the **M2** packageId only (M1 has no
  InteractionDefs), for the Harmonious/Diversive/Turmoil compatibility interactions — normal
  pairwise instruction/tone, **not** batched (these are meant to be notable). Wording should ask
  for the felt compatibility or friction between the two pawns, not a transcript.

**Tier 2 — minimal C# glue. New adapter `integrations/PawnDiary.PersonalitiesBridge/`** (own
About.xml with `modDependencies` on M1 and `loadAfter` Pawn Diary; structure mirrors
RimTalkBridge). A real assembly reference to M1 is recommended over reflection — its extension API
is small, public, and intended for this.

- **Tier A (always on):** one `RegisterPawnContextProvider` line,
  `enneagram_root=<Root.label>, variant=<Variant.label>` (clone `ProvidePersonaLine`'s shape,
  including the `[MethodImpl(NoInlining)]` + active-mod guard isolation pattern).
- **Tier B (toggle, default on in the adapter's own settings):** map `Enneagram.Root` (9 values,
  optionally refined by `MainTrait`) to a 1–2 sentence outlook rule and call
  `PawnDiaryApi.SetPsychotypeOverride(pawn, sourceId, rule)`. The 9 rule strings are a static
  lookup table written in the register of `PSYCHOTYPE_PLAN.md`'s appendix catalog: behavioral,
  mechanics-free, label never echoed. Change detection via `ExtractPersonality()` hash (clone
  `AppliedPersonaHash` debouncing); reset-on-toggle-off / reset-on-new-game must walk the same
  broad pawn set as `PersonaSync.TouchedPawns()`.
- Pure Root→rule mapping goes in a `Source/Pure/` file and gets a console test project
  `tests/PersonalitiesBridgeLogicTests/` mirroring `tests/RimTalkBridgeLogicTests/`.
- Tier 2 targets **M1 only** (confirmed 1.6); M2 stays XML-only best-effort.

### 2. Psychology

**Tier 1 — XML only. New file `1.6/Defs/Compat/DiaryCompat_Psychology.xml`**, gated on the list of
known packageIds (`Community.Psychology.UnofficialUpdate` plus the original's — read the installed
About.xml to confirm the original's id before shipping; `enableWhenPackageIdsLoaded` accepts a
list, so list every known variant and extend from user reports):

- `domain=Interaction`, `matchPackageIds` catch-all for its conversations/flirting/hangout
  interactions → ambient day-note batch (clone `rimtalk_chatter`).
- `domain=Thought`, `matchPackageIds` catch-all → low-weight ambient batch (these fire often;
  must not flood; set a high `minEventsToWrite`).
- `domain=Hediff`, explicit `matchDefNames` for the anxiety/PTSD hediff(s) **once their defNames
  are confirmed from the installed XML** — the one part of Psychology worth a non-batched,
  occasionally-recorded event (a real mental-health arc beat). Pair with a
  `DiaryObservedConditionDef` (`observerType=PawnHediff`, promptEnabled only) so an *ongoing*
  anxious state tints prompt tone between events, exactly like `MapThreatActive`.
- Skip Romance-domain matching: Psychology's drives live outside PawnRelationDefs/Needs, so there
  is no clean structured matcher.

**Tier 2 — deferred behind a hard verification gate.** Do not code against guessed signatures:
before any glue, inspect the installed `Psychology.dll` (or the matching fork source) and confirm
`CompPsychology` / `Pawn_PsycheTracker` public reads. Then, in order of value:

1. One context-provider line summarizing the Big-Five radar (e.g.
   `psyche=very open, low neuroticism, …` — bucketed adjectives, not raw floats).
2. Psychotype override — same pattern as the Personalities bridge, but input is five continuous
   axes, so the mapper is a small "dominant/extreme axis" bucketing function rather than a lookup
   table. Do this only after the Personalities bridge ships and proves the pattern.

### 3. Vanilla Social Interactions Expanded

**Tier 1 — XML only (covers ~90% of the value). New file `1.6/Defs/Compat/DiaryCompat_VSIE.xml`**,
gated `enableWhenPackageIdsLoaded: VanillaExpanded.VanillaSocialInteractionsExpanded`:

- `domain=Interaction`, three groups:
  - `vsie_vent` — `matchDefNames: VSIE_Vent`; emotionally loaded, so give it a light `promotion`
    policy (clone the `romance` group's) and a confiding/unburdening instruction set.
  - `vsie_teaching` — `matchDefNames` for `VSIE_Teaching` + the 12 `VSIE_Teaching_<Skill>` defs
    (or `matchPrefixes: VSIE_Teaching`); ambient day-note batch, instruction themed on
    teaching/learning between the pair.
  - ~~`vsie_discord` — `matchDefNames: VSIE_Discord` (cooperative-work chatter); ambient day-note
    batch like `rimtalk_chatter`.~~ **CORRECTION (2026-07-10):** `VSIE_Discord` is NOT cooperative-work
    chatter — it is an anger-driven INSULT (`symbol=Insult`, `socialFightBaseChance=100`,
    `recipientThought=Insulted`; `InteractionWorker_Discord` only fires for angry pawns). There is no
    "cooperative-work chatter" interaction in VSIE; that was an unverified guess. It is now routed into
    Pawn Diary's core `insults` group via `PawnDiary.Vsie/1.6/Patches/AddDiscordToInsults.xml` (VSIE-gated
    `PatchOperationFindMod`), so it batches with the social fight it usually starts instead of getting its
    own mis-voiced group.
- `domain=Romance`: add a **new sibling group** `friendship_relation` claiming `VSIE_BestFriend`
  (do not widen the existing `romance_relation` group — its tone/instructions stay
  romance-specific; `GroupDomain.Romance` matches any PawnRelationDef defName). Instruction:
  a friendship becoming official — what this person has quietly become to the pawn.
- `domain=Thought`, `matchPackageIds` catch-all for the ~20+ `VSIE_` ThoughtDefs → ambient batch
  (don't hand-enumerate; they are numerous and version-dependent).
- Vanilla `Chitchat`/`DeepTalk`: no action needed (see research findings).

**Tier 2 — optional, lowest priority: gathering events.** `GatheringDef` activities have no
domain to match, so the only route is the External path from `EXTERNAL_API.md`'s quickstart:
a tiny adapter `integrations/PawnDiary.VsieBridge/` with one Harmony postfix on the gathering
start/end, calling `PawnDiaryApi.SubmitEvent` with `eventKey="vsiebridge_<gathering>"` for the
celebrant/attendees, claimed by matching `domain=External` groups in a
`1.6/Defs/DiaryExternalGroups_Vsie.xml`. Scope strictly to `VSIE_BirthdayParty` and `VSIE_Funeral`
(colony-narratively important); skip the flavor gatherings (skygazing, snowmen, …) — list defNames
explicitly, no catch-all. Re-confirm VSIE's gathering hook points against the installed version's
source before writing the patch; gathering internals shift between versions more than
InteractionDefs do.

## New files summary

```
1.6/Defs/Compat/DiaryCompat_123Personalities.xml     Tier 1, XML only
1.6/Defs/Compat/DiaryCompat_Psychology.xml           Tier 1, XML only
1.6/Defs/Compat/DiaryCompat_VSIE.xml                 Tier 1, XML only

integrations/PawnDiary.PersonalitiesBridge/          Tier 2 (mirrors PawnDiary.RimTalkBridge/):
  About/About.xml                                    modDependencies on M1, loadAfter Pawn Diary
  Source/PawnDiaryPersonalitiesBridge.csproj
  Source/PersonalitiesBridgeMod.cs                   settings: Tier B toggle
  Source/EnneagramSync.cs                            clones RimTalkBridge PersonaSync.cs shape
  Source/Pure/EnneagramLensMapping.cs                pure Root→rule table (test-covered)
tests/PersonalitiesBridgeLogicTests/                 console harness, mirrors RimTalkBridgeLogicTests

integrations/PawnDiary.VsieBridge/                   Tier 2, optional (gatherings only)
  + 1.6/Defs/DiaryExternalGroups_Vsie.xml            claims vsiebridge_* eventKeys
```

Psychology Tier 2 is deliberately absent from this list until its API is verified.

## Sequencing

1. All three Tier-1 XML compat files (zero C# risk, inert without the target mod, matches the
   "minimal XML-only" preference). Each can ship independently.
2. `PawnDiary.PersonalitiesBridge` (the only target with a confirmed, purpose-built external API;
   lowest-risk proof that the psychotype-bridge pattern generalizes beyond RimTalk).
3. Psychology Tier 2 — only after direct DLL/source verification of `CompPsychology`.
4. VSIE gathering bridge — smallest value-add; do last or drop.

## Risks / decision points

- **M2 on 1.6 unverified** (About.xml caps at 1.5). Ship its XML anyway (inert if unloaded) and
  flag "best-effort" in CHANGELOG/DOCUMENTATION.
- **Psychology's packageId varies by fork**; list every known id, expect to extend from reports.
- **Psychology's 1.6 health uncertain** — Tier 1 degrades gracefully (matchers never fire), so it
  ships regardless; hold Tier 2 until a tester confirms 1.6 behavior. Keep Rimpsyche on the radar
  as the modern alternative target.
- **Thought-domain catch-alls by packageId** could over-capture if a target mod ships a very noisy
  thought; the ambient batch + `minEventsToWrite` is the guardrail. If a specific def misbehaves,
  intercept it with a lower-`order` disabled group rather than dropping the whole catch-all.
- Never grant the core mod an assembly reference to any of the three targets — adapters only.

## Verification

- **XML groups:** load target mod + Pawn Diary, trigger one matching event (M2 compatibility
  interaction / Psychology conversation / `VSIE_Vent`, `VSIE_BestFriend`), confirm the diary entry
  appears with the intended tone and batching; then confirm the group is inert with the target mod
  absent (no warnings, not listed as capturable). Extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`
  with these manual checks.
- **Personalities bridge:** pure mapping covered by `tests/PersonalitiesBridgeLogicTests/`;
  in-game, toggle Tier B on/off and confirm `GetPsychotype` reflects the override and that
  toggle-off/new-game sweeps clear it (same smoketest choreography as the RimTalk Tier B cases).
- **VSIE gathering bridge (if built):** dev-trigger a birthday/funeral, confirm one entry per
  configured participant and that the External group claims the `vsiebridge_*` keys (no
  unclaimed-key warning).
- Adapter Harmony/comp code is verified manually in-game; the pure test harnesses stay
  RimWorld-free, per the existing rule.
