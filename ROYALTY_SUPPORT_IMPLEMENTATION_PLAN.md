# Pawn Diary — Royalty Support Implementation Plan

Status: Phases 0–3 and the Narrative N3-R core dependency are implemented as of 2026-07-18.
Phase 2 now ships exact persona-weapon formation, meaningful separation/recovery, destruction, and
transfer lifecycle pages with guarded Harmony collection, saved state, late-visible recovery,
package-gated settings/prompts, English/Russian localization, prompt fixtures, and N3-R evidence.
Its automated loaded suite is user-confirmed green, while the hands-on Phase-2 matrix remains open.
Phase 3 now owns the first qualifying persona kill through the existing Tale page and enriches the
existing wielder-death page with pre-`UnCode` context. After the pre-dead timing fix and two
fixture-only Tale-batch query corrections, its focused loaded rerun passed 244/244 on 2026-07-19.
Its automated loaded coverage is green while the hands-on matrix remains open.
Phase 4 is now code-complete with pure/build coverage. Its first loaded-game run reached 249/252; the
second reached 250/252 and confirmed the title-postfix and ritual-token fixes before exposing a
same-tick title-edge dedup defect plus one Phase-3 mod-profile-sensitive fixture. After those were
corrected, the user-confirmed loaded rerun passed 252/252. Automated coverage is green while hands-on
acceptance remains open. Phases 5–8 remain. This status does not pass,
waive, or remove any earlier Biotech B1 manual
acceptance row.

Scheduling authority: implement Royalty phases only in the waves assigned by
`DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md`; this file remains the technical authority for Royalty.

This plan turns Royalty from a few isolated title, psylink, ritual, thought, and raid facts into a
coherent set of personal story arcs. Its flagship is the relationship between a pawn and a persona
weapon. Later slices add truthful royal succession, a deliberately small set of dramatic permits,
and the Royal Ascent as a colony chapter. The plan deliberately builds on Pawn Diary's current event
routes rather than creating a parallel DLC pipeline.

The plan incorporates the decisions in `design/DLC_ATMOSPHERE_RESEARCH.md` and the canonical shared
contract in `DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md`: narrative facets are evidence
attached to source-specific events, not new generic capture types. Royalty is a source and guarded
context provider for that layer, and must still work without Ideology or any other DLC plan being
implemented.

Implementation must follow `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md`: live RimWorld
reads stay behind guarded impure adapters, policy stays pure and tested, tuning and prompt policy
live in XML, every behavior slice updates documentation and the changelog, every slice builds, and
the mod remains fully usable without any paid DLC.

## 1. Review outcome and recommended release boundary

The repository and installed RimWorld 1.6 APIs support the broader Royalty direction, but the work
should ship in three independently releasable increments.

### R1 — Persona relationship and progression correctness

Required for the first Royalty release:

- A persisted persona-weapon bond state with formation, meaningful separation, recovery, and bond
  ending.
- Persona context on the first consequential kill, using the existing tale/death capture path so the
  victim is known and the action produces one page.
- Persona context on the wielder's death rather than a competing second bond-ending page.
- Event-relevant persona traits, bounded to one or two facts and selected without parsing English.
- Immediate, faction-aware royal-title transitions, including title loss, with the current scanner
  retained as a fallback.
- Cause-aware psylink/title deduplication for bestowing, anima linking, neuroformers, and unknown or
  modded sources.
- Pure tests, focused RimTests, save compatibility, XML policy, localization, documentation, and
  no-DLC verification.

### R2 — Succession and dramatic permit moments

- Exact inheritance correlation between a deceased titleholder, heir, faction, inherited title, and
  any later bestowing or title mutation.
- Explicit heir appointment when an exact player-facing source can be distinguished from vanilla's
  automatic fallback assignment.
- Successful use of only the story-sized permit families: military aid, transport shuttle, orbital
  strike, and orbital salvo.
- Permit ownership of quick military aid so the same action does not also fan out as a generic
  friendly-raid page.

### R3 — Royal Ascent and ambient court pressure

- A start chapter for the exact `EndGame_RoyalAscent` quest.
- A bounded active event window that shades prompts with royal-hosting pressure without generating
  recurring complaint pages.
- One exact completion or failure chapter through the existing quest route.
- No claim that the Stellarch has arrived until an exact arrival signal is correlated.

This boundary is intentional. Persona weapons are Royalty's strongest pawn-scale relationship and
deserve a complete lifecycle before the mod adds more breadth. Title and psylink correctness belong
in R1 because existing coverage already emits those pages and must not conflict with the new hooks.

## 2. Product outcome

After the full plan is implemented:

1. A pawn can remember bonding with a named persona weapon, being kept apart from it long enough for
   that separation to matter, recovering it, and the bond ending through destruction, death, or a
   later transfer.
2. The first qualifying kill with that weapon becomes a relationship milestone inside the
   existing combat story. Ordinary later kills do not become a stream of persona pages.
3. Persona traits shape only the moments they bear on. A bloodthirsty reaction may matter for a kill;
   jealousy may matter for wielding another weapon; unrelated stat traits do not become a mechanics
   dump.
4. Royal promotions, demotions, and losses use the actual previous title, new title, and faction.
   Honor/favor ticks do not produce pages.
5. An inheritance names the deceased titleholder and heir only when vanilla reports that exact
   relationship. A later ceremony or promotion does not repeat the same succession claim.
6. Bestowing and anima-linking rituals remain the canonical story events for their moments, with the
   resulting title or psylink level attached. Their downstream mutations do not create duplicate
   progression pages.
7. A neuroformer or otherwise unclaimed psylink change can still produce one progression page, and
   modded sources retain the scanner fallback.
8. A dramatic permit use records the permit owner, permit family, faction, title context, and setting
   only after the effect succeeds. Routine resource and labor permits stay silent.
9. The Royal Ascent reads as a beginning, a period of pressure, and one ending—not as a generic
   accepted quest plus repeated raid and hospitality noise.
10. Royal duties and court expectations are concise context on relevant thought, title, or quest
    pages. The mod does not create recurring throne-room or apparel complaint pages merely because a
    need remains unmet.
11. If Ideology support is implemented, Royalty events may ask its resolver for event-relative belief
    context through plain facets and topics. If it is absent, Royalty behavior is unchanged.
12. A base-game-only colony sees no errors, no Royalty-only settings clutter, no empty fields, and no
    changed event behavior.

## 3. Scope and non-goals

### 3.1 Required R1 scope

- Guarded Royalty snapshots in `DlcContext` for persona weapons, persona traits, faction-specific
  titles, newly introduced duty categories, and current psylink level.
- Plain contracts for those snapshots; no live `Pawn`, `Thing`, `Def`, Verse, Unity, Harmony, or
  settings objects in pure policy.
- An XML-owned `DiaryRoyaltyPolicyDef` initially covering separation timing, kill qualification,
  trait salience, cause-correlation windows, prompt limits, and optional exact-ID compatibility
  corrections; R2/R3 extend the same Def with their own policy.
- A saved persona-bond state machine with load baselining and no catch-up pages.
- A new `PersonaWeapon` event only for lifecycle edges that have no richer existing owner.
- Enrichment of qualifying existing `Tale` and `Death` events rather than duplicate persona events.
- Immediate royal-title observation plus fallback scanning and duplicate arbitration.
- Psylink/title cause correlation for bestowing, anima linking, neuroformer use, and fallback changes.
- Prompt templates, interaction groups, settings labels, Keyed and DefInjected localization, compact
  prompt behavior, pure tests, RimTests, docs, changelog, and a rebuilt committed DLL.

### 3.2 R2 scope

- Persisted pending-succession facts produced only after vanilla's exact inheritance candidate is
  committed by the titleholder-death path.
- Title-transition correlation that recognizes an inherited claim even when the actual rank arrives
  later through favor or a bestowing ceremony.
- Explicit heir appointment only behind an exact source correlation.
- A new `RoyalPermit` event with an XML permit-family allowlist.
- A transient permit-owner cache and fallback behavior when ownership is ambiguous.
- Duplicate arbitration between military-aid permits and `RaidFriendly`.

### 3.3 R3 scope

- Exact quest-root matching for `EndGame_RoyalAscent`.
- A start-only `DiaryEventWindowDef`, a bounded active prompt line, and an exact terminal prompt
  policy.
- Map-witness selection rather than an accepted-quest page for every colonist on every loaded map.
- The same stable map-witness policy for the terminal quest page rather than the current all-colonist
  quest fanout.
- Optional event-relative belief topics if the Ideology resolver exists.

### 3.4 Explicit non-goals

- No page for every kill made with a persona weapon.
- No page for every equip, unequip, inventory transfer, caravan transition, map removal, or short
  weapon swap.
- No general weapon biography system for ordinary weapons.
- No prompt dump of all persona traits, trait mechanics, cooldown ticks, psyfocus numbers, hediffs,
  or stat modifiers.
- No title page for each honor/favor change and no diary interpretation of raw favor as prestige.
- No hardcoded English ranking of titles, duties, persona temperaments, or permit importance.
- No resource-drop, labor-team, aerodrone, trade, or cooldown-ready permit pages in the initial
  permit release.
- No routine meditation page. Meditation focus may enrich a relevant psychic-identity entry after a
  separate exact collector check, but meditation ticks are not an event source.
- No new page for every title-related mood thought. Existing thought capture remains the source;
  Royalty adds bounded context only when relevant.
- No new page for each raid during Royal Ascent. The active quest window supplies atmosphere while
  existing raid policy decides whether a particular raid already merits a page.
- No generic `BondLifecycleEventData`, `IdentityTransitionEventData`, `JourneyChapterEventData`, or
  `AmbientPressureEventData` event families. Those words remain facets on concrete events.
- No DLC dependency in `About/About.xml`, no direct XML cross-reference to a Royalty Def, and no
  throwing `DefDatabase<T>.GetNamed` lookup for DLC content.
- No retrofit of new context into already-generated diary entries.
- No LLM-driven mutation of titles, permits, bonds, quests, or any other game state.

## 4. Definition of done

### 4.1 R1 is complete only when

- A real bond formed after the feature is installed produces exactly one persona lifecycle page.
- An existing bond in an old save is baselined silently.
- A short drop, equipment swap, caravan transition, save/load, or map transition produces no false
  separation page.
- A separation that survives the XML-defined threshold produces one page, and a later recovery
  produces one page only if that separation was previously emitted.
- Weapon destruction can close the relationship using a snapshot captured before vanilla erases the
  coded pawn; ordinary map removal cannot.
- The wielder's death contains the persona relationship in the death page and does not also produce a
  competing persona bond-end page.
- The first qualifying combat tale for that bond contains the weapon and selected relevant trait
  facts; the same action produces no second persona or generic killer-POV page.
- Later ordinary kills do not repeat the milestone.
- Promotions, demotions, first titles, and complete title loss use actual previous/new/faction facts.
- Bestowing and anima linking produce one canonical event with their mutations attached.
- Neuroformer and unknown-source psylink changes still have a truthful fallback owner.
- All saved event facts remain stable after the pawn changes title, weapon, faction, or map.
- Royalty inactive means every new collector, patch, state scan, and formatter cleanly returns empty
  or no-ops.
- Pure tests, focused RimTests, XML loading, all existing pure suites, Debug MSBuild, and the repository
  verification hook pass.

### 4.2 The full Royalty program is complete only when

- Exact inheritance and explicit heir-appointment scenarios meet the R2 dedup rules.
- Each allowed dramatic permit produces one owner page after successful use; excluded permits do not.
- Quick military aid does not also produce a generic friendly-raid fanout when the permit event owns
  the action.
- Royal Ascent produces one preparation chapter, bounded active pressure, and one terminal chapter.
- No ascent page claims arrival merely because the quest was accepted.
- Optional Ideology enrichment is source-owned, event-relative, and absent without changing page
  eligibility.
