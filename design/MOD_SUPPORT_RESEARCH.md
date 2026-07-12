# Mod support research — next integration candidates

> Status: research findings, 2026-07-12. Companion to `MOD_COMPAT_PLAN.md` (which planned the
> 1-2-3 Personalities / Psychology / VSIE work; Personalities and VSIE shipped, Psychology stays
> deferred). Nothing here is implemented — this document ranks what to support next.
>
> **Verification method.** packageIds, supported versions, maintenance dates, and def inventories
> were read from each mod's published GitHub source (About.xml + cloned `Defs/`) on the research
> date. Steam Workshop pages were **not reachable from the research environment** (proxy-blocked),
> so subscriber/rating counts are proxies, not numbers — popularity claims lean on ecosystem
> signals (translations, add-on/compat mods, "best mods 2026" lists, dedicated wikis). Anything
> weaker is marked UNVERIFIED. Closed-source mods (no GitHub) have UNVERIFIED defNames that must
> be dumped in-game before writing matchers.

## Selection criteria (from the request)

1. Supportable **without extending the current API**.
2. Popular.
3. Maintained.
4. Beneficial to Pawn Diary — or Pawn Diary beneficial to *them*.

## What "no API extension" means here

Everything recommended below fits surfaces that already shipped:

| Surface | Cost | What it matches |
|---|---|---|
| `DiaryInteractionGroupDef` XML matchers | pure XML, inert without the target mod | 16 domains: Interaction, Thought, Romance (any PawnRelationDef), Hediff, MentalState, Tale, MoodEvent (GameConditionDef), Inspiration, Ability, Raid, Quest, Ritual, Work, Progression, Reflection, External |
| `DiaryObservedConditionDef` tone tint | pure XML | lasting hediffs/conditions recolor prompt tone between events |
| `RegisterPawnContextProvider` | 1 registration + 1 pure function in an adapter | one `key=value` line in every prompt's pawn summary |
| `SetPsychotypeOverride` | small C# adapter (RimTalk + Personalities bridges are the templates) | source-owned outlook lens from another mod's personality model |
| External event adapter | small Harmony hook + External group XML (the VSIE gathering bridge is the template) | anything with no def signal |
| Outbound: `PawnDiaryApi` reads + lane sharing | consumed by *their* code, zero change on our side | AI mods reading diary memories/pawn summaries, reusing the player's LLM lanes |

Already covered — do not re-plan: RimTalk (XML + full bridge), SpeakUp (core XML group), VSIE
(compat XML + gathering bridge), 1-2-3 Personalities M1+M2 (compat XML + Enneagram bridge).
Psychology stays deferred per `MOD_COMPAT_PLAN.md` (1.6 health uncertain) — and Rimpsyche below
is the recommended replacement for that slot.

## Ranked recommendations

### Tier 1 — do these first (high value, existing surfaces, verified maintained)

> Implementation plans exist for every Tier-1 candidate: [`RIMPSYCHE_BRIDGE_PLAN.md`](RIMPSYCHE_BRIDGE_PLAN.md),
> [`VEE_COMPAT_PLAN.md`](VEE_COMPAT_PLAN.md), [`HOSPITALITY_COMPAT_PLAN.md`](HOSPITALITY_COMPAT_PLAN.md),
> [`ALPHA_MEMES_COMPAT_PLAN.md`](ALPHA_MEMES_COMPAT_PLAN.md), [`VIE_MEMES_COMPAT_PLAN.md`](VIE_MEMES_COMPAT_PLAN.md),
> [`WBR_COMPAT_PLAN.md`](WBR_COMPAT_PLAN.md), [`VTE_COMPAT_PLAN.md`](VTE_COMPAT_PLAN.md).

1. **Rimpsyche – Personality Core** — the flagship. 1.6-native Psychology successor with a
   *documented* modder API and a 34-node numeric personality model: the richest psychotype
   override source found anywhere, plus a context line and a one-liner XML matcher pack. Fills
   the deferred-Psychology slot with none of its risks.
2. **Vanilla Events Expanded** — richest pure-XML event/condition surface found; `VEE_` prefix;
   its long "purple" conditions (Drought, Long Night, Whiteout: 15–120 days) are exactly what
   the tone-tint def was built for. Updated the day before this research.
3. **Hospitality** — probably the most-installed candidate on the list; guest charm/diplomacy
   interactions and recruitment thoughts are strong diary beats; packageId matching also covers
   the community "(Continued)" fork.