- `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, `TEST_COVERAGE_PLAN.md`, `CHANGELOG.md`, localization,
  prompt fixtures, and the committed DLL match the shipped behavior at every release boundary.

## 5. Reviewed current coverage and gaps

Pawn Diary already has several Royalty-aware seams. The implementation should extend them rather
than replace them.

| Current seam | What it already does | Gap this plan closes |
|---|---|---|
| `DlcContext.RoyalTitle*` | Safely reads the most senior title behind `ModsConfig.RoyaltyActive` and null guards. | Only returns a global label/defName; it does not preserve faction, previous title, cause, duties, or loss. |
| Progression scanner | Emits psylink increases and most-senior-title changes. | Title loss is silently discarded; cause and faction are unknown; downstream ritual mutations can duplicate. |
| `ritualRoyal` group | Matches throne speech and anima linking with generic courtly guidance. | It does not own correlated title/psylink mutations or provide persona/title-specific facts. |
| `progressionPsylink` and `progressionRoyalTitle` | Provide generic progression pages and prompts. | They cannot distinguish bestowing, anima linking, neuroformer use, inheritance, demotion, or fallback. |
| Thought groups | Include bestowing quality, throne-room reign, decree, disinheritance, and title-need thoughts. | They lack bounded event-time title/persona context and can sound like recurring mechanics complaints. |
| `TaleSignal` and death cache | Know combat tale participants and capture the death instigator/weapon. | No persona-bond milestone is selected, and the weapon's relationship facts are not preserved. |
| `raidFriendly` | Captures friendly raids and fans a page to eligible colonists. | Royal permit aid can be misread as an independent friendly raid and create too many POV pages. |
| Quest route and event windows | Track exact quest root defNames, acceptance/terminal signals, mapless quests, and prompt shading. | Quest group selection is signal-centric; Royal Ascent needs exact root ownership and a start-only window. |
| Royal-title prompt enchantment | Adds the current title as optional prompt flavor. | It is current-state flavor, not an event-time title transition or succession record. |

The existing behavior must remain the fallback whenever a new exact hook cannot be registered or an
ownership correlation expires without a richer source claiming it.

## 6. Verified RimWorld 1.6 seams

These findings were checked against the locally installed RimWorld 1.6 managed assembly and Def XML.
The coding agent must re-check exact signatures immediately before patching and register fragile
targets through `DiaryPatchRegistrar` with a null check and warning.

### 6.1 Persona weapon lifecycle

- `CompBladelinkWeapon.CodeFor(Pawn)` establishes a real coded relationship and calls
  `OnCodedFor`. Its load path can also restore/correct coding, so program state and Scribe mode must
  be gated before treating the transition as new gameplay.
- `CompBladelinkWeapon.Notify_Equipped(Pawn)` can run on ordinary re-equips. It is not a bond-formed
  event by itself.
- `CompBladelinkWeapon.Notify_EquipmentLost(Pawn)` runs on temporary equipment loss and weapon
  swaps. It is evidence for a pending separation, never immediate authorization for a diary page.
- `CompBladelinkWeapon.Notify_WieldedOtherWeapon()` is a useful jealousy/reaction clue, but also
  fires for short swaps and cannot own a page without state policy.
- `CompBladelinkWeapon.Notify_KilledPawn(Pawn)` receives only the killer. It does not receive the
  victim and therefore must not be the canonical first-kill hook.
- `CompBladelinkWeapon.UnCode()` clears the relationship. Destruction, map removal, and pawn death
  can all reach it, so the caller/cause must be captured before using it narratively.
- `PostDestroy` and `Notify_MapRemoved` both uncode. Destruction can be a story ending; map removal is
  lifecycle cleanup and must remain silent.
- `Pawn_EquipmentTracker.Notify_KilledPawn` forwards a kill to equipment after the game already knows
  both participants through the tale/death path. The existing `TaleSignal`, its XML victim-role
  metadata, and the death-context instigator are the correct place to identify the killer and attach
  a qualifying persona milestone; never assume the first Tale pawn is the killer.
- On pawn death, `Pawn_EquipmentTracker` uncodes the bonded weapon. The existing death prefix/cache
  must snapshot the weapon relationship before that cleanup.

### 6.2 Persona trait projection

`WeaponTraitDef` exposes localized label/description text plus structural fields such as bonded
thoughts, kill thoughts, bonded/equipped hediffs, and a worker class. The initial implementation
should project structure into plain tokens and let XML policy assign salience. It must not parse
localized adjectives such as “jealous,” “kind,” or “bloodthirsty.”

The projection should remain mod-friendly:

- Preserve the trait defName only as a stable equality/save/debug key.
- Preserve bounded, sanitized localized label and description as display facts.
- Project structural flags such as `has_kill_thought`, `has_bonded_thought`,
  `has_bonded_hediff`, and `has_equipped_hediff`.
- Project the worker type name as a plain token for XML category mapping; do not pass `Type` into pure
  policy.
- Allow optional exact trait overrides in XML for exceptional modded behavior.
- Select normally one and at most two traits for an event.

Freewielder/never-bond weapons never enter the bond lifecycle because no coded relationship exists.

### 6.3 Royal title and succession

- `Pawn_RoyaltyTracker.OnPostTitleChanged(Faction, RoyalTitleDef, RoyalTitleDef)` is the central
  post-change edge. It is private in the checked build, so it requires defensive manual registration
  rather than an unconditional attribute patch.
- Award/loss `Thought_MemoryRoyalTitle` memories are added before that post-change edge; they require
  short staging in `ThoughtSignal`, not a scope opened by the later title callback.
- `SetTitle`, `TryUpdateTitle`, and `ReduceTitle` converge on title-change behavior but do not by
  themselves explain why the change occurred.
- A `RoyalTitle` preserves its faction, title Def, received tick, and inherited marker. The adapter
  must project these to plain values and never retain the live object.
- `Pawn_RoyaltyTracker.AllTitlesForReading` supplies the current faction-specific title rows; the
  fallback scanner should project the highest title per faction rather than compare only
  `MostSeniorTitle`.
- `RoyalTitleDefExt.TryInherit(...)` reports the exact heir candidate and current-title relationship,
  but it does not itself transfer a title or grant favor. It is authoritative correlation evidence,
  not sufficient page authorization alone.
- `Pawn_RoyaltyTracker.Notify_PawnKilled()` consumes that outcome. When the heir does not already
  hold an equal/higher title, it grants the accumulated favor and marks the deceased pawn's
  `RoyalTitle.wasInherited=true`. The postfix of this outer path is the committed succession edge.
- Inheritance can grant favor and defer the visible rank to a later bestowing ceremony. Rank
  similarity alone is therefore not proof of inheritance; a pending exact correlation is required.
- `Pawn_RoyaltyTracker.SetHeir(Pawn, Faction)` is also used by automatic fallback behavior. A diary
  page requires correlation with an explicit player-facing quest/action source, not merely this
  setter firing.
- `RitualOutcomeEffectWorker_Bestowing.Apply(...)` has the actual target, bestower/faction context,
  and performs title/psylink mutation. It is the correct cause scope for ceremony ownership.

### 6.4 Psylink causes

- `Hediff_Psylink.ChangeLevel` changes the level but carries no narrative cause.
- `CompPsylinkable.FinishLinkingRitual` is an exact anima-linking cause.
- `RitualOutcomeEffectWorker_Bestowing.Apply` is an exact Imperial-bestowing cause.
- The vanilla neuroformer is the `PsychicAmplifier` item using an implant-use comp. Matching the
  parent Def by the plain defName string is DLC-safe; the plan must not add a direct XML Def
  reference.
- The current progression scan is the correct last-resort route for modded or unknown causes.

The implementation should capture before/after levels at the exact cause boundaries and leave the
central level mutation alone unless a later spike proves a central patch is necessary.

### 6.5 Permits

- `FactionPermit.Notify_Used()` fires after a permit succeeds and updates its use state. It exposes
  the permit, faction, and title context but not its owning pawn.
- A small transient owner cache can be populated when `Pawn_RoyaltyTracker.GetPermit(...)` returns a
  permit for a specific pawn. A rare bounded fallback search is acceptable; an ambiguous owner means
  no permit page, not a guessed pawn.
- `FactionPermit.Notify_Used` must be inspected in a prefix if the plan later needs the pre-use
  cooldown state. Raw favor expenditure must not be claimed unless vanilla exposes it directly.
- Royal military aid marks incident parameters with `raidArrivalModeForQuickMilitaryAid = true`
  before invoking `RaidFriendly`. That exact flag allows the permit event to claim the action while
  preserving generic friendly raids as a fallback.
- Vanilla's successful `RaidFriendly.TryExecute` returns before `FactionPermit.Notify_Used` runs.
  Pawn Diary's incident postfix therefore sees the friendly raid first. Arbitration must stage the
  quick-aid raid briefly; it cannot assume a permit ownership token already exists.
- The reviewed dramatic vanilla families are small/large/grand military aid, transport shuttle,
  orbital strike, and orbital salvo. XML owns the exact allowlist.

### 6.6 Royal Ascent

- The exact quest root is `EndGame_RoyalAscent`.
- Quest acceptance begins the hosting/preparation chapter; it does not prove the Stellarch is
  physically present.
- The quest lasts through an extended stay, raid pressure, and a shuttle pickup, and the existing
  quest terminal path receives success/failure.
- Current quest capture already preserves `quest.root.defName` and can drive an exact event window.
- A start-only window plus the existing quest terminal page avoids an event-window end page and a
  second quest-completion page describing the same moment.

## 7. Target architecture

Royalty follows the normal boundary:

```text
guarded Harmony/game edge
    -> event-time Royalty snapshot DTO
    -> pure lifecycle/cause/ownership policy
    -> existing source-specific event or one narrow new event
    -> prompt planning and formatting
    -> transport / persistence / UI
```

No pure method may accept a live `Pawn`, `Thing`, `CompBladelinkWeapon`, `RoyalTitle`, `FactionPermit`,
`Quest`, `Def`, settings object, Harmony object, or translation result that it later tries to resolve
off-thread.

### 7.1 New plain snapshot contracts

The exact syntax may follow repository conventions, but the information boundary should be stable.

#### `PersonaWeaponSnapshot`

- `weaponThingId`
- `weaponDefName`
- `displayName`
- `codedPawnId`
- `codedPawnName`
- `isCurrentlyPrimary`
- `isDestroyed`
- `traits : List<PersonaTraitFact>`

#### `PersonaTraitFact`

- `traitDefName`
- `label`
- `description`
- `workerTypeToken`
- `hasKillThought`
- `hasBondedThought`
- `hasBondedHediff`
- `hasEquippedHediff`

#### `RoyalTitleSnapshot`

- `pawnId`
- `factionId`
- `factionName`
- `titleDefName`
- `titleLabel`
- `seniority`
- `receivedTick`
- `wasInherited`
- `dutyCategoryTokens`

`dutyCategoryTokens` are structural categories projected from the checked `RoyalTitleDef` fields:
`throne_room`, `bedroom`, `apparel`, `food`, `work_restriction`, `joy_restriction`, `decree`, and
`speech`. Emit only categories that the new title introduces relative to the previous title and that
are effective for the pawn where vanilla exposes that check. They are prompt hints, not lists of room
thresholds, apparel items, disabled work types, or other numeric mechanics. Entitlements such as
permits and maximum psylink level are not duty tokens.

#### `RoyalTitleMutationSnapshot`

- `pawnId`
- `factionId`
- `previousTitle`
- `newTitle`
- `causeToken`
- `tick`
- `correlationId`

#### `RoyalSuccessionFact`

- `deceasedPawnId`
- `deceasedPawnName`
- `heirPawnId`
- `heirPawnName`
- `factionId`
- `factionName`
- `inheritedTitleDefName`
- `inheritedTitleLabel`
- `heirAlreadyHeldHigherTitle`
- `createdTick`
- `expiresTick`
- `claimed`

#### `RoyalPermitUseSnapshot`

- `ownerPawnId`
- `ownerPawnName`
- `permitDefName`
- `permitLabel`
- `permitFamilyToken`
- `factionId`
- `factionName`
- `titleDefName`
- `titleLabel`
- `mapId`
- `mapLabel`
- `usedDuringCooldown`
- `tick`

#### `RoyalPsychicMutationSnapshot`

- `pawnId`
- `previousPsylinkLevel`
- `newPsylinkLevel`
- `causeToken`
- `tick`
- `correlationId`

All human-readable fields are captured, sanitized, and bounded on the main thread. DefNames and IDs
are identity/correlation facts, not prompt prose.

Saved event-specific Royalty facts should use the repository's existing semicolon-delimited
`DiaryEvent.gameContext` contract rather than adding parallel live/model fields. New standalone
markers are `persona_weapon=` and `royal_permit=`; tale, death, ritual, progression, and quest
enrichment appends namespaced keys such as `persona_weapon_name=`, `royal_cause=`, and
`succession_heir=` to the source's existing marker. Every external label/description must pass
`PromptContextLines.CleanLine` (including semicolon flattening) and a field cap before assembly.
XML template fields then read exact keys through `source=GameContext`/`contextKey`, as current event
types do. Add a dedicated `DiaryEvent` model field only if a later implementation proves that one
event needs irreducibly different per-POV Royalty facts.

### 7.2 Saved persona-bond state

Use a small deep-scribed record keyed by the weapon's stable Thing ID, not a deep- or
reference-scribed live weapon:

- `weaponThingId`
- `weaponDefName`
- `lastDisplayName`
- `bondEpoch`
- `currentPawnId`
- `currentPawnName`
- `previousPawnId`
- `phaseToken`
- `bondStartedTick`
- `pendingSeparationTick`
- `separationEmitted`
- `firstConsequentialKillObserved`
- `firstConsequentialKillEventRecorded`
- `lastPrimaryObservedTick`
- `endedTick`
- `endCauseToken`
- saved bounded trait facts needed to close the story after destruction

The stable narrative identity is `(weaponThingId, bondEpoch)`. Re-coding the same surviving weapon to
a different pawn starts a new epoch while preserving the previous pawn as optional transfer context.

The state is a list/dictionary owned by `DiaryGameComponent`; it contains no `Thing` or `Pawn`
reference. Normalize null strings, unknown phase tokens, negative ticks, duplicate IDs, and oversized
trait lists after load. A dedicated scribed schema/baseline marker distinguishes “this old save has
never initialized Royalty state” from the legitimate initialized state “no bonds exist”; never infer
that distinction from an empty list alone.

### 7.3 Saved royal observation state

Replace the scalar “most senior title” assumption for new observation with a small per-pawn list keyed
by faction ID. Each `RoyalTitleObservationState` stores:

- `factionId`
- `factionName`
- `titleDefName`
- `titleLabel`
- `seniority`
- `lastObservedTick`

The guarded adapter projects the pawn's highest current title in each faction to plain snapshots.
The exact title hook updates the matching faction row immediately; the scanner compares the full
plain list as fallback, including a saved faction row disappearing as title loss. The current
most-senior `DlcContext.RoyalTitle*` accessors remain valid for general pawn-summary flavor.

Retain the existing scalar `lastObservedRoyalTitleDefName`/label fields for save compatibility and
read them only during migration. Old saves silently baseline the new per-faction list; they do not
turn the scalar value into an invented faction-specific transition.

Use a Royalty-specific baseline/version flag rather than the existing shared
`baselineProgressionOnNextScan`, which may already be false in an upgraded save before the new
per-faction field exists.

Psylink observation keeps the existing highest-level scalar, augmented only with any unclaimed
mutation/cause state needed by the ownership cache.

### 7.4 Transient correlation caches

Use separate, resettable caches for:

- active bestowing/anima/neuroformer cause scopes;
- unclaimed title and psylink mutations awaiting a richer ritual owner;
- permit instance to owner pawn;
- quick-military-aid raid signals awaiting the later successful permit callback;
- destruction/death/map-removal persona end causes;
- exact tale/persona ownership during one capture action.
- persona-trait kill-memory signals briefly awaiting the Phase-3 Tale owner through the verified
  `MemoryThoughtHandler.TryGainMemory` callback; Phase-2 bonded situational thoughts require no cache.
- exact royal-title award/loss thought signals briefly awaiting a title/ritual owner.

Every cache must:

- use stable IDs rather than long-lived game references where practical;
- expire by elapsed tick comparison, never `TicksGame % interval`;
- be bounded defensively;
- expose `Reset()` and be cleared from `DiaryGameComponent.FinalizeInit()` on new game and load;
- fail open to an existing generic event when a richer owner never arrives, except when doing so
  would assert an unknown pawn or cause.

Secondary-source arbitration must work in either callback order. Keep both a short pending-secondary
entry and a short recent-owner token: an owner claims an already-staged thought/raid, while a
thought arriving just after its lifecycle/tale/title owner is recognized and suppressed as the same
action. Tests must exercise both orders.

The pending quick-aid cache may hold the already-created `RaidFanoutSignal` for only the short
main-thread correlation window so it can flush the exact current fallback after expiry. It is never
scribed, is cleared on `FinalizeInit`, and is the bounded exception to the stable-ID preference.

### 7.5 Event types and ownership

Add only the event types that lack a richer existing source:

- `PersonaWeaponEventData` for `bond_formed`, `bond_separated`, `bond_recovered`, and a standalone
  `bond_ended` only when destruction/transfer has no existing death owner.
- `RoyalPermitEventData` for successful allowlisted permit use.

Reuse existing sources for everything else:

- first consequential persona kill -> `TaleEventData` with saved persona context;
- wielder death -> `DeathEventData` with saved persona context;
- title/succession -> `ProgressionEventData` with richer Royalty facts and defName/kind tokens;
- psylink -> existing `RitualEventData` or `ProgressionEventData` according to cause ownership;
- Royal Ascent -> `QuestEventData` plus `DiaryEventWindowDef`;
- title/persona reaction thought -> existing `ThoughtEventData` when normal thought policy admits it.

This is the main duplicate-prevention rule: the source that knows the full gameplay moment owns page
existence. Royalty context enriches that page; it does not authorize a second generic page.

#### Stable saved event keys

Freeze synthetic saved `interactionDefName` values before Phase 2 so prompt selection, settings, old
saves, and tests do not depend on localized labels:

| Source | Proposed stable values |
|---|---|
| Persona lifecycle | `PersonaWeaponBondFormed`, `PersonaWeaponBondSeparated`, `PersonaWeaponBondRecovered`, `PersonaWeaponBondEnded` |
| Persona tale milestone | `PersonaWeaponFirstConsequentialKill` while `tale_source_def` preserves the actual TaleDef |
| Title progression | `RoyalTitleGained`, `RoyalTitleReduced`, `RoyalTitleLost`; retain `RoyalTitleChanged` for old saves/fallback compatibility |
| Succession | `RoyalSuccession`, `RoyalHeirAppointed` |
| Permits | `RoyalPermitMilitaryAid`, `RoyalPermitTransportShuttle`, `RoyalPermitOrbitalStrike`, `RoyalPermitOrbitalSalvo` |
| Royal Ascent | keep the actual quest root `EndGame_RoyalAscent` |

The first-kill signal performs persona qualification before the current Tale group gate. When it
qualifies, it resolves the synthetic Tale-domain persona group; if that group is disabled, it may
fall back to the original Tale group only as ordinary combat, without persona-milestone wording.
The observation also precedes the Tale signal-enable gate: either way the gameplay milestone is
consumed truthfully, while settings still control whether any page is emitted.

Representative saved context keys are:

- `persona_weapon`, `persona_weapon_id`, `persona_weapon_def`, `persona_weapon_name`, `bond_epoch`,
  `bond_previous_state`, `bond_new_state`, `bond_end_cause`;
- `persona_milestone`, `tale_source_def`, `persona_trait_1`, `persona_trait_2`, and bounded matching
  trait descriptions/relevance tokens;
- `royal_transition`, `royal_faction_def`, `royal_faction`, `previous_title`, `previous_title_def`,
  `title`, `title_def`, `royal_cause`, `royal_duty_changes`;
- `succession_deceased`, `succession_heir`, `succession_title`, `succession_faction`;
- `royal_permit`, `permit_def`, `permit_label`, `permit_family`, `permit_faction`,
  `used_during_cooldown`;
- `previous_psylink_level`, `psylink_level`, `psylink_cause`;
- optional `narrative_facets` and `belief_topics` comma-separated stable tokens.

Context builders must omit unknown optional claims rather than writing misleading `false`, `none`, or
empty prose fields. Required parser/sentinel fields follow existing event conventions.

### 7.6 Required shared narrative evidence

After Narrative Continuity Phase N1 exists, every Royalty page introduced by this plan must submit
plain, per-POV evidence in the same slice that creates the canonical event:

| Royalty moment | Facet | Suggested belief topics |
|---|---|---|
| Bond formation, separation, recovery, transfer | `bond_lifecycle` | `weapons`, `bonding`, `loyalty` |
| First consequential kill | `bond_lifecycle` | `weapons`, `violence`, `death` |
| Promotion, demotion, succession | `identity_transition` | `authority`, `status`, `duty`, `death` |
| Dramatic military/orbital permit | source event plus optional `identity_transition` | `authority`, `violence`, `service` |
| Royal Ascent start/end | `journey_chapter` | `authority`, `loyalty`, `duty` |
| Active court pressure | `ambient_pressure` | `authority`, `status`, `hospitality` |

Unknown topics are ignored. These facets never create a page and never make Royalty depend on the
Ideology implementation.

Use stable continuity identities:

- persona lifecycle: `royalty-persona|<weaponThingId>|<bondEpoch>` with the weapon as subject;
- title transition/succession: pawn subject plus exact transition phase; faction/title remain
  source-owned event facts, not part of a localized arc key;
- Royal Ascent: one quest/window arc key shared by start, active pressure, and terminal chapter;
- permit: no invented arc key unless an exact active Ascent or persona relationship already applies.

The Royalty provider may propose a matching persona bond, exact current title/duty, or active court
pressure. It must pass exact arc/subject/topic applicability to the shared selector and must not put a
generic title into unrelated daily entries. Ordinary event enrichment is selected by the shared
`NarrativeContextSelector`; Royalty code must not append its own second cross-DLC prompt block.

## 8. Persona weapon lifecycle policy

### 8.1 State machine

| Current state | Evidence | Next state | Diary behavior |
|---|---|---|---|
| untracked | real null -> pawn coding during play | bonded active | Emit one `bond_formed` page. |
| untracked on load/baseline | existing coded bond | bonded active | Save baseline; emit nothing. |
| bonded active | weapon no longer primary but bond still exists | separation pending | Save start tick; emit nothing. |
| separation pending | same weapon becomes primary before threshold | bonded active | Clear pending; emit nothing. |
| separation pending | XML threshold elapses and bond still exists | separated | Emit one `bond_separated` page. |
| separated | same weapon becomes primary | bonded active | Emit one `bond_recovered` page. |
| active/pending/separated | weapon destroyed | ended | Emit `bond_ended` unless a richer death event owns the ending. |
| active/pending/separated | bonded pawn dies | ended | Attach context to death page; no separate persona page. |
| any live phase | pawn/weapon observation unavailable or off-map | unchanged/suspended | Emit nothing; unavailable is not separation evidence. |
| active/pending/separated | map removal only | reconciled/baselined | Emit nothing. |
| ended | surviving weapon codes to another pawn | new bond epoch | Emit new pawn's bond page; optional prior-bond context if exact. |

The threshold and reconciliation cadence belong in XML. The plan intentionally does not lock a tick
count into C# or this design file.

Lifecycle observation/state transitions continue while a Royalty group is disabled; only page
emission is disabled. Re-enabling a group therefore starts from current truth and never releases a
backlog of formation, separation, recovery, or first-kill pages.

### 8.2 Bond formation

Bond formation requires all of the following:

- `ModsConfig.RoyaltyActive` is true.
- The weapon has a live bladelink comp with a real coded pawn after the action.
- The previous coded pawn was null or different for this bond epoch.
- The game is in normal play, not loading, resolving cross-references, or initializing a save.
- The pawn is eligible under existing Pawn Diary ownership/generation rules.
- No equivalent bond epoch has already been recorded.

The event captures the event-time weapon display name and selected traits. A later player rename or
mod language change must not rewrite the saved event.

### 8.3 Separation and recovery

An unequip callback starts evidence collection only. The persisted/reconciled state decides whether
the separation became narratively meaningful.

The reconciliation pass should run alongside the existing slow progression scan, not from
`Pawn.Tick`, `Thing.Tick`, or another hot patch. For each tracked eligible bond, it checks only the
known bonded pawn and weapon identity. It must not scan every thing on every map each tick.

Reuse the progression scan's elapsed cadence, not its current `Progression` signal-enable early
return. Persona state reconciliation must execute before or independently of that gate so disabling
Progression or a persona group cannot freeze state and create a catch-up story later. Event emission
still respects the applicable signal/group settings.

R1 follows the current progression scope of eligible free colonists on loaded maps. If the pawn or
weapon cannot be observed—for example during a caravan/world transition—the policy suspends or
cancels an un-emitted pending-separation timer. Off-map elapsed ticks cannot prove separation. An
already-emitted separated state may remain saved and recover normally when the exact bond becomes
observable again.

Recovery is authorized only when `separationEmitted` is true. This prevents a normal weapon swap from
creating a “reunion” page after no separation page existed.

### 8.4 First consequential kill

The canonical hook is the existing `TaleSignal`, because it has both participants, the Tale Def,
the exact active `Pawn.Kill` correlation, and current equipment at the same gameplay moment.

“Has the killer” means an exact XML TaleDef row names distinct initiator/recipient roles and those
pawns match the most-recently-added exact active kill scope. Missing or contradictory role evidence fails closed;
list order is never treated as semantics. The shipped qualifier is only the verified vanilla
`KilledMajorThreat`; `KilledMan` does not exist in RimWorld 1.6 and must not be used as a fallback.
The same policy separately lists the eight ordinary double-pawn companion Tales vanilla can emit
around that qualifier. Companion rows can be owned by an already-qualified milestone but can never
qualify one themselves; `KilledBy` stays with victim-death ownership.

Pure `PersonaMilestonePolicy` receives:

- whether the killer has an active tracked bond;
- whether the exact bonded weapon is the killer's current primary equipment at tale capture;
- whether this bond epoch has already observed its first consequential kill;
- the tale defName and existing tale significance facts;
- bounded victim facts already allowed by the tale/death path;
- XML kill qualification rules;
- projected persona trait facts.

If it qualifies:

- attach `PersonaWeaponContext` to the existing killer-POV tale event;
- override the generic tale batch/pair shape with one solo killer POV for the synthetic milestone;
- mark the gameplay milestone observed even if the persona group is disabled, so a later kill can
  never be mislabeled as the bond's first; separately record whether the page was accepted;
- select normally one and at most two kill-relevant traits;
- claim the killer-POV combat page for that action so no parallel persona event is submitted;
- claim exact ordinary companion Tales staged in that same kill call, including delayed batch input;
- preserve any separate victim death page or victim-POV handling already required by death policy.

If it does not qualify, every staged companion returns to existing Tale batching. Both bounded Tale
and Thought buffers fail open on defensive overflow, so capacity pressure cannot silently lose an
ordinary signal. XML ships with a deliberately narrow initial policy and is tunable without
recompilation.

### 8.5 Trait relevance

Pure selection ranks structure before optional exact overrides:

1. An event-specific structural signal, such as a kill thought for a kill milestone.
2. A worker-category mapping explicitly assigned in XML.
3. A bonded thought/hediff for formation, separation, or recovery.
4. A bounded compatibility override for a modded trait whose structure cannot express its behavior.
5. Otherwise omit the trait from the prompt.

No localized word matching is permitted. Ties use a stable hash of event identity and trait defName so
the same event remains deterministic while different bonds do not always choose the first XML entry.

Persona trait thoughts are secondary evidence when they are caused by the same kill action. Vanilla
`WeaponTraitDef.bondedThought` rows are `Thought_Situational`, so they never enter
`MemoryThoughtHandler.TryGainMemory` and Phase 2 must not construct a dead staging scope around them.
For Phase 3, open a narrow scope only for verified stored `killThought` Defs on the exact current
weapon, stage each matching pawn/Def once, and let the accepted Tale event claim it. Carry only
expected Defs not already staged while that exact `Pawn.Kill` scope remains open, so either callback
order is handled synchronously; consume each at most once. The memory hook has no victim argument,
so after the scope closes an unmatched memory must fail open through the unchanged `ThoughtSignal`
path rather than a killer-wide recent-owner cache. If no Royalty event claims the scope, flush each
staged signal independently. Never suppress a ThoughtDef globally merely because a persona trait can
produce it.

### 8.6 Bond endings and transfers

Cause precedence is:

1. bonded pawn death;
2. weapon destruction;
3. exact transfer/re-code;
4. unknown uncode;
5. map removal/lifecycle cleanup.

Death owns its existing page, including when the exact live coded bond is pending/separated and its
weapon is no longer primary. Destruction may own a persona page. A transfer is two truths—a prior
bond ended and a new one formed—but the implementation should prefer one new-owner formation page
with concise exact previous-bond context when both edges correlate. Unknown uncode is saved for
reconciliation and normally silent. Map removal is always silent.

## 9. Royal title and succession policy

### 9.1 Immediate title transitions

The defensive post-title hook captures faction-specific previous and new title snapshots. Pure
`RoyalTitleTransitionPolicy` classifies:

- first title;
- promotion;
- demotion;
- title loss;
- same-title/no meaningful change;
- mutation already claimed by a bestowing or succession correlation.

Acquisition/cause wording is evidence-gated: `bestowed` only inside an exact bestowing scope,
`inherited` only after the committed succession path, and otherwise `unknown`. A non-inherited title
change is not automatically labeled “granted.”

The title Def's structural seniority determines promotion/demotion. Localized title words are never
parsed. The event may include one concise newly introduced duty-category line; it must not list room
statistics, apparel requirements, favor, or cooldowns.

“Prior identity” comes from the already-saved event-time pawn summary plus the exact previous title.
Do not invent a hardcoded social class such as “commoner” when vanilla supplies no prior title or
role fact.

After an accepted exact event, update the existing `PawnProgressionState` title baseline immediately.
The slow scanner remains a safety net if the private hook cannot be found or a mod mutates titles by
an unexpected path. A fallback scanner event uses `cause=unknown` and must include title loss instead
of discarding an empty current label.

Royalty observation/baseline advancement must run on the elapsed cadence independently of the
current top-level `Progression` signal-enable early return. Settings control dispatch, not knowledge
of the new title or psylink level; re-enabling Progression must not replay changes that occurred while
it was disabled.

The title and psylink scanner branches must return before comparison when Royalty is inactive. An
empty guarded snapshot means “DLC data unavailable,” not “the pawn lost the title.” Preserve saved
state while inactive and silently reconcile/baseline on reactivation before emitting new changes.

Vanilla adds `Thought_MemoryRoyalTitle` before it calls `OnPostTitleChanged` (and title reduction adds
the lost thought before replacing the live title). The Thought adapter must therefore recognize that
exact guarded memory, project its `titleDef` plus award/lost relationship in `DlcContext`, and stage
the signal briefly. The later title, succession, or bestowing ritual owner claims it; if the title
hook never arrives, the thought expires through the existing Thought path. This prevents one
promotion from becoming both a title page and a generic title-thought page without globally
suppressing either ThoughtDef.

### 9.2 Bestowing ownership

Bestowing opens a short cause scope before vanilla mutates title and psylink. Mutations captured
inside that scope are held as unclaimed facts. The completed `RitualSignal` claims and appends them to
the ritual event.

If the ritual event never arrives because another mod short-circuits the normal route, the cache
expires into at most one fallback progression event. No mutation is silently lost, and normal vanilla
behavior produces only the ritual page.

### 9.3 Exact succession

While `Notify_PawnKilled` runs, capture successful `TryInherit` candidate outcomes in a nested
transient scope. At the outer postfix, create an exact `RoyalSuccessionFact` only when the candidate
did not already hold an equal/higher title and the matching deceased `RoyalTitle` was marked
`wasInherited`. Do not infer succession from:

- title rank changes alone;
- `wasInherited` without the heir/deceased relationship;
- the death of an arbitrary royal;
- favor granted after death;
- a later bestowing ceremony with no pending matching fact.

`GainFavor` runs before the outer method marks/commits inheritance and may itself trigger a title
change. Any title hook reached inside the active succession scope is staged against the candidate
rather than dispatched immediately. The outer postfix either commits and claims that mutation as
part of succession or releases it to the normal fallback if vanilla did not mark the inheritance.

The succession page may be emitted at that committed outer edge if the heir is an eligible diary
pawn. Its wording must distinguish “inherited the late titleholder's claim/title” from “was formally
bestowed a new rank” when vanilla delays the visible rank.

A later title/bestowing mutation claims the pending fact by heir, faction, and compatible title within
the XML correlation lifetime. It may add ceremony detail but must not repeat the inheritance claim as
a second progression page.

If the heir already holds an equal/higher title, vanilla applies no inheritance effect. Record no
succession page and never invent a promotion, demotion, inherited claim, or new rank.

### 9.4 Heir appointment

`SetHeir` alone is insufficient because vanilla can call it automatically. R2 may emit an appointment
page only when a transient scope from the exact explicit quest/action source identifies:

- appointing pawn/titleholder;
- chosen heir;
- faction;
- source action or quest;
- previous heir if known.

If that exact source cannot be registered robustly, defer the page and keep heir facts as succession
context. Silence is better than presenting automatic bookkeeping as a deliberate appointment.

### 9.5 Court pressure

Title requirements are supporting context, not a scheduler. Existing admitted thoughts such as
decree failure, disinheritance, or title-need complaints may receive the event-time title and one
relevant duty category. Repeated copies remain governed by current thought cooldown/dedup policy.

No periodic “noble is still unhappy about the throne room” event is added.

## 10. Psylink ownership and psychic identity

### 10.1 Cause tokens

The initial stable cause tokens are:

- `imperial_bestowing`
- `anima_linking`
- `neuroformer`
- `succession_related` only when exact correlation proves it
- `unknown`

Tokens are schema values and remain English. Player-facing prose comes from localized prompt/Keyed
text.

### 10.2 Ownership rules

| Cause | Canonical page | Mutation behavior |
|---|---|---|
| Imperial bestowing | existing completed ritual | Attach title and psylink before/after; suppress matching progression pages. |
| Anima linking | existing completed ritual | Attach psylink before/after; suppress matching progression page. |
| Neuroformer | existing psylink progression | Add `cause=neuroformer`; no ritual owner exists. |
| Unknown/modded | existing scanner progression | Preserve current fallback with bounded unknown wording. |

Cause correlation uses pawn ID, before/after level, and a short XML-defined time window. Same-tick
proximity alone is insufficient if the pawn or levels do not match.

Whether a page is enabled or claimed, the observed psylink baseline advances to current truth. A
disabled Progression group/signal therefore cannot bank a later catch-up page.

### 10.3 Meditation focus

The broader atmosphere plan treats psychic practice as identity, not routine labor. A later phase may
add a guarded `MeditationIdentitySnapshot` to psylink, quiet-reflection, or persona-weapon psychic
moments after the exact 1.6 public collector is verified. It should contain only available focus
labels/categories and never a meditation tick stream.

Meditation focus is therefore an optional enrichment, not an R1 blocker.

## 11. Dramatic permit policy

### 11.1 Allowlist and family mapping

XML maps exact permit defName strings to one of:

- `military_aid`
- `transport_shuttle`
- `orbital_strike`
- `orbital_salvo`

The initial vanilla allowlist includes the reviewed small/large/grand military-aid permits, transport
shuttle, orbital strike, and orbital salvo. Any modded permit is silent unless its Def or structural
adapter explicitly opts into a family. Unknown permit families fail closed.

### 11.2 Capture

`FactionPermit.Notify_Used` authorizes the page only after successful use. Resolve the owner from the
session cache; if exactly one eligible owner cannot be established, do not guess.

Capture only facts actually available at the success edge:

- owner and title;
- permit label/family;
- faction;
- map/setting when known;
- whether vanilla marked it as use during cooldown, if captured safely from pre-use state.

Do not promise that aid arrived, a target was hit, or a shuttle completed its trip unless a later
exact outcome source is added. The page describes the pawn calling in/exercising the permit.
Likewise, debt/cost wording is allowed only when the pre-use `usedDuringCooldown` fact is true, and
even then the prompt must not invent a favor amount.

### 11.3 Duplicate arbitration

For quick military aid:

1. The successful incident postfix sees `raidArrivalModeForQuickMilitaryAid` before vanilla calls
   `FactionPermit.Notify_Used`, so it stages a bounded pending `RaidFanoutSignal` instead of
   dispatching immediately.
2. The later successful allowlisted military-aid permit callback resolves its owner and matches the
   pending raid by faction, map, and correlation window.
3. The permit page owns the caller's action and consumes the pending generic raid.
4. If no matching successful permit arrives before expiry—for example a mod used the quick-aid flag
   directly—the pending signal flushes through the current `RaidFriendly` path.

This avoids losing stories when a mod bypasses the permit edge while preventing one vanilla button
press from becoming a permit page plus a colony-wide friendly-raid set.

The incident postfix also emits generic event-window signals before raid capture. The reviewed XML
does not currently turn `RaidFriendly` into an event-window page, but Phase 6 must pin that with a
regression test. If a future window does, suppress it through the existing capture-capability seam
when the permit path is healthy rather than adding a second hardcoded exception.

## 12. Royal Ascent chapter policy

### 12.1 Start chapter

Add an exact event window for `source=Quest` and `matchDefNames=EndGame_RoyalAscent`, gated with the
existing `enableWhenPackageIdsLoaded=Ludeon.RimWorld.Royalty` field. Its initial policy is
`recordStartEvent=true`, `recordEndEvent=false`, `recordTimeoutEvent=false`,
`recordEndWithoutActive=false`, `keepActive=true`, and `recordScope=MapWitness`. Completed/failed
quest signals close it; a conservative XML timeout silently prevents a stale window if another mod
swallows the terminal signal.

On accepted:

- select one stable `MapWitness`/eligible POV rather than every colonist;
- record a start page framed as preparation/commitment;
- retain the current Quest bridge's mapless scope and activate one colony-wide window, so acceptance
  and terminal signals address the same saved state even with multiple loaded maps;
- do not say the Stellarch has arrived;
- do not create a second generic quest-accepted page.

### 12.2 Active pressure

While active, the event window adds one bounded prompt-context line indicating that the colony is
hosting/preparing for a royal ascent and living under elevated court pressure. It may attach the
`ambient_pressure` facet but cannot independently authorize pages.

Because the current Quest lifecycle bridge has no exact map, the first implementation treats this as
colony-wide pressure and makes the context available to eligible pawns on loaded colony maps. It must
not call that proof that the Stellarch is present on a particular map. Exact host-map correlation is a
deferred refinement, not a reason to guess `Find.CurrentMap`.

Existing raid, thought, death, work, and social routes continue to decide their own events. The
window changes framing, not capture frequency.

### 12.3 Terminal chapter

Add a root-aware quest classifier that first tries non-catch-all Quest groups against the exact root
defName, then falls back to the existing accepted/completed/failed signal classification. Resolve the
group once in `QuestFanoutSignal` and use that same group for availability, the saved instruction,
and settings lookup; do not select an exact group and then accidentally reclassify by calling
`IsQuestEnabled(signal)` or `InstructionForQuest(signal)`.

Use the same root-first resolution when `DiaryPipelineAdapters` recovers a group for a saved event.
`DiaryEventDomainClassifier` currently collapses Quest classification to `signal`; extend the pure
contract to preserve both saved root defName and signal so regeneration after save/load still selects
the exact Royal Ascent group.

Add an optional XML Quest fanout policy on `DiaryInteractionGroupDef`, defaulting to the current
`AllEligible` behavior. The Royal Ascent group sets `MapWitness`; `QuestFanoutSignal` then selects one
stable eligible pawn across loaded maps using the same deterministic witness rule as event windows.
Other quest groups retain their existing all-colonist fanout.

- completed -> one stable-witness Royal Ascent completion prompt;
- failed -> one stable-witness Royal Ascent failure prompt;
- close the event window without recording a second window-end page;
- preserve existing mapless quest fallback and stable witness selection.

If an exact arrival source is added later, it can create a midpoint phase only while the matching
window is active. Acceptance alone remains insufficient evidence.

## 13. Event behavior and dedup matrix

| Gameplay moment | Canonical owner | Royalty addition | Suppress/merge |
|---|---|---|---|
| Persona weapon codes during play | `PersonaWeapon` | Bond name and selected traits | Re-equip callback and load restoration |
| Short weapon swap/drop | none | Pending state only | All equip/lost noise |
| Persistent separation | `PersonaWeapon` | Duration framing and bond facts | Repeated lost/equipment callbacks |
| Recovery after emitted separation | `PersonaWeapon` | Reunion facts | Ordinary re-equip |
| First qualifying kill | existing `Tale` | Weapon, bond milestone, relevant traits | Parallel persona killer page; preserve victim death handling |
| Bond/kill persona trait thought in same action | lifecycle or `Tale` owner | Reaction evidence | Matching standalone thought; unmatched thought falls back |
| Later ordinary kill | existing current behavior | Usually none | Persona milestone repeat |
| Wielder death | existing `Death` | Bond and weapon context | Separate bond-ending page |
| Weapon destruction | `PersonaWeapon` | Exact destruction ending | Unknown/map-removal uncode |
| Promotion/demotion/loss | existing `Progression` | Faction, before/after, cause, duty delta | Scanner duplicate |
| Matching title award/loss thought | title progression, succession, or ritual owner | Emotional reaction evidence | Standalone thought only when unclaimed |
| Bestowing title/psylink | existing `Ritual` | Correlated mutations | Matching progression pages |
| Anima linking | existing `Ritual` | Correlated psylink change | Matching progression page |
| Neuroformer | existing `Progression` | Exact cause | Unknown scanner duplicate |
| Committed inheritance after titleholder death | existing `Progression` Royalty kind | Deceased/heir/faction/title fact | Later matching generic title page |
| Explicit heir appointment | existing `Progression` Royalty kind | Exact appointer/heir facts | Automatic fallback assignment |
| Dramatic permit use | `RoyalPermit` | Owner/title/faction/family | Matching quick-aid friendly raid |
| Routine permit | none | none | all permit noise |
| Royal Ascent accepted | quest event window start | Preparation chapter | generic accepted fanout |
| Royal Ascent active | existing event routes | ambient pressure context | periodic/ascent-only pages |
| Royal Ascent terminal | existing `Quest` | exact stable-witness success/failure chapter | window-end duplicate and all-colonist fanout |

## 14. Persistence and save compatibility

### 14.1 Event-time snapshots

Any Royalty fact used by a generated page must be copied into the saved event payload before async
generation. Rebuilding from the live pawn later is incorrect because:

- a title can change;
- a weapon can be renamed, destroyed, transferred, or un-coded;
- a persona trait list can change through mods;
- a faction or quest can disappear;
- the pawn can die or leave the map;
- the language or loaded mod set can change.

Formatting on background threads uses saved plain strings only. It must not call `.Translate()` or
read a live Def.

### 14.2 Old-save baselining

On the first valid scan after adding R1:

- existing coded persona bonds become active baselines with their current weapon/pawn identity;
- `firstConsequentialKillObserved` defaults conservatively for pre-existing bonds, so the feature
  does not claim a later kill as that bond's first merely because the mod was updated;
- existing title and psylink state preserve the current `PawnProgressionState` baseline;
- no formation, separation, promotion, loss, or psylink page is backfilled;
- new fields normalize without throwing when absent.

Persist explicit initialization/schema markers for the global persona list and per-pawn Royalty
observation state. Default-missing means “baseline once,” while an initialized empty collection
means the real current state is empty.

For a genuinely new bond after baselining, normal lifecycle behavior begins immediately.

### 14.3 Scribe rules

- Deep-scribe only Pawn Diary's plain state records.
- Do not deep-scribe or reference-scribe persona weapons, permits, titles, or quests.
- Use stable strings/integers/booleans and defensive defaults.
- Normalize unknown future enum/token values to the existing safe `untracked`/`none` sentinels until
  a versioned `unknown` state is introduced; do not reinterpret them as a live bond or a real ending.
- Bound tombstones/ended bond records by XML or a defensive cap while retaining records still needed
  by saved pending events.
- Reset all static caches in `FinalizeInit`; they otherwise leak across exit-to-menu and another save.

## 15. XML, prompts, settings, and localization

### 15.1 `DiaryRoyaltyPolicyDef`

Add one policy Def with safe C# fallbacks for:

- separation debounce and reconciliation cadence;
- bond tombstone retention/caps;
- qualifying tale defNames, explicit killer/victim role corrections, and optional victim/significance
  conditions;
- exact same-`Pawn.Kill` companion Tale defNames and roles, separate from qualification;
- trait structural weights and worker-category mappings;
- maximum selected persona traits;
- title/psylink cause correlation lifetimes;
- succession correlation lifetime;
- dramatic permit defName -> family mappings;
- prompt text caps for weapon, trait, title, faction, and permit facts;
- optional exact compatibility corrections/exclusions.

All Royalty Def identifiers are plain strings. The XML must not reference a Royalty Def object and
therefore needs no `MayRequire` tag for those matcher entries.

### 15.2 Interaction groups and prompts

Add or refine groups for:

- one `PersonaWeapon`-domain lifecycle group matching the four bond synthetic keys;
- one Tale-domain persona-combat group matching `PersonaWeaponFirstConsequentialKill`;
- the existing `progressionRoyalTitle` group expanded to the new gain/reduced/lost/succession/heir
  synthetic keys while retaining `RoyalTitleChanged` for old saves;
- one `RoyalPermit`-domain group matching the four dramatic family keys;
- one exact Quest-domain Royal Ascent group matching `EndGame_RoyalAscent` and branching on the saved
  completed/failed signal;
- existing bestowing/anima groups with correlated facts.

Use exact `DiaryEventPromptDef.eventType` policies for the synthetic keys where the lifecycle edge
needs different guidance. Keep settings at the relationship/family-group level instead of creating a
separate toggle for every transition.

Prompt instructions should ask for personal meaning, changed identity, dependence, duty, pressure, or
relationship. They must explicitly avoid mechanics summaries and avoid claiming causes absent from
the saved facts.

### 15.3 Settings

Use the existing event-group settings surface. Do not add a master “Royalty support” toggle unless a
later UX review proves it necessary.

- New Royalty-only groups use the existing XML
  `enableWhenPackageIdsLoaded=Ludeon.RimWorld.Royalty` gate. Apply the same gate to the existing
  Royalty-only `ritualRoyal`, `progressionPsylink`, and `progressionRoyalTitle` groups, so the current
  centralized runtime-availability check hides and deactivates them while Royalty is inactive even
  though Pawn Diary's XML Defs themselves still load.
- Keep `DiaryRoyaltyPolicyDef` XML-only in the first slice unless Prompt Studio can hide the entire
  policy group behind the same active-package check. Do not trade XML editability for no-DLC settings
  clutter.
- Existing `Progression`, `Ritual`, `Tale`, `Death`, `Thought`, `Raid`, and `Quest` controls retain
  ownership of reused routes.
- Disabling a persona lifecycle group stops its pages but state should still reconcile safely enough
  not to produce a false catch-up burst when re-enabled. The exact disabled-state policy belongs in
  pure tests.

### 15.4 Localization

- Player-facing labels, fallback event text, settings text, and prompt fragments use Keyed entries in
  `Languages/English/Keyed/PawnDiary.xml`.
- Def labels/instructions/system prompt text use DefInjected localization.
- Captured RimWorld/modded labels and descriptions are localized on the main thread, sanitized, and
  bounded.
- Stable schema tokens such as `bond_formed`, `imperial_bestowing`, `military_aid`, `none`,
  `unknown`, and field names remain English by design.
- `LlmClient` background-thread strings remain outside `.Translate()` per repository policy.

### 15.5 Prompt modes

- Full mode may include weapon name, one or two relevant traits, cause, faction/title, and one duty
  delta when relevant.
- Balanced mode should normally include weapon/title/cause plus at most one trait or duty fact.
- Compact mode must keep the event, actor, before/after or relationship edge, and omit optional trait
  descriptions before required facts.
- Empty optional context produces no blank key or sentinel line.

## 16. File-level change map

Exact filenames may be adjusted to existing conventions, but responsibilities must stay separated.

### 16.1 New production files

- `Source/Defs/DiaryRoyaltyPolicyDef.cs`
- `Source/Pipeline/Royalty/PersonaWeaponContracts.cs`
- `Source/Pipeline/Royalty/PersonaLifecyclePolicy.cs`
- `Source/Pipeline/Royalty/PersonaMilestonePolicy.cs`
- `Source/Pipeline/Royalty/PersonaTraitPolicy.cs`
- `Source/Pipeline/Royalty/RoyalTitleTransitionPolicy.cs`
- `Source/Pipeline/Royalty/RoyalSuccessionPolicy.cs`
- `Source/Pipeline/Royalty/RoyalMutationOwnershipPolicy.cs`
- `Source/Pipeline/Royalty/RoyalPermitPolicy.cs`
- `Source/Pipeline/Royalty/RoyaltyContextText.cs`
- `Source/Models/PersonaBondState.cs`
- `Source/Models/RoyalTitleObservationState.cs`
- `Source/Capture/Events/PersonaWeaponEventData.cs`
- `Source/Capture/Specs/PersonaWeaponEventSpec.cs`
- `Source/Capture/Events/RoyalPermitEventData.cs`
- `Source/Capture/Specs/RoyalPermitEventSpec.cs`
- `Source/Ingestion/Sources/PersonaWeaponSignal.cs`
- `Source/Ingestion/Sources/RoyalPermitSignal.cs`
- `Source/Generation/RoyaltyMutationCorrelationCache.cs`
- `Source/Generation/RoyalPermitOwnerCache.cs`
- `Source/Patches/DiaryRoyaltyPatches.cs`
- `1.6/Defs/DiaryRoyaltyPolicyDef.xml`
- `tests/RoyaltyContextTests/RoyaltyContextTests.csproj`
- `tests/RoyaltyContextTests/Program.cs`
- `tests/PawnDiary.RimTest/PawnDiaryRoyaltyFlowTests.cs`

If the Ideology work has already made `DlcContext` partial, use
`Source/Generation/DlcContext.Royalty.cs`. Otherwise, making it partial is an acceptable small
structural slice documented at implementation time.

### 16.2 Existing production files likely to change

- `Source/Generation/DlcContext.cs` — guarded Royalty projections.
- `Source/Models/PawnArcState.cs` — progression baselines or references to new plain state.
- `Source/Core/DiaryGameComponent.cs` — scribing, normalization, cache reset, and state ownership.
- `Source/Core/DiaryGameComponent.Progression.cs` — persona reconciliation, title-loss fallback,
  psylink/title ownership, and baseline updates.
- `Source/Core/DiaryGameComponent.EventFactory.cs` — new narrow event creation.
- `Source/Core/DiaryGameComponent.EventWindows.cs` — share deterministic mapless witness selection
  with the exact Royal Ascent Quest fanout if a small extraction is needed; retain colony-wide
  mapless active-window semantics.
- `Source/Ingestion/DiaryEvents.cs` — new signal registration/entry points.
- `Source/Ingestion/Sources/TaleSignal.cs` — first qualifying persona milestone and killer-POV
  ownership.
- `Source/Ingestion/Sources/QuestSignal.cs` — exact quest-root classification.
- `Source/Ingestion/Sources/RaidSignal.cs` — quick-military-aid arbitration.
- `Source/Ingestion/Sources/RitualSignal.cs` — claim bestowing/anima mutations.
- `Source/Ingestion/Sources/ThoughtSignal.cs` — bounded persona/title context only when relevant.
- `Source/Capture/Events/TaleEventData.cs` — saved persona context.
- `Source/Capture/Events/DeathEventData.cs` — saved bond-at-death context.
- `Source/Capture/Events/ProgressionEventData.cs` — title cause/faction/succession and psylink cause.
- `Source/Capture/Events/RitualEventData.cs` — saved mutation facts.
- `Source/Capture/Events/QuestEventData.cs` — exact root classification facts if not already exposed
  to pure decision code.
- `Source/Capture/Events/RaidEventData.cs` — quick-aid secondary-source flag if needed.
- `Source/Capture/DiaryEventType.cs` and `Source/Capture/Catalog/DiaryEventCatalog.cs` — register
  `PersonaWeapon` and `RoyalPermit` only.
- `Source/Pipeline/DiaryEventDomainClassifier.cs` — recognize the two new stable source markers and
  preserve Quest root plus signal for exact saved-event recovery.
- `Source/Pipeline/DiaryEventPromptKeys.cs` — change only if tests prove the generic exact-def/group/
  domain precedence cannot already select the new prompt policies.
- `Source/Pipeline/CaptureCapabilityRegistry.cs` — add readiness tokens only if scanner/fallback
  arbitration needs to expose exact-hook health through the existing capability seam.
- `Source/Defs/InteractionGroups.cs` — add the two narrow group domains/classifiers, the exact
  non-catch-all Quest-root-first helper, and an optional default-`AllEligible` Quest fanout policy;
  reuse its existing package-availability gate.
- `Source/Settings/PawnDiarySettings.cs` — typed enablement helpers for the two new domains if signals
  do not call the resolved group's existing `IsGroupEnabled` path directly.
- `Source/Generation/DiaryPipelineAdapters.cs` — recover new domains and exact Quest-root groups from
  saved payloads.
- `Source/Pipeline/PromptContextDetail.cs` — required/optional priorities for new Royalty context keys
  in full, balanced, and compact modes.
- `Source/Settings/AdvancedFieldCatalog.cs` — optional; register Royalty tuning only if the whole
  advanced group can be hidden while the Royalty package is inactive.
- `Source/Patches/DiaryPatchRegistrar.cs` — defensive Royalty target registration.
- `Source/Patches/DiaryEventSignalPatches.cs` — stage quick-aid `RaidFanoutSignal` instances at the
  successful incident postfix instead of submitting them before `Notify_Used` can claim the action.
- `Source/Patches/DiaryDeathPatches.cs` — pre-cleanup persona snapshot.
- `1.6/Defs/DiaryInteractionGroupDefs.xml`
- `1.6/Defs/DiaryEventPromptDefs.xml`
- `1.6/Defs/DiaryPromptTemplateDefs.xml` if a dedicated source template is necessary.
- `1.6/Defs/DiaryEventWindowDefs.xml`
- `Languages/English/Keyed/PawnDiary.xml`
- `Source/Core/DiaryGameComponent.PromptTestSuite.cs` — synthetic persona, permit, title-loss,
  bestowing, and Royal Ascent fixtures.

### 16.3 Tests and documentation to update per shipped slice

- relevant existing pure pipeline/capture/prompt-variant tests;
- `tests/PawnDiary.RimTest/PawnDiaryDefSmokeTests.cs`;
- prompt suite fixtures for every new group and compact mode;
- `DOCUMENTATION.md`;
- `EVENT_PROMPT_MAP.md`;
- `TEST_COVERAGE_PLAN.md`;
- `CHANGELOG.md` with the implementation date;
- `1.6/Assemblies/PawnDiary.dll` after every C# behavior slice.

## 17. Phased implementation sequence

Each phase is a smallest safe change. Do not combine later phases merely because the hooks are in the
same vanilla class.

### Phase 0 — Freeze pure contracts and XML policy

> **Implementation status (2026-07-18): complete.** `RoyaltyContracts` maps persona continuity to
> the existing N0/N1 `bond_lifecycle` + `weapon` contract with
> `royalty-persona|<weaponThingId>|<bondEpoch>`. `DiaryRoyaltyPolicyDef` owns separation/correlation,
> Tale qualification, structural trait weights/mappings, output/text caps, and compatibility rows;
> its detached snapshot retains safe code fallbacks. Pure policies now cover the complete R1
> lifecycle, first-consequential-milestone ownership, structural trait selection, faction-specific
> title transitions/duty deltas, and title/psylink owner/fallback dedup matrix. The assembly-free
> `RoyaltyContextTests` suite passed 164 assertions at that checkpoint. Phase 0 itself claimed no
> loaded-game acceptance; Phase 1 collection/persistence is now implemented as described below,
> while all player-visible Royalty behavior remains unimplemented.

1. Confirm Narrative Continuity N0 token/arc-key contracts and add plain Royalty snapshot/state
   contracts that map to them without referencing live shared providers.
2. Add `DiaryRoyaltyPolicyDef` and safe fallback loading.
3. Implement the R1 pure lifecycle, trait, milestone, title, and mutation-ownership decisions.
4. Add `RoyaltyContextTests` covering all policy without Verse/RimWorld references.
5. Add no capture patches and no player-visible behavior.

Exit gate: pure tests pass; XML parses; the contracts can express every matrix row without live game
objects.

Narrative Continuity N0–N1 must land before Phase 2 creates persona pages. Phase 1 may baseline
Royalty state in parallel, but it must not invent a temporary Royalty-only continuity save schema.

### Phase 1 — Guarded collection and silent baselining

> **Implementation status (2026-07-18): code-complete, loaded-game execution not claimed.** Added
> double-guarded `DlcContext` projections for persona weapons/structural traits, highest title per
> faction with duty categories, and current psylink level. Added a versioned deep-scribed global
> persona ledger plus nested per-pawn faction-title observations, deterministic malformed/null/
> duplicate/cap normalization, conservative old-save bond/title/psylink baselines, and resettable
> plain transient correlation shells cleared from `FinalizeInit`. Existing bonds consume the
> historical first-kill milestone and all baselines are silent. `RoyaltyContextTests` now passes 187
> assembly-free assertions; `PawnDiaryRoyaltyStateFixtureTests` builds with the RimTest assembly and
> covers Scribe round-trip, missing-key markers, and no-Royalty collectors. No Phase-2 hook, page,
> provider, prompt, setting, or other player-visible behavior was added. A real loaded-game RimTest
> run remains an explicit acceptance item rather than an inferred claim.

1. Extend `DlcContext` with guarded persona/title/psylink projections.
2. Add deep-scribed persona state and normalization.
3. Baseline existing bonds/titles/psylink silently.
4. Add/reset transient cache shells.
5. Add save round-trip and no-DLC RimTests.

Exit gate: old/new saves load, no pages change, no-DLC is clean, Debug build passes, docs/changelog
describe structural behavior where applicable.

### Narrative N3-R core dependency — Persona/title provider and evidence

> **Implementation status (2026-07-18): complete; loaded-game execution not claimed.** The fixed
> Narrative Continuity provider list now replaces the Royalty empty stub with bounded plain persona
> and faction-title facts. Persona candidates require the exact frozen bond arc or weapon subject;
> title candidates require the exact POV pawn plus Royalty title-domain or authority/status/duty
> evidence, preventing unrelated identity pages from receiving generic rank context. The existing
> persona mapper and the new title-transition mapper emit shared `bond_lifecycle` and
> `identity_transition` evidence; title/faction facts never become a Royalty-only arc key. The guarded
> main-thread adapter copies current Phase-1 bond/title truth, formats optional prose from
> `DiaryRoyaltyPolicyDef`/DefInjected fields, and passes snapshots through existing build requests.
> No existing event supplies Royalty evidence yet, so this dependency is structurally inert until
> Phase 2 or Phase 4's canonical owner attaches it. Court pressure remains deferred to the Wave-9
> N3-R extension. Pure suites pass 196 Royalty and 125 Narrative Continuity assertions. No Harmony
> hook, page source, setting, save field, or Phase-2 lifecycle behavior was added.

### Phase 2 — Persona lifecycle pages

> **Implementation status (2026-07-18): code-complete; automated loaded suite user-confirmed green;
> manual acceptance still open.**
> `DiaryRoyaltyPatches` defensively registers exact `CompBladelinkWeapon` coding, equipment,
> destruction, map-removal, and `UnCode` seams only while Royalty is active. Live reads remain behind
> `DlcContext`; `PersonaLifecyclePolicy` commits the deep-scribed bond row before optional dispatch.
> Formation and exact transfer start one epoch page, short swaps remain pending/silent, the independent
> elapsed reconciliation deadline emits one threshold separation and only its corresponding recovery,
> destruction emits one standalone ending, and pawn death/map removal/unknown cleanup remain silent
> owners for later reconciliation or Phase 3. Historical bonds missed by the first loaded-map baseline
> are adopted silently when they become visible, and current provider facts require exact live coding.
> Vanilla bonded thoughts are situational rather than memory callbacks, so the dead Phase-2 staging
> path was removed; exact thought ownership remains reserved for Phase-3 kill memories.
> `PersonaWeaponEventData`/Spec, bounded context formatting, the exact `PersonaWeapon` domain/group,
> four event-prompt rows, append-only `SoloImportant` projection, bilingual localization, and four dev
> prompt fixtures are present. N3-R persona evidence is attached after the page exists. Pure coverage
> passes 226 Royalty, 665 capture, and 2,265 pipeline assertions after the Phase-3 additions; the core
> and focused real-coding/hook-audit RimTest DLLs build. The user confirmed the Phase-2 automated loaded
> suite green. The hands-on exit-gate scenarios still require a recorded pass, so this phase is not
> marked manually acceptance-complete.

1. Defensively register coding, equipment evidence, destruction, and cleanup hooks.
2. Emit bond formation.
3. Verify bonded-thought delivery; because vanilla uses situational thoughts, keep Phase 2 out of the
   memory hot path and reserve exact staged ownership for Phase-3 kill memories.
4. Add slow reconciliation for separation/recovery.
5. Add destruction/transfer ownership.
6. Add XML groups, prompts, settings, localization, prompt fixtures, docs, changelog, and DLL.

Exit gate: bond, short-swap, separation, recovery, destruction, map-removal, transfer, and save/load
acceptance scenarios pass.

### Phase 3 — Persona combat and death integration

> **Implementation status (2026-07-19): code-complete; focused loaded rerun 244/244; automated loaded
> coverage green and manual acceptance pending.** A qualifying Tale is resolved only from exact XML killer/victim roles
> inside the matching active `Pawn.Kill` scope while the exact coded weapon is still the killer's
> primary. The saved first-kill truth advances even when its Royalty-gated group is disabled or page
> creation fails; only a durable accepted page sets the separate recorded flag. The canonical page is
> forced to one solo killer POV, retains `tale=` plus the source Tale/role facts, and attaches N3-R bond
> evidence without creating a standalone persona domain marker. Exact same-call companion Tales and
> persona-trait `killThought` Defs are staged losslessly; only the durable page claims them, while a
> disabled/rejected milestone releases every ordinary path. Late Thought ownership is per-Def and
> one-shot, with fail-open bounded buffers. The death prefix snapshots persona facts before vanilla
> `UnCode`, including a still-coded non-primary live bond, and the existing Tale/fallback death owner
> appends them without emitting `PersonaWeaponBondEnded`. Save normalization preserves observed versus
> recorded and repairs recorded-implies-observed. Pure suites pass 245 Royalty, 665 capture, and 2,290
> pipeline assertions; expanded real-kill/delayed-batch/disabled-output/non-primary-death/Scribe
> RimTests compile. The first loaded run exposed that `KilledMajorThreat` precedes `health.SetDead()`;
> the balanced exact kill scope now supplies death proof at that callback. The expanded rerun's two
> remaining failures were fixture-only combat-batch queries: `talecombat` is a saved `group=` value,
> while the event Def is the source Tale or `TaleCombatBatch`. The corrected focused rerun passed all
> 244/244 loaded tests. Pure baselines are 245 Royalty, 2,290 pipeline, 665 capture-policy, and 125
> Narrative Continuity assertions. The Phase-3 hands-on matrix remains unchecked, and R1 still waits
> on Phase 4.

1. Project persona context into `TaleSignal` at capture time.
2. Add pure qualifying-milestone and trait selection.
3. Mark the qualifying gameplay milestone observed regardless of group enablement; track accepted
   page creation separately so a later kill is never presented as the first.
4. Stage/claim exact kill-thought and same-call companion-Tale side effects, with lossless ordinary
   fallback when no milestone owns them.
5. Snapshot bond context in the existing death prefix/cache.
6. Add per-POV dedup tests and preserve victim death behavior.

Exit gate: first qualifying kill and wielder death each produce one canonical page; ordinary kills do
not flood; R1 persona scope is complete.

### Phase 4 — Title and psylink correctness

> **Implementation status (2026-07-19): code-complete; pure/build and automated loaded coverage green
> at 252/252; manual acceptance pending.** The exact private
> `OnPostTitleChanged(Faction faction, RoyalTitleDef prevTitle, RoyalTitleDef newTitle)`
> callback and three cause boundaries register defensively through `DiaryRoyaltyPatches`; changed
> targets warn once and retain the scanner. Exact gained/promoted/demoted/lost decisions use faction ID
> and structural seniority, advance the per-faction baseline before optional dispatch, and include
> disappeared-faction loss in scanner fallback. Bestowing/anima stage bounded detached mutations for
> the existing ritual owner, neuroformer owns one cause-aware progression page, unknown/expired owners
> fail open at most once, and exact `Thought_MemoryRoyalTitle` signals stage/claim/release unchanged.
> Observation continues while output is disabled; inactive Royalty marks observation unavailable but
> preserves saved truth for a later silent baseline. XML owns matching, windows, caps, prose, and prompt
> projection; English/Russian data and append-only SoloImportant fields 107–112 are covered. Pure suites
> pass 287 Royalty, 2,437 pipeline, 665 capture-policy, and 125 Narrative Continuity assertions. Runtime
> and RimTest assemblies build. The second loaded run confirmed the exact postfix and ritual-catalog
> fixes, then exposed a same-tick title-edge dedup collision and one Phase-3 fixture dependency on other
> mods' equipment-removal patches. Both were corrected, and the subsequent user-confirmed loaded rerun
> passed 252/252. The hands-on matrix still must run before R1.

1. Register the private post-title hook defensively.
2. Emit faction-aware gain/promotion/demotion/loss and update the scanner baseline.
3. Fix scanner title-loss fallback.
4. Add bestowing/anima/neuroformer cause scopes and delayed fallback ownership.
5. Attach mutations to ritual events and suppress matching progression duplicates.
6. Add full tests, docs, changelog, localization, and rebuilt DLL.

Exit gate: R1 is releasable.

### Phase 5 — Succession

1. Add the pure succession policy and its deterministic tests.
2. Register the nested `TryInherit` candidate hook plus the outer `Notify_PawnKilled` commit scope.
3. Persist only committed pending succession facts and correlation expiry.
4. Add succession progression context and later title/bestowing claiming.
5. Spike the exact explicit-heir source; ship appointment only if it is distinguishable from
   automatic assignment.
6. Add death/title/bestowing dedup fixtures.

Exit gate: all succession claims name only relationships vanilla reported exactly.

### Phase 6 — Dramatic permits

1. Add the pure permit policy and its deterministic tests.
2. Populate/reset the permit-owner cache.
3. Capture successful allowlisted use.
4. Add permit event type, groups, prompts, and format modes.
5. Add stage/claim/expiry arbitration for quick military aid in `RaidSignal` and the elapsed cache
   flush.
6. Verify every excluded permit remains silent.

Exit gate: R2 is releasable.

### Phase 7 — Royal Ascent and ambient pressure

1. Add exact quest-root/ownership policy tests.
2. Add exact non-catch-all quest-root-first classification and use the resolved group consistently
   for settings/instructions.
3. Add the default-preserving XML Quest fanout policy and set Royal Ascent to one stable witness.
4. Add start-only event window and stable witness selection.
5. Add active prompt shading and the exact terminal group/policy.
6. Verify no accepted/terminal/window-end or all-colonist duplicates.
7. Attach the required journey/pressure evidence and shared arc references; verify the Royalty
   provider stays empty when no exact Ascent/authority context applies.

Exit gate: R3 is releasable.

### Phase 8 — Compatibility and release hardening

1. Test representative modded persona traits and permit Defs.
2. Exercise missing/private-hook fallback behavior.
3. Test exit-to-menu/load cache clearing and time-skip reconciliation.
4. Run all pure suites, focused RimTests, Def smoke tests, Debug build, and verification hook.
5. Reconcile docs, prompt map, coverage plan, changelog, and DLL.

## 18. Test plan

### 18.1 Pure `RoyaltyContextTests`

Persona lifecycle:

- new coding during play -> one formation;
- load baseline -> no formation;
- repeated equip -> no formation;
- lost then recovered before threshold -> no event;
- pending then pawn goes off-map/unobservable -> no elapsed-time separation;
- threshold elapsed -> one separation;
- repeated separated observations -> no repeat;
- recovery without emitted separation -> none;
- recovery after emitted separation -> one recovery;
- destroyed -> one ending;
- map removed -> none;
- pawn death -> death ownership, no persona ending;
- exact transfer -> one new bond epoch with bounded prior context;
- unknown uncode -> fail closed/reconcile;
- disabled group -> no catch-up burst on re-enable.

Persona milestone and traits:

- qualifying first tale with group enabled -> enrich and mark observed/page-recorded;
- qualifying tale while the bonded weapon is not the current weapon -> does not consume the bond
  milestone;
- qualifying first tale with group disabled or rejected -> mark observed but do not relabel a later
  kill as the first;
- second qualifying tale -> no repeat;
- nonqualifying tale -> current behavior unchanged;
- killer/victim ownership preserves victim death route;
- qualifying tales that would normally batch or pair become exactly one solo killer milestone;
- initiator-killer and recipient-killer Tale mappings both identify the correct pawn;
- ambiguous or death-context-mismatched killer role fails closed;
- an exact verified kill memory is claimed by the matching Phase-3 milestone, while an unmatched or
  unclaimed memory retains current Thought behavior;
- kill-thought trait outranks unrelated equipped-hediff trait;
- no relevant trait -> weapon only;
- more than maximum -> deterministic bounded selection;
- localized/modded wording does not affect rank;
- malformed/oversized labels are sanitized/capped.

Title/psylink:

- first title, promotion, demotion, loss, and no-op;
- two factions with the same localized title remain distinct by stable ID;
- hook event updates scanner baseline and prevents duplicate;
- title/psylink changes while Progression is disabled update observation state but emit nothing and
  do not replay on re-enable;
- missing hook scanner fallback includes loss;
- bestowing claims title plus psylink and emits one ritual;
- matching title award/loss thought is claimed by title/ritual ownership and unmatched thought falls
  back unchanged;
- anima linking claims psylink and emits one ritual;
- neuroformer owns one progression;
- expired unclaimed mutation emits at most one fallback;
- mismatched pawn/level cannot claim a mutation;
- compact format keeps before/after and omits optional duty text first.

Succession/permits/ascent:

- exact candidate plus outer `wasInherited` commit creates one fact;
- candidate without outer commit creates none;
- unrelated death/title change cannot claim it;
- expired succession cannot relabel a later promotion;
- equal/higher-title heir produces no succession page or false promotion;
- automatic heir assignment is silent;
- each allowed permit maps to the expected family;
- unknown/resource/labor permits fail closed;
- ambiguous permit owner is silent;
- matched staged quick aid is claimed by the later permit callback and suppresses friendly-raid
  fanout;
- unmatched staged quick aid expires into the existing raid fallback;
- ascent acceptance starts one window/page;
- acceptance does not claim arrival;
- active window adds context but no page;
- completion/failure closes without a window-end duplicate.
- Royal Ascent terminal fanout selects one stable witness while ordinary quest groups still select
  all current eligible colonists.

### 18.2 Existing pure suites

Extend or keep passing:

- `DiaryCapturePolicyTests`
- `DiaryPipelineTests`
- `PromptVariantsTests`
- `DiarySaveNormalizationTests`
- any event-window, retention, decoration, and prompt-enchantment suites affected by the contracts.

Tests must prove compact/full behavior, save normalization, unknown token safety, and unchanged base
events when Royalty context is empty.

### 18.3 RimTest scenarios

Use direct fixture setup and deterministic helper calls; do not wait for random tales, quest rolls, or
permit availability.

- Royalty inactive/no tracker -> all collectors empty and hooks no-op.
- Persona bond round trip with saved state.
- Real coding creates one formation; a late-visible missing row is silently adopted and `UnCode`
  removes saved-only current context.
- All six exact `CompBladelinkWeapon` lifecycle methods retain Pawn Diary's owned patch methods.
- Short swap versus persistent separation/recovery.
- Destruction versus map removal.
- First qualifying kill with a real victim and preserved death handling.
- Pawn death snapshots the weapon before vanilla uncode.
- Title gain and complete loss; hook and fallback variants.
- Bestowing/anima mutation ownership using controlled fixture signals.
- Successful dramatic permit and military-aid raid arbitration.
- Exact Royal Ascent start, active context, completion, and failure.
- Exit-to-menu/new-game cache reset.
- Def smoke test for all new groups, prompts, policy, and windows.

### 18.4 Verification commands

Run after the relevant phase:

```powershell
dotnet run --project tests\RoyaltyContextTests\RoyaltyContextTests.csproj
dotnet run --project tests\DiaryCapturePolicyTests\DiaryCapturePolicyTests.csproj
dotnet run --project tests\DiaryPipelineTests\DiaryPipelineTests.csproj
dotnet run --project tests\PromptVariantsTests\PromptVariantsTests.csproj
dotnet run --project tests\DiarySaveNormalizationTests\DiarySaveNormalizationTests.csproj
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
powershell -ExecutionPolicy Bypass -File .githooks\verify.ps1
```

Run focused RimTests through the repository's documented RimTest workflow and inspect Player.log for
Harmony target warnings, XML/config errors, duplicate pages, and no-DLC null errors.

## 19. Acceptance scenarios

1. **No Royalty:** A base-game pawn produces the same diary behavior as before, with no warning or
   empty Royalty line.
2. **Old save with persona weapon:** Loading silently establishes the bond; no fake “we just bonded”
   or “first kill” page appears.
3. **New bond:** Coding a persona weapon during play produces one page naming the pawn and saved
   weapon.
4. **Ordinary swap:** The pawn briefly equips another weapon and returns; no separation or recovery
   page appears.
5. **Real separation:** The bond remains but the weapon stays away beyond policy; one separation page
   appears, followed by one recovery page only after reunion.
6. **Destroyed weapon:** Destruction closes the bond once; map removal and save/load do not imitate
   destruction.
7. **Wielder death:** The death page mentions the bonded weapon; there is no second bond-ending page.
8. **First consequential kill:** One existing combat page records the bond milestone and relevant
   trait; later ordinary kills use normal behavior.
9. **Modded persona trait:** A structurally recognizable modded trait can be selected without a
   shipped English-name list; an unknown trait is safely omitted.
10. **Promotion and loss:** A pawn's promotion and later complete title loss both name the correct
    faction and before/after state.
11. **Bestowing:** One ritual page contains the actual title/psylink result; no matching progression
    page follows.
12. **Anima link:** One ritual page contains the new level; no generic psylink duplicate follows.
13. **Neuroformer:** One progression page identifies the known source without inventing a ritual.
14. **Modded psylink source:** The fallback scanner produces one unknown-source progression page.
15. **Inheritance:** The eligible heir remembers the exact deceased titleholder; a later ceremony
    does not restate inheritance as a new discovery.
16. **Automatic heir:** Vanilla silently assigns a fallback heir; Pawn Diary does not call it a
    deliberate appointment.
17. **Military-aid permit:** The caller gets one permit page; the same troops do not create a
    colony-wide friendly-raid fanout.
18. **Routine permit:** A resource or labor permit remains silent.
19. **Royal Ascent start:** One stable witness writes about undertaking the chapter; the entry does
    not claim the Stellarch has arrived.
20. **Royal Ascent pressure:** Relevant later pages can feel the active court pressure without extra
    periodic pages.
21. **Royal Ascent ending:** Success or failure creates one exact terminal page and closes the window.
22. **Ideology absent/present:** Royalty page eligibility is identical; only optional event-relative
    belief context differs when the resolver is available.
23. **Save/load and rename:** Saved entries retain event-time weapon/title/faction text after live
    state changes.
24. **Second loaded game:** No permit owner, mutation cause, or persona state leaks from the previous
    save's static cache.

## 20. Performance, safety, and failure handling

- Every live Royalty access begins with `ModsConfig.RoyaltyActive` and the relevant tracker/comp null
  guard.
- Scanner/reconciliation branches also gate before comparing saved state, so DLC deactivation cannot
  masquerade as title loss, bond ending, or psylink mutation.
- Do not patch `Pawn.Tick`, `Thing.Tick`, `Hediff.Tick`, or another hot generic path.
- Reconcile persona separation through the existing elapsed-time progression cadence and only for
  tracked bonds.
- Treat the permit-owner `GetPermit` postfix as a potentially frequent UI path: gate first, perform
  one bounded dictionary update, and do no translation, LINQ, pawn scan, or allocation on an
  unchanged mapping.
- Hot patch bodies gate on DLC/activity/common-case checks before allocation, translation, LINQ, or
  reflection.
- Register private/fragile hooks individually through `DiaryPatchRegistrar`; one missing target logs
  one warning and leaves the scanner/generic route working.
- Copy exact original parameter names and full signatures from the installed assembly when writing
  Harmony patches.
- Do not patch a generic method or assume an inherited method is declared on a derived type.
- Keep transient caches bounded, expiring, and reset on every `FinalizeInit`.
- Use no long-lived live `Pawn`, `Thing`, `Quest`, permit, title, or Def references in event payloads
  or static caches.
- If owner/cause/correlation is ambiguous, omit the claim or use the existing generic fallback.
- Translate and sanitize on the main thread. Background prompt work receives plain strings.
- Matcher lists use safe string defNames; no DLC `DefDatabase.GetNamed` call and no direct XML DLC Def
  reference.
- No Royalty-specific error is allowed to stop vanilla title, permit, equipment, quest, tale, death,
  or ritual behavior. Patch bodies catch and report through existing patch safety conventions.

## 21. Risks and mitigations

| Risk | Mitigation |
|---|---|
| The private title method changes in a RimWorld update. | Defensive target lookup, one warning, and retained slow scanner fallback. |
| Equipment callbacks turn ordinary swaps into relationship drama. | Persisted debounce state; callback is evidence only. |
| The bladelink kill callback cannot identify the victim. | Use `TaleSignal`/death context as canonical; never infer a victim there. |
| Load restoration looks like a new bond. | Gate program/Scribe state and baseline old saves before emission. |
| `UnCode` conflates death, destruction, and map removal. | Capture caller cause before cleanup; apply explicit precedence; map removal stays silent. |
| A persona weapon is transferred after being destroyed/recreated by a mod. | Stable Thing ID + epoch when exact; otherwise begin a new bond without an invented prior owner. |
| Bestowing creates ritual, title, and psylink pages. | Hold mutations for ritual claiming, then expire to one fallback only if unclaimed. |
| Inheritance rank is delayed. | Persist exact pending succession; never infer from later rank alone. |
| `SetHeir` includes automatic assignments. | Require exact explicit-source scope or defer appointment pages. |
| Permit callback lacks owner. | Small `GetPermit` session cache; ambiguous owner fails closed. |
| Military aid is also a friendly raid, and the raid postfix runs before permit `Notify_Used`. | Stage exact quick-aid raid signals briefly; the later permit callback claims them, while unmatched signals expire into the current fallback. |
| Quest acceptance is mistaken for Stellarch arrival. | Start wording says preparation; arrival remains deferred without exact evidence. |
| Royalty scope duplicates the Ideology plan. | Royal events emit optional facets/topics only; belief resolver remains an independent client. |
| Modded labels or descriptions crowd the prompt. | Main-thread sanitization, per-field caps, maximum trait count, compact-mode precedence. |
| Disabled groups cause delayed catch-up pages. | Continue safe reconciliation or baseline on re-enable according to a tested pure policy. |

## 22. Deferred follow-ons

Only consider these after R1–R3 are stable:

- a verified meditation-focus identity snapshot for psychic reflection;
- a first consequential persona-weapon fight without a kill, if an exact non-noisy source is proven;
- exact Royal Ascent host-map correlation when vanilla supplies one unambiguously;
- exact Royal Ascent arrival/departure midpoint phases;
- richer persona jealousy/wielding-another-weapon moments if they can be made non-noisy;
- long-lived persona weapon transfer history beyond one previous bond;
- compatibility adapters for modded persona weapon systems that do not use `CompBladelinkWeapon`;
- court ceremony types beyond current ritual capture;
- an arc-reflection summary that can cite several already-saved Royalty events without creating new
  gameplay hooks.

None of these should weaken the one-action/one-page rule or expand the initial permit allowlist by
default.

## 23. Coding-agent handoff checklist

Before each phase:

- [ ] Re-run CodeGraph on the exact symbols/files being changed.
- [ ] Re-check the relevant RimWorld 1.6 method signatures and parameter names locally.
- [ ] Read the relevant lore file for Harmony, pawns, saving, performance, Defs, or incidents.
- [ ] Confirm the phase can no-op cleanly without Royalty installed.
- [ ] Identify the canonical source and every secondary source that could duplicate it.
- [ ] Add or update the pure test before wiring the impure hook.
- [ ] Keep thresholds, allowlists, category mappings, prompt policy, and text caps in XML.
- [ ] Use plain DTOs across the pure boundary and event-time snapshots across async work.
- [ ] Register fragile patches defensively and preserve the existing fallback path.
- [ ] Update comments for novice C#/RimWorld readers in every touched source file.
- [ ] Update documentation, prompt map, coverage plan, changelog, and localization with behavior.
- [ ] Run focused tests, relevant existing pure suites, Def smoke tests, RimTests, and Debug build.
- [ ] Stage/review the rebuilt `1.6/Assemblies/PawnDiary.dll` whenever C# changed.
- [ ] Inspect the final diff for unrelated edits in the already-dirty worktree.

Implementation should begin with Phase 0 only. Do not wire a Harmony hook until the plain lifecycle,
ownership, dedup, and save-normalization decisions it feeds are represented by deterministic tests.