4. **Alpha Memes + VIE Memes and Structures** — the two Ideology ritual packs; both route
   straight into the existing Ritual + Thought surfaces on clean prefixes (`AM_`; `VME_`/`VFEA_`).
   Themed funerals, wicker-man burnings, leadership challenges — rare, memorable, low-noise.
   Both actively maintained (Alpha Memes committed on the research day).
5. **Way Better Romance** — pure XML matcher win, zero code: 3 interactions (hookup/date/hangout
   attempts) + 14 thoughts (rebuffed, failed, asexual-lovin' nuances); optional orientation
   context line from its traits. MIT, maintained, the de-facto romance overhaul.
6. **Vanilla Traits Expanded** — 53 traits as a context-provider line (a kleptomaniac's diary
   hiding thefts writes itself) plus `VTE_` thought and mental-break matchers.

### Tier 2 — strong fit, but needs curation, a def dump, or an availability check first

7. **Romance On The Rim** — richest romance content (wedding *ritual*, breakup/cheating precepts,
   many gesture interactions) and complementary to WBR — but closed source (defNames need an
   in-game dump) and a possible Workshop takedown must be checked first (§ caveat below).
8. **Alpha Genes** — technically the cleanest large mod audited: ~970 defs all on vanilla def
   classes under one `AG_` prefix (49 real AbilityDefs, 108 hediffs, mutation-instability arcs,
   a lab quest). Biotech-gated audience.
9. **A RimWorld of Magic** — highest storytelling ceiling (lich arcs, possession, mana storms,
   magic-bestowing rituals); actively maintained by the original author. Needs curation: its
   ~439 abilities are a custom def class (`TorannMagic.TMAbilityDef` — invisible to the Ability
   matcher), and blanket `TM_` hediff matching would flood the diary with buff/cooldown hediffs.
   Capture mental states, game conditions, inspirations, rituals, and a curated hediff list.
10. **Vanilla Skills Expanded** — no def-matcher surface at all, but two cheap adapter wins: an
    expertise/passion context line, and a once-per-pawn "gained expertise" external event — a
    permanent milestone with near-zero noise.
11. **Dubs Bad Hygiene** — only with a curated allowlist. The disease arcs (cholera, dysentery,
    dehydration → Hediff surface + tone tint) and embarrassment/luxury beats are excellent; the
    daily bathroom thoughts are pure noise and must stay excluded. No defName prefix — list
    defs individually.
12. **Intimacy (– A Lovin' Expansion / Friends n' Lovers)** and **Personality Plus** — growing
    2025-26 mods that fit the surfaces (intimacy-need context line; 32-archetype psychotype
    lookup respectively), but closed source and both carry the takedown-banner caveat.

### Tier 3 — AI-mod partnerships (where *Pawn Diary benefits them*)

These consume Pawn Diary's outbound API — diary memories as conversation/narration context, and
lane sharing so players configure one endpoint instead of two. Zero core changes; value scales
with outreach.

13. **RimMind suite** — best structural partner found: modular AI suite (memory, personality,
    dialogue modules), MIT, 1.6-only, OpenAI-compatible config, and the author *already ships*
    `Bridge-RimChat` / `Bridge-RimTalk` mods — a `Bridge-PawnDiary` is exactly their established
    pattern. Small userbase today; cheap to court via GitHub.
14. **RimGPT** (Brrainz) — alive (v2.2.3, Jan 2026), famous author (Harmony's), MIT. Value is
    one-directional but novel: the spoken narrator gossiping about what pawns wrote in their
    diaries. Nothing to import back.
15. **RimAgent: Orca** — LLM storyteller, MIT, 1.6, already ships extension points (XML
    knowledge-base defs, RimTalk hooks) that can be targeted unilaterally: ship knowledge defs
    describing Pawn Diary, and it could read diary summaries when planning incidents.
16. **Interaction Bubbles** — not AI, but a cheap unilateral immersion win: Harmony-inject
    synthetic play-log entries ("X scribbles in her diary…") that Bubbles renders for free;
    degrades gracefully without it.
17. **Rimteller / RimAI (AI Oracle) / Powerful AI Integration / RimChat / EchoColony** —
    conceptual fits (diary context in, incident reasoning out) but closed-source and/or locked
    to specific providers (Player2, Anthropic-only); outreach targets only, in that order.

### Deprioritized / negative results (recorded so they aren't re-researched)

- **RimDialogue** — the genre's flagship, but **pulled from the Workshop by Ludeon (Nov 2025,
  monetization ToS)**; cannot be updated, degrading on 1.6. Its orphaned users are Pawn Diary's
  natural audience; its open-source context-packaging client is reference material only.
- **Mind Matters** — could not be found at all in 2026; presumed dead/renamed. Dropped. (Nearest
  discoverable dynamic-trait alternative: "People Can Change", 1.6 status UNVERIFIED.)
- **Gastronomy** — thought-only surface, all on a convenient `Gastronomy_` prefix, but fires
  every meal → high noise. Worth a small extreme-stages-only matcher as a Hospitality companion,
  not standalone. Original is 1.5-only; 1.6 lives in a "(Continued)" re-upload keeping the same
  packageId (so a packageId-gated patch covers it automatically).
- **Medieval Overhaul** — audited weak on pawn-story content: no interactions, rituals, tales, or
  mental states; its ~36 thoughts are food noise. At most the cultist quest chain via the Quest
  surface. GitHub also lags the 1.6 Workshop build (def drift risk).
- **Vanilla Genetics Expanded** — animal-hybrid mod (0 GeneDefs); modest colonist-diary value.
  **"Vanilla Genes Expanded" does not exist** — for VE colonist genes, the follow-up research
  target is the Vanilla Races Expanded series.
- **VIE modules other than Memes and Structures** (Dryads, Icons and Symbols, Hats and Rags,
  Relics and Artifacts, Anima Theme, Sophian Style) — all verified to contain no
  ritual/thought/interaction defs worth capture.
- **Rim of Madness — Vampires (Continued)** — superb tone-tint material (vampirism recoloring
  every entry, feeding, frenzies) but the 1.6 build self-describes as unstable and its packageId
  could not be verified. Best-effort XML later, not now.
- **Prison Labor** — unique prisoner-drama niche, exemplary maintenance record, but revolts are
  likely C#-driven (adapter effort) and its def surface is unverified. Revisit on demand.
- **Individuality (Continued)** — no event-shaped defs; at most a small confidence/psychic-
  sensitivity context line. Deprioritized versus Rimpsyche.
- **VQE – The Generator** — good quest-lifecycle showcase (multi-stage generator saga, meltdown),
  active; ranked below the tiers above only because quest events are already captured generically
  — revisit if a VE-quest-series pass is wanted.
- **Claude Storyteller** — single-provider (Anthropic-only), tiny; skip.
- **RimSaga** — Pawn Diary's closest functional ancestor (Gemini colony chronicle), lapsed at
  1.5. No integration; relevant only for Workshop-description discoverability.
- **RimRelations / Rimder** — UI/control mods, no event content. Skip.

---

## Per-mod findings

Ordering follows the ranking. defName lists are verified from source unless flagged.

### Rimpsyche – Personality Core

| | |
|---|---|
| packageId | `Maux36.Rimpsyche` (verified from About.xml) |
| Workshop | 3535112473 |
| Source | github.com/jagerguy36/Rimpsyche (author Maux) |
| Versions | **1.6 only** (native); Harmony only |
| Maintenance | v1.0.41 released 2026-07-03; 566 commits, 35 releases — very active |
| Popularity | UNVERIFIED numbers; strong proxies: third-party integrations already exist (EchoPsyche, "RimTalk: Persona Director"), zh translation packs, positioned as *the* Psychology successor for 1.6 |

- **Model:** 15 hidden facets (−50..50) aggregate into **34 numeric personality nodes** (−1..1),
  each a `PersonalityDef` prefixed `Rimpsyche_` (Talkativeness, Bravery, Compassion, Morality,
  Optimism, Aggressiveness, …). Plus InterestDomainDefs and a continuous Kinsey-style sexuality
  model. No hediffs, no ticking.
- **Public API (documented — GitHub wiki "For Modders"):** `CompPsyche` on pawns →
  `Personality.GetPersonality(PersonalityDef)` float; `PsycheDataUtil.GetPsycheData(pawn)`;
  `RimpsycheDatabase.RegisterTraitGate/RegisterGeneGate`; and an
  **`InteractionHook(initiator, recipient, topic, alignment, …)`** conversation-outcome hook
  that carries the conversation *topic* and how well it went — data with no def signal.
- **Integration plan (mirrors the Personalities bridge exactly):**
  - Tier 1 XML: route `Rimpsyche_Smalltalk` / `Rimpsyche_StartConversation` /
    `Rimpsyche_Conversation` / `Rimpsyche_ConversationAttempt` InteractionDefs and the 3 social
    ThoughtDefs (`Rimpsyche_Smalltalk`, `Rimpsyche_ConversationOpinion`, `Rimpsyche_ConvoIgnored`)
    into an ambient conversation bucket — one prefix matcher.
  - Tier A context line: top ±3 nodes + interests (bucketed adjectives, not raw floats).
  - Tier B psychotype override: dominant/extreme-node bucketing → outlook rule — the same shape
    `MOD_COMPAT_PLAN.md` sketched for Psychology's Big-Five, now against a *documented* API.
  - Optional Tier C: Harmony postfix on `InteractionHook` → External event with topic+alignment
    ("argued about X with Y") — the single richest conversation signal available from any mod.
- **Caveats:** declares `incompatibleWith` Psychology (unofficial), 1-2-3 Personalities, and
  Personality Plus — so all personality bridges stay independent adapters, only one active per
  save (they already are). Companion modules by the same author (Disposition 3578212339,
  Sexuality 3627304156, Relationship in dev) can ride the same adapter later; the Sexuality
  module's Workshop page showed a possible takedown banner (§ caveat below).

### Vanilla Events Expanded

| | |
|---|---|
| packageId | `VanillaExpanded.VEE` |
| Workshop | 1938420742 |
| Source | github.com/Vanilla-Expanded/VanillaEventsExpanded |
| Versions | 1.4/1.5/1.6; has `1.6/Mods/NoOdyssey` conditional folder |
| Maintenance | v2.01 committed 2026-07-11 (day before research); v2.0 was a full remake |
| Popularity | UNVERIFIED numbers; one of the oldest VE mods (2019), JP/DE/ES/PL/FR translations |

- **Def surface (1.6):** 30 IncidentDefs (incl. purple raid variants `RaidEnemyPurple`,
  `ManhunterPackPurple`, `InfestationPurple`, `AnimalInsanityMassPurple`, and arrivals like
  `VEE_WandererJoinsTraitor`, `VEE_WhiteoutRefugees`), 15 GameConditionDefs (`VEE_Drought`,
  `VEE_LongNight`, `VEE_Scorch`, `VEE_Whiteout`, psychic conditions), 8 HediffDefs
  (`VEE_SunSickness`, `VEE_Aurora`, psychic hediffs), 2 ThoughtDefs (`MightJoin`, `Traitor` —
  the traitor-wanderer arc, note: unprefixed).
- **Fit:** MoodEvent matcher for the conditions, **tone tint for the long purple conditions**
  (15–120 days — they should color every entry written under them), Raid matcher for the purple
  raid variants, Hediff matcher for the psychic hediffs, Thought matcher for the traitor arc.
  `VEE_` prefix covers nearly everything.
- **Noise:** LOW-MEDIUM — purple events are rare by design (post-year-3), but condition hediffs
  apply colony-wide; dedupe per condition, not per pawn per day.

### Hospitality

| | |
|---|---|
| packageId | `Orion.Hospitality` — **also used by the "(Continued)" fork** (Zaljerem, Workshop 3509486825), so packageId matching covers both |
| Workshop | 753498552 |
| Source | github.com/OrionFive/Hospitality |
| Versions | 1.0–1.6 (community-PR 1.6 port merged 2025-07) |
| Maintenance | Orion accepts community PRs but is "not actively adding features" — community-maintained under the original ID, plus the Continued fork |
| Popularity | ~13,500 workshop ratings per search snapshot (UNVERIFIED); one of the longest-running staple mods, large add-on ecosystem |

- **Def surface (1.6):** InteractionDefs `GuestDiplomacy`, `CharmGuestAttempt`,
  `ScroungeFoodAttempt`; memory ThoughtDefs `GuestClaimedBed`, `GuestAngered`,
  `GuestRecruitmentForced`, `GuestOffendedRelationship`, `GuestPleasedRelationship`,
  `EndorsedByRecruiter`, `GuestDismissiveAttitude`, `GuestExpensiveFood`, `GuestCheapFood`;
  situational thoughts (`GuestCantAffordBed`, `GuestBedCount`, …); IncidentDefs `VisitorGroup`,
  `VisitorGroupMax`, `HappyGuestJoins`. **No TaleDefs/HediffDefs/MentalStateDefs.**
- **Fit & caveats:** interactions + memory thoughts via XML (match by packageId — defNames have
  no consistent prefix). **Perspective caveat:** most thoughts land on the *guest*, who has no
  diary; the diary-worthy view is the colonist side (initiator of charm/diplomacy, plus the
  colony moment when `HappyGuestJoins` fires). `VisitorGroup`/`HappyGuestJoins` are
  FactionArrival/Misc-category incidents — if the Raid matcher is category-locked, they need the
  External adapter; verify against the classifier before promising them.
  A "guests are staying with us" context line (from `CompGuest` presence) would be the
  highest-value single piece.
- **Noise:** MODERATE — recruiters spam `GuestDiplomacy`/`CharmGuestAttempt` every visit; batch
  as ambient day-notes and keep the rare events (`HappyGuestJoins`, `GuestRecruitmentForced`,
  `GuestAngered`) as promoted beats.

### Alpha Memes

| | |
|---|---|
| packageId | `Sarg.AlphaMemes` |
| Workshop | 2661356814 |
| Source | github.com/juanosarg/AlphaMemes |
| Versions | 1.5/1.6; requires Harmony, VE Framework, Ideology |
| Maintenance | v4.1201 committed **2026-07-12 (research day)** — excellent |
| Popularity | UNVERIFIED numbers; flagship Ideology expansion of the Alpha series |

- **Def surface:** ~20 RitualPatternDefs on a uniform `AM_` prefix, including a **Funeral
  Framework** with 12 themed funerals (sky burial, mummification, blast-off, rum burial,
  fleshcrafting…); matching ritual PreceptDefs; 39 ThoughtDefs that are almost all ritual-outcome
  quality tiers (`AM_TerribleRodeo`…`AM_UnforgettableRodeo`, funeral outcomes); InteractionDef
  `AM_Speech_Baptism`; a few hediffs (`AM_CatharsisHediff`, `AM_Kamikaze`) and AbilityDefs
  (`AM_Ocular*`). No TaleDefs/MentalStateDefs/GameConditionDefs.
- **Fit:** the Ritual surface's best showcase — themed funerals are peak diary material (a
  colonist writing about launching their friend's corpse into orbit). Ritual matcher + outcome
  ThoughtDef matcher on `AM_`; noise LOW (rare, event-driven).

### VIE — Memes and Structures

| | |
|---|---|
| packageId | `VanillaExpanded.VMemesE` |
| Workshop | 2636329500 |
| Source | github.com/Vanilla-Expanded/VanillaIdeologyExpanded-Memes |
| Versions | 1.4/1.5/1.6 |
| Maintenance | v4.07 committed 2026-03-19 |
| Popularity | UNVERIFIED numbers; core VE Ideology module |

- **Def surface:** 13 RitualPatternDefs (`VME_WickerManBurningRitual`,
  `VME_LeadershipChallengeRitual`, `VME_PlagueFestivalRitual`, `VME_CeremonialSuicideRitual`,
  `VME_SlaveEmancipationPattern`, `VME_OrgyRitual`, `VME_TradingFairRitual`, …); 49 ThoughtDefs
  (mostly ritual-outcome tiers plus `VME_AttendedParty`, `VME_GotSomeLovin`); interrogation
  InteractionDefs on a **second prefix**: `VFEA_InterrogatePrisoner`, `VFEA_InterrogationSuccess`,
  `VFEA_InterrogationRefused`, `VFEA_Intimidate`; fleshcrafted-part hediffs (`VME_Fleshcrafted*`).
- **Fit:** same profile as Alpha Memes — Ritual + Thought matchers, plus the interrogation
  interactions as a distinct (dark) pairwise group. Matcher must cover both `VME_` and `VFEA_`.
  Noise LOW. The other six VIE modules were audited and add nothing capturable.

### Way Better Romance

| | |
|---|---|
| packageId | `divineDerivative.Romance` |
| Workshop | 2877731755 |
| Source | github.com/divineDerivative/WayBetterRomance (MIT) |
| Versions | 1.4/1.5/1.6 |
| Maintenance | commits through 2026-04-07; steady since 2022 |
| Popularity | UNVERIFIED numbers; the standard romance overhaul, RU translation, RJW/RotR compat patches exist |

- **Def surface:** InteractionDefs `TriedHookupWith`, `AskedForDate`, `AskedForHangout`;
  14 ThoughtDefs (`RebuffedMyHookupAttempt(Mood)`, `FailedDateAttemptOnMe`,
  `GotSomeLovinAsexual`, `PassionateLovinAsexualPositive/Negative`, …); orientation TraitDefs
  (slot-free) plus Faithful/Philanderer; lover/spouse-count PreceptDefs; date/hangout JobDefs
  (no def signal — the initiating interaction suffices).
- **Fit:** pure Tier-1 XML — the interactions/thoughts are exactly diary-shaped (asked someone
  out, got rebuffed, awkward hangout). Optional context line for orientation from its traits.
  Capture the attempt/rejection tier as promotable pairwise events, not every flirt.

### Vanilla Traits Expanded

| | |
|---|---|
| packageId | `VanillaExpanded.VanillaTraitsExpanded` |
| Workshop | 2296404655 (one stale search snippet claimed removal — likely erroneous, GitHub active; manual check cheap) |
| Source | github.com/Vanilla-Expanded/VanillaTraitsExpanded |
| Versions | 1.4/1.5/1.6 |
| Maintenance | commit 2026-04-12 |
| Popularity | UNVERIFIED numbers; FR/DE/RU/ES translations, third-party add-ons |

- **Def surface (all `VTE_`):** 53 TraitDefs (`VTE_Kleptomaniac`, `VTE_MadSurgeon`,
  `VTE_Vengeful`, `VTE_Wanderlust`, `VTE_Perfectionist`, …); 27 ThoughtDefs (trait-conditional:
  `VTE_HarvestedOrgans`, `VTE_MyRivalsAreAlive`, `VTE_CreatedLowQualityItem`, …);
  3 MentalStateDefs (`VTE_Kleptomaniac` stealing spree, `VTE_TechnophobeTantrum`,
  `VTE_PanicFreezing`). No InteractionDefs/TaleDefs.
- **Fit:** (1) context-provider line for the traits — verified: `BuildPawnSummary` does *not*
  include traits (they only feed psychotype rolls and display decorations), so a compact line for
  these unusually voice-defining traits is real added information. (2) `VTE_`
  Thought matcher — thoughts are trait-conditional, so already personalized. (3) MentalState
  matcher for the 3 breaks (rare, high drama). Exclude the ambient recurring thoughts
  (`VTE_AnimalsInColony`, `VTE_EnvironmentDark`).

### Romance On The Rim

| | |
|---|---|
| packageId | `telardo.romanceontherim` (verified indirectly via a compat mod's dependency block) |
| Workshop | 2654432921 — **possible takedown, verify first** (§ caveat) |
| Source | closed — defNames UNVERIFIED, need in-game dump |
| Versions | 1.3–1.6; active 1.6 line through V1.6.1.0 |
| Popularity | UNVERIFIED; big enough to have third-party compat mods (R_IOTR, updated 2026-06) |

- **Content:** Romance need; many romantic gesture InteractionDefs (kissing, playing songs,
  swimming together); ThoughtDefs; 4 PreceptDefs (Cheating / Romance attempt / Breakup /
  Marriage proposal); a **wedding ritual on the Ideology ritual system** — the marquee diary
  event; 1.6.1 added artifact quests.
- **Fit:** XML matchers across Interaction/Thought/Ritual/Quest once defNames are dumped;
  optional Romance-need context line ("romance: starved/fulfilled"). Complementary to WBR
  (players run both). Do after WBR proves the romance-capture tuning.

### Alpha Genes

| | |
|---|---|
| packageId | `sarg.alphagenes` |
| Workshop | 2891845502 |
| Source | github.com/juanosarg/AlphaGenes |
| Versions | 1.5/1.6; requires Harmony, VEF, **Biotech DLC** |
| Maintenance | v4.1 committed 2026-06-19 |
| Popularity | UNVERIFIED numbers; flagship Biotech gene mod, 10-language translation mod |

- **Def surface (~970 defs, uniform `AG_` prefix, all vanilla def classes):** 356 GeneDefs
  (grant the rest); **49 real vanilla AbilityDefs** (`AG_BansheeScream`, `AG_PetrifyingGaze`,
  `AG_InsanityBlast`, …) — the first big test of the Ability matcher against a mod; 108
  HediffDefs (`AG_LethalInstability`, `AG_UnstableMutation/Major/Catastrophic`,
  `AG_Regeneration`, …); 17 ThoughtDefs (`AG_EldritchVisage`, `AG_Thalassophobia`, `AG_Awe`);
  2 MentalStateDefs; 1 QuestScriptDef (abandoned-lab opportunity site).
- **Fit:** Ability + Hediff (curated: the mutation-instability arc is the story) + Thought +
  MentalState + Quest matchers, tone tint for permanent mutations, xenotype context line.
  Noise LOW-MODERATE (combat powers fire in bursts; batch them).

### A RimWorld of Magic

| | |
|---|---|
| packageId | `Torann.ARimworldOfMagic` |
| Workshop | 1201382956 (canonical; 3624646773 appears unofficial) |
| Source | github.com/TorannD/RWoM |
| Versions | 1.0–1.6 |
| Maintenance | v2.6.4.4 committed 2025-12-28, by the original author |
| Popularity | UNVERIFIED numbers; PCGamesN best-mods 2026, dedicated wiki, add-on class ecosystem |

- **Def surface (1.6):** **~439 abilities on custom class `TorannMagic.TMAbilityDef`** — *not*
  visible to the vanilla-AbilityDef matcher; per-cast capture would need an adapter and isn't
  worth it. Usable via XML: 5 MentalStateDefs (`TM_Berserk`, `TM_PanicFlee`, psychotic wanders),
  ~10 GameConditionDefs (`TM_ManaStorm`, `ManaDrain`, `WanderingLich`, `DemonAssault`,
  `DivineBlessing`), 8 InspirationDefs (`ID_Enlightened`, `ID_ArcanePathways`), ~6
  InteractionDefs (`TM_MagicLore`, `TM_MightLore`), IncidentDef `ArcaneEnemyRaid` (Raid surface),
  Ideology rituals (`TM_SeverenceRitual`, `TM_GiftingRitual`, `TM_BestowMagicClassRitual` +
  botched/flawless outcome thoughts), 66 ThoughtDefs incl. magic-hating precept moods, and ~360
  hediffs — **curate**: tone tint for the transformative ones (lich, undead, possession), ignore
  the buff/cooldown noise.
- **Fit:** class trait + level context line; XML matchers on the curated lists above. Do not
  blanket-match `TM_` hediffs. The custom ability class is the one part that would need adapter
  work — skip it initially.

### Vanilla Skills Expanded

| | |
|---|---|
| packageId | `vanillaexpanded.skills` (lowercase) |
| Workshop | 3400246558 (current; original 2854967442 is "[OUTDATED]" — match by packageId only) |
| Source | github.com/Vanilla-Expanded/VanillaSkillsExpanded |
| Versions | 1.4/1.5/1.6 |
| Maintenance | v2.011, 2025-09-19; no 2026 commits yet |
| Popularity | UNVERIFIED numbers; DE/PL/RU translations, companion mods |

- **Def surface:** 47 custom `VSE.Expertise.ExpertiseDef`, 6 custom PassionDefs (Critical/
  Natural/Apathy). **No ThoughtDefs/HediffDefs/InspirationDefs** — nothing for XML matchers.
- **Fit:** adapter-only, both pieces tiny: (1) context line "expertise: Master Architect;
  critical passion in Construction"; (2) External event on expertise gain — once per pawn,
  permanent, a genuine milestone entry ("Today I committed my life to surgery"). Near-zero noise.

### Dubs Bad Hygiene

| | |
|---|---|
| packageId | `Dubwise.DubsBadHygiene` |
| Workshop | 836308268 |
| Source | github.com/Dubwise56/Dubs-Bad-Hygiene |
| Versions | 1.0–1.6 (ships Odyssey content) |
| Maintenance | commit 2025-12-23 |
| Popularity | UNVERIFIED numbers; long-running staple, PCGamesN best-mods 2026 |

- **Def surface:** ~17 ThoughtDefs with **no common prefix** (mixed casing: `tookDump`,
  `SoiledSelf`, `WashPrivacy`, `HotBath`, `DrankUrine`, …) — must be listed individually;
  HediffDefs `Diarrhea`, `Dysentery`, `Cholera`, `DBHDehydration` (lethal), `BadHygiene`.
  The `TowerContamination`/`SewageSpill` IncidentDefs are **commented out in 1.6** — don't
  target them.
- **Fit:** allowlist only. Keep: the diseases (Hediff matcher + tone tint — a cholera outbreak
  arc is excellent), `SoiledSelf`, `DrankUrine`, `openDefecation`, `WashPrivacy`/`ToiletPrivacy`
  (embarrassment), `HotBath`/`HeatedPool` (rare luxury). Never capture: `tookDump`, shower
  thoughts, `HygieneLevel`/`BowelLevel` situationals (daily noise).

### Intimacy (– A Lovin' Expansion / Friends n' Lovers) and Personality Plus

- **Intimacy:** packageId `lovelydovey.sex.witheuterpe`, Workshop 3498422643 (apparently
  renamed; UNVERIFIED which title is current), 1.6, closed source, active expansion ecosystem
  (Gender Works, Socio Butterfly). Intimacy need + charm interaction + intimacy precepts;
  deliberately SFW. Fit: interaction/thought matchers after an in-game def dump; intimacy-need
  context line. Noise HIGH (need-driven) — capture attempts/rejections/precept violations only.
  Prompt-tone handling deserves a look given the theme.
- **Personality Plus:** packageId `side1iner.personalityplus` (verified via Rimpsyche's
  incompatibleWith list), Workshop 3533950499, closed source. 32 categorical personality
  archetypes → the easiest psychotype lookup table possible, serving the non-Rimpsyche audience.
  Carries the takedown-banner caveat.

### Tier 3 partnership notes

- **RimMind:** packageId `mcocdaa.RimMindCore`, Workshop 3707741395 (+ module items), MIT,
  github.com/RimWorld-RimMind-Mod, 1.6-only, active Apr–Jul 2026. Their Memory module and Pawn
  Diary's diary memories are near-duplicates — context-share/dedupe both ways; personality
  module ↔ psychotype API; lane sharing removes duplicate endpoint setup. Action: open a GitHub
  issue proposing a `Bridge-PawnDiary`, pointing at `EXTERNAL_API.md`.
- **RimGPT:** packageId `brrainz.rimgpt`, Workshop 2960127000, MIT, v2.2.3 Jan 2026, 1.4–1.6.
  OpenAI+Azure-locked (won't consume lanes). Action: propose (issue or PR) a feature reading
  `GetContextSnapshot`/`GetRecentEntryTitles` so the narrator can reference pawns' diaries.
- **RimAgent Orca:** packageId `RedstonePanda.Orca`, MIT, 1.6-only, active Jul 2026,
  github.com/RedstonePanda00/RimAgentOrca. Ships XML knowledge-base extension points and RimTalk
  hooks. Action: ship knowledge defs describing Pawn Diary (unilateral, XML-only), then propose
  diary-summary reads.
- **Interaction Bubbles:** Workshop 1516158345, github.com/Jaxe-Dev/Bubbles, v4.2 Aug 2025,
  built for 1.6. No API — renders vanilla play-log entries. Action (unilateral, on our side):
  optionally emit a synthetic play-log entry when a diary page is written; Bubbles displays it
  for free, and without Bubbles it's still a harmless log line. Small, purely cosmetic — park it
  until someone asks.
- **Watchlist:** "Powerful AI Integration" (Workshop 3744421283, closed source, richest
  functional overlap — memory/rumors/arcs + OpenAI-compatible config; needs first-hand Steam
  verification of author/popularity), Rimteller (3638662307), RimAI AI Oracle (3652573198),
  RimChat (3623376155), EchoColony family (Player2-locked). Also **RimAI Framework**
  (github.com/oidahdsah0/Rimworld_AI_Framework) — a shared LLM-access framework whose API
  overlaps Pawn Diary's lane-sharing/completion surface; worth studying, possibly worth a compat
  shim if it gains traction. **RimTalk Expand Memory** (third-party RimTalk add-on) is worth
  watching for double-memory conflicts with the existing RimTalk bridge.

---

## Cross-cutting caveats

- **"Removed from community" banners.** Search-index snapshots showed Steam's
  moderation-removal banner on **Romance On The Rim** (+ its translations), **Rimpsyche –
  Sexuality** (module only, not the core), and **Personality Plus** — and stale-looking ones on
  VTE and Interaction Bubbles that contradict their active GitHub repos. Either a 2026 moderation sweep
  of romance/sexuality-adjacent mods or a crawler artifact; unverifiable from the research
  environment. **Two-minute Steam-client check required before committing effort to those
  three.** Rimpsyche Core, WBR, and Intimacy showed no banner.
- **Closed-source def dumps.** RotR, Intimacy, and Personality Plus need their defNames dumped
  from an installed copy before matchers can be written (same "read the installed About.xml"
  rule `MOD_COMPAT_PLAN.md` already applies to Psychology).
- **Incident-category check.** Hospitality's `VisitorGroup`/`HappyGuestJoins` are
  FactionArrival/Misc incidents; verify the Raid-domain classifier accepts them before planning
  that capture path (else route via External adapter or skip).
- **Steam popularity numbers** were unobtainable throughout (research environment could not
  reach the Workshop); before final sequencing, spot-check subscriber counts in a browser —
  it may reorder Tier 1/2 at the margins but is unlikely to change the tiers themselves.

## Suggested sequencing

1. **Rimpsyche bridge** (`integrations/PawnDiary.RimpsycheBridge/`, clones the Personalities
   bridge shape: XML group + context line + psychotype override; `InteractionHook` capture as a
   stretch). Highest ceiling, documented API, fills the Psychology gap.
2. **One pure-XML compat wave** — VEE, Alpha Memes, VIE Memes, WBR, VTE thoughts/breaks
   (+ Gastronomy's extreme stages if trivial): five small `DiaryCompat_*.xml` files, each inert
   without its target, shippable independently and testable with the existing smoketest
   choreography.
3. **Hospitality** (XML groups + `CompGuest` context line in a small adapter; decide the
   incident question first).
4. **Curated packs**: Alpha Genes, RWoM (curated lists), Dubs Bad Hygiene allowlist.
5. **Def-dump batch** after the Steam availability check: RotR, Intimacy, Personality Plus.
6. **Partnership outreach** (parallel, low effort): RimMind GitHub issue, RimGPT proposal,
   Orca knowledge defs.
