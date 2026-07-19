# Pawn Diary - Maintainer Guide

Last updated: 2026-07-15

Related files:

- `AGENTS.md`: detailed rules for code agents and deep architecture constraints.
- `EVENT_PROMPT_MAP.md`: event-to-prompt coverage map.
- `INTEGRATIONS.md`: the shipped public integration contract for other mods (adapter reference).
- `CHANGELOG.md`: milestone history.
- `design/`: design and planning notes kept out of the root — the planned external API capability
  queue (`design/EXTERNAL_API_CAPABILITIES.md`), plus historical implementation/gap-analysis plans
  (`design/EVENT_COVERAGE_PLAN.md`, `design/BODY_PART_EVENTS_PLAN.md`).

## 1. Purpose

Pawn Diary records meaningful RimWorld colony moments and turns them into short diary pages through
configured OpenAI-compatible API lanes. RimWorld loads the compiled DLL at startup through Harmony
patches, Defs, a `GameComponent`, and an inspector tab. There is no `main()`.

Diary pages belong only to free humanlike colonists old enough for first-person writing
(`DiaryTuningDef.minimumFirstPersonAgeYears`, default 7). Animals, prisoners, slaves, enemies,
visitors, non-colonists, and underage colonists do not own pages. If only one participant is eligible,
the event becomes a solo entry. If two eligible colonists are involved, the initiator entry is
generated first and the recipient entry gets hidden continuity from it.

Arrivals and deaths are neutral boundary pages. Arrival pages introduce a pawn's diary and are forced
to the front of that pawn's saved event list; on a new game, non-arrival capture waits until founding
colonist arrivals have been recorded. Loading a save re-arms that founding bootstrap when any
diary-eligible free colonist is missing an arrival page (a session wedged by the pre-2026-07-08
backstory bug, a mid-game install of the mod, or a join that happened while recording was off); the
ArrivalSignal capture drops pawns that already have their page, so healthy saves are untouched.
Death pages end the diary and hide later same-tick events for
that pawn. If RimWorld resurrects the same saved pawn, the death page stays in history but stops
acting as a terminal boundary, so later diary pages can attach, generate, render, and compact normally
until another death occurs.

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, `Languages/`, and the compiled DLL in
`1.6/Assemblies/PawnDiary.dll`. The development checkout also uses `LoadFolders.xml` to expose the
separate in-game test assembly only while RimTest Redux is active. Source and tests are kept in the
repo for development, but the Workshop payload omits source code and other development-only folders.

| Path | Role |
|---|---|
| `About/` | Mod metadata, mod version, preview, icon, dependency declaration. |
| `LoadFolders.xml` | Normal 1.6 load roots plus the RimTest-only development test assembly gate. |
| `1.6/Defs/` | XML-owned policy: event groups, tuning, prompts, styles, UI, text effects. |
| `Languages/` | Keyed and DefInjected English text plus optional translation sources. |
| `Source/Capture/` | Pure Event Catalog payloads and decisions, including Biotech B1 growth/family/birth contracts and Royalty persona-weapon lifecycle identity/dedup plus forced first-kill POV decisions. |
| `Source/Ingestion/` | `DiaryEvents.Submit` bus + one `DiarySignal` capture/emit class per source (impure edge), including Royalty persona lifecycle/Tale enrichment and ritual-owned title/psylink mutation context. |
| `Source/Integration/` | Public API surface for other mods (`PawnDiaryApi`, request DTOs). Contract: `INTEGRATIONS.md`. |
| `Source/Core/` | `DiaryGameComponent` partials: dispatch pipeline, save/load, scans, generation queue. Also `PawnMemoryRepository` (per-pawn memory store; inert until the memory wiring lands). |
| `Source/Generation/` | Runtime context builders, prompt adapters, LLM client, and DLC-safe live reads, including guarded Odyssey location/mobile-home/lifecycle and Royalty persona/title/psylink snapshots. |
| `Source/Pipeline/` | Pure prompt planning, archive eligibility, progression/arc selection policy, request JSON, response cleanup, text decoration, API policy, the DLC-neutral Narrative Continuity contracts/selector/reflection policy, Odyssey lifecycle/journey/location/history/writer/context policy, Royalty persona/title/psylink decisions plus save normalization, and the pure pawn-memory extraction/recall/eviction layer under `Pipeline/Memory/` (inert until wired). |
| `Source/Defs/` | XML schemas and detached snapshot adapters for tuning/policy Defs, including `DiaryOdysseyPolicyDef` and the Royalty policy plus DefInjected provider prose. |
| `Source/Models/` | Scribe-facing saved models and conversions, including detached Odyssey journey/history, Royalty persona/faction-title observation state, and the `MemoryFragment` pawn-memory row. |
| `Source/Patches/` | Harmony startup, domain hooks, inspect-tab/command patches, guarded Odyssey lifecycle seams, and defensively registered Royalty persona coding/equipment/destruction/cleanup plus exact kill/death correlation seams. |
| `Source/Settings/` | Saved settings, API lane UI/controller, prompt/style editors, XML tuning/template override tabs. |
| `Source/UI/` | Diary inspect tab, card rendering, paging, formatting. |
| `tests/` | Standalone pure-helper projects plus the optional in-game `PawnDiary.RimTest` smoke suite. |
| `TEST_COVERAGE_PLAN.md` | Staged roadmap and requirement matrix for complete pure, in-game, runtime, persistence, UI, and compatibility coverage. |
| `prompt-lab/` | Prompt fixture and variant validation harness. |
| `integrations/` | Separate adapter mods for other mods: example/API explorer, RimTalk, SpeakUp, Rimpsyche, VSIE, 1-2-3 Personalities, and Powerful AI Integration. Not loaded in-game until deployed. |
| `scripts/publish.ps1` | Local Workshop payload prep; builds/packages example + SpeakUp by default and all seven adapters with `-PublishAllAdapters`. |
| `scripts/deploy-integrations.ps1` | Creates sibling-mod junctions for every adapter under `integrations/` in the RimWorld `Mods/` root. |

## 3. Runtime Flow

The mod is a loaded RimWorld library, not a standalone program. Startup, capture, generation,
storage, and UI are all framework callbacks around one saved `DiaryGameComponent`.
Runtime code reaches that saved component through `DiaryGameComponent.Instance`; `Current` is kept
only as a compile-time-blocked binary-compatibility alias so it does not shadow `Verse.Current`.

This is the top-level shape:

```mermaid
flowchart TD
    A["RimWorld loads DLL"] --> B["Startup"]
    B --> C["Patch game hooks"]
    B --> D["Register Diary tab"]
    C --> E["Hooked events"]
    F["Tick scanners"] --> E
    E --> G["Submit signal"]
    G --> H["Dispatch"]
    H --> I{"Catalog keeps it?"}
    I -- "No" --> J["Drop"]
    I -- "Yes" --> K["Save diary event"]
    K --> L{"Generate now?"}
    L -- "No" --> M["Pending or catch-up"]
    L -- "Yes" --> N["Build full prompt plan"]
    N --> O["Select context detail"]
    O --> P["Render lane prompt variant"]
    P --> U["LLM request"]
    U --> V["Apply result"]
    V --> K
    K --> Q["Archive old display rows"]
    K --> R["Diary UI"]
    Q --> R
    K --> S["Save/load"]
```

### 3.1 Startup

`DiaryModStartup` is the startup hook. RimWorld runs its static constructor when the DLL loads.

Startup does three jobs:

1. Apply normal Harmony patches.
2. Register fragile reflection patches through `DiaryPatchRegistrar`.
3. Inject the hidden Diary inspect tab onto humanlike pawn and corpse defs.

The Diary tab is hidden from the normal tab strip, but it must still be registered. The inspect
command, diary links, selected pawns, and selected corpses all open that same registered tab.
`PatchAllSafely` also catches partial assembly type-load failures before per-class Harmony patching,
then patches whatever types were available so one reflection problem cannot abort startup.

### 3.2 Capture And Dispatch

There are two ways events reach the diary system.

Harmony hooks submit one-shot events, such as social interactions, mental breaks, quests, raids,
deaths, arrivals, rituals, abilities, and thoughts.

`DiaryGameComponent` tick scans submit slower state-based events, such as work sampling, thought
progression, hediff progression, pawn progression, quest state recovery, day reflections, yearly arc
reflections, event windows, and observed conditions.

Both paths submit a `DiarySignal` through `DiaryEvents.Submit`. From there, every event uses the same
dispatcher path:

1. Confirm the game is in a recordable state.
2. Check `recentEvents` for deduplication.
3. Build a plain capture payload and `CaptureContext`.
4. Ask the pure Event Catalog whether to record, drop, batch, fan out, or route the event.
5. Emit the chosen diary event shape.

Live RimWorld objects stay in the signal adapters. The Event Catalog works on DTOs and primitive
context so its decisions remain testable without loading RimWorld.

### 3.3 Storage And Retention

Recorded events are saved as full hot `DiaryEvent` rows in `DiaryEventRepository`.

Each pawn has a `PawnDiaryRecord` containing references to the hot events that belong to that pawn.
Pair and shared events can therefore stay as one backing event while appearing in more than one
pawn's diary.

Retention keeps recent pages hot and moves old displayable POVs into compact `ArchivedDiaryEntry`
rows owned by `DiaryArchiveRepository`. Archived rows remain visible in the Diary tab, but they no
longer retry generation, receive title backfill, feed opener/previous-entry continuity, or count as
evidence for day/quadrum reflection scans. Narrative Continuity N1 preserves only bounded, prose-free
references and selected-candidate keys on archived rows, then rebuilds pawn-scoped arc/subject indexes
for a later source-owned provider; the archive itself does not create prompt context. Yearly arc
reflections deliberately sample both hot and archived diary pages as memory candidates, then
de-duplicate by event ID so a shared hot/archive page appears once.

**Pawn memory (`design/MEMORY_SYSTEM_DESIGN.md`, implemented but deliberately not wired in).** The
building blocks for a per-pawn associative memory layer exist: the saved `MemoryFragment` model
(tagged, keyworded, importance-weighted text fragments), the `PawnMemoryRepository` store (saved
master list plus per-pawn/deposit-key indexes rebuilt on load, idempotent per pawn+event deposit),
the pure `Pipeline/Memory/` layer (one shared tag/keyword/importance extraction mechanism, a
deterministic similarity selector with seeded gates and a 1-hop spreading-activation pick, and an
importance x recall-recency eviction planner), the `MemoryContextPrompt` field composer, and the
`DiaryMemoryTuningDef` (`Diary_Memory`) tuning surface mirrored by `MemoryPolicySnapshot`. Nothing
references them yet: no capture hook deposits fragments, no prompt template requests the
`MemoryContext` source, `DiaryMemoryPolicy.Snapshot()` has no caller, and no settings checkbox
exists. Player-visible behavior is unchanged; the layer is covered by `tests/PawnMemoryTests` and
goes live only when design §14 steps 4–7 (capture hooks, prompt plumbing, eviction scheduling,
settings toggle) are implemented.

Future wiring must preserve this order and ownership contract: snapshot settings/`Diary_Memory` on
the main thread; after registering a `DiaryEvent`, recall from copied `MemoryFragmentSnapshot` rows
**before** depositing the current event; freeze the result on the first-person POV and update only
the selected live rows' recall bookkeeping; never deposit blank fragment text; then apply per-pawn
eviction followed by the colony-wide cap. `DiaryGameComponent` must scribe the repository under
`pawnMemoryFragments`, rebuild its transient indexes in `PostLoadInit`, and schedule periodic
eviction by elapsed deadlines rather than tick modulo. The detailed source-side checklist lives at
the top of `Source/Pipeline/Memory/MemoryContracts.cs` so the future adapter is hard to wire backward.

### 3.4 Generation

Generation starts only after an event exists in the saved hot store.

`DiaryPipelineAdapters` copy current settings, XML Def policy, localization, and live pawn facts
into pure pipeline contracts. Pure helpers then plan the prompt, build request JSON, parse provider
responses, clean generated text, and decide title behavior.

**Narrative Continuity (Master Waves 1–6 / N1 + N2-B + N2-O + N3-B identity + N3-R core)** supplies the shared persistence and
optional prompt seam for DLC integrations. Each first-person event POV can save bounded, explicitly
known evidence, prose-free references, selected-candidate keys, and frozen `narrativeContext`; old
saves normalize all four to empty. `NarrativeContextBuilder` snapshots
`DiaryNarrativeContinuityDef` on the main thread, collects the fixed provider list, and invokes the
pure selector. Exact Stirring,
Waking, and Void Awakened monolith windows were the first real source-owned evidence emitters: each
saves a `journey_chapter` phase and `anomaly-monolith|0` reference only after its canonical page is
authorized. Canonical Biotech growth pages save an `identity_transition` phase for the child and the
stable saved family arc when available, then pass a guarded plain snapshot through the fixed provider
list. The Biotech provider may add exact family continuity (`since_birth`, directly observed childhood,
or a current exact parent baseline) and the child's visible current non-Baseliner xenotype. It never
enumerates genes, predicts a future xenotype, infers parental emotion, or creates another POV/page.
Only exact lesson/play/care counters qualify as observed upbringing; a prior recorded growth age alone
does not turn a child-only arc into family evidence. Exact `Parent`/`ParentBirth` baseline rows qualify
even when they have no activity count.
Gene identity progression pages now add exact-subject identity evidence and may freeze one N3-B
candidate from the leading gene theme already selected by Phase 5. The candidate key
`biotech|identity|<pawnId>|gene|<geneDefName>` is persisted through the ordinary selected-key history,
so the shared repetition penalty can prefer a fresh applicable lens later. The provider never rescans
or lists installed membership, creates another page, or adds a new save owner; older xenotype-only
snapshots retain their existing xenotype Def-name fallback.
N2-O replaces the Odyssey stub with one bounded mobile-home provider candidate only when the guarded
adapter proves the exact POV pawn is currently inside vanilla's grav field and captures an exact visible
ship/location. A matching committed journey ID upgrades the lens to exact-arc relevance; a Biotech
growth/family page aboard that ship may otherwise use it only as verified ambient home context. The
DefInjected factual unit is frozen at event time, includes no engine/fuel/cell/hidden-site state, and
creates no page. Pure departure/landing evidence factories freeze `departed`, `arrived`, and `returned`
references, including exact ship/place subjects and an optional correlated departure event ID. O1.4's
canonical landing owner now supplies arrival evidence only after its DiaryEvent exists; the factories
still do not authorize an event themselves. N3-R replaces the Royalty stub with bounded persona-bond
and current faction-title candidates. Persona facts require the exact saved persona arc or weapon
subject; title facts require the exact POV pawn plus Royalty title-domain or authority/status/duty
evidence. A Biotech gene/body identity event therefore cannot pull generic rank context. Title
evidence uses the shared `identity_transition` facet and keeps title/faction as event facts rather than
inventing a localized title arc. Phase 2 persona lifecycle pages now attach exact `bond_lifecycle`
evidence after their canonical `DiaryEvent` exists, so those pages can select the saved bond lens.
Title identity remains inert until its Phase-4 owner attaches exact title evidence. Ideology and
Anomaly provider slots remain empty until their source waves;
Royalty court pressure remains deferred to the later N3-R extension. Provider absence, no relevant DLC, unconnected POVs, child-only
arcs, unknown locations/knowledge, or malformed translated format strings preserve the ordinary prompt
with no narrative-context field.

`DiaryPipelineAdapters` copy the frozen context into the plain payload, and first-person template
fields render it only when non-empty, prefixed by DefInjected policy wording. All neutral chronicle and
title templates omit the field. The event-time selector uses the player's global context-detail preset,
so Full/Balanced/Compact budgets choose complete factual lenses before text is saved; prompt assembly
never truncates a selected fact. Archive compaction copies
only references and selection keys, and `DiaryArchiveRepository` rebuilds bounded pawn-scoped exact-arc
and exact-subject indexes after load or retention. Source-specific pages remain their own sole capture
owners; N2-B/N2-O only enrich already-authorized pages and cannot create another event.

N0 froze exactly four evidence facets—`identity_transition`, `bond_lifecycle`,
`journey_chapter`, and `ambient_pressure`—rather than creating generic DLC events. They cover the
planned Royalty persona/title/ascent moments, Ideology conversion/stance interpretation, Biotech
growth/family/mechanitor moments, Anomaly visible transformation/monolith/containment pressure, and
Odyssey departure/landing/home pressure. Arc keys use lowercase source-owned grammar such as
`biotech-family|&lt;birtherId&gt;|&lt;pregnancyHediffId&gt;` (or the child-ID fallback) and
`odyssey-journey|&lt;shipStableId&gt;|&lt;departureTick&gt;`, and
`royalty-persona|&lt;weaponThingId&gt;|&lt;bondEpoch&gt;`; they contain stable IDs only
(never localized labels) and reference equality is ordinal/case-sensitive. N1 serializes the frozen
additive save-key suffixes under each POV/archive row; it performs no retroactive inference or
catch-up on older pages.

**Royalty Phases 0–4 plus Narrative N3-R core (Master Wave 5; last Phase-4 loaded run green at
256/256, expanded 258-test build awaiting execution; hands-on matrices pending)**
freeze the detached R1 boundary and now own persona-weapon lifecycle, first-kill/death enrichment,
and exact title/psylink correctness.
`RoyaltyContracts`
represents persona weapon/trait/bond state, faction-specific title before/after facts, and
psylink/title mutation cause scopes using primitives and copied lists.
`PersonaLifecyclePolicy` advances formation, meaningful separation, recovery, destruction/death,
transfer, unobservable, map-removal, and disabled-output truth deterministically; page eligibility is
a separate result so later re-enabling cannot create catch-up stories. `PersonaTraitPolicy` ranks
structural kill/bond signals and XML worker mappings before bounded exact compatibility overrides,
uses a stable event+Def-name tie break, and selects at most two sanitized facts without parsing
localized wording. `PersonaMilestonePolicy`, `RoyalTitleTransitionPolicy`, and
`RoyalMutationOwnershipPolicy` freeze first-kill ownership, faction/seniority title classification,
and exact bestowing/anima/neuroformer/succession/unknown fallback dedup rules.
`RoyalMutationPageSelectionPolicy` then chooses one independently enabled title/psylink Progression
route from a combined batch, preferring the richer title only when that route is actually enabled.
The XML Def contains
only primitive values and string identifiers with safe code fallbacks; it has no direct Royalty Def
reference. Phase 1 adds `DlcContext.Royalty` collectors double-guarded by
`ModsConfig.RoyaltyActive` plus live pawn/comp/tracker null checks. They copy coded persona weapon and
structural trait facts, the highest title per faction with bounded duty categories, and the current
psylink level; the existing `DlcContext.RoyalTitle*` summary remains available unchanged. A versioned
global `royaltyPersonaBonds` deep list and nested versioned per-pawn faction-title list distinguish an
old missing key from a legitimate initialized-empty ledger. The first available scan baselines
existing bonds/titles/psylink silently; a historical bond first exposed later by a returning caravan or
newly loaded map is also adopted silently instead of being lost behind the global version marker.
That first sight is baseline-only; primary/not-primary lifecycle inference starts on the next
reconciliation observation so one sample cannot both establish history and imply elapsed separation.
Pre-existing bonds conservatively mark their historical first consequential kill observed.
Normalization repairs null/unsafe/duplicate rows, ticks, phases, and caps, while `FinalizeInit` clears
plain future-correlation cache shells so state cannot cross colonies. Royalty-inactive scans preserve
saved title/psylink truth. N3-R projects live Phase-1 truth into plain persona/title provider facts,
applies deterministic caps/order, and uses DefInjected Royalty policy formats with empty fallback on
malformed prose. Persona provider rows are revalidated against the exact pawn's current coded
`bondedWeapon`, so an ambiguous `UnCode` or unavailable saved row cannot masquerade as verified current
context. Exact applicability means existing non-Royalty pages still select no Royalty lens.

Phase 2 defensively registers exact `CompBladelinkWeapon` coding, equip/loss, destruction,
map-removal, and `UnCode` seams only while Royalty is active. The component commits the deep-scribed
bond row before it optionally dispatches `PersonaWeaponSignal`, so an ineligible pawn, disabled
`personaWeaponLifecycle` group, or failed page cannot create later catch-up prose. Coding and exact
transfer start a new bond epoch and one formation page. A short primary-weapon swap remains pending
and silent; an independent XML-cadenced reconciliation deadline emits one separation only after
`60000` observable not-primary ticks, and emits recovery only when that separation page was accepted.
Unavailable/off-map evidence cancels or pauses the inference instead of proving separation.
Destruction owns one standalone ending. Pawn death ends lifecycle state without a Phase-2 page, map
removal is silent, and unknown cleanup remains live for reconciliation. The user confirmed the
Phase-2 automated loaded suite green; its explicit hands-on acceptance rows remain open.

Vanilla persona-trait `bondedThought` rows are situational thoughts, not stored memories, and therefore
never traverse `MemoryThoughtHandler.TryGainMemory`. Phase 2 deliberately has no bonded-memory
correlation scope. Lifecycle context is bounded and projects the
weapon label, phase, previous/new state, localized durations, exact previous pawn when relevant,
ending cause, and at most two mapped persona traits. Internal weapon/pawn IDs, epoch, ticks, and
correlation keys are never prompt-template fields. The Royalty-package-gated exact group and four exact
event-prompt rows route all lifecycle pages through `SoloImportant`; English/Russian DefInjected and
Keyed text plus four prompt-test fixtures cover formation, separation, recovery, and ending. The source
attaches N3-R bond evidence only after page creation. A focused loaded fixture now drives real coding,
late-visible silent adoption across the production one-tick colonist-snapshot boundary, next-cadence
separation inference, `UnCode` context invalidation, and all six exact Harmony targets; its automated
loaded run is user-confirmed green. The remaining hands-on Phase-2 exit-gate scenarios are listed in
`tests/SAVE_COMPATIBILITY_SMOKETEST.md`; title/psylink pages remain Phase 4.

Phase 3 integrates persona combat through existing owners instead of adding a parallel kill source.
The `Pawn.Kill` prefix opens a short-lived exact death scope while the pawn/weapon relationship still
exists. `TaleSignal` accepts a milestone only when the configured Tale rule supplies distinct known
killer/victim roles, the Tale victim matches that scope, and the killer's current primary is the exact
coded weapon. The shipped qualifier is the verified vanilla `KilledMajorThreat`; `KilledMan` is not a
RimWorld 1.6 Tale and is deliberately absent. Vanilla records `KilledMajorThreat` during
`Pawn.DoKillSideEffects`, before
`health.SetDead()`, so that matched balanced scope—not the still-false `victim.Dead` property—is the
death proof at Tale capture time. The first qualifying truth is saved as observed even when
`personaWeaponMilestone` is disabled or page creation is rejected, so re-enabling cannot retell a
later kill as the first. Save normalization also treats a recorded page as proof that observation
occurred while preserving the valid observed-without-page state. An
accepted milestone relabels that one existing Tale as `PersonaWeaponFirstConsequentialKill`, forces
one solo killer POV, preserves `tale=` plus `tale_source_def`, source label, and role markers, and sets
the separate durable-page flag only after repository insertion. It does not add `persona_weapon=`, so
the event remains Tale-owned and existing victim death routing/dedup stays intact.

Vanilla may record an ordinary melee, ranged, mortar, child/colonist/animal, faction-leader, or
capacity Tale around the major-threat Tale in that same call. XML lists those eight exact
initiator-killer/recipient-victim companions. They stage only after their roles match the same active
scope: an accepted milestone owns them, while an unclaimed scope releases them in order to ordinary
Tale batching. `KilledBy` remains outside that list so the victim's existing death route is never
stolen. Defensive companion overflow fails open rather than losing a Tale.

The same prefix copies exact `killThought` Def names only from structural traits on the current coded
primary weapon. Exact pawn/Def callbacks stage once in the active scope. A durable milestone discards
already-staged signals and carries only still-missing expected Defs while that exact scope remains
open, so either Tale/Thought callback order is handled synchronously. The transient buffer is bounded
to 128 unique Defs, de-duplicates repeats, and fails open on overflow. Once `Pawn.Kill` closes, the
Thought callback has no victim identity, so unmatched late memories deliberately follow the ordinary
path rather than a killer-wide recent-owner suppression. Closing an unclaimed scope releases each
Thought and companion Tale through its own failure-isolated ordinary submission. This makes
disabled/rejected milestones lossless without duplicating claimed signals.

For a bonded wielder's own death, `DeathContextCache` captures weapon, bond, and bounded trait facts
before vanilla `UnCode` and appends them to whichever existing Tale/fallback death description wins.
The exact live coded bond remains eligible when separation is pending/separated and the weapon is no
longer primary; primary status is a kill-milestone rule, not a death-ending rule. Pawn death therefore
produces no standalone `PersonaWeaponBondEnded` page. All live Royalty reads remain double-guarded in
`DlcContext`, and the kill hot path gates Royalty before allocating its Thought-name list. Policy XML
contains only string Def names/primitive roles, so a no-Royalty game follows the unchanged
Tale/Thought/death paths. Pure suites pass 245 Royalty, 2,290 pipeline, 665 capture-policy, and 125
Narrative Continuity assertions. The first loaded run's 240/241 timing failure is fixed. The expanded
rerun reached 242/244: both remaining failures were fixture-only combat-batch queries that searched
for the group Def as an event Def instead of reading the saved `group=talecombat; batch=tale` fields.
After those queries were corrected, the focused loaded rerun passed 244/244 on 2026-07-19. This marks
Phase 3 automated loaded coverage green only; its hands-on matrix remains open. That historical
Phase-3 result did not close R1; Phase-4 acceptance status is recorded below.

Phase 4 defensively registers RimWorld 1.6's private
`Pawn_RoyaltyTracker.OnPostTitleChanged(Faction faction, RoyalTitleDef prevTitle, RoyalTitleDef newTitle)`
through the existing
registrar, plus the exact bestowing worker, anima-link completion, and implant-use boundaries. Each
target is resolved by exact signature; a missing/changed method warns once and leaves the independent
scanner active. Live title, faction, duty, actor, item-defName, memory, and psylink reads remain in
double-guarded `DlcContext.Royalty`. Detached policies classify first title, promotion, demotion,
complete loss, and no-op from faction identity and numeric seniority—never localized labels. The exact
callback advances its faction row before optional dispatch, and the fallback scanner diffs removed
faction rows into truthful loss events. Exact title-event dedup keys include transition plus stable
before/after title Def names, so a repeated callback is suppressed without merging two different
same-tick edges such as promotion followed by loss. Observation also advances while Progression is
disabled. If Royalty is inactive, saved observations are preserved and marked unavailable
synchronously in `LoadedGame` (as well as defensively on the scanner), so a paused immediate resave
cannot retain a stale readable marker; the next available pass silently baselines before recognizing
new edges.

Bestowing, anima linking, and neuroformer hooks open bounded, resettable before/after scopes. The
existing bestowing/anima `RitualFanoutSignal` is canonical: it appends the matching title and/or
psylink mutation to the mutated pawn's perspective only, claims only after that child page is stored,
and prevents separate progression pages. Other eligible attendees retain their ordinary ritual
perspective pages without claiming the target's personal mutation context; an attendee page that is
stored first cannot consume the target's pending batch.
If that enabled ritual route never arrives, the pending batch expires into at most one ordinary
truthful fallback. Disabling the Royalty policy master suppresses the canonical bestowing/anima ritual
page itself; disabling either that master or the canonical `ritualRoyal` group consumes the exact
mutation and matching delayed title memory without transferring the ceremony into a Progression/
Thought page. Disabled output still advances observations and expiry. When an expired
combined fallback contains title plus psylink, the title route wins only if enabled; otherwise the
enabled psylink route remains eligible and claims the same batch's title memory. Maintenance is
two-phase: map observers plus exact pending owners in caravans/travelling transporters reconcile
mutation/title truth first, then unmatched title memories release. The live-owner roster also keeps
temporarily off-map colonists out of the dead/departed prune; only expired owners absent from that
roster are silently removed so they cannot occupy the bounded queue forever. Neuroformer
use is matched by the XML-owned parent-item `defName` string and owns one
cause-aware `PsylinkLevel` page; unknown/modded changes remain scanner fallbacks. Exact
hook adapters close at most once before downstream dispatch. Saved title/psylink observation fields are
checkpointed while the boundary closes; if after-state capture/bookkeeping fails before ownership is
consumed, those fields are restored and the active transient scope is cancelled so the scanner can
recover the live change instead of being blinded or poisoning the bounded cache. Exact
`Thought_MemoryRoyalTitle` award/loss signals stage briefly and are claimed only by matching pawn and
title edges. Capture/claim paths never publish unrelated expired rows; the ordered component pass owns
release. Unmatched/expired signals return unchanged to ordinary Thought capture, so no ThoughtDef is
suppressed globally. Before save, live pending mutations become their one truthful fallback, live
scanner-owned title changes reconcile and claim their exact memory, and only the remaining unmatched
memories flush to Thought capture. This keeps both save-and-continue and save/reload single-owner while
still allowing all plain transient scopes to reset through `FinalizeInit`.

Saved Phase-4 mutation context may contain `royal_mutation_pawn`, `royal_cause`,
`royal_transition`, `royal_faction_id`, `royal_faction`, `previous_title`, `previous_title_def`,
`title`, `title_def`, `royal_duty_changes`, `previous_psylink_level`, `psylink_level`, and
`psylink_cause`. The formatter sanitizes/caps all text and drops optional duty prose before required
before/after facts. SoloImportant's append-only indices 107–112 project mutation pawn, cause,
transition, faction, psylink cause, and duty changes; required title/psylink facts reuse earlier stable
fields. Exact English/Russian prompts cover four title transitions, psylink/neuroformer, bestowing,
and anima linking. Pure suites pass 318 Royalty, 2,437 pipeline, 665 capture-policy, and 125 Narrative
Continuity assertions; the runtime and 258-test RimTest assemblies build. The first loaded-game run reached
249/252 and exposed the corrected title-postfix argument name plus ritual-token fixture. The second
run reached 250/252, confirming both fixes, then exposed a distinct same-tick title-edge dedup bug and
a Phase-3 death-fixture collision with other loaded equipment-removal patches. After the dedup identity
 and fixture setup were corrected, the user-confirmed loaded rerun passed 252/252. The adversarial
 hardening expanded and strengthened the loaded suite; its user-confirmed rerun passed 256/256. The two
new save/expiry ownership regressions compile but have not yet had a loaded execution, so 256/256 remains
the last confirmed runtime result. Every Phase-2/3/4 hands-on row remains explicit R1 acceptance work.

**Biotech canonical growth, family continuity, and birth ownership (Master Wave 3 / Phases 0–3,
plus Phase 4 automated hardening)** owns age-7/10/13
mutations after they
actually commit. The existing biological-birthday patch snapshots the child before vanilla work;
the dynamically registered `ConfigureGrowthLetter`/`MakeChoices` hooks claim a birthday only when
both stable methods/fields are available. A real configured letter delays the ordinary Birthday
route and writes a detached `pendingBiotechGrowthMoments` row, so postponing, saving, loading, and
choosing later retains exact ownership without saving a live letter or opening/scanning its UI.
Auto-resolved birthdays use the same before/after diff immediately. Missing hooks/correlation,
empty or failed diffs, disabled canonical settings, expired ownership, and malformed state fail open
to at most one mature ordinary Birthday; a destroyed/missing pawn expires silently.

The pure diff exposes only verified traits, increased passions, nickname changes, a responsibility
boolean, and XML-owned qualitative opportunity wording—never the tier number, option/work lists, or
counts. Phase 2 deep-saves `BiotechFamilyArcState` rows containing stable IDs only. The guarded exact
`HediffWithParents.SetParents(Pawn, Pawn, GeneSet)` observer opens pregnancy/labor arcs from parent
facts; PlayLog `BabyPlay`/`Lesson*` and accepted `GaveLesson`/`WasTaught` memories update
per-child/adult counters before ordinary interaction/thought page settings are consulted. A transient
pair/kind window de-duplicates the two evidence sources, and old saves silently baseline living player
babies/children plus exact `Parent`/`ParentBirth` relations without inventing pregnancy, birth, or
past lessons.

At growth completion, deterministic policy ranks eligible direct parents/birth parents before adults
with enough exact teaching evidence, then engagement band, recency, same-map presence, and stable ID.
The result is child solo, supporter solo, or a child-initiator/supporter-recipient pair. Prompt context
contains the stable family key and qualitative opportunity/upbringing bands, never raw observation
counts. Each consumed growth age advances summarized counters so ages 10/13 describe only newly
observed upbringing while lifetime evidence remains available for supporter selection. Completion
attaches source-owned N1 identity evidence to every generated POV, consumes current trait keys and
newly passionate skill milestones, and marks the age consumed even when page settings suppress output.
Family arcs normalize duplicate/malformed rows, cap supporter rows, retain live minor-child/pending/
event-referenced state, and compact before removing settled unreferenced history. Pregnancy/labor rows
with no child ID stay live only while their exact tracked hediff remains on the birther; interrupted or
completed rows then follow the XML retention grace instead of becoming permanent. The normal
`PregnancyLabor` → `PregnancyLaborPushing` transition replaces the tracked labor ID on the same
pregnancy arc instead of opening a second arc. Multiple children born in the same exact birth tick keep
separate child-keyed arcs while cloning the shared pregnancy/labor and supporter evidence. A known
incoming parent ID matches only that exact saved ID; wildcard correlation is allowed only when the
incoming snapshot lacks that parent. Growth-page replay
checks use the stable child ID and canonical age across both hot and archived events, so supporter-solo
pages stored in the adult's diary still prevent duplicates. `familyArcId` is the complete shared key
(`biotech-family|...`) and is never prefixed twice.

Phase 3 dynamically patches RimWorld 1.6's exact `PregnancyUtility.ApplyBirthOutcome` signature with
prefix/postfix/finalizer ownership. A stack-safe main-thread correlation scope stages only the mature
birth Def names configured in `DiaryBiotechPolicyDefs.xml` (`GaveBirth` and
`BabyBorn`/`Stillbirth` by default) while vanilla completes; the same policy Def owns exact birther and
partner miscarriage Thought names. Only a non-null performed `LordJob_Ritual` marks a ritual birth—a
plain ChildBirth precept supplied by ordinary labor or a growth vat does not. An exact ritual-job
reservation arbitrates the later childbirth-ritual postfix. Once Pawn Diary's postfix commits canonical
ownership, an exception from a later third-party postfix does not release the already-consumed mature
signals. The
postfix resolves the returned child or corpse, records only exact `healthy`, `infant_illness`, or
`stillbirth` outcome and `pregnancy`, `surrogacy`, or `growth_vat` method tokens, attaches the existing
pregnancy/labor arc (or a stable child fallback), and selects at most two unique eligible adults in
birther, distinct genetic-mother, then father order. The child is always the subject, never a POV.

If vanilla naming is unfinished, detached `pendingBiotechBirths` rows save the exact snapshot and
adult writer order plus each writer's event-time name, pawn summary, surroundings, solo/pair
continuity, previous-entry excerpts, weapon, handwriting/decorations, generation eligibility, and
calendar date. A bounded tick poll reloads only the current child/corpse name and emits at the original
birth tick after naming resolves, its deadline passes, or waiting can no longer improve the name. The
historical diary reference is inserted chronologically, before a same-call final-death page, so a
birther who dies in childbirth still retains the birth page inside the arrival/death bounds.
Hot and archived context checks by exact family arc plus child prevent replay. Every emitted POV saves
source-owned `bond_lifecycle` evidence; no gene list or predicted identity is copied. Disabled canonical
settings, no eligible/resolvable writer, invalid snapshots, hook drift, or a thrown owner release staged
mature routes in original order at their captured tick; their diary references use the same historical
insertion rule, so fail-open fallback also stays before a same-call final-death boundary. An exact ritual-job reservation suppresses only the matching later
ritual postfix. `Hediff_Pregnant.Miscarry` closes the matched arc as `loss` and enriches its existing
thought context; unexplained disappearance closes silently as `ended_unknown`, never as an inferred
miscarriage. The complete path gates `ModsConfig.BiotechActive` and remains inert without the DLC.

Phase 4 projects the verified B1 facts through the existing important pair/solo prompt templates.
Growth prompts can receive age, the localized qualitative description derived from the opportunity
band, chosen trait/meaning, up to four changed interests, name/responsibility changes, and exact
supporter/writer roles; birth
prompts can receive the child, outcome, method, named participants, writer roles, and exact ritual or
birther-death facts. Full keeps every non-empty projected field. Balanced and Compact always retain
the event-defining growth opportunity/trait/interests and birth child/outcome/writer roles even when
their optional budget is exhausted; longer explanations and participant names may drop first. Stable
IDs, raw band tokens, numeric growth tiers, ticks, and family correlation tokens stay in detached
ownership state or internal `gameContext` where applicable, but have no prompt-template selector.
English/Russian indexed DefInjected labels are pinned
to the important-template field order, and the dev prompt suite includes localized synthetic growth
and birth previews. The two pending ownership lists now have XML-owned 256-row admission defaults and
a shared hard 2048-row corruption ceiling. Normal load/maintenance preserves every established owner
up to that hard ceiling, even if XML is later lowered or an older save already exceeds the configured
limit; while full, only the incoming growth/birth owner is rejected so its ordinary source fails open.
Defensive hard-ceiling repair remains deterministic and keeps the newest valid rows. The loaded-game growth/birth/no-DLC/old-save and
adapter acceptance matrix remains manual in `tests/SAVE_COMPATIBILITY_SMOKETEST.md`.

Biotech Phase 5 provides pure policy, exact ownership, and a slow fallback observer. `GeneIdentityContracts.cs` defines
detached, cleaned gene facts with stable Def names, endogene/xenogene identity, active/hidden/
suppressed truth, structural effect flags, and an optional qualitative magnitude band. It also
separates complete live installed membership (up to the fixed defensive ceiling), active salience
facts, and exact added/removed mutation facts.
`GeneSaliencePolicy.cs` scores
those facts using the primitive-only fields in `DiaryBiotechPolicyDefs.xml`: structural category
weights, event-delta and gene-kind bonuses, duplicate-category diversity, force/exclude corrections,
label/description/total-text caps, and a 512-row observed-membership default with a fixed 2048-row
corruption ceiling. Selection is deterministic and returns at most four themes;
hidden, inactive, suppressed, malformed, and excluded facts are omitted, and full gene membership is
never an output. `DlcContext.TryCaptureGeneIdentity` is main-thread-only and double-gated by
`ModsConfig.BiotechActive` plus `pawn.genes`; it copies only direct `GeneDef` fields and the base
description, never `DescriptionFull` or arbitrary mod getters. The progression cadence writes a
nested `GeneIdentityObservationState` even when Progression output is disabled. Its frozen additive
keys store an explicit version, current xenotype Def/label, sorted installed Def names, and an
additive `geneObservedMembershipTruncated` marker. The XML-owned cap applies to this saved baseline,
not to exact live before/after snapshots. When the cap is hit, fallback comparison treats membership
as incomplete instead of inventing a removal when an earlier name displaces the final saved row.
Version 2 introduced that marker, so version-1 rows silently establish a fresh baseline once; an
empty list remains distinguishable from an old save because version zero means uninitialized. The old
scalar xenotype fields remain populated for migration. Loading a save while Biotech is inactive
invalidates only this comparison baseline, so saving DLC-off and later re-enabling Biotech performs a
silent rebaseline rather than reporting stale DLC state.

`GeneIdentityTransitionPolicy` compares stable xenotype identity and complete installed membership,
retains exact before-state facts for removed genes, and admits slow membership-only fallback only when
the XML-owned `geneMinimumFallbackChanges` threshold is met (two by default). Stable xenotype Def
changes always qualify; localized label-only changes with the same Def do not, so switching language
cannot invent a page. Membership deltas are suppressed when either snapshot is explicitly incomplete;
stable xenotype identity can still be compared. `GeneIdentityContextFormatter` exposes only selected themes and bounded,
separator-safe labels—never the complete membership set. The event-time context uses
`gene_identity_transition`, previous/current xenotype identity, exact or observed cause, up to four
theme label/description/change/category rows, optional reimplanting pawn identity, and the
`identity_transition` narrative facet. The first valid already-selected theme also supplies the N3-B
identity lens through XML-owned prose and a stable gene Def key; no second gene scan or full-membership
prompt projection occurs.

`BiotechXenogermMutationPatch` defensively registers the verified RimWorld 1.6 public signatures
`GeneUtility.ImplantXenogermItem(Pawn, Xenogerm)` and
`GeneUtility.ReimplantXenogerm(Pawn, Pawn)`. Prefix/postfix snapshots own every non-empty exact
mutation, update the saved observation before the next slow scan, and emit one recipient-only
`GeneIdentityChanged` Progression page. Reimplantation occurs inside `Ability.Activate`; a bounded
same-call scope is claimed only after the canonical page commits, so the outer generic Ability page is
suppressed on success and remains available if canonical dispatch fails. Reverse-stack claiming skips
nonmatching nested abilities, and all three local-Ability patch phases are contained by
`DiaryPatchSafety`. Exact gene-event dedup keys omit the current tick so the configured recent-event
window can suppress adjacent-tick duplicate capture. The scope is cleared at every Game construction
and contains no saved/live reference after the call closes. Empty mutation,
first-old-save baseline, disabled output, one-row fallback noise, and active/suppressed recalculation
remain silent. Every live read is still behind `ModsConfig.BiotechActive` plus `pawn.genes`, and the XML
contains no DLC Def reference or dependency.

The exact reimplant RimTest enters the public vanilla method twice, but the replay is separated by a
fixture-only simulation of the normal xenogerm-regrowth cooldown. RimWorld 1.6.4871 live evidence
showed why this is required: immediately reimplanting again while `XenogermReplicating` remains active
is intentionally lethal vanilla behavior and correctly creates a death page. The fixture removes only
`XenogermLossShock`, `XenogermReplicating`, and the recipient's `XenogerminationComa` before the second
call, then retains the broad no-new-event assertion. A later 227/228 all-DLC run passed this corrected
replay and the N3-B real-implant narrative assertion; its sole failure was a stale test-only official-
DLC group matrix, now synced with the Biotech-gated `progressionXenotype` production row. Production
capture never removes or changes these hediffs.

Biotech Phase 6 adds a human-centered mechanitor lifecycle without polling bandwidth. Each diary's
nested `MechanitorObservationState` stores an explicit observation version, current mechlink flag,
first-controlled/first-combat consumption, bounded stable-ID mech tenure rows, and bounded exact boss
call rows. The first old-save scan copies current Overseer relations silently and consumes historical
“first” milestones; imported relations begin tenure at that scan tick rather than backdating service
from vanilla relation history. Later slow scans normalize once per controller and then maintain tenure.
Exact first-relation and hostile-combat observations consume their milestones even when generation is
disabled, so re-enabling output cannot turn a later occurrence into a false “first.” All live
tracker/relation/name projection is
double-gated in `DlcContext`, so no-Biotech profiles return empty facts. Exact Harmony owners are
`Hediff_Mechlink.PostAdd/PostRemoved`, `Pawn_RelationsTracker.AddDirectRelation` for the string-matched
`Overseer` relation, configured successful `TaleRecorder` combat Tales, the existing `Pawn.Kill`
prefix before relation cleanup, and `CompUseEffect_CallBossgroup` correlated to
`GameComponent_Bossgroup.Notify_BossgroupCalled`, boss `Pawn.SpawnSetup`, and
`GameComponent_Bossgroup.Notify_PawnKilled`. Only hostile target Tales qualify for the first-combat
milestone. A committed controller page claims the generic combat Tale; disabled/failed canonical output
still fails open to generic ownership while the exact observation remains consumed. Mech loss records only a
non-numerically player-named mech or one observed for the XML minimum (15 days by default). Boss defeat
rows save the spawned boss pawn's load ID, so one death resolves one controller across overlapping
same-kind calls; legacy rows backfill only when exactly one matching call is unambiguous. Completed
mech-loss rows are recycled at the observation cap before active ownership is refused. `Pawn.Kill`
starts death-suppression scope only for actual mechanitors, projects loss only for mechanoids, and
balances nested scope release in its finalizer. Boss defeat prose proves only that the caller's saved
threat was defeated and never names a final attacker. XML
owns the combat Tale role lists, tenure threshold, 64-mech and 16-boss defaults, one Biotech-gated
Progression group, and all English/Russian prompt/UI prose; fixed corruption ceilings remain in code.
Every canonical mechanitor page includes the stable `mechanitor_moment=<token>` context field. Exact
mechlink installation/removal use `mechlink_installed` / `mechlink_removed` alongside the controller
label, so prompt and test consumers never need to infer the lifecycle edge from localized prose.
Mechlink callbacks on an unspawned generated pawn establish the saved present/absent baseline silently;
only a spawned controller in inactive Scribe mode can emit an install/removal page. This preserves a
real first live operation while preventing pawn generation or restoration from inventing history.

Live surroundings are optional prompt flavor and are collected fail-soft. In particular,
`Room.GetRoomRoleLabel()` can lazily recalculate the room's stats/role; if RimWorld or another room
patch throws during that recalculation, Pawn Diary omits only the room-role label and continues
recording the event and collecting the remaining context. The frequently sampled fallback is silent
so one unstable room cannot flood the log.

`LlmClient` owns the background HTTP work for OpenAI-compatible API lanes. It handles retries,
failover, cooldowns, timeouts, and session cancellation. Finished results return to the main thread,
where the matching `DiaryEvent` is updated with text, status, model metadata, titles, and unread
flags.

Completed LLM results are drained from both `GameComponentTick` and `GameComponentUpdate`. That means
requests already in flight can still finish while the game is paused.
Do not use `LongEventHandler.ExecuteWhenFinished` as a background-to-main-thread marshal for this
path; queue results and drain them from a real main-thread hook such as `GameComponentUpdate` or
`OnGUI`.

### 3.5 Tick Work And Catch-Up

`GameComponentTick` always runs cheap game-time scans.

Expensive generation catch-up is demand-driven. Load catch-up, delayed raid pages, and orphaned
"writing..." recovery request a pass; the pass scans only the XML-tuned `activeScanEventWindow` hot
events, which defaults to 1000 events globally.

Pending LLM work itself is not saved. After load, only hot events still inside the active scan window
can be requeued.

First-person generation is skipped for pawns below the XML Consciousness floor. Neutral arrival and
death pages bypass that guard because they are boundary chronicle pages, not first-person writing.
Resurrected pawns reuse their existing `PawnDiaryRecord`; generation checks ignore an older death
boundary while the same pawn load ID is alive again.

### 3.6 Ingestion Bus (`DiaryEvents.Submit`)

Every captured event enters through `DiaryEvents.Submit(signal)` (`Source/Ingestion/`). A Harmony
hook or scanner builds the matching `DiarySignal` subclass and submits it; the dispatcher then runs
the universal path:

| Step | What happens |
|---|---|
| Guard | `CanRecordGameplayEventNow` rejects events when the game is not in normal play. |
| Starting-arrival flush | On new games, any non-arrival signal first tries to record founding-colonist arrivals, so the arrival page remains the first diary page even if a Harmony source fires early. Each colonist is isolated: a pawn whose context build or capture throws loses only their own arrival page, and the flush still completes so the gate cannot wedge the diary. |
| Dedup check | `recentEvents` rejects duplicate source keys before payload/context work runs. |
| Decide | `DiaryEventCatalog.Get(payload.EventType).Decide(payload, ctx)` applies pure XML-backed policy. |
| Generic dedup | A short XML-tuned event-type safety key (`genericEventTypeDedupTicks`) rejects repeat type+subject emissions after the decision. Death descriptions share one key across Tale and fallback sources. |
| Dedup mark | Source and generic keys are marked only after the catalog keeps the event. |
| Emit | `signal.Emit(sink, decision)` creates the selected diary event shape and queues follow-up work. |

The dedup order is intentional. The check happens before `Decide`, so a duplicate does not build
context, run pure policy, or consume source-side random state. `AbilitySignal` depends on this because
its chance roll is read lazily from `Rand.Value`.

The generic event-type check happens after `Decide` because it needs the payload event type and final
decision shape. It is a short safety net for sources with no detailed key and for cross-source shapes
that should collapse together, currently neutral death-description pages.

The mark happens after `Decide` and both dedup checks. If the catalog drops an event, such as an
ability that fails its chance roll, the dedup window is not consumed.

A `DiarySignal` is the impure capture+emit half of one source. Pure decision data and game-context
formatting stay in `Source/Capture/Events/*EventData.cs`, covered by standalone tests where possible.
Colony-wide sources extend `DiaryFanoutSignal`.

For fan-out events, the dispatcher checks the colony key once. It then runs each per-pawn child
through the same path and marks the colony key only after at least one entry emits.

Every `DiaryEventType` now routes through this bus.

One-shot Harmony captures submit directly. Scanner and flush sources are still triggered by component
scans, but they also submit signals. A scan whose episode state depends on whether the entry recorded
can call `Dispatch` directly and read its `bool` result.

Adding a source means adding the hook or scanner, a signal, the catalog `Spec`, and XML policy.
Shared guard, dedup, decision, and emission glue stays in `Dispatch`.

The old per-source dedup dictionaries are gone. `recentEvents` stores the source-prefixed or generic
event-type key, that key's own dedup window, and the recorded tick.
`Source/Capture/RecentEventExpiry.cs` owns the pure expiry rule, and
`Source/Capture/GenericEventTypeDedup.cs` owns the generic key format.

A short-window source cannot evict a still-live long-window key. A zero or negative window opts out
of dedup instead of clearing the store. The coverage table below lists each source's signal.

### 3.7 External integrations (`PawnDiaryApi`)

Other mods push events INTO the diary through one public facade:
`PawnDiary.Integration.PawnDiaryApi.SubmitEvent(ExternalEventRequest)` (or the
`SubmitEvent(ExternalEventRequest, out SubmitEventOutcome)` overload when they need to distinguish
the reason a submission did not record), when they need to track the created entry
`SubmitEventWithHandle(ExternalEventRequest)`, or when they already own final prose
`SubmitDirectEntry(ExternalDirectEntryRequest)`, or when they want Pawn Diary to write from a
caller-authored instruction `SubmitPromptEntry(ExternalPromptEntryRequest)` (`Source/Integration/`).
Calls are validated, wrapped so an adapter bug can never break the game loop, main-thread guarded,
and then submitted as external ingestion signals — so external events get the standard treatment: pure
`ExternalEventData.Decide`, the shared dedup store (`externalEventDedupTicks`, with a per-request
override), and event-store writes. Player event filters affect only Pawn Diary's automatic game
listeners; external API submissions are still triggerable when their own validation, master
integration switch, budget, dedup, and pawn gates pass. A supplied eligible partner makes ordinary
external events pairwise; direct entries require nonblank `partnerText` too. Write request DTOs can
set `forceRecord=true` for adapter-owned triggers that must record a diary event. Forced writes skip
soft gates (external budget, source and generic dedup); they still require valid fields,
main-thread/game readiness, the master integration toggle, the ordinary `SubmitEvent` group
requirement, and a base diary-eligible subject.

The master integration toggle is saved in `PawnDiarySettings.allowExternalIntegrations`, appears on
the main mod settings page as *Allow external mod integrations*, and defaults to enabled. The public
facade exposes `PawnDiaryApi.IsExternalApiEnabled` so adapters can distinguish "the game/component is
not ready yet" (`IsReady=false`) from "the player has disabled external API behavior"
(`IsExternalApiEnabled=false`). When disabled, submissions and reads return their safe empty values
and registered providers/listeners are not invoked; registration itself remains accepted so a later
re-enable works without restarting. Owner-guarded reset and one-shot cancellation calls remain
available while disabled so an adapter can release state and work it already owns.

Beyond diary events, the v2 API exposes the LLM connection itself (`ApiVersion` bumped `1 → 2`).
`GetApiSetup()` returns a `DiaryApiSetupSnapshot` — routing mode, global request knobs, and one
`DiaryApiLaneSnapshot` per configured endpoint/model lane — and `AddApiLane(ExternalApiLaneRequest)`
appends a new lane that, when enabled, becomes active immediately: it is persisted via
`PawnDiarySettings.Write` and pushed to the shared `LlmClient` through `ApplyLaneConfiguration`,
mirroring the lane-relevant steps `PawnDiaryMod.WriteSettings` runs when the settings window closes.
Both live in `Source/Settings/IntegrationApiSettings.cs`, with pure token↔enum mapping and add-request
validation in `Source/Pipeline/ApiLaneImport.cs` (covered by `DiaryPipelineTests`). Unlike the diary
reads, these operate on **global** mod settings, so they are gated by the main thread and the master
toggle but **not** by `IsReady` — an adapter can read or configure lanes at the main menu. Public DTOs
speak stable string tokens (`authMode`, `apiMode`, `routingMode`, `contextDetailOverride`) rather than
the internal enums. `DiaryApiLaneSnapshot` can return the player's real `apiKey` — so an adapter can
reuse the player's provider (the "one key serves both mods" case) — but because **any** loaded mod can
call `GetApiSetup()`, the raw key is **withheld by default**: it is included only when the player opts
in with the separate **Share API keys with other mods** checkbox (`enableExternalKeySharing`, default
`false`), surfaced on the snapshot as `keySharingEnabled`. When off, `apiKey` is empty and `hasApiKey`
still reports presence. The master integration toggle governs event/context/lane access; sharing a
plaintext key is a strictly higher-trust action on its own switch. `AddApiLane` records the requesting
mod's `sourceId` on the lane (`addedBySourceId`, echoed in the snapshot) so an API-injected lane stays
attributable rather than indistinguishable from a hand-added row.

API v3 (`ApiVersion` `2 → 3`) exposes the automatic-capture **event filters** — the per-interaction-
group on/off toggles on the settings *Events* tab. `GetEventFilters()` returns a
`DiaryEventFilterSnapshot` per settings-visible group (key = defName, label, domain, `enabled`,
`defaultEnabled`, `hasOverride`); `IsEventFilterEnabled(key)` reads one; `SetEventFilterEnabled(key,
enabled)` flips it using the **same saved flag** as the tab — `PawnDiarySettings.SetGroupEnabled`,
which drops the override when it matches the XML default — then persists via `PawnDiarySettings.Write`.
The change takes effect for future captured events immediately (filters are read per event via
`IsInteractionEnabled`/`IsTaleEnabled`/… at capture time), so no cache invalidation is needed. The API
and the Events tab share one source of truth: `IntegrationApiSettings` calls the now-internal
`PawnDiaryMod.EventFilterGroupsForSettings` / `IsSettingsEventFilterGroup` / `EventFilterLabel`, so the
exposed set (non-External, non-package-gated groups) and its order cannot drift from the UI. Same
gating as the v2 members (master toggle + main thread, no loaded game).

API v4 (`ApiVersion` `3 → 4`) adds editable-psychotype setters and one-shot LLM completions.
`SetPsychotype(pawn, defName, pin)` and `SetPsychotypeCustomRule(pawn, rule)` write the pawn's
player-editable psychotype — its base type / custom rule, the layers the Psychotype Studio edits — so an
adapter can *seed* an outlook the player can then tweak or clear, rather than the source-locked override
slot. `RequestLlmCompletion(request)` runs one instruction+input prompt on a chosen (or the first active)
lane and returns a poll handle read via `GetLlmCompletionResult(handle)`; it wraps a new one-shot
`LlmClient.SendSingleCompletion` (sibling of the connection test, but with a real system prompt and token
budget) behind a handle store (`ExternalLlmCompletionService`) that marshals the background result back to
the main-thread poller. It spends the player's tokens, so it is master-toggle-gated, sourceId-attributed,
one-shot, and input/output-capped. Unlike the global lane-management reads, completion requests require
a loaded game so they can reserve the same XML-tuned per-source/global rolling prompt budget as other
token-spending adapter calls. They share normal diary lane concurrency, cooldown, timeout, and game-
session cancellation; at most 64 handles are admitted until terminal results are polled. Turning the
master integration toggle off blocks new work but does not block polling an already-issued handle:
terminal polling is the cleanup operation that consumes its bounded service slot. Adapters must still
discard such a result when their own feature or the master integration switch is off.

API v5 (`ApiVersion` `4 → 5`) adds `RegisterExternalPsychotypeGenerator(ExternalPsychotypeGenerator)` — an
adapter that produces a pawn's outlook asynchronously (e.g. the 1-2-3 Personalities bridge's LLM transform)
registers three main-thread callbacks (`canReroll` / `isBusy` / `reroll`), and the per-pawn voice editor
(`Dialog_PawnWritingStyle`) shows a **Regenerate** button and a live **generating…** status for pawns it
owns, refreshing the editable custom rule when the new outlook lands. Registration mirrors
`RegisterPawnContextProvider` (process-global, main-thread, replace-by-sourceId, a throwing generator
disabled for the session) through the `ExternalPsychotypeGenerators` registry.

API v6 (`ApiVersion` `5 → 6`) additively exposes `DiaryPsychotypeSnapshot.savedCustomRule`: the saved
player-editable custom outlook before external-override resolution. `rule` remains the effective prompt
rule. This lets an adapter migrate away from its own old locked override without mistaking that override
for, or overwriting, player-authored custom text.

API v7 (`ApiVersion` `6 → 7`) adds `CancelLlmCompletion(handle)` so adapters can cancel obsolete
one-shot HTTP work and immediately release its bounded handle slot, plus `GetPsychotypeRule(defName)`
so a mapped built-in psychotype can be published through the reversible source-owned override layer
without changing the player's base selection. Cancellation and owner-guarded `Reset*Override` cleanup
remain callable after the master integration switch is turned off; starting work and reading gameplay
data remain disabled.

API v8 (`ApiVersion` `7 → 8`) adds thread-safe capture-capability health reporting:
`SetCaptureCapabilityReady(id, ready)` and `IsCaptureCapabilityReady(id)`. A compatibility group's
`disableWhenCaptureCapabilitiesReady` list suppresses its lower-fidelity XML path only while one of
those richer capture paths is actually ready. These two calls are the deliberate exception to the
API's main-thread rule: their locked pure registry is safe from a background Mod constructor and does
not require a game or the master integration switch.

For ordinary `SubmitEvent` calls, policy stays in XML: the request's `eventKey` string plays the
defName role, and an External-domain `DiaryInteractionGroupDef` must claim it (required-match, like
Romance — an unclaimed key records nothing and logs one warning naming the submitting mod). The
classifier applies the same runtime gates as every other domain: an External group that is inert
(`disableWhenPackageIdsLoaded` active, `enableWhenPackageIdsLoaded` unsatisfied, or a listed capture
capability ready) is treated as absent, so its key is rejected with the same "no group claims
eventKey" warning. Adapter mods ship
their own External groups plus optional narrower `DiaryEventPromptDef` rows; the core ships only the
`externalDevTest` group so the Debug Actions entry "Submit test external event..." can exercise the
whole path with no adapter installed. The full public contract — versioning, threading, eventKey
conventions, packaging — lives in `INTEGRATIONS.md`.
Wrapped prompt entries are the middle ground between ordinary events and direct text injection: the
adapter supplies `promptInstruction`, Pawn Diary stores it as protected `external_prompt_instruction`
context, and the normal first-person prompt wrapper still owns persona/style, safety text, live
context, response parsing, budget, lifecycle, and persistence. Their External group is optional; if
one claims the submitted key, its label, styling, and prompt metadata still apply.
Every gameplay/state `PawnDiaryApi` entry point is main-thread only. Off-main-thread calls return the
documented safe value, use a thread-safe diagnostic path, and do not ask RimWorld to marshal work.
Adapters that collect data on worker threads must own their queue and drain it from a main-thread
callback such as their own `GameComponentUpdate` or `OnGUI` hook. API v8's two pure, locked
capture-capability calls are the only thread-safe registration/read exception.

The public adapter contract is only the `PawnDiary.Integration` namespace: `PawnDiaryApi` plus its
request/result/snapshot DTOs. Runtime helpers in capture, ingestion, generation, pipeline, UI, and
settings stay `internal` unless RimWorld needs a public type for XML Def loading, Scribe/save data,
settings serialization, debug-action discovery, or lifecycle reflection (`Mod`, `GameComponent`,
`ITab`). `[InternalsVisibleTo("DiaryPipelineTests")]` lets the standalone pure test project reach
the internal pure helpers without widening the mod's external compile-time surface. A defensive
`ExternalEventRequestText.MaxRequestContextLines` (64) caps the total
`key=value` fields one request can write into saved gameContext, so raising the XML-tuned
enchantment-candidate cap cannot grow saved state without bound (mirrors `MaxListeners`). Protected
prompt fields are added first; ordinary adapter `extraContext` can only use the remaining slots, so a
candidate-heavy request cannot exceed the same absolute ceiling.
Saved external `gameContext` always starts with `external=...`; the domain classifier gives that
marker precedence so adapter-supplied `extraContext` keys cannot make an external page display as a
native Thought, Work, Hediff, or other built-in event domain. Adapter input is confined to being a
*value*, never a structural field. `ExternalEventData.BuildGameContext` flattens the `;` field
separator (and line breaks) out of the adapter-controlled `eventKey`/`sourceId` and length-caps them
before they become marker values, so a `;`-laden key cannot forge an extra `key=value` field.
`ExternalEventRequestText.JoinAdapterExtraContext` — shared by the ordinary event path and the
direct-entry path — drops any `extraContext` line whose key is a reserved internal game-context key:
the event-domain markers, the structural death/arrival/reflection markers, classifier value keys,
prompt `ContextField` keys, and the protected `external_prompt_*` fields (its reserved set is the one
place to register a new internal key). Free-form adapter keys (`location`, `weather`, ...) still pass
through. Because `DiaryContextFields` reads first-match, a caller can therefore neither smuggle an
extra field through a marker nor override an API-owned or internal field.
That same marker now drives external authorship attribution: `DiaryEntryView` derives a cleaned
`ExternalSourceId` from the saved `source=` field, the Diary tab shows it in the entry footer, and
public entry snapshots expose `externallyAuthored` / `externalSourceId` without adding save fields.

Token-spending external submissions pass one more API-side guard before dispatch. The component owns
a transient rolling reservation list (not saved) and evaluates it with the pure
`ExternalApiBudgetPolicy`: ordinary `SubmitEvent` / `SubmitEventWithHandle` requests estimate main
generation cost from the player's `maxTokens`, the queueable POV count, and optional title follow-up
tokens; `SubmitPromptEntry` uses that same estimate but does not require an External group; while
`SubmitDirectEntry` estimates only the title-only requests it can actually queue when
`generateTitleIfMissing` is true. `forceRecord=true` skips this reservation for valid write
requests. Prompt-test mode, missing active API lanes, per-pawn diary-generation disablement, and
incapacitation skips do not consume budget because they would not enqueue LLM work. The XML knobs live in `DiaryTuningDef`
(`integrationPromptBudgetEnabled`, `integrationPromptBudgetWindowTicks`,
`integrationPromptBudgetMaxRequestsPerSource`, `integrationPromptBudgetMaxRequestsGlobal`,
`integrationPromptBudgetMaxTokensPerSource`, `integrationPromptBudgetMaxTokensGlobal`); a 0/negative
`windowTicks` is clamped to the default by the tuning accessor, so it never silently disables the
gate. Rejections return the existing safe API values (`false` / `recorded=false`) and log once per
source/reason. A reservation is refunded (`ReleaseExternalApiBudgetReservation`) when the dispatcher
then drops the event (dedup window or pawn state), so a burst of duplicate or invalid
submissions cannot exhaust an adapter's window without any tokens actually being queued. `SubmitEvent`
dispatches the signal directly (rather than fire-and-forget) so it can apply that refund while keeping
its documented "validated and handed off" return; callers needing the real outcome use
`SubmitEventWithHandle`, or the `SubmitEvent(request, out SubmitEventOutcome)` overload which
distinguishes `DroppedBudget` from `DroppedByPipeline` and the other drop reasons.

The public v1 read side is prompt-free and snapshot-based. `GetRecentEntryTitles`,
`GetContextSnapshot`, `GetEntryStats`, `GetEntrySnapshot`, and `GetEntryStatus` all read the same hot
plus compact-archive views used by the Diary tab, apply `DiaryEntryTitleQuery` where relevant, and
return plain DTOs rather than live RimWorld objects. Snapshots can expose external source
attribution (`externallyAuthored`, `externalSourceId`), lifecycle status, title/prose presence,
semantic domain, atmosphere, and aggregate counts, but never prompts, raw provider responses, errors,
or in-flight pages.

The public v1 context side exposes the machinery Pawn Diary already builds for prompts without
driving another LLM call. `GetWritingStyle` publishes the pawn's base saved diary voice;
`GetAvailableWritingStyles` lists the effective style catalog; `SetWritingStyleOverride` and
`ResetWritingStyleOverride` manage source-owned temporary voice rules (when two adapters contend for
the same slot and both sourceIds are packageIds of active mods, the later-loading mod wins —
`ExternalOverrideArbitration` + `ExternalSourceLoadOrder`; unresolvable sourceIds keep
last-writer-wins); `RegisterPawnContextProvider`
adds compact adapter-owned `key=value` lines to pawn summaries; `GetPawnSummary` returns structured
identity/mood/health/thought/provider facts; `GetPromptEnchantments` returns the prompt-enchantment
candidate set before the final weighted roll; and `GetContextBundle` packages style, summary,
enchantments, and recent memory in one DTO.

The buildable example adapter keeps all direct public API calls in
`integrations/PawnDiary.ExampleAdapter/Source/PawnDiaryExampleApi.cs`. That file acts as the
copyable integration layer: it wraps status checks, submissions, reads, prompt previews, context
providers, status listeners, style/psychotype reads and writes, the external psychotype-generator
hook, and one-shot LLM completion request/poll/cancel calls, with XML doc comments spelling out
required args and safe return values. The explorer window and quick debug actions call through that
facade so adapter authors can ignore the UI harness when copying the sample.

`SubmitEventWithHandle` returns stable `DiaryEntryHandle` values when the pipeline creates an entry,
and `RegisterEntryStatusListener` lets adapters receive compact lifecycle snapshots after a POV's
main text or title status changes. `SubmitDirectEntry` creates normal saved diary events from
caller-authored prose without queuing the main LLM rewrite; optional caller titles are cleaned and
stored immediately, and `generateTitleIfMissing` may use the existing title-only path when enabled.
`forceRecord=true` still requires a base diary-eligible subject, but bypasses soft direct-entry gates
so caller-authored prose can be stored for adapter-owned triggers.

`PreviewPrompt(ExternalEventRequest, string povRole = null)` and
`PreviewPrompt(ExternalPromptEntryRequest, string povRole = null)` build side-effect-free prompt
snapshots with RNG save/restore. They never register an event, cross-reference pawn diaries, consume
dedup windows, queue generation, or spend tokens. `ExternalPromptEntryRequest` adds required
`promptInstruction`; the live generation path stores it as protected `external_prompt_instruction`
context while Pawn Diary still owns persona/style, safety, context, parser expectations, budget,
title follow-ups, and storage. Ordinary external requests can also provide `promptFragment`,
`promptEnchantmentCandidates`, and `replacePromptEnchantments`; those fields are cleaned, capped, and
protected from `extraContext` spoofing.

The preview snapshot additionally reports `contextDetailLevel` (the effective global preset) and a
`contextPresets` list — one `DiaryPromptContextPresetPreview` per preset (Full/Balanced/Compact) with
that preset's `budgetChars`, assembled prompt text, and `keptFields`/`cutFields` diagnostics. The
per-field `reason` strings are fixed-English diagnostic tokens (like the `event:`/`sex=` sentinels),
not localized text: they explain why a field was kept or cut for tooling, not for player display.
Balanced/Compact budgets come from `DiaryContextDetailDef` (`Diary_ContextDetail`), so they can be
retuned in XML; Full is unbudgeted and preserves the original prompt shape.

`integrations/PawnDiary.RimTalkBridge/` is the first real adapter target: a two-way bridge between
Pawn Diary and RimTalk, shipped as the separate mod `PawnDiary: RimTalk bridge`. Its behavior is
gated by an **integration-level** setting (`PawnDiaryRimTalkBridgeSettings.integrationLevel`, an int
Scribe key so save data stays stable), with `PawnDiaryRimTalkBridgeMod.LevelAtLeast(n)` as the
null-safe gate everywhere:

The bridge settings use one vertically scrolling `Listing_Standard`. The listing explicitly sets
`maxOneColumn`: without it, Verse moves overflowing rows into off-screen columns and `CurHeight`
measures only the last column, causing the scroll canvas to collapse until the window appears empty.
The canvas is also floored to the visible viewport height before drawing.

- **Off (0)** — no data crosses in either direction and no chat-originated entry is possible.
- **Shared context (1, default)** — outbound, `DiaryContextInjector` registers a diary-memories
  section into RimTalk's prompt builder (`ContextHookRegistry.InjectPawnSection` on
  `ContextCategories.Pawn.Thoughts`, plus a `{{ pawn.diary }}` Scriban variable). Optional persona
  synchronization is independently Off by default; import mode registers a `chat_persona=` pawn-context
  provider, while export mode publishes Pawn Diary's outlook. No diary entry originates from chat.
- **Shared context + conversations (2)** — `ConversationTracker` (fed by the
  `RimTalkCreateInteractionPatch` Harmony postfix on `TalkService.CreateInteraction`) groups displayed
  chat by reply chain and quiet window, then passes each finished conversation through the bounded
  editorial funnel below. Per-line PlayLog capture is disabled while that rich hook reports ready, so
  only an accepted whole conversation can create an entry without a duplicate ambient path.

The hook boundary was rechecked against installed RimTalk **v1.0.14**: every live `DisplayTalk` route
converges on `TalkService.CreateInteraction`; that build has no separate greeting, urgent, or group-line
emission boundary for the bridge to patch. This is an upstream-version maintenance surface. The bridge
registers that postfix independently and publishes
`aimmlegate.pawndiary.rimtalkbridge.displayed-conversation` only after Harmony succeeds. If target
resolution fails, bridge Level 2 releases the capability gate and core `rimtalk_chatter` ambient XML
automatically resumes; Levels 0/1 publish a separate intentional no-capture claim so their semantics
remain unchanged and a healthy bridge never duplicates the fallback.

The Level-2 funnel is:

```text
RimTalk reply chain
→ cheap local scoring
→ bounded ranked queue
→ small batched LLM assessment
→ accepted candidates use normal Pawn Diary generation
→ exactly one linked pairwise DiaryEvent (two POV pages)
```

`ConversationCandidatePolicy` is pure. It rejects monologues, duplicate chains, empty speakers, and
zero caps, then scores personal signals such as reciprocal talk, speaker alternation, and localized
keyword categories. Event overlap, announcements, and duplicate pairs reduce the score; length alone
is never enough. Reaction terms are normalized, de-duplicated, and count/length-limited before saving,
with XML categories retained and custom additions kept in one bounded category.

`ConversationCandidateQueue` ranks by score descending, older first, then stable root-talk id. It
deduplicates root ids, enforces its global and per-pair limits, replaces the weakest only when a
stronger candidate arrives, and expires old work. Defaults live in
`1.6/Defs/RimTalkConversationAssessmentDefs.xml`: 12 queued candidates, 6 per batch, 2 per pair, at
most 2 assessment batches per in-game day, a 15,000-tick retry gap, and 60,000-tick expiry. Local
scoring is free. The daily count and retry gap are saved; the queue and request are transient, so
reloading cannot reopen the paid allowance.

`RecentDiaryEventCache` listens through the frozen id
`aimmlegate.pawndiary.rimtalkbridge.assessmentstatus`. It keeps bounded, locked event facts, excludes
this bridge's own entries, and adds both participants' context on the main thread. Failed, skipped,
and prompt-only outcomes remove pending seeds, so nonexistent pages cannot become related-event
aliases. This helps distinguish a native-event echo from a new personal consequence without expanding
the core API.

`ConversationAssessmentCoordinator` owns one transient queue and one completion handle. Its ~250-tick
main-thread pass polls first, then starts at most one batch through
`PawnDiaryApi.RequestLlmCompletion`. Each candidate sends short aliases, up to four capped transcript
lines, and up to three recent event summaries (default user-text cap: 3,600 characters). Charged,
user-authored, and keyword lines receive priority slots. No persona, pawn summary, surroundings,
writing style, or diary prose is sent. The response parser accepts only the frozen schema and active
aliases, caps focus text, fills missing rows with `ignore`, and fails closed on invalid output.

Every assessed conversation has one outcome: `ignore`; `related` (new explicit personal consequence
linked to one supplied event); or `standalone` (explicit durable interpersonal content independent of
the supplied events). Only the latter two reserve the existing per-pawn/colony/pair throttle and call
`SubmitPromptEntry`. Their extra context carries `conversation_assessment`, stable reason, explicit
`conversation_focus`, and—only for related outcomes—the actual `related_event_id` plus
`avoid_related_event_recap=true`. The final localized prompt tells normal generation to focus on that
consequence and not retell the native event. A rejected Pawn Diary submission refunds the throttle.
The root dedup token remains `rimtalkbridge|<RootTalkId>`.

Accepted chat events charge both POV pawns a rolling **60,000-tick (one game day) cooldown**.
Conversations involving either pawn are discarded early and checked again before submission; rejected
submissions refund the reservation. The pawn-id/tick map is saved under
`rimTalkConversationCooldownTicksByPawn`, so reloads cannot bypass it. The legacy per-pawn setting is
now `0` or `1`, and pre-0.3 zero values migrate to enabled recording before the new zero-means-off
behavior is applied.

Semantic assessment defaults on and uses a saved lane selector (`assessmentLaneIndex=-1` means first
active lane). Candidates wait until expiry when no lane is active. Budget or concurrency rejection
keeps the queue for retry; transport or invalid-output failure creates nothing. Turning semantic mode
or the master integration switch off still polls an in-flight handle to release it, then discards the
result. Off mode uses the stricter XML-tuned local scorer and makes no assessment request.

The settings expose two editable inputs. `conversationReactionTermsCsv` overrides the localized
reaction lexicon (blank restores the DefInjected defaults); `assessmentPromptOverride` replaces the
localized assessment policy (blank restores `assessmentSystemPrompt`). A code-owned English JSON
schema prefix is always added before the editable text, so overrides and translations cannot remove the
contract. XML owns the editor limits and cooldown. Unicode text-element counting is used consistently,
and invalid provider output fails closed.

Threading and lifecycle are the two subtle parts. The diary-memory section is served to RimTalk from
a **cache** that is refreshed only on the main thread (`DiaryContextInjector.RefreshFor`), because
RimTalk may call the section delegate on a background prompt-assembly task and `PawnDiaryApi` reads
are main-thread-only; a lock guards the shared cache. `RimTalkBridgeGameComponent` runs one throttled
(~250-tick) pass that refreshes stale diary/colony/shared-memory caches, runs Tier-B persona sync,
flushes quiet conversations into the candidate queue, polls/applies assessment, then starts new work.
It resets the recent-event cache, queue, in-flight map, assembler, throttle, and other static caches in
`FinalizeInit`, then restores only the scribed rolling pawn cooldown (statics leak across exit-to-menu +
load). All RimTalk-typed methods are `[MethodImpl(NoInlining)]`
and only reached after the mod's cached `RimTalkActive` (`cj.rimtalk`) guard, so a missing RimTalk can
never raise `TypeLoadException`. Pure decision logic (assembly, Unicode matching/overlap, scoring,
queue ranking/gating, editable-policy validation, batch formatting, response parsing, submission
planning, throttling, and context
formatting) lives under `Source/Pure/` and is unit-tested by
`tests/RimTalkBridgeLogicTests/` without loading the game.

Persona synchronization defaults to **Off**, preserving independent personas, and otherwise has one
explicit authority direction: **Pawn Diary → RimTalk** publishes
the pawn's effective psychotype (and never its writing-style rule) through RimTalk's supported
`PersonaService.SetPersonality`,
while **Pawn Diary ← RimTalk** imports RimTalk's persona as a source-owned **psychotype (outlook)**
override (`PersonaSync` applies `SetPsychotypeOverride`). An optional LLM transform uses Pawn Diary's
first active API lane to reshape the source for the receiving mod and falls back to direct sync on
admission failure, no configured lane, or generation failure. The Pawn Diary → RimTalk prompt keeps
the psychotype as the primary content: every source tendency must remain recognizable, wording and
length should stay close to the source, and concrete outlooks must not collapse into generic speech or
personality traits. It no longer forces short source text up to a 200-character minimum. The reverse
prompt now follows the same source-faithful rule for persona → diary conversion: every supported
durable character tendency and contradiction stays recognizable, while only surface speech mechanics
with no outlook meaning may be omitted instead of being turned into invented motives. It also stays
near the source length rather than forcing a 200-character minimum. A Unicode-safe local cap guarantees
no stored result exceeds 300 characters in either direction. Writing-style prose is never sent to
RimTalk or the LLM. Instead, the bridge recognizes exactly five active hediff
styles (`mind-crumbled`, `silent-focus`, `pain-needle`, `blank-bliss`, `bright-fog`) and the five child
catalog styles. Each recognized non-silent style selects a narrowly authored transform instruction;
ordinary adult styles select none, and custom/external prose is never inspected. While Pawn Diary owns
RimTalk's persona, the same 250-tick pass also maintains talk initiation from the selected psychotype's
XML-authored baseline (`RimTalkPersonaChattinessDefs.xml`) with a stable pawn+psychotype relative ±15%
variation. Invalid XML/profile floats fall back to finite defaults, and profile names are trimmed.
`silent-focus` adds no prose and replaces that inferred value with absolute `0`. Before its first
export write, the bridge saves both the player's prior RimTalk persona and talk-initiation value; it
restores both only when Pawn Diary authority ends, including across reloads. Import and export source
keys plus resolved target text are also scribed, so a reload retries a rejected local write without
paying for the same LLM transform again. Each transformed direction includes its own stable prompt
revision, so source-faithfulness corrections invalidate that direction's previously cached vague text
exactly once without disturbing direct-sync or the other direction's cache. Obsolete/mismatched
transform handles are explicitly cancelled
instead of consuming one of the core's 64 slots until timeout. The older `personaLedDiaryVoice` key
remains serialized only for migration: old opt-in saves become import mode, while old opt-out saves
remain Off. Import mode reapplies on stable source-key change and clears via `ResetPsychotypeOverride`
when authority ends; every reset also sweeps the stale writing-style override older bridge versions
placed. The controlling editor patch is registered dynamically so RimTalk UI drift can disable only
that notice/button instead of breaking the whole bridge; when LLM transform is enabled it shows
a Regenerate action (RimTalk's persona editor for export, Pawn Diary's psychotype editor for import).
The settings page also shows the exact direction-specific request contract whenever the LLM checkbox
is enabled: export uses only the effective Pawn Diary psychotype rule as `userText` (with at most one
locally selected localized child/recognized-health modifier appended to the system prompt), while
import uses the complete current RimTalk persona string. Neither direction adds a general pawn summary,
diary entries, memories, or writing-style prose. This is a schema disclosure rather than a live-pawn
preview because RimWorld can open Mod Settings without a loaded game.
Other advanced controls cover semantic/local-only assessment and lane selection, editable
reaction terms and semantic prompt, and per-pawn/colony/pair throttle knobs
(`ThrottlePolicy`; daily/colony counters are transient, but the one-day pawn cooldown is saved; a zero
per-pawn daily cap disables conversation recording). The old `useRimTalkEngine` Scribe key remains readable but
its toggle is hidden and its value is ignored: accepted conversations always use normal pairwise
`SubmitPromptEntry`; the now-unreachable direct engine client source has been removed. The legacy
`minRepliesForImportant` key is likewise still read but no longer shown
or used. Frozen save/registry tokens (source id, `rimtalkbridge_conversation` event key, three listener
ids) live in `BridgeIds`. The full design rationale is in `design/RIMTALK_BRIDGE_PLAN.md`.

Template authors can explicitly place `{{ pawn.diary_persona }}`. In two-pawn prompts,
`{{ recipient.diary_persona }}` addresses the other pawn and must be guarded with `{{ if recipient }}`
because RimTalk sets `recipient` to null for monologues. The variable returns Pawn Diary's psychotype
only from a main-thread-built cache; writing styles are never included. It is registered for opt-in use only:
the bridge never auto-injects it or creates a prompt entry.
All RimTalk registrations that need localized descriptions run from a `StaticConstructorOnStartup`
registrar after language loading; the earlier mod-constructor timing has no active language yet.

Two further Level-1 outbound context variables extend the bridge (both follow the same
main-thread-refresh / background-read cache split, `design/RIMTALK_BRIDGE_CONTEXT_EXTENSION_PLAN.md`):

- **`{{colony_events}}` — colony situation (`ColonyContextInjector`, default OFF).** A short curated
  line about the colony *right now*: threat level (`Map.dangerWatcher.DangerRating`), active game
  conditions (`GameConditionManager.ActiveConditions`), an atmospheric Anomaly note (DLC-gated on
  `ModsConfig.AnomalyActive` via `Find.Anomaly`), and the top ongoing quests
  (`QuestManager.QuestsListForReading`). Weather/season are left to RimTalk. Lines are weighted, then
  ordered/trimmed by the pure `ColonyEventsFormat` (settings cap `colonyEventCount`, hard cap 400
  chars). Registered as a `RegisterEnvironmentVariable` **and** an `InjectEnvironmentSection` after
  `Environment.Weather`; the per-map cache is keyed by `map.uniqueID` and refreshed on the tick pass.
  The read path has no expiry of its own, so the refresh pass clears a map's block when it has no free
  colonists (an old line such as "under serious attack" stops once everyone has left) and evicts blocks
  for maps that no longer exist. Off by default because it overlaps RimTalk's own live-event mods —
  Pawn Diary contributes a curated, atmospheric summary instead of a live feed.
- **`{{diary_shared}}` — pair shared memory (`SharedMemoryInjector`, default ON).** When two colonists
  talk, the diary moments they *share* (entries where one is subject and the other partner, via
  `DiaryEntryTitleQuery.partnerPawnId`) are injected as "previous interactions", picked
  weighted-randomly by the pure `SharedMemorySelection` (recency × importance, seeded from a stable
  per-pair value — **never `Verse.Rand`**; settings cap `sharedMemoryCount`, hard cap 500 chars). This
  is a **context** variable (RimTalk hands the provider a `RimTalk.Prompt.PromptContext` with all
  participants), so it registers via `ContextHookRegistry.RegisterContextVariable`. Because a context
  variable is invoked at prompt time for a pair only then known, the provider is *lazy*: it serves the
  cached block or enqueues the pair on a `ConcurrentQueue` and returns "" once; the main-thread pass
  drains the queue, reads the shared entries, and fills a `pairKey`-keyed cache (a second entry-status
  listener marks a pair stale when either pawn's diary changes; `Mod.WriteSettings` marks all pairs
  stale so a changed `sharedMemoryCount`/toggle takes effect at once). The block is symmetric, so the
  pass prefers the lower-load-id pawn as subject but falls back to the other pawn when the first is
  unreadable (unspawned or diary-ineligible, e.g. a prisoner) — so a colonist↔non-colonist pair still
  surfaces the colonist's shared memories; a build where neither pawn can be read retries later rather
  than caching a permanent empty. `IsPreview` returns a cheap localized sample. Zero-config delivery:
  since RimTalk has no context-*section* injection, an optional system
  **prompt entry** embedding `{{diary_shared}}` is auto-registered (`autoInjectSharedMemory`, default
  ON) via `RimTalkPromptAPI.CreatePromptEntry`/`AddPromptEntry`; it is reconciled idempotently
  (remove-by-modId then add) from the tick pass and `Mod.WriteSettings`, and **removed** when the
  feature is turned off — prompt entries persist in the user's active RimTalk preset, so that cleanup
  is mandatory.

Verified against the installed `cj.rimtalk` **v1.0.14** DLL (plan ⚠️ V1): `RegisterContextVariable`,
`RegisterEnvironmentVariable`, `InjectEnvironmentSection`, `RegisterEnvironmentHook`, the
`PromptContext` fields (`AllPawns`/`Pawns`, `IsMonologue`, `IsPreview`), and the prompt-entry API all
exist, so no degrade path was needed. **⚠️ U1 (open, verify in-game):** whether RimTalk renders an
`InjectEnvironmentSection` into its *default* prompt is still unconfirmed for `{{colony_events}}` (the
same open question the shipped `InjectPawnSection` carries). The `{{colony_events}}` variable and the
`{{diary_shared}}` prompt entry work regardless; if the injected environment *section* does not render,
switch `ColonyContextInjector.RegisterAll` to `RegisterEnvironmentHook(Environment.Weather, Append, …)`.

Compatibility groups shipped inside this repo for other mods use the group gate
`enableWhenPackageIdsLoaded` (inverse of `disableWhenPackageIdsLoaded`): the group is enabled only
while one of the listed target mods is in the mod list, so it sits fully inert otherwise.
`1.6/Defs/Compat/DiaryCompat_RimTalk.xml` adds `rimtalk_chatter`, an Interaction-domain compat
group gated on RimTalk's packageId (`cj.rimtalk`). It claims `RimTalkInteraction` PlayLog rows before
the broad `other` fallback can see them, captures the rendered chat text, and routes ordinary chatter
through the same `AmbientDayNote` batching/promotion policy as SpeakUp whenever no richer capture path
claims readiness. Its `disableWhenCaptureCapabilitiesReady` list contains the bridge's healthy
displayed-conversation id plus its intentional Levels-0/1 no-capture id. Therefore: RimTalk alone keeps
the ambient fallback; bridge Level 0 is fully off; Level 1 is context-only; healthy Level 2 records
only assessed whole conversations; and broken/renamed Level-2 hooks degrade to ambient XML instead of
silently losing chat. With RimTalk absent, the row does not appear in event settings and has no runtime
effect.

`1.6/Defs/Compat/DiaryCompat_Rimpsyche.xml` provides the same bridge-optional parity for Rimpsyche.
When `Maux36.Rimpsyche` is active without `aimmlegate.pawndiary.adapter.rimpsyche`,
`rimpsyche_chatter` batches `Rimpsyche_` PlayLog conversations into the stable
`RimpsycheAmbientDay` note (min 6/sample 3), and `rimpsyche_afterfeel` themes package-owned ThoughtDefs.
Both groups are disabled when the typed adapter loads; its lower-order groups and signed-alignment hook
then become the sole Rimpsyche path. Without Rimpsyche, both fallback groups are inert and hidden.

The remaining core compatibility packs are pure XML and Russian-localized. All target content is
matched by plain strings and package gates, so none creates a target-mod or DLC assembly dependency:

- **Alpha Memes** (`Sarg.AlphaMemes`) separates funerals, other rituals, eligible thoughts, baptism,
  and two visible reflective hediffs. Ritual matchers use the runtime classifier shape
  `PreceptDefName;BehaviorWorkerClass`: exact precept stems are therefore `matchPrefixes`, followed by
  the broader `AM_` ritual family.
- **Vanilla Ideology Expanded - Memes and Structures** (`VanillaExpanded.VMemesE`) separates four
  verified severe rites from the general `VME_` ceremony family, plus eligible ritual afterthoughts
  and interrogation. The severe rows use the installed `*Precept` names, not similarly named workers.
- **Way Better Romance** (`divineDerivative.Romance`) themes its three real InteractionDefs and fourteen
  memory ThoughtDefs with pairing/orientation-neutral prose. Date/hangout invitations ambient-batch;
  hookup attempts remain immediate. Its apparent succeeded/failed names are RulePackDefs, not events.
- **Vanilla Traits Expanded** (`VanillaExpanded.VanillaTraitsExpanded`) adds exact allowlists for its
  recordable memories and three actual MentalStateDefs. A target-gated XML patch appends twenty
  trait-to-psychotype affinity rules without referencing a VTE Def directly.
- **Hospitality** (`Orion.Hospitality`) records colonist-owned guest diplomacy/charm as ambient hosting
  work and guest scrounging only when a colonist actually participates. Guest-held recruitment and
  bookkeeping thoughts are deliberately excluded. The generic `allowSingleEligiblePawn` batch flag
  lets XML batch the one diary-eligible side of a colonist/guest interaction while every group that
  does not opt in keeps the old solo route. Guest arrival and the `HappyGuestJoins` join-request
  incident use one-shot `MapWitness` pages; the latter never claims the player has accepted the
  request. The optional guest-presence context-provider phase remains deliberately deferred; Phase 1
  is complete.
- **Vanilla Events Expanded** (`VanillaExpanded.VEE`) adds purple-raid and visible ambient-hediff
  groups, six live GameCondition prompt tints, and four one-shot incident families (earthquake,
  meteorites, space battle/shuttle crash, and purple manhunters). Each incident chooses one stable map
  witness instead of fanning out. Verification intentionally omitted the neutral
  `VEE_VisitorGroupRaid`, hidden traitor hediffs, and situational-not-memory thoughts so the diary never
  records a betrayal too early or exposes secret state.

`EventWindowRecordScope.MapWitness` is the reusable middle ground for an incident hook that supplies a
map but no subject pawn: the eligible colonist with the smallest stable load ID owns exactly one page.
`Map` still fans out and `SubjectPawn` still requires a pawn supplied by the signal. Thought capture now
classifies the live `ThoughtDef`, so package-wide Thought groups can inspect `modContentPack`; saved-event
recovery remains defName-only by design.

Five further personality/social integrations ship as **standalone adapter mods** under `integrations/`
(junction-deployed for development by `scripts/deploy-integrations.ps1`), so a player installs only the
ones matching their mod list. SpeakUp and Rimpsyche additionally keep smaller core fallback groups for
players who use the target mod without its adapter; loading the adapter disables its fallback:

- **`PawnDiary.SpeakUp` (`Pawn Diary: SpeakUp`)** — five target-gated Tier-1 Interaction groups classify
  deep talks, jokes, prisoner talks, thought reactions, and catch-all chatter. They preserve rendered
  dialogue; core's SpeakUp `Ensue` suppression guard prevents rendering from scheduling another reply.
  The prisoner group matches SpeakUp's exact `Prisoner*` conversation defNames (not a bare `Prisoner`
  prefix, which would swallow Anomaly DLC's `PrisonerStudyAnomaly` from the core anomaly group), and all
  five ambient groups set `allowSingleEligiblePawn` so a colonist↔prisoner/guest talk batches into one
  day note instead of emitting solo pages. A default-on reflection-only Tier 2 observes the verified
  `DialogManager`/`Talk` surface without a SpeakUp.dll build reference, samples already-rendered
  emitter-POV lines, and submits one `speakupbridge_conversation` External pair event at the configurable
  threshold (default 3, range 1–5). It attaches to a `Talk` only from its opening reply (checked via
  `Statement.Iteration`), so a conversation already in flight when capture is enabled is skipped rather
  than recorded with inverted roles or partial samples. In-flight Talk state clears on load/toggle-off;
  pawn departure or death drops it. Tier-1 ambient
  fragments deliberately coexist with the whole-conversation event pending the diary-level duplication
  smoketest. The old `speakup_chitchat`/`SpeakUpAmbientDay` fallback and saved setting token moved
  unchanged into `Defs/Compat`: SpeakUp alone keeps that behavior, loading the adapter disables only the
  fallback, and force-loading the adapter without SpeakUp is inert. Core `teaching`'s existing SpeakUp
  disable gate remains unchanged until its original collision rationale is reproduced.
- **`PawnDiary.RimpsycheBridge` (`Pawn Diary: Rimpsyche`)** — loading the adapter disables core's
  `rimpsyche_chatter`/`rimpsyche_afterfeel` fallback, then its XML assigns Rimpsyche conversation rows
  (ambient min 6/sample 3) and package-owned memories their own voice. Tier A contributes a cached
  `psyche=` context line: up to three localized descriptors above the XML magnitude floor and two
  interests, never raw floats. Default-on Tier B maps the two dominant nodes from distinct behavioral
  families into a reversible source-owned psychotype override on a 250-tick change-detected pass. Persona sync is a single-choice
  mode: Off, map the dominant family/sign to a built-in psychotype, apply deterministic localized direct
  text, or ask a selectable Pawn Diary LLM lane to rewrite the psyche/interests summary (falling back to
  direct text). That deterministic `base outlook:` is authored entirely from the pawn's dominant
  Rimpsyche nodes; it is **not** the pawn's current Pawn Diary psychotype. The default transform prompt
  treats this external-personality-derived outlook as authoritative: every selected tendency/contrast
  must survive, while psyche descriptors and interests may only sharpen supported emphasis and may
  never replace the base with a generic personality summary. The result is installed through
  `SetPsychotypeOverride`, whose source-owned override wins over both the saved custom rule and base
  psychotype; it replaces the effective Diary personality rather than extending or concatenating it. The rounded
  34-node vector plus mode/effective-prompt settings form a stable saved source key, so changing this
  localized default regenerates an old default-produced target once while a player-customized prompt
  keeps its own identity. Resolved targets survive reloads and rejected local writes are retried without
  repeating a paid LLM request. Switching mode/off or disabling integrations releases only the
  bridge-owned override, leaving
  the player's base/custom outlook untouched. Obsolete completion handles are cancelled immediately.
  Existing `usePsychotypeOverride` settings migrate to Direct text/Off. Default-on Tier C
  signature-checks Rimpsyche v1.0.41's
  `InteractionHook`, records only conversations over the XML absolute-alignment threshold (`0.55`)
  under frozen key `rimpsyche_conversation`, and persists a per-pair 60,000-tick cooldown. The
  six-family mapping, compact formatter, stable hashes, threshold, and cooldown are pure-tested. Typed
  RimPsyche reads stay behind the active-package guard. While LLM mode is selected, its scrollable
  settings page shows the exact `userText` schema and live XML caps: localized `psyche=` descriptors,
  optional localized `interests=`, and the localized deterministic `base outlook:`. It also states that
  raw node/interest scores and ordinary Pawn Diary/pawn-summary data are not sent.
- **`PawnDiary.PowerfulAiBridge` (`Pawn Diary: Powerful AI Integration`)** — a deliberately thin,
  one-way persona bridge. It reads the enabled PAI character bound to each pawn through reflection, so
  the adapter has no compile-time dependency on `DynamicRoleStoryteller.dll`, and copies the complete
  nonblank semantic persona surface (`dialoguePrompt`, `dialogueSpeechHabits`, `storyRole`, `prompt`,
  and `dialoguePersonaPreset`) into a reversible, source-owned Pawn Diary psychotype override. It never
  reads or writes conversations, memories, credentials, or any PAI setting. The one settings choice is
  **Disabled**, **Direct** (default), or **LLM-assisted**. Direct preserves the source fields in a capped
  structured rule. LLM-assisted sends that same complete structured block through one selectable Pawn
  Diary API lane and falls back to Direct when no lane is available, admission is temporarily rejected,
  or generation fails. Stable saved fingerprints avoid repeat token spend; source/persona, mode, lane,
  or localized transform-prompt changes trigger a refresh. Disable, persona removal, target-mod removal,
  and Pawn Diary's master integration switch release only this adapter's override. Like the Rimpsyche
  bridge, it registers the public external psychotype generator so the Pawn Diary editor can regenerate
  an LLM-assisted result. Formatting/fingerprinting are pure-tested by
  `tests/PowerfulAiBridgeLogicTests/`. Its Workshop payload intentionally includes the complete bridge
  `Source/` tree; `scripts/publish.ps1 -PublishPowerfulAiAdapter` builds and stages it independently,
  while `-PublishAllAdapters` includes it in a full release.
- **`PawnDiary.Vsie` (`Pawn Diary: Vanilla Social Interactions Expanded`)** — mostly XML, **plus** a
  tiny assembly (`PawnDiaryVsie.dll`) for the gathering hook. Four gated `DiaryInteractionGroupDef`s
  for VSIE (`VanillaExpanded.VanillaSocialInteractionsExpanded`): `vsie_vent` (Interaction, ambient
  batch + promotion), `vsie_teaching` (Interaction, prefix matcher `VSIE_Teaching` covering the base
  def and all 12 skill variants, ambient batch), `friendship_relation` (Romance, `VSIE_BestFriend`),
  and `vsie_thoughts` (Thought, `matchPackageIds`). `VSIE_Discord` (an anger-driven insult, not
  co-working chatter) is routed into the core `insults` group via a VSIE-gated `PatchOperationFindMod`
  in `1.6/Patches/` rather than a group of its own, so it batches with the social fight it usually
  triggers. **Gathering bridge:** VSIE's group gatherings emit no InteractionDef/TaleDef, so a Harmony
  postfix on the **base-game** `RimWorld.GatheringWorker.TryExecute` (no VSIE assembly reference —
  `__instance.def.defName` is matched as a string, like the core capture pattern) forwards the two
  colony-important ones — `VSIE_BirthdayParty` and `VSIE_Funeral` — to the public API as External
  events (`vsie_birthday` / `vsie_funeral`), claimed by two `domain=External` groups
  (`vsieBirthdayGathering` / `vsieFuneralGathering`) in `1.6/Defs/DiaryExternalGroups_Vsie.xml`. The
  event is recorded once, from the organizer's POV, so the colony **moment** is remembered; each
  attendee's private feeling already arrives via `vsie_thoughts` (VSIE's `VSIE_Attended…`/`VSIE_Had…`
  mood thoughts). The flavor gatherings (dates, movie night, skygazing, snowmen, beer/binge/outdoor
  parties) are intentionally not captured (pure map `Source/Pure/VsieGatheringMap.cs`, covered by
  `tests/VsieBridgeLogicTests/`). Every group is `enableWhenPackageIdsLoaded`-gated and the gathering
  `PatchAll` is skipped unless VSIE is active, so the whole mod is inert without VSIE. Because the
  gathering entries are External-domain (which Pawn Diary's Events tab deliberately excludes — see
  `IsSettingsEventFilterGroup`), the adapter carries its **own mod settings** (`VsieBridgeMod` +
  `VsieBridgeSettings`) with a per-type toggle for birthdays and funerals (both default on; the postfix
  checks `AllowsEventKey`); VSIE's four non-External XML groups stay toggleable in Pawn Diary's Events tab.
- **`PawnDiary.PersonalitiesBridge` (`Pawn Diary: 1-2-3 Personalities`)** — XML **plus** a small
  assembly. Tier 1 (XML): `personalities123_thoughts` (Thought, `matchPackageIds` on M1+M2) and
  `personalities123_interactions` (Interaction, `matchPackageIds` on M2, not batched). The assembly
  (`PawnDiaryPersonalities123.dll`, net472, hard-refs `SP_Module1.dll`) turns each colonist's Enneagram
  root (`SP_Root1..9`) into their **editable** Pawn Diary psychotype through one single-choice **mode**
  setting with three escalating tiers: *map to a built-in psychotype* (`SetPsychotype` to the mapped
  `DiaryPsychotype_*`, pinned), *override from personality* (`SetPsychotypeCustomRule` from the pure
  `EnneagramLensMapping` outlook text), or *experimental LLM transform* (`RequestLlmCompletion` on a
  selectable lane with an editable prompt, seeding the custom rule from the model's rewrite and falling
  back to the override text on any miss; the transform input carries a localized base outlook authored
  from the pawn's 1-2-3 Enneagram root — never from their current Pawn Diary psychotype — as the
  authoritative text to rewrite, so a small model must retain every tendency/contradiction and may use
  the personality style/main trait only as secondary emphasis rather than inventing from a type number).
  Both deterministic and LLM modes write that result through `SetPsychotypeCustomRule`; psychotype
  resolution selects the custom rule instead of the base rule, so the 1-2-3 outlook is a complete
  replacement, not an extension or concatenation of the previous Diary personality.
  Change-detected by `<mode>:<root>` and **saved** with the game
  (a reload never re-seeds over the player's edits); re-seeds on a mode or root change, and re-seeds the
  whole colony on **any effective** bridge or selected Pawn Diary lane change. The component saves a
  deterministic, secret-free configuration fingerprint, so changes are detected across process restarts
  and no raw prompt/endpoint credential is written to the game save. Because that fingerprint includes
  the resolved prompt, the improved localized default re-seeds an old default-produced result once;
  player-authored prompt text remains authoritative and unchanged. In the LLM tier the bridge also registers an
  external psychotype generator (`RegisterExternalPsychotypeGenerator`), so the per-pawn voice editor gets
  a Regenerate button + loading status wired to the component's `RerollTransform` / `IsTransformInFlight`. The pure mapper
  (root → outlook rule, root → built-in psychotype, transform-input assembly) is unit-tested by
  `tests/Personalities123BridgeLogicTests/`. Read-only toward 1-2-3 Personalities; a one-time first-tick
  sweep releases locked overrides earlier versions placed (even when 1-2-3 is inactive) and preserves
  any player custom rule underneath. SP_Module1-typed reads are `[NoInlining]` behind the cached `SimplePersonalitiesActive`
  (`hahkethomemah.simplepersonalities`) guard. LLM mode exposes its exact nonblank `userText` schema in
  the now-scrollable settings page: `personality style:`, `main trait:`, localized `base outlook:`, and
  the raw target-owned `details:` serialization returned by 1-2-3's `ExtractPersonality`. The disclosure
  explicitly lists that entries, memories, the existing Diary psychotype, and writing-style prose are not added.

Thought-domain caveat (applies to all `*_thoughts` compatibility groups above): a Thought-domain
group only assigns instruction/tone; whether a thought is recorded, and whether it folds into the
ambient thought note, is
governed by Pawn Diary's global thought policy (mood-offset thresholds), **not** by a per-group
`<batch>` (which is Interaction-only and silently ignored elsewhere). Those groups therefore theme
their thoughts and lean on the mood threshold as the flood guard.

## 4. Event Sources

The catalog of every event the diary reacts to (`DiaryEventType`), with the `DiarySignal` that carries
it onto the bus.

| Event type | Observed by | Ingestion | Shape |
|---|---|---|---|
| Thought | `MemoryThoughtHandler.TryGainMemory` | `ThoughtSignal` | solo (+ ambient) |
| Inspiration | `InspirationHandler.TryStartInspiration` | `InspirationSignal` | solo |
| Ability | `Ability.Activate` overloads | `AbilitySignal` | solo (sampled) |
| Romance | `Pawn_RelationsTracker.AddDirectRelation` | `RomanceSignal` | pair |
| Raid | `IncidentWorker.TryExecute` | `RaidFanoutSignal` | fan-out |
| MoodEvent | `GameConditionManager.RegisterCondition` | `MoodEventFanoutSignal` | fan-out |
| MentalState | `MentalStateHandler.TryStartMentalState` | `MentalStateSignal` | pair + solo |
| Tale | `TaleRecorder.RecordTale` | `TaleSignal` | solo / batch / death |
| Hediff | `Pawn_HealthTracker.AddHediff` + scan | `HediffSignal` | solo body/health page or day-reflection |
| Interaction | `PlayLog.Add` | `InteractionSignal` | pair / solo / batch / ambient |
| Work | Periodic job sampling | `WorkSignal` (via work scan) | solo |
| ThoughtProgression | Periodic scan | `ThoughtProgressionSignal` (via scan) | solo |
| Progression | Periodic scan | `ProgressionSignal` (via scan) | solo |
| PersonaWeapon | `CompBladelinkWeapon` coding/equipment/destruction/cleanup hooks + elapsed reconciliation | `PersonaWeaponSignal` | solo important lifecycle page |
| DayReflection | Sleep/rest flush | `DayReflectionSignal` (aggregation flush) | solo day/quadrum reflection |
| ArcReflection | Sleep/rest flush + major psylink/xenotype progression trigger | `ArcReflectionSignal` (memory aggregation flush) | solo yearly arc reflection |
| Quest | `Quest.Accept`/`End` + state scan | `QuestFanoutSignal` | fan-out |
| Ritual | Ideology/psychic ritual completion | `RitualFanoutSignal` / `PsychicRitualFanoutSignal` | fan-out; XML group guidance plus role/perspective instruction. Anomaly's 16 installed psychic rituals route exactly into invitation, flesh/weather, predation, mind, abduction, or death-refusal guidance before the generic modded fallback. |
| Death | `Pawn.Kill` + death TaleDefs | `DeathFallbackSignal` (+ Tale death routes) | neutral description |
| Arrival | Starting scan + `Pawn.SetFaction` | `ArrivalSignal` | neutral description |
| External | `PawnDiaryApi.SubmitEvent` / `SubmitPromptEntry` (other mods) | `ExternalEventSignal` | solo / pair |

| Source | How it is observed | Result |
|---|---|---|
| Social interactions | `PlayLog.Add` | Pair, solo, batched, or ambient note by XML group; optional batch promotion is scaled by the shared random-generation setting. |
| Mental states | `MentalStateHandler.TryStartMentalState` | Social fighting can be pairwise; other breaks are solo. |
| Romance | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise lover/spouse/ex relation moments. |
| Tales and combat | `TaleRecorder.RecordTale` | Solo, pair, delayed combat batches, or death description. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first page. |
| Deaths | `Pawn.Kill` plus XML death TaleDefs | Neutral final page. |
| Mood events | `GameConditionManager.RegisterCondition` | One entry per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | XML-filtered memory entries; ambient thoughts can batch. Memories vanilla rejects (accept-gates fire before `thought.pawn` is assigned) are ignored — never gained, so never recorded. If a malformed/modded `ThoughtDef` throws while resolving its localized label, capture continues with the stable `defName` as a technical fallback. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, chemical, and similar worsening stages. |
| Pawn progression | Periodic scan plus defensive Royalty exact hooks | Passion-only skill milestones, cause-aware psylink gains, xenotype changes, faction-aware royal-title gained/promoted/demoted/lost transitions, and newly gained personality traits. Exact Royalty hooks advance saved truth immediately; the scanner remains a loss-aware fallback and observes while output is disabled. Bestowing/anima ritual owners suppress matching progression, while neuroformer owns one progression page. Trait gains feed the trait's own character-card description (no stat/mechanic lines) into the prompt so any trait — vanilla or modded — is voiced as a felt personality shift without a hardcoded per-trait table. First observation baselines existing saves to avoid retroactive spam; major psylink/xenotype changes can request a rare arc reflection after the normal page records. |
| Royalty persona weapons | Defensive `CompBladelinkWeapon` coding, equip/loss, destruction, map-removal, and `UnCode` hooks plus an independent elapsed scan | Exact coding/transfer creates one formation epoch; a short weapon swap is silent; one meaningful separation appears only after the XML threshold and only its accepted page authorizes recovery; destruction creates one standalone ending. State advances even when output is disabled, and late-visible old bonds baseline silently. Current narrative context requires the exact live coded weapon rather than saved state alone. Pawn death/map removal remain page-silent here, and all live reads no-op without Royalty. |
| Biotech growth moments | `Pawn_AgeTracker.BirthdayBiological`, `ChoiceLetter_GrowthMoment.ConfigureGrowthLetter` / `MakeChoices` | Age-7/10/13 before/after ownership. A committed verified mutation becomes one child-solo, supporter-solo, or child/supporter page; postponed letters survive save/load, and pending ownership never tick-expires while the pawn's growth letter is still open (vanilla growth letters have no timeout) — even past the pawn's next birthday, where the answered letter still attaches to its original claim via a whole-pawn fallback lookup; auto-resolved growth completes immediately; stable child-ID/age checks across hot and archived events prevent replay regardless of which diary owns the page. N2-B can enrich that same page with exact saved family continuity and a visible current non-Baseliner identity, selected through the shared bounded provider policy. Unsupported/failed/disabled ownership releases the existing Birthday route and consumes trait/skill baselines. Entire path is inert without Biotech. |
| Biotech family birth | `PregnancyUtility.ApplyBirthOutcome`, nested Tale/Thought/Ritual correlation, naming poll | One exact healthy/ill/stillborn canonical birth, written by at most two unique eligible adults in role-certainty order. XML owns mature-birth/loss classifier names. Unresolved naming survives save/load with frozen event-time prompt/display context and chronological insertion before a same-call death boundary; writer/child resolution covers maps, caravans, and travelling transporters, so a family that leaves the map during the naming window keeps its page; hot/archive family+child ownership prevents replay; disabled/invalid/thrown ownership releases staged mature routes. Exact miscarriage closes/enriches the existing thought path without inventing an extra event. Capture is inert without Biotech, but saved pending/arc state keeps maintaining itself when the DLC is later disabled: the naming poll finds nothing, so a pending birth flushes its canonical page from the frozen event-time facts with the birth-time child name after the normal grace (or prunes when every writer is gone), and family arcs keep compacting/pruning instead of freezing. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo inspiration entry. |
| Hediffs | `Pawn_HealthTracker.AddHediff` and scan | Immediate or day-reflection health entries by XML policy, including string-matched Anomaly mental afflictions, artificial/anomalous body-part gains, and living-pawn natural body-part losses. |
| Work | Periodic current-job sampling | Non-social, non-violent work, controlled by XML odds/cooldowns and the shared random-generation setting. |
| Raids and infestations | `IncidentWorker.TryExecute` | Fan-out to eligible colonists; ordinary raids can delay generation. |
| Quests | `Quest.Accept`, `Quest.End`, defensive UI/state scan | Accepted quests are bookkeeping/event-window signals only. Completed and failed quest outcomes create shared-effort entries; prompt labels reject placeholder names and humanize code-like quest defNames. |
| Event windows | `IncidentWorker.TryExecute`, `Quest` lifecycle, `Thing.SpawnSetup`, `SignalAction_Letter`, `CompProximityLetter`, `Building_VoidMonolith.Activate`, `Pawn_AgeTracker.BirthdayBiological`, `Pawn_HealthTracker.AddHediff`, `PrisonBreakUtility.StartPrisonBreak` | XML starts/ends narrative windows or one-shot events, writes phase entries, and can bias prompts while active. A Def may also attach an optional plain `narrativeEvidence` template after a page exists; exact deliberate monolith levels use this without authorizing extra pages, while timer-driven activation stays silent because vanilla supplies a random colonist rather than a truthful actor. |
| Observed conditions | Periodic live-state scan (map danger, active game conditions, evidence things, pawn hediffs) | Lasting states read from live state, not a guessed duration: bias prompts while present, optionally record start/end pages, and end after a debounce when live state stops showing them (Plan 12; see §5.1). |
| Rituals | Ideology and psychic ritual completion hooks | Fan-out by role/perspective when DLC content is active. |
| Abilities | `Ability.Activate` overloads | Cooldown-weighted caster entry, scaled by the shared random-generation setting. |
| Day reflections | Sleep/rest trigger | One reflective page per pawn/day when important signals exist. Near the end of a quadrum, a pawn with enough important entries may write one longer quadrum reflection instead; that skips the ordinary daily reflection for that night. |
| Arc reflections | Sleep/rest trigger and major psylink/xenotype progression trigger | Rare yearly life-arc page per pawn, with optional extra major-event pages after the configured gap up to `arcReflectionMaxEntriesPerYear` (default 2). The sleep/rest annual check is gated by `arcReflectionEnabled`, not by day summaries. It samples existing hot/archive diary pages from the current year, de-duplicates by event ID, excludes prior reflections/death descriptions/recently used memories, and never stores a separate history fact database. |
| External mod events | `PawnDiaryApi.SubmitEvent` / `SubmitPromptEntry` called by adapter mods (§3.7, `INTEGRATIONS.md`) | Solo or pairwise page from another mod. Ordinary `SubmitEvent` requires External-domain group XML to claim the submitted `eventKey`; wrapped prompt entries can be group-less because their protected `promptInstruction` supplies the entry instruction. |

**Anomaly semantic precision (Master Wave 2 / A0.0–A0.2).** Psychic-ritual live capture and recovery
construct the stable classifier key `PsychicRitual;<PsychicRitualDef.defName>`. Orders `770–775` are six
package-gated exact families covering all 16 installed RimWorld 1.6 defs; the order-`776`
`ritualAnomalyPsychic` token row remains the future/modded fallback. The exact rows and fallback are
hidden from settings when `Ludeon.RimWorld.Anomaly` is absent. They change only prompt guidance—not
page count, role fan-out, success criteria, or captured facts—and every instruction defers agency and
victimhood to `psychic_ritual_perspective`/target facts rather than asserting an unverified result.

`Building_VoidMonolith.Activate(Pawn)` supplies one completed `VoidMonolith;activated` signal with
the reached level defName and exact activator only for deliberate activation. The prefix reads the
private scheduled activation tick before vanilla clears it; when that tick is already due, vanilla's
random colonist argument is not treated as an actor and the automatic transition stays silent. If the
field cannot be resolved, the fragile hook warns and disables itself rather than risking false
attribution. XML maps `Stirring` to the stable existing `VoidMonolithActivation` window, `Waking` to
`VoidMonolithWaking`, and `VoidAwakened` to `VoidMonolithVoidAwakened`; discovery remains its own
prologue. All three deliberate activation pages are one-shot `SubjectPawn` rows with distinct
localized fallback text. Their optional `narrativeEvidence` blocks save visible `journey_chapter` phases
`stirring`/`waking`/`void_awakened`, major salience, and the primary per-save arc key
`anomaly-monolith|0`. No hidden entity, host, downside, terminal choice, or terminal outcome is saved.

Hooks are grouped by domain under `Source/Patches/`. Fragile reflection targets register through
`DiaryPatchRegistrar` so missing methods warn and no-op instead of breaking startup. Capture hooks,
per-tick work, save/load bookkeeping, startup registration, and vanilla UI overlays isolate failures
with one-time logging and preserve vanilla behavior. Live `OpinionOf` reads used by day summaries,
interaction promotion, and pairwise prompt continuity share one fail-soft guard: a throwing social-
thought walk contributes no baseline or neutral opinion instead of aborting the whole component tick.

## 5. XML Policy

XML owns policy that designers should be able to change without recompiling.

| XML file | Owns |
|---|---|
| `DiaryInteractionGroupDefs.xml` / `Defs/Compat/*.xml` | event classification, group instructions/tones, batching, hediff modes, colors, default enablement, optional-mod compat groups |
| `DiaryEventWindowDefs.xml` | one-shot or timed story windows from game signals |
| `DiaryObservedConditionDefs.xml` | live-state conditions such as map danger, game conditions, evidence things, and visible hediffs |
| `DiaryEventPromptDefs.xml` | event prompt text, enhancements, and optional forced model names |
| `DiaryPromptTemplateDefs.xml` / `DiaryPromptDef.xml` | prompt field shapes and shared system/final instructions |
| `DiaryPersonaDefs.xml` / `DiaryHediffPersonaOverrideDefs.xml` | writing styles (incl. child styles) and temporary hediff-driven style overrides |
| `DiaryPsychotypeDefs.xml` | pawn psychotypes (outlook layer): Neutral + 17 adult + 3 trait-gated + 5 child types, families, skill affinities, trait gates |
| `DiaryPsychotypeRollPolicyDefs.xml` | numeric tuning for the psychotype roll: family bases, bonuses, wildcard chance, jitter range, duplicate penalty |
| `DiaryPsychotypeTraitPolicyDefs.xml` | canonical trait/degree mappings, family/member roll bonuses, and gated takeover chance |
| `DiaryNarrativeContinuityDefs.xml` | DLC-neutral evidence/lens/reflection caps, score precedence, compact budgets, repetition/age policy, category coexistence, reflection priority, and localized optional prompt wording; the main-thread builder snapshots it before fixed-order pure provider selection. The repetition policy is live: every narrative-capable source feeds the selector the POV pawn's most recent persisted selection keys (newest hot pages, then archive rows, bounded by `maxRecentSelectedCandidateKeys`), so `repetitionPenalty` dampens re-picking the same lens while exact-arc continuations stay exempt via `exactArcRepetitionPenalty` |
| `DiaryBiotechPolicyDefs.xml` | B1 growth/family/birth thresholds, growth-tier opportunity bands, localized passion/upbringing and N2-B family/current-identity prose, pending/fallback/correlation timing, exact pregnancy/labor/activity/memory plus mature-birth/miscarriage matchers, supporter thresholds/caps, naming timing, family retention, two-writer birth cap, pending-growth/pending-birth admission limits, Phase-5 gene category/theme/text/observation/fallback-significance policy, N3-B salient-gene identity prose, and Phase-6 mechanitor combat Tale roles/tenure/state caps; Phases 1–6, N2-B, and the first N3-B slice use these fields live |
| `DiaryPromptEnchantmentDefs.xml` / `DiaryHumorCueDefs.xml` | weighted live-context and hidden humor cues |
| `DiarySignalPolicyDefs.xml` / `DiaryTuningDef.xml` | scan intervals, odds, cooldowns, thresholds, reflection policy, fallback tuning |
| `DiaryUiStyleDef.xml` / `DiaryTextDecorationDefs.xml` | UI dimensions/colors and display-only rich-text decoration |

`DiaryUiStyleDef.xml` owns the Diary tab's preferred size. `<tabHeight>` is a preferred height, not
an absolute one: before every draw the tab clamps itself to the space actually available above its
bottom anchor — inspect tabs hang above the inspect pane's tab strip, not the screen bottom — minus
`<tabScreenHeightMargin>` of clear screen kept above the tab, while `<tabMinHeight>` keeps it usable
on ordinary resolutions. If the screen is shorter than that minimum, the tab shrinks further rather
than running off-screen.

Interaction groups match by domain, exact `defName`, optional package id, and ordered token matchers.
Prefer exact names, `matchPrefixes`, `matchSuffixes`, and `matchSegments`; use legacy
substring-style `matchTokens` only when broad matching is truly intended. Lower `order` wins, so put
specific groups before broad groups. The pure matcher lives in `Source/Capture/GroupNameMatcher.cs`.
Three runtime gates control availability: `disableWhenPackageIdsLoaded` silences a group while a
replacement mod is loaded, and `enableWhenPackageIdsLoaded` keeps a compatibility group inert unless
one of its target mods is present. `disableWhenCaptureCapabilitiesReady` silences a lower-fidelity
group only while any listed adapter capability reports ready, so hook drift can release the fallback
without relying on package presence. All gates are enforced uniformly: `IsGroupEnabled`,
`EventFilterGroupsForSettings`, and the External-domain classifier (`ClassifyExternal` consumers in
`PawnDiaryApi` and `ExternalEventSignal`) all treat a gated group as inert, so a compatibility group
sits harmless across automatic capture, the settings UI, and the integration API. External-domain
groups classify the integration-API `eventKey` strings other mods submit (see §3.7).

Biotech has two live, exact, package-gated settings rows: `progressionGrowthMoment` (`Progression`,
`BiotechGrowthMoment`, order 800) owns committed age-7/10/13 changes, while `biotechFamilyBirth`
(`Tale`, `BiotechFamilyBirth`, order 315) owns exact canonical births. Effective growth settings inherit an
explicit legacy `eventWindowBirthday` override until the new row is toggled. When canonical growth is
off but Birthday is on, the ordinary Birthday is delayed until choice completion and released once;
if both are off, observation/baselines still advance without a page. Effective birth settings similarly
inherit explicit `talelife` and, for ritual births, `ritualChildbirth` intent. A new explicit override
always wins; missing overrides fall back to the new XML default. Canonical birth freezes the effective
choice at event completion: enabled ownership stages/suppresses the mature Tale/Thought/Ritual rows,
while disabled or writerless ownership releases them through their original settings.

`DiaryEventWindowDef.enableWhenPackageIdsLoaded` provides the corresponding gate for compatibility
windows. Start matching, restored active state, prompt candidates, timeout scanning, and direct
recording all reject a missing target package, so removing a mod also clears its saved active window.
Observed-condition compatibility still uses exact live-state strings because that Def type never
resolves target content and a missing condition can never become active.

Hediff body-part events use the same XML classifier, but classify by a synthetic key when the live
HediffDef is a body-part change: `BionicArm_addedpart`, `Tentacle_addedpart_organicpart`, or
`MissingBodyPart_missingpart`. The saved `gameContext` carries `part_kind=`, `part_tier=`,
`body_attitude=`, and optional `part_cause=` markers so saved pages recover the same group after
load. `DiaryTuningDef.xml` owns body-part tier overrides, body-mod trait/precept/inhumanized lists,
and efficiency thresholds; the C# fallback values keep missing XML safe. The localized prompt prose
for these groups deliberately carries the atmosphere in XML: anomalous flesh leans sensory and
unexplained, artificial parts stay embodied and practical, and lost natural parts emphasize absence,
phantom feeling, and adjustment.

Interaction `PairEvent` batches only use the combined batch prompt when two or more moments collect in
the quiet window. If the window flushes with a single moment, the entry is emitted as a normal
standalone interaction with the original defName, label, first POV texts, and group instruction. That
keeps low-frequency insults/slights from being written as artificial "batch" summaries.

Event prompts resolve from narrow to broad: source defName, interaction group, classifier key, then
domain. Prompt text, enhancement text, and forced-model text resolve independently, so a narrow row can
override one field and inherit the others.

Progression policy is split the same way as other sources: `DiaryInteractionGroupDefs.xml` owns the
`Progression` and `Reflection` groups and their importance, `DiaryEventPromptDefs.xml` owns broad
progression/arc prompt guidance, `DiaryPromptTemplateDefs.xml` exposes progression fields and the
`SoloArcReflection` template, and `DiaryTuningDef.xml` owns milestones, psylink hediff defName
matchers, arc cadence, major-progression policy for psylink severity and configured xenotype
defNames, high-stakes arc memory tokens, and the memory-shortfall retry backoff.

Optional DLC or mod content should normally be handled as string matches. Do not hard-reference DLC
defs or C# types unless they are guarded as described in `AGENTS.md`. Missing DLC content should
simply never match.

Hediff policy has two separate knobs:

- `DiaryPromptEnchantmentDefs.xml` adds condition/status context to prompts.
- `DiaryHediffPersonaOverrideDefs.xml` can temporarily force the writing style.

If the same hediff wins a writing-style override, its matching prompt-enchantment cue is suppressed so
the condition is not repeated in both the style block and the `important context:` line. A saved
external writing-style override shadows hediff-driven style, so hediff prompt enchantments are not
suppressed on behalf of a hediff style while the external rule is active.

Event windows are for one-shot signals and bounded story phases. A `DiaryEventWindowDef` can start,
end, time out, write phase pages, and add a weighted prompt candidate while it is active.
`keepActive=false` turns the start signal into a one-shot page. `recordScope=SubjectPawn` records only
the pawn carried by the signal; `recordScope=MapWitness` deterministically records one eligible
colonist from a map-level signal that has no subject, while `Map` retains colony-wide fan-out.
Load-time `ConfigErrors` reject a persistent window with
`keepActive=true`, no positive `timeoutTicks`, and no usable `endSignals` trigger, because that shape
has no guaranteed close path and could leave prompt context active forever.

A persistent window can also declare a **still-present probe** so it closes early once its spawning
threat is gone, instead of waiting out its full `timeoutTicks`. Two optional plain-string lists drive
it (empty/absent = no probe = timeout-only behavior, as before): `stillPresentThingDefNames` (the
window stays active while any listed ThingDef is spawned on its map, DLC-safe via `GetNamedSilentFail`)
and `stillPresentFactionDefNames` (stays active while any spawned pawn of a listed faction defName is
on the map). The timeout scan probes these every scan; when neither matcher is satisfied the window is
silently removed (no end page — use `endSignals` for a resolution page). This is the event-window
analog of how an observed condition ends when its observation stops. `MechClusterLanded` uses it: a
`Mechanoid`-faction probe ends the up-to-three-day dread window as soon as no mechanoids remain, so a
destroyed cluster stops coloring prompts promptly.

Hot event-window paths use `EventWindowPolicy.CouldMatchByDefName` before resolving labels or doing
expensive work. Window recording is isolated from normal raid, quest, hediff, and other capture paths;
a window failure must not suppress the base diary entry.
The EVT-22 loaded fixture distinguishes a Def being loaded/enabled from its optional package being
active: exact monolith Defs must exist in either configuration, create their source-owned chapters
when Anomaly is active, and remain inert when `MissingRequiredPackage()` reports Anomaly absent.

Event-window pages honor the player's settings Events row: `RecordEventWindowPhase` classifies the
window's `defName` in the Interaction domain (e.g. `Birthday` -> `eventWindowBirthday`) and skips the
page when `PawnDiarySettings.IsGroupEnabled` says the row is off. Only the page is suppressed —
window open/close state, dedup stamps, and active prompt candidates keep working, and the Biotech
growth fallback relies on this gate to consume baselines without releasing a Birthday page when both
its rows are disabled.

The event-coverage pass added three incident-driven tone windows: `MechClusterLanded` (records one
start page per map colonist, then keeps a decaying dread candidate active for up to three days),
plus `ShortCircuitAftermath` and `SelfTameJoined` (tone-only, never pages). Every def that records
a page has a companion Interaction-domain display group (`eventWindow*`, `observedPitGate`,
`observedFleshmassHeart`) so the saved page classifies to a proper label/importance in the Diary
tab instead of the catch-all.

**In-game API Explorer.** `integrations/PawnDiary.ExampleAdapter/` ships a Dev-mode window that
drives every public `PawnDiaryApi` family from a three-pane UI (method tree | request form | running
result log), grouping related overloads into one form. It includes the psychotype snapshot/rule,
editable base/custom layers, source-owned override pair, external generator registration, and the
paid one-shot completion request/poll/cancel lifecycle. It exists so adapter authors and the
maintainer can probe the contract interactively without writing throwaway code. Open it via Dev
mode → Debug Actions → *Pawn Diary Example Adapter*
→ *Open API explorer…*. The method tree supports filtering/collapse, the request form exposes
width-aware single-line fields plus multiline editors for prose/context values and a shared-state
reset button, method rows show plain-language endpoint descriptions under each API signature,
method titles and field labels show short help popovers on hover, and the result log keeps short
histories compact so the selected detail stays visible. All request fields start with concrete
sample values for quick submit/preview testing. Ordinary, wrapped-prompt, and direct-entry builders
default to `exampleadapter_quiet_moment`, `exampleadapter_prompt_idea`, and
`exampleadapter_direct_note` respectively, so every shipped External group/settings row has a live
submission path. The completion request form warns that invoking it can spend provider tokens.
Opening the explorer closes the Debug Actions
launcher, and the window has a thin drag strip so it behaves as a movable, resizeable debug overlay:
clicking outside it keeps it open while normal game UI/camera input still passes through. A concise operator guide lives at
`integrations/PawnDiary.ExampleAdapter/API_EXPLORER.md`. The same mod also registers the two process-global hooks
(`RegisterEntryStatusListener`, `RegisterPawnContextProvider`) through the copyable
`PawnDiaryExampleApi.cs` facade and exposes their activity in the explorer's Hooks tab. Its pure
text-parsing helpers are unit-tested by
`tests/ExampleAdapterParsingTests/`.

### 5.1 Observed conditions (lasting game state, Plan 12)

Observed conditions are for lasting states that should be re-read from live game state instead of
guessed from a timeout. Examples: map danger, toxic fallout, solar flare, or observable Anomaly
evidence.

The flow is:

1. `DiaryGameComponent.ObservedConditions.cs` polls due `DiaryObservedConditionDef` rows.
2. Live state is copied into plain `ObservedConditionObservation` DTOs.
3. `ObservedConditionPolicy.Plan(...)` diffs observations against saved active rows.
4. The component persists `ActiveObservedConditionState` rows and optionally records start/end pages.

The pure policy lives under `Source/Capture/ObservedConditions/` and is covered by
`tests/DiaryObservedConditionTests`. Ticks only gate debounce. Truth always comes from the current
observation set, so loading a save mid-condition or missing an end signal self-corrects on the next
poll.

Observer types are DLC-safe:

- `MapDanger`: home-map danger rating or spawned hostile count.
- `GameCondition`: matching active game condition defName.
- `ThingPresent`: spawned observable things/filth via `ListerThings.ThingsOfDef`. A Def can also
  list `suppressWhenThingDefNames`; if any of those spawned thing defs are present on the same map,
  that Def reports no observation and the normal end-debounce path resolves its active state.
- `PawnHediff`: visible pawn hediffs only; hidden hediffs are skipped.
- `PawnUnnaturalCorpse`: Anomaly, pawn-scoped. No defName list — the matcher is the DLC's own
  tracker (`GameComponent_Anomaly.PawnHasUnnaturalCorpse` via the guarded `DlcContext` accessor).
  Emits one Pawn-scoped observation per colonist who is currently being imitated by an unnatural
  corpse, so the prompt bias lands only on the haunted pawn (not the whole map). When the corpse is
  destroyed/dissolves vanilla clears the tracker link, the observation stops, and the pure policy
  ends the state via its normal missing/end-debounce path — the same end-on-disappearance mechanism
  `MetalhorrorEmergence` relies on. No-ops cleanly without the Anomaly DLC.
- `MapHiddenHediff`: senses whether ANY home-map colonist carries a matching hediff **including hidden
  ones**, collapsed to a single map-level boolean. Tone-only by contract — the collector emits an empty
  evidence label and never names the hediff or a host, so a Def can color prompts with "the colony is in
  this hidden state" dread without revealing the hidden mechanic. This is how `MetalhorrorInfection`
  senses an undiscovered infection.
- `RecentEvidence`: reserved, currently no-op.

Prompt influence from lasting sources is age-aware. `DiaryEventWindowDef` and
`DiaryObservedConditionDef` both support `promptDecayTicks` and `promptDecayMinMultiplier`: as the
window/condition ages, its candidate weight fades toward the multiplier floor and any
`normalPromptWeightMultiplier` override relaxes back toward ordinary prompt-enchantment context.
Observed conditions also support `maxActiveTicks` and `restartCooldownTicks`, saved per condition
identity, so a condition can force-stop after a configured age and then avoid immediately restarting
if its original evidence lingers.

Prompt bias follows the same missing/end debounce as the lifecycle policy. A condition that is missing
but still inside its `endDebounceTicks` can continue to color prompts, which smooths short lulls during
combat or similar states. Once the debounce boundary is reached, prompt bias stops even if the saved row
is retained to retry an optional end page because no eligible pawn was available.

Shipped notable defs:

- `MapThreatActive`, `ToxicFalloutActive`, `SolarFlareActive`: prompt-tone only.
- `SeasonalFloodActive`: Odyssey-gated map-scoped `ThingPresent` observer for the exact
  `SeasonalFlood` ThingDef. It has short end hysteresis plus a restart cooldown, shades authorized
  prompts while floodwater exists, and never records start/end pages. Its `MayRequire` gate removes
  the Def and settings row entirely when Odyssey is inactive.
- `AnomalyGrayFleshEvidence`: records the observable Anomaly sample but hides the item label from
  prompts; the LLM-facing wording frames it as paranoia and fear that something may infect and
  imitate people. It decays over time, is suppressed once a visible metalhorror or metalhorror debris
  appears, and force-stops with a restart cooldown if no emergence happens, so lingering evidence
  cannot keep or immediately reactivate suspicion forever.
- `MetalhorrorEmergence`: enabled map-scoped observer for the spawned visible `Metalhorror` ThingDef. It
  has **no** `maxActiveTicks` cap — its natural end trigger is reliable: when a metalhorror dies it
  becomes a `Corpse_Entity` (a different def), so `ThingsOfDef(Metalhorror)` stops matching and the end
  debounce releases the `normalPromptWeightMultiplier=0` override shortly after the kill. That lets a
  multi-day rampage keep the override live as long as the metalhorror is actually on the map. The cap and
  cooldown that `AnomalyGrayFleshEvidence` carries are intentionally absent here, because the lingering
  plain-item problem those guard against does not apply to a dead entity.
- `MetalhorrorInfection`: enabled map-scoped `MapHiddenHediff` observer for the hidden `MetalhorrorImplant`
  hediff. The metalhorror situation often is not over once the visible entity is killed — one metalhorror
  emerges from one host at roughly half-colony infection, so other colonists can still be carrying the
  implant silently. This condition senses "is any home-map colonist infected?" as a map-level boolean and
  keeps a softer dread-tone override alive until the colony is genuinely clean. It is tone-only by
  contract: the collector emits an empty evidence label and the prompt prose never names a hediff or a
  host, so the hidden mechanic is never revealed. Like emergence it carries no cap, because a cured-or-dead
  host's `hediffSet` is genuinely empty. While a metalhorror rampages and colonists are also infected,
  both conditions fire and the stronger (Emergence) candidate wins the weighted pick.
- Event-coverage pass (see `EVENT_PROMPT_MAP.md` §5 for the full weight table): `ColdSnapActive`,
  `HeatWaveActive`, `VolcanicWinterActive` (base-game climate), `BloodRainActive`, `DeathPallActive`,
  `UnnaturalDarknessActive` (Anomaly game conditions), and `ObeliskPresence` (the three
  `WarpedObelisk_*` ThingDefs), `HarbingerTreePresence`, `NociospherePresence`, and
  `UnnaturalCorpsePresence` are all prompt-tone only. `UnnaturalCorpsePresence` is the lone
  `PawnUnnaturalCorpse` observer: it keys on the haunted pawn via the Anomaly tracker so only the
  colonist being imitated gets the dread, and it ends automatically when the corpse is destroyed. Its
  localized prompt text frames the condition as a personal haunting by a corpse wearing that pawn's
  face, without explaining the Anomaly mechanics.
  `PitGatePresence` and `FleshmassHeartPresence` additionally record one start page per map
  colonist and have companion display groups (`observedPitGate`, `observedFleshmassHeart`).
  `HarbingerTreePresence`, `PitGatePresence`, and `FleshmassHeartPresence` carry
  `maxActiveTicks`/`restartCooldownTicks` backstops (mirroring `AmbrosiaSprouted`) so their prompt
  bias cannot run unbounded in the rare case a resolved threat leaves a same-defName remnant.
  `ThrumboVisit`, `AlphabeaversActive`, `CropBlightActive`, and `AmbrosiaSprouted` are light
  weighted-random flavor with `maxActiveTicks` caps and `restartCooldownTicks` so long-lived
  evidence cannot push prompts forever. `ThingPresent` matches exact defNames only, so every row
  above lists verified ThingDef names; a wrong name is silently inert.
- VEE adds six exact-string `GameCondition` observers: drought, long night, scorch, whiteout,
  psychic bloom, and a psychic-hum family (`VEE_PsychicHum`, overdrive, stimulation, and unprefixed
  `PsychicRain`). They are tone-only, decay over 120,000 ticks, and disappear through the ordinary
  live-state debounce; no VEE Def is resolved when the mod is absent. Short `SpaceBattle` is handled
  by its one-shot incident window instead of receiving a second, lingering tint.

Page recording is transactional: start/end state is committed only after a page is actually written.
`ConfigErrors` rejects `recordScope=SubjectPawn` unless `scope=Pawn`.

## 6. Prompts And Writing Styles

Prompts are compact `key: value` lines. Empty values and `none`/`n/a`/`unknown` sentinels are dropped.
Templates cover solo, pair, batch, day reflection, quadrum reflection, neutral arrival/death, and
title requests.

Prompt policy layers:

1. Shared system prompts from `DiaryPromptDef`. The shared first-person prompt asks for 1-3
   sentences and carries two anti-sameness rules (vary the opening; avoid a short list of stock
   phrases). The important/combat templates (`SoloImportant`, `PairImportant`, `PairCombat`)
   override it with a 2-5 sentence variant, matching 2-5 final instructions, and a 200-token
   response cap so high-stakes moments get more page weight than small talk.
2. Structured fields from `DiaryPromptTemplateDef`.
3. Event prompt/enhancement/forced-model rows from `DiaryEventPromptDef`.
4. Interaction-group instructions and tones. Groups may carry `instructions`/`tones` variant pools
   next to the legacy singular fields; a non-empty pool fully replaces the singular value and one
   variant is picked per entry by a stable hash, so repeated events of the same kind rotate their
   angle instead of always receiving the same guidance. Most high-traffic groups now ship both
   pools.
5. Writing style from the pawn's saved `DiaryPersonaDef`, unless temporarily overridden by hediff.
6. Psychotype (outlook) from the pawn's saved `DiaryPsychotypeDef` — an independent second voice layer
   (see §6.1), folded into the same combined voice block, before the writing style.
7. Optional prompt enchantments, event windows, observed conditions, and humor cues.

Prompt context detail is applied after those layers have produced the full typed prompt value set,
but before `PromptAssembler` renders the user prompt. `Full` is the compatibility preset and keeps
the current template field list unchanged. `Balanced` and `Compact` run the pure
`PromptContextSelector`: core event facts, role names, direct instructions, external wrapped-prompt
fields, and structural reflection/death/arrival fields are always kept; optional continuity,
enchantment, relationship, setting, pawn-summary, hidden-humor, and broad game-context fields are
ranked by event domain and field source, then kept until the preset's character budget is spent. The
required set also contains Biotech B1's central qualitative facts: growth age/localized opportunity,
chosen trait and changed interests, exact family writer roles, and birth child/outcome/method. The
Royalty persona lifecycle core—weapon label, lifecycle phase, previous/new state, and edge-specific
duration, ending cause, or exact previous pawn—is likewise required in `SoloImportant` when present;
mapped trait meanings remain optional. Compact therefore preserves the lifecycle truth even with its optional
budget exhausted. Internal
IDs, numeric tiers, ticks, and correlation keys are not template fields at any preset. The
selector is deterministic, records kept/cut fields with reasons, and never changes saved
`gameContext` or archived diary data. A cut only means that field is omitted from this one LLM user
prompt.

There is no random chance in context trimming. The selector uses fixed scores so a given event,
template, context detail level, and prompt-field list always produce the same kept/cut result. The
only "probability-like" behavior is upstream: optional pages, prompt enchantments, observed-condition
cues, and humor cues may or may not be present before the selector runs. Once they are in the typed
prompt values, `PromptContextSelector` treats them deterministically.

Context detail presets:

| Preset | Budget target | Selection behavior | Intended use |
|---|---:|---|---|
| `Full` | unlimited | Passes through every renderable template field. | Compatibility and larger models. |
| `Balanced` | 650 chars default; 1,000 reflection; 600 neutral death/arrival | Keeps required fields, then preserves high-signal optional context such as severe pawn state, combat tools, event guidance, domain-specific quest/ritual/ability/progression facts, and threatening surroundings. On ordinary events it also drops the weakest optional fields (routine continuity hints, low-signal tone/setting). | General small models where the strongest flavor should survive. |
| `Compact` | 350 chars default; 600 reflection; 400 neutral death/arrival | Keeps the same required fields but cuts aggressively, usually dropping weaker continuity, numeric metadata, ordinary tone/setting, and broad low-signal context first. | Very small or local fallback models. |

The selector never rewrites, compresses, or summarizes a field value. It either keeps the complete
`label: value` line or cuts that entire field. This is deliberate: prompt previews and saved debug
prompts remain auditable, and the cut report can name exactly which field was removed.

```mermaid
flowchart TD
    A["DiaryEvent + live context snapshot"] --> B["DiaryPipelineAdapters.BuildPromptRequest"]
    B --> C["DiaryPromptPlanner.Build"]
    C --> D["Project full PromptValues"]
    D --> E{"Context detail"}
    E -- "Full" --> F["Keep renderable template fields"]
    E -- "Balanced / Compact" --> G["Build field candidates"]
    G --> H["Always keep required core fields"]
    G --> I["Score optional fields by source, domain, and context key"]
    H --> J["Spend preset character budget"]
    I --> J
    J --> K["Keep highest-priority optional fields"]
    J --> L["Cut remaining complete fields"]
    F --> M["PromptAssembler renders user prompt"]
    K --> M
    L --> N["Selection report: kept, cut, chars, reasons"]
    M --> O["DiaryPromptPlan.userPrompt"]
    N --> O
```

Prompt Studio can override shared system prompts and per-event prompt/enhancement/forced-model text.
Saved override keys must stay stable because they are part of mod settings.

Quest prompts are deliberately sanitized. The raw quest defName stays in saved context for UI/domain
classification, but model-facing fields use labels, signals, factions, and rewards. Accepted quests
do not generate diary pages; completed and failed outcomes fan out as shared colony effort.

Writing styles are backed by `DiaryPersonaDef`. Some code and save fields still say "persona" for
compatibility, but player-facing text should call them writing styles. Hediff style overrides are
prompt-time only and never change the saved picker value. External writing-style overrides are saved
separately above the base style and are owned by the adapter `sourceId` that set them.

The stock catalog deliberately spreads styles across sentence *syntax*, not just mood: besides the
terse/fragment presets it includes six syntax-outlier styles (run-on chains, formal address to the
diary, self-debate in question-and-answer turns, counting habits, second-person self-address, and
least-important-detail-first openings). Style rules avoid hard per-entry sentence counts so they
compose with both the shared 1-3 sentence prompt and the 2-5 sentence important/combat templates;
a rule may still fix a *shape* (for example fragment triplets), just not the entry length.

Each pawn can also carry an optional **pawn-specific custom writing-style prompt** authored by the
player from the pawn's Diary tab (`PawnDiaryRecord.customWritingStyleRule`). Blank means "use the
selected base style"; nonblank means "use this prompt when no higher-priority override is active." It
is saved on the pawn's own diary record, so it never touches `DiaryPersonaDef` XML or the global
`PersonaPresetStore` catalog. The custom prompt keeps line breaks (sanitized by the pure
`PlayerWritingStyleText.CleanRule`, which wraps `PromptTextSanitizer.Multiline`) so the editor stays
readable, unlike the one-line external-API override sanitizer.

The effective writing-style priority, resolved by the pure `WritingStyleResolutionPolicy` from a
runtime snapshot built in `HediffPersonaOverrides.ResolveWritingStyle`, is:

1. **External API override** rule (adapter-owned, highest).
2. **Hediff override** style rule (temporary, while the condition is present).
3. **Pawn-specific custom prompt** (player-authored from the Diary tab).
4. **Base selected style** Def rule (lowest).

Generation only consumes the final `rule` string; the Diary tab opens the Writing Style dialog
(`Dialog_PawnWritingStyle`) from a compact header icon, so the editor affordance does not reserve a
separate row above the diary pages. At the top of the writing-style section, the dialog uses the full
`WritingStyleResolution` metadata to show one prominent **Currently used** status: base style, the
pawn's custom prompt, a health-condition override, or an external-mod override (including its source
id). An override status also says that any saved custom prompt is waiting until the override ends.
This replaces the old read-only effective-prompt preview, which mostly duplicated either the base or
custom text already visible directly above it.
The integration API's `PawnDiaryApi.GetWritingStyle` continues to return the base saved style only
(custom prompt and temporary overrides are not exposed there).

The writing-style rule is appended to the **system prompt** for first-person shapes, never rendered
as a field in the **user** prompt. The single load-bearing line is
`PromptAssembler.ComposeSystem(baseSystemPrompt, personaVoiceBlock, includePersona)`; the voice block
is built by `DiaryPipelineAdapters.CombinedVoiceBlock`, which joins the psychotype lens, the writing
style, and the humor cue in that fixed order (outlook first, style last as the harder mechanical
constraint). Each of the three blocks splices its rule into the localized Keyed frame **verbatim**
(`DiaryPipelineAdapters.InjectVoiceRule` resolves the frame with the arg-free `.Translate()` and
replaces `{0}` itself). Never switch these back to the args-`Translate` overloads: vanilla routes
those through `GrammarResolverSimple.Formatted`, whose `GenText.CapitalizeSentences` pass treats every
`:` as a sentence break and silently rewrites player/XML-authored rule text
("sentinel: end every entry…" became "sentinel: End every entry…"). The neutral death/arrival
chronicles and the title follow-up set `includePersona=false`
so they stay voice-free (psychotype included). Regression tests in `tests/DiaryPipelineTests` pin this
contract: pure unit tests on `ComposeSystem`, an end-to-end test that the psychotype lens reaches the
system prompt before the style and never the user prompt, and a shipped-XML contract test asserting
every first-person template keeps `includePersona=true` and every neutral/title template opts out.

### 6.1 Psychotypes (outlook layer)

The **psychotype** is a second per-pawn voice layer, independent of the writing style. Where a writing
style controls sentence *mechanics* (how a pawn writes), a psychotype is a 1-2 sentence semantic *lens*
(who is looking; what they notice, value, and fear, and how they judge). Rolling the two independently
multiplies them into many distinct diary voices. Psychotypes are backed by `DiaryPsychotypeDef`
(`1.6/Defs/DiaryPsychotypeDefs.xml`): a Neutral (empty-rule) fallback, 17 adult types across four
families (grounded, inward, intense, anxious), 3 **trait-gated** adult types, and 5 child types. The
**label is picker text only and is never injected into the prompt** — `DiaryPsychotypes.RuleFor`
deliberately drops it, a documented divergence from `DiaryPersonas.RuleFor`; the outlook must show
through the entry, never be named.

The initial roll (pure `PsychotypeRollPolicy`, snapshotted by `PsychotypeRolls`) is a two-stage draw
from the pawn's **skill passions** (minor = 1 pt, burning = 2): stage 0 folds the 12 skills into five
domains; stage 1 weights the four families; stage 2 weights the members inside the rolled family
(per-skill nudges on the def, combo signatures, a child→adult continuity nudge, a band-aware duplicate
penalty), with deliberate extra randomness (a 12% wildcard branch that ignores the profile, plus a
per-candidate jitter). Every numeric weight, bonus, threshold, and odds value in that roll is
XML-owned in `DiaryPsychotypeRollPolicyDefs.xml` (projected into the pure `PsychotypeRollWeights`
DTO by `DiaryPsychotypeRollPolicyDef.cs`); only the combo-signature count thresholds stay in code (see
`design/PSYCHOTYPE_PLAN.md` "Out of scope"). A Psychopath never rolls Dependent and a Kind pawn never
rolls Ruthless.

**Traits feed the roll** through `DiaryPsychotypeTraitPolicyDefs.xml`, projected into the pure
`PsychotypeTraitAffinities` algorithm, additively on top of the
passion signals and deliberately scaled above them — a supported trait dominates the outcome while
the passions break ties and colour the rest (a Sanguine pawn leans Content even against a burning
violence profile). Each supported trait maps to a canonical key — simple traits by
defName (Psychopath, Cannibal, Bloodlust, Jealous, Greedy, TooSmart, TorturedArtist, Kind, Abrasive,
Recluse), spectrum traits per degree (NaturalMood → Depressive/Pessimist/Optimist/Sanguine, Nerves →
Nervous/Volatile, Neurotic → Neurotic/VeryNeurotic) — and adds stage-1 family weight plus stage-2
member weight toward its compatible psychotypes (a Sanguine pawn leans Content, a Very neurotic one
Perfectionist/Paranoid, and so on). The three **extreme traits** go further: Psychopath, Cannibal, and
Bloodlust each unlock a psychotype of their own — **Hollow**, **Ravenous**, **Bloodthirsty** (all
intense) — gated by `<requiredTrait>` on the def so no other pawn can ever roll them (the gate holds
on every branch: profile, wildcard, and flat rolls). A pawn holding such a trait adopts its gated
psychotype outright `<gatedTakeoverChance>` (45% in the shipped Def) of the time; otherwise the normal roll continues with
the gate open and the trait bonuses applying, so the outcome is dominant, not guaranteed. The manual
per-pawn picker ignores the gate — the player may hand-assign anything. Unknown/modded traits simply
contribute nothing unless a compatibility patch supplies an affinity. The shipped
`1.6/Patches/VtePsychotypeAffinities.xml` does exactly that for twenty Vanilla Traits Expanded
trait/degree keys through `PatchOperationFindMod`; without VTE, the patch is a clean no-op.

The effective psychotype priority, resolved by the pure `PsychotypeResolutionPolicy`, is **External API
override > pawn custom rule > base type** (there is no hediff psychotype layer in v1). The master
setting **Use pawn psychotypes** (`enablePsychotypes`, default on) gates the *automatic* layer: when off,
the base/custom outlook resolves to an empty rule (the block is omitted) and pending rolls stay deferred.
An active **external API override** (e.g. the RimTalk bridge) is an explicit opt-in from another mod, so
it still applies while the toggle is off rather than being silently dropped. The prompt wrapper is the
keyed string `PawnDiary.Prompt.PsychotypeLens`, placed before the writing-style block by
`CombinedVoiceBlock`.

**Children and crystallization.** The first-person minimum age (`minimumFirstPersonAgeYears`) drops to
7, so children keep a diary in the naive child catalogs of *both* layers. A record stamps which band
("child"/"adult") its rolls were made for; a lazy main-thread check
(`DiaryGameComponent.EnsureVoiceStage`, run before generation resolves the voice) compares that band to
the pawn's current age against `psychotypeCrystallizationAgeYears` (default 13, the final vanilla growth
moment) and, on mismatch, re-rolls both unpinned layers onto the adult catalogs (psychotype with a +1
continuity nudge from the child type). Player-made picks/edits/re-rolls are **pinned** and never
auto-re-rolled. `EnsureVoiceStage` never runs during a UI draw or a read-only prompt preview (it consumes
`Rand` and mutates the record); the tab tooltip and dialog repaint use the read-only
`ResolvePsychotypeForDisplay`, and `PreviewExternalEventPrompt` resolves rules with `ensureVoiceStage:false`.
The band stamp is also deferred for a still-unstamped legacy record while the layer is off, so enabling it
later still freezes an established pre-feature voice to Neutral instead of re-rolling it.

**Save compatibility.** New per-pawn fields (`psychotypeDefName`, `customPsychotypeRule`,
`externalPsychotypeOverrideRule/SourceId`, `voiceStageBand`, `psychotypePinned`, `writingStylePinned`)
default safely on old saves. On load, a legacy record with generated entries adopts **Neutral** (so an
established voice does not shift) while an entry-less record rolls a fresh psychotype lazily on its next
entry — the type is always created if missing. The `DiaryPsychotypeDef` XML is loaded at startup like
any Def, so the "type" exists regardless of the save.

**Editing and API.** Per-pawn, the psychotype is edited from the **same** editor as the writing style
(`Dialog_PawnWritingStyle`, opened by the Diary tab header icon), as a second section with a
stage-filtered picker, a re-roll button, a custom-rule area, a pin/unpin control, and an
override-explanation panel; the header-icon tooltip shows both layers. The **catalog** itself is edited
from the settings **Styles** tab — a *Psychotypes* studio under *Writing styles*
(`PawnDiaryMod.PsychotypeStudio`, backed by `PsychotypePresetStore`, Scribe key `psychotypePresets`) that
mirrors the writing-style Persona Studio: the player retunes a built-in's label/rule/family or adds a
**custom** psychotype (label + rule + family). `DiaryPsychotypes.All` merges those edits over the XML
defs and caches the result (mirroring `DiaryPersonas.All`), so overrides flow to the roll, the per-pawn
picker, and generation in one place. Custom rows are **manual-only**: the merge flags them `custom` so
`RollCandidates` skips them (never auto-assigned) while `PickerDefsFor` keeps them for hand assignment;
built-in *overrides* keep the built-in defName, so they still roll with the edited family/rule. The
public integration API adds `GetPsychotype` (read the effective outlook), the source-owned
`SetPsychotypeOverride` / `ResetPsychotypeOverride` pair (mirroring the writing-style override), and — in
v4 — the **editable-layer** setters `SetPsychotype` (base type, pinned) and `SetPsychotypeCustomRule`
(the player's custom rule), which let an adapter *seed* an outlook the player can then edit rather than
locking one. The RimTalk bridge's Tier B uses the source-owned override so RimTalk supplies *who* the pawn
is while Pawn Diary keeps *how* they write; the 1-2-3 Personalities bridge instead seeds the editable
layers. When two adapters contend for the same pawn's **override** slot, the LATER-loading mod wins and the earlier mod's write
returns `false` — the RimWorld "lower in the list overrides" convention, decided by the pure
`ExternalOverrideArbitration.MayDisplace` over load-order indexes resolved (and process-cached) by
`ExternalSourceLoadOrder`. SourceIds that are not active-mod packageIds keep the old last-writer-wins,
and a stale owner whose mod was removed is always displaceable. `Reset*` stays owner-guarded. The
v4 editable-layer setters are plain last-writer-wins — no arbitration — since they write the player's own slot.

Prompt enchantments add one weighted live-context cue to eligible first-person prompts. Event windows
and observed conditions feed the same planner, so active threats can bias otherwise unrelated diary
pages until they close. `normalPromptWeightMultiplier` can dampen ordinary health/mood context.
Eligible first-person prompts can also receive one hidden humor cue; `DiaryTuningDef.humorChance`
defaults to `0.20` (roughly one in five eligible entries), while cue flavor is selected by event
stakes. The writer's temperament then applies one flat, non-cumulative multiplier on top of the base
rate: an upbeat temperament (the Optimist/Sanguine degrees of NaturalMood, or Anomaly's Joyous trait)
or a Social skill passion (minor or burning) applies `humorElevatedChanceMultiplier` (default `2`),
while a dour/anxious/unfeeling temperament (Pessimist, Depressive, Nervous, Neurotic, Very neurotic,
Psychopath, or Anomaly's Disturbing trait) applies `humorReducedChanceMultiplier` (default `0.5`).
The two directions are mutually exclusive rather than stacked: within a direction several qualifiers
still count once, and a writer who qualifies for both (say a Sanguine psychopath) offsets back to the
base rate. Both trait-key lists and all three chance values are XML-authored and exposed through
Advanced tuning. Each event+POV roll uses a stable private seed inside `Rand.PushState`/`PopState`, so
humor never advances RimWorld's shared RNG and the pure multiplier/seed policy is regression-tested.
When an active hediff forces a temporary writing-style override, all hediffs matched by any active
persona-override rule are suppressed from the prompt-enchantment pool so the same condition does not
arrive once as style and again as "important context." When an external writing-style override is
active, it sits above the hediff style layer, so those hediff prompt-enchantment suppressions are not
applied.

First-person prompts also receive two compact continuity hints from the pawn's previous page when one
exists. `LastOpener` is the first sentence and is labeled as an opening to avoid repeating;
`PreviousEntryEnding` is the XML-tuned final sentence excerpt (`previousEntryEndingSentenceCount`,
`previousEntryEndingMaxChars`) and is labeled for continuation. Both are captured as plain strings on
the new `DiaryEvent`; arrival/death boundary pages use their neutral display role when they are the
previous page.

Imported game/mod text is flattened and capped before it reaches the model. This applies to live
hediff descriptions, labels, titles/roles, scenario text, and quest descriptions. Pawn Diary's own
XML/Keyed prompt text, field labels, writing styles, and humor cues are not sentence-capped by that
guard.

Starting-colonist arrival prompts are the exception for pawn backstories: the founding arrival
context includes each pawn's childhood and adulthood title, full in-game backstory description, and
compact mechanical effects (skill bonuses, disabled work/tasks/tags, required tags, and
forced/disallowed traits). These backstory descriptions are flattened to one prompt-safe line and
semicolon-stripped so they stay inside the saved arrival field, but they are not sentence-capped; the
arrival instruction asks the model to connect those facts with the starting scenario to explain how
the pawn plausibly reached that beginning. The description read does not trust vanilla's
`BackstoryDef.FullDescriptionFor` (other mods transpile it, and a bad interaction can throw for
specific modded backstories): on failure it warns once per backstory def and falls back to the raw
description template, so the arrival flush never aborts on a broken description.

Direct speech is allowed only in selected first-person interaction prompts, and only inside a closed
`[[speech]]...[[/speech]]` block. The speech instruction is phrased default-off: the model is told
that the notes almost never contain the POV pawn's actual spoken words and to write a private
reaction with no speech markup unless the notes really quote or report words the pawn said (small
and large models alike were inventing quotes when the old wording led with the positive case and a
worked speech example). Generated Social-log speech injection remains disabled/hidden; the
saved setting exists only for compatibility.

Title generation is enabled by default. Main entries queue their own title request after successful
generation. The broad missing-title sweep runs after load or settings save, not every generation
scan. Bad title responses are rejected and fall back to the opening words of the finished entry:
titles must stay within the short one-line title contract, and answer labels, instruction echoes,
reasoning-style lines, or terminal periods are treated as unusable model output.

## 7. Settings And UI

The settings window is split into **Main**, **Prompts**, **Styles**, **Events**, and **Advanced** tabs. Main
covers API lanes, routing mode, request tuning, a dedicated prompt context detail section, title generation,
atmospheric formatting, prompt enchantments, the "Show experimental XML override pages" switch, one
shared random-generation weight for optional chance-gated pages, and diary-event retention caps. Dev mode
exposes prompt-test mode and extra diagnostics in settings; bulk export
lives in RimWorld's Debug Actions menu. The export writes every saved hot page, compact archived
page, archive-only orphan row, and backing event record to `PawnDiaryExports/` under RimWorld's
save-data folder, and copies the generated file path to the clipboard.
Connection rows use a fixed label column and shared right-side action-button columns so endpoint,
model, API-key, auth, reasoning-effort, and reasoning-tag controls stay aligned across localized UI
text.
The global prompt context detail setting defaults to `Full` and is shown in its own section at the
bottom of the Main tab. The `Full`, `Balanced`, and `Compact` rows are the selector: clicking a row
changes the shared setting. The section shows an illustrative "sent vs cut first" display for the
presets. The display is not a live prompt preview; it explains the selector's shape so players can
see which kinds of facts lower presets keep and trim. Each API lane can inherit the global setting
or force its own `Full`, `Balanced`, or
`Compact` level, so a small fallback/local model can receive a shorter prompt without changing
richer primary lanes. Live generation first builds the full plan only to resolve prompt routing and
forced-model hints, then pre-renders prompt variants for the selected primary lane and its failover
lanes at each lane's effective detail level. Retry within one lane reuses that lane's variant;
failover switches to the next lane's pre-rendered variant.

```mermaid
flowchart LR
    A["Global context detail"] --> C["Effective level"]
    B["API lane override"] --> C
    C --> D["Render prompt variant for lane"]
    D --> E["LlmGenerationRequest.promptVariants"]
    E --> F["Try primary lane"]
    F -- "retry same lane" --> F
    F -- "failover" --> G["Apply next lane's pre-rendered variant"]
    G --> H["Send request"]
    H --> I["Successful result stores prompt actually sent"]
```

Prompts is the home for normal prompt text editing: the four shared system prompts plus per-event
prompt/enhancement/forced-model overrides. Its prompt-type picker uses compact labels and keeps
internal event keys out of the visible menu. It no longer exposes the experimental raw
prompt-policy drawer; template fields, prompt weights, hidden prompt policy, and other prompt Def
schema remain XML-owned instead of being edited through the normal settings UI.
Styles is the writing-style editor for `DiaryPersonaDef` labels, rules, and theme tags.

Events is the home for automatic event filters. Each visible `DiaryInteractionGroupDef` can be
disabled per player to stop Pawn Diary's own game listeners and scanners from auto-recording that
event group. The list shows every non-External, non-package-gated group, including
`defaultEnabled=false` rows (such as `questAccepted`) so the player can opt INTO a group the XML
ships disabled; a group with no player override still inherits its XML default. The single
`reflection` row governs all three reflection signals — day, quadrum, and life-arc pages — because
that group matches `DayReflection`, `QuadrumReflection`, and `PawnArcReflection` via `matchDefNames`.
These filters intentionally do not block external mod API submissions, so adapter-owned triggers
remain callable through `PawnDiaryApi`. The raw XML override editor on Advanced is disabled until the experimental override switch is
enabled from Main or from the Advanced gate panel. Advanced uses a compact two-pane editor: a left rail of
groups and a right body that draws one widget per field type -- checkbox, slider, numeric text,
single-line text, or multi-line text/list/table area -- with per-field and per-group reset, accent
coloring for customized values, a name filter that
flattens the rail into a search view, All/Changed/Raw text filters, "Reset changed in this tab",
and "Copy changed summary". The copy action is intentionally only a plain text review summary
(key, label, current snippet, XML/default snippet), not a stable import/export format. Rich tooltips
combine authored help with the live value, XML default, range, and customized status. Tuning contains
XML-owned parameters (dedup windows, ability sampling,
surroundings, weather chances, ritual quality labels, mood-condition families, health/enchantment
thresholds, body-part event tier/attitude policy, mood/pain/opinion buckets, hediff/skill scanner
intervals, day/quadrum/arc reflection weights, signal policies, context reactions). The thought,
ambient-thought, thought-progression, pawn-progression, and work knobs are edited **only** in the
signal-policy groups (`DiarySignalPolicyDef`): `DiarySignalPolicies` reads the policy def before
falling back to `DiaryTuningDef`, so the duplicate rows that also existed in the tuning groups were
dead controls and were removed (the `DiaryTuningDef` fields remain solely as the getter fallback when
a signal def is absent; `TuningOverrideMigration` prunes any saved override for the removed rows).
Field labels span the full row width so long names never clip. The catalog (`AdvancedFieldCatalog`)
is declarative and drives both the UI and the runtime override seam. Static tuning fields build during
settings load; Def-backed prompt-policy groups are appended lazily after `DefDatabase` has loaded, so
dynamic groups such as humor cues cannot be cached empty by early settings deserialization.
Dynamic Def-backed group prefixes are Keyed UI strings, while each Def's label still comes from its
DefInjected label or defName fallback. The Advanced page labels these overrides experimental because
they write directly into live XML Def instances and are intended as a testing/escape-hatch surface.

Overrides persist per player in `TuningOverrideStore` (a typed twin of `PromptOverrideDictionary`) and
take effect immediately by writing straight into the live Def instance fields via cached reflection or
small custom accessors for nested policy objects. Every existing `DiaryTuning.Current.field` /
`def.field` reader picks the new value up with no call-site changes; pristine XML defaults are
snapshotted once before the first override so Reset can restore them (signal/context `-1` "inherit
tuning" sentinels and `<null>` list inheritance markers are preserved). Prompt text overrides use the
same `{0}`, `{1}` placeholder convention as Keyed strings; cue lists use one item per line and accept
`<null>` to suppress configured cue rows. Empty inherited multi-line text/list overrides collapse to a
single effective-value preview drawer row until the player explicitly opens them, so optional
prompt-text escape hatches do not dominate the Advanced UI. The preview names the source
(override/XML default/shared prompt) and shows a short snippet of the active value. Clearing a string
override back to blank restores the
snapshotted XML value for the live session because the persisted override store treats blank as "no
override". Prompt-enchantment appearance odds expose the canonical `chance` field only; the legacy
`<frequency>` XML alias is still accepted for compatibility but is not a separate settings override.
Compact tables use these line formats: `WeatherDef=chance`,
`maxExclusive=ritualQualityLabel`, `enabled|label|source|contextKey` for prompt fields,
`level|chance|frequency|weight|severity` for prompt-enchantment severity tiers, and
`categoryKey|thoughtDefName|stageIndex:severity,...` for thought progression rules, mirroring the
XML fields `categoryKey`, `thoughtDefName`, and `stages/li/stageIndex` + `severity`. These compact
tables, plus one-integer-per-line progression skill milestones, draw a small syntax strip under the
raw box: parsed columns are color-previewed, the first malformed line is shown immediately, and
invalid edits stay in the box without being applied to the live Def override until the same checked
parse succeeds.
Structural non-prompt UI and text-decoration XML remains XML-only.

The Diary UI is an inspect tab registered for humanlike pawns and their corpse defs. By default it
appears in the pawn inspect-tab row for eligible colonists and selected colonist corpses. A setting can
instead hide the tab and add a bottom command button that opens the same UI. Programmatic opens from
that command, Social-log links, and linked-entry cards temporarily expose the hidden tab long enough
for RimWorld's inspect-pane opener to resolve it, then clear that state when the tab closes. The
inspect-tab draw path and programmatic open helper also re-apply tab registration once after startup,
covering load orders where RimWorld finalizes resolved tab lists after static constructors. The
command helper is marked with RimWorld's `StaticConstructorOnStartup` because it owns the static Unity
texture cache for the button icon; the icon itself still loads lazily from the main-thread gizmo path
and falls back to the vanilla book icon if the mod texture is missing.
Inspect-tab mode intentionally does not draw unread-page badges on the tab button. When the tab is
hidden and the bottom command button is active instead, that command still shows its unread/writing
status overlay.

Production UI shows completed pages. Each expanded non-archived page has a muted rewrite icon beside
the model/provenance footer, so players can regenerate that page with the current model routing;
pairwise pages rewrite both POVs when both are still eligible. Dev mode also shows pending/failure
rows, raw prompt/status data, and copy buttons. Bulk dev actions live under RimWorld's Debug Actions
menu as `Pawn Diary > Event test panel...`, which opens a non-pausing sectioned dev panel for
selecting a test pawn and partner. The same debug category also exposes
`Pawn Diary > Export all diary pages...` for full hot/archive text export and
`Pawn Diary > Purge archived entries for pawn...` for direct per-pawn cleanup. The panel shows two
sections. Diary owns the former Diary tab action strip: a mock-page filler, per-pawn archive purge,
the per-pawn persona picker, and transient formatting preview buttons. Fixtures owns prompt-only
fixture batch/clear tools. The old real-event trigger buttons are intentionally not exposed; saved
`events` section selections fall back to Diary. Buttons that mutate or delete save data use the
XML-owned danger tint. The selected pawn, partner, active section, per-section scroll, and selected
fixture IDs are saved on `DiaryGameComponent`, so the panel state survives closing/reopening and
normal save/load.
Preview
buttons open the selected pawn's Diary tab only to display the transient card; they do not save diary
events. The prompt-only section uses the same synthetic fixture registry as the old Diary tab
prompt-suite controls, but can create a selected prompt-test batch at once and can clear all
prompt-suite entries afterward. The mock
filler seeds 6,000 saved pages over 3
in-game years (about 2,000 pages per year) without calling the LLM, and dev-mode retention skips
mock stress histories so autosaves do not immediately shrink the fixture. Histories page by in-game
year; newest cards start expanded. Long histories are kept cheap by the active-event cap,
visible-entry caching, sliced main-thread year indexing, cached virtual row offsets/heights, and
viewport drawing that only emits cards inside the scroll slice plus the XML-tuned overscan buffer
(`virtualizedEntryOverscanHeight`, default 800 pixels above and below the viewport). The sliced indexer
uses `uiHistoryScanMaxEventsPerFrame` and `uiHistoryScanFrameBudgetSeconds` for year indexing,
selected-year card materialization, and selected-year row layout, so opening a pawn with thousands of
pages shows a loading panel instead of freezing the game. The loading panel reports the active load
phase only when no usable cached list exists yet: first open, uncached pawn switch, or opening a year
with no cached cards. The tab keeps a small LRU of loaded pawn views so returning to a recent pawn
restores its visible list instead of rebuilding from zero. Same-pawn index refreshes and selected-year
card refreshes build quietly behind the currently visible list; the full-tab index loading gate is
based on the absence of a cached year index, not on the selected-year card-loading flag, so pagination
cannot make a quiet refresh flash the blocking loading panel. Once a year is visible, same-year data
and layout refreshes, including completed generation/title updates, scroll, highlight refreshes, and
collapse/expand, rebuild row offsets in place instead of returning to the loading panel; loaded large
years seed the clicked card's current blend so the clicked card can still animate open or closed.
Expanded-card height measurement is isolated in `DiaryEntryCardMeasurer`, which owns the wrapped-text
height cache and invalidates it on card width, debug display, render token, and pawn-name highlight
revision. The highlight revision itself only advances when the periodically rebuilt name/color set
actually differs from the cached one (compared order-insensitively by the pure
`DiaryNameHighlighter.SameHighlights`), so the routine refresh of an unchanged colony no longer
re-measures every expanded card. Expanded-card drawing is routed through a renderer request in
`ITab_Pawn_Diary.EntryCards.cs`, leaving selection, scroll, sliced layout, and expansion state in the
inspect tab while keeping the Verse/Unity IMGUI measurement and draw paths together.
Selected-year rebuilds invalidate row layout defensively so virtualized row offset
arrays cannot be reused against a changed list. An in-progress sliced build (year index, selected-year
cards, or row layout) is invalidated only by a STRUCTURAL change — a different pawn, a tab filter
toggle, or a different event count — never by a `DiaryStateVersion` tick. That counter is process-wide,
so it advances whenever any pawn's entry status/text/title changes anywhere in the colony; tying the
build identity to it made active generation reset an in-progress scan to event zero on every tick, so
a large history could never finish loading. Letting each scan complete once started keeps switching
responsive under generation; the completed index quietly refreshes behind the visible list to pick up
the new state within a few frames. Per-event work in those sliced scans is kept cheap by
`DiaryContextFields`: each indexed event and each materialized card calls it several times (arrival
and death bounds checks, status reads, and source-domain recovery, which probes up to ~13 markers).
It scans the context string in place and allocates only when a value is returned, so the common
"key absent" path is allocation-free — important because the per-frame time budget would otherwise
run out after only a couple of entries, making a long history take many seconds to load. The tab indexer does not perform the older
cross-colony arrival-page fallback scan while opening; it scans the selected pawn's saved diary
references once, resumes selected-year loading across frames, skips any bad/stale entry with a
one-time log, then slices the selected year's card and layout work. Inspect-tab and command badges
do not touch saved diary records during pawn
selection; they read a transient per-pawn status cache. The new-page badge is backed by a saved
per-pawn unread flag that is set when main LLM text finishes and cleared when that pawn's Diary tab
opens, while writing dots reuse cached pending counts after the Diary tab finishes its sliced load.
Archived pages use the same cards and dev copy controls as hot pages, but the normal rewrite icon is
hidden because compact archive rows intentionally discard prompt/raw-response/retry state.

`DiaryTextFormat` escapes raw model rich text before applying safe formatting. Display-only text
decorations and pawn-name highlights happen at render time; generated text is not mutated on save.

**Per-entry accent color (color cues).** Each saved `DiaryEvent` carries a stable `colorCue` string
chosen at record time from the matching interaction-group / event-window / observed-condition Def.
At render time `DiaryUiStyleDef.ColorForCue` maps that cue to the card's accent color, which drives
the left spine strip and the group-label chip (and, for the three distress cues `combat`,
`socialFight`, `mentalBreak`, also a matching page tint and header rule). The full palette lives in
`DiaryUiStyleDef.xml` (`<cueColors>`); the cue vocabulary is documented in the
`DiaryInteractionGroupDefs.xml` header comment. Current themed cues beyond the distress/generic
ones: `bodyPartAnomalous` (living/wrong body changes), `bodyPartArtificial` (prosthetic/bionic
body-part gains), `bodyPartLost` (natural part loss), `psychic` (bright violet — psylink gains,
psycast abilities), `royalty` (gold — royal-title gains, royal rituals), `strangeChat` (green),
`white` (warm white — heartfelt moments, birthdays, skill passions, day reflections), and
`quadrumReflection` (light blue). `extremeDark` (dark blood-red) is reserved for broader
Anomaly/dread content (void monolith, metalhorror, anomaly tales) and is deliberately not used for
psylink, which is bright-psychic rather than horror. `colorCue` is saved per-event, so historical
entries keep whatever cue they were recorded with.

**Render-time paragraph reflow (default atmosphere).** Because prompts only ever ask for sentence
counts (1-3, 2-5, 2-4, 4-7, 5-8) and never for explicit paragraph breaks, a multi-sentence entry arrives
as a single line that would otherwise wrap as one dense block. For the ordinary (non-Fractured /
-Unsettled /-Memorial) atmosphere, `NormalRoleplayBlocks` runs each prose line through the pure
`DiaryParagraphReflow.ReflowLine` helper and turns the returned chunks into separate paragraph
blocks separated by a blank-line gap. Breaks land, in priority order, at sentence ends (`.!?`),
RimWorld year mentions (`55xx` followed by a clause boundary), semicolons, and em-dashes, with a
hard length cap that falls back to a space boundary (words are never split mid-token). Saved
`GeneratedText` is never mutated — both the height-measure pass and the draw pass use the same
helper, so wrapped heights stay in sync. Tuning is in `DiaryUiStyleDef.xml`
(`paragraphReflowEnabled`, `paragraphReflowTargetChars`/`MaxChars`, the four `…SplitOn…` cue
toggles, and `paragraphReflowMinBreakSpacing`); set `paragraphReflowEnabled=false` to render the
model's own line breaks verbatim. The style accessor clamps target/max lengths to at least 1 and
`max >= target`; if invalid options still reach the pure helper, it returns the line whole instead of
attempting a split. `[[speech]]` blocks and the three special atmospheres are not reflowed; they
already own their line/sentence styling.

## 8. API And Reliability

Each enabled endpoint/model/mode/auth row is an API lane. Supported request modes are OpenAI-compatible
Chat Completions and OpenAI Responses. Auth can be bearer, no auth, custom API-key header, or `key=`
query parameter. Logs **and every player-visible status/error string** (settings-window row status,
model-fetch and connection-test failures, diary-tab errors) pass through the same redaction, so a
`key=` query parameter echoed back in an error body or a `Bearer` token is masked before it is shown or
logged; bearer tokens are masked in full regardless of which characters they contain. A non-finite
temperature is coerced to a valid value when the request body is built, so a corrupt setting can never
emit `NaN`/`Infinity` and invalidate the JSON.

Routing modes are Balanced, Prefer top rows, and Failover only. A `DiaryEventPromptDef.forcedModel`
can try a matching active model first; blank, unknown, disabled, or failed forced lanes fall back to
normal routing. Recipient follow-ups and title requests try to pin to the previous successful lane.
The shipped `QuadrumReflection` and `ArcReflection` prompt rows can use the same forced-model field
for rare long reflections.

`LlmClient` handles concurrency, per-lane cooldowns, transient retries, timeout/permanent failures,
session cancellation on new game/load, and result handoff to the main thread. On a 429/503 that
carries a `Retry-After` header it skips the fast local retries and cools the lane for the longer of
the server's requested wait (capped at one hour) and the local exponential backoff, so a rate-limited
endpoint is not re-hit before it allows. `LlmResponseParser` supports Chat and Responses output
shapes, strips reasoning/transcript leaks, normalizes or removes malformed speech markers (including
common `speach` typos and incomplete bracket tags), removes model-leaked Unity rich-text angle tags
and stray numbered format placeholders (`{0}`/`{1}`/`{0:D2}`/`{}`) echoed from an unfilled prompt
template, unwraps whole-response Markdown fences from compatible models, strips leading final-answer labels
after reasoning cleanup, and trims saved text locally. The reasoning-scrub stages are guarded so a
malformed or mismatched reasoning tag, or an ordinary diary line that merely resembles a label or
self-audit, can never empty or truncate an otherwise valid entry (which would surface as a spurious
"no content" failure): mismatched close tags recover the answer, a trailing stray closer drops only
itself, and only unambiguous `final:`/`final answer:` prefixes are stripped from the very start.

In settings, each row's reasoning-capability probe (`/models`) is single-flight per row but re-runs
once if the row's endpoint/key changed while a probe was in flight, so the cache reflects the final
edit rather than a stale intermediate value.

Reasoning-effort serialization differs by mode. In OpenAI Responses, every explicit effort
(`none`/`minimal`/`low`/`medium`/`high`/`xhigh`) is sent as `reasoning.effort`, since the server
honors `none`. In Chat Completions there is no "off" wire value, so a `none` effort is expressed by
**omitting** `reasoning_effort` entirely — sending `reasoning_effort:"none"` makes
OpenAI-compatible gateways try to apply a thinking budget, which non-reasoning models reject (e.g.
Google's endpoint returns HTTP 400 "Thinking budget is not supported for this model." for Gemma).
`default` omits the field in both modes.

**Capability-aware effort (clamping).** Some `/models` responses (OpenRouter, some gateways) attach
a per-model `reasoning` object (`default_enabled`, `supported_efforts[]`, `default_effort`,
`mandatory`, `supports_max_tokens`). When present, `ModelReasoningCapability` parses it and
`ModelCapabilityCache` stores it (keyed by endpoint+model, deliberately excluding the API key).
`LlmClient.BuildRequestJson` then runs the chosen effort through
`ModelReasoningCapability.EffectiveReasoningEffort`: a non-reasoning model forces `none` (the direct
fix for the Gemma 400 above); an explicit `none` stays `none` even when the provider does not list it
as a supported effort; an unsupported enabled effort clamps to the provider default or the highest
supported level; an unknown capability (OpenAI-direct, local GGUF — no `reasoning` object) passes the
effort through unchanged, preserving today's unconditional behavior. The settings effort
dropdown also consults the cache: when capability is known it only offers supported levels and shows
a tooltip; when unknown the full ladder is offered as before. **Capability auto-refreshes** on four
triggers so a player rarely clicks Fetch manually: when the settings window opens (one-shot, for any
row whose capability is not yet cached), when a row's URL/key/auth changes (background refresh, once
per change), when Test connection runs (in parallel), and on the manual Fetch click. A lightweight
capability-only refresh path (`ApiConnectionController.RefreshCapability`) updates just the
thread-safe cache without disturbing the single-flight picker, so several rows can refresh at once;
its per-row in-flight guard is lock-protected because cancellation happens on the UI thread while
async continuations may finish on a worker thread.
Providers that return no `reasoning` object cache nothing and degrade gracefully.

**Per-lane reasoning tag.** A **Reasoning tag** dropdown (default *Auto*) controls how
`LlmResponseParser.StripReasoningTextBlocks` removes private thinking leaked into message content.
*Auto* uses the built-in broad tag/fence/heading list (`think`/`thinking`/`reasoning`/`analysis`/
`thought`/`reflection`/`scratchpad`), which is wide enough that most players never need to pick a tag
manually — the dropdown is an escape hatch, not a required step. Picking a specific tag **prepends**
it so it is tried first, useful only for an exotic wrapper not yet in Auto's list; common tags keep
working regardless. False-positive risk is negligible because the strippers only act on wrapper form
(`<tag>…</tag>`), fenced ```` ```tag ```` blocks, and `Tag:` headings — never the bare word in prose,
so a pawn writing "my reflections on the raid" is safe. The tag is normalized by
`ApiEndpointPolicy.NormalizeReasoningTag` and threaded through `LlmGenerationRequest.reasoningTag`.

The settings-window **Test connection** button runs independently per row: starting a test on one
lane does not block or cancel a test on another, and each row shows its own "Testing…"/success/
failure status. `ApiConnectionController` keeps per-row state (a generation counter for stale-result
rejection plus a busy flag and status string) and a thread-safe `ConcurrentQueue` for the
background-to-main-thread result handoff, drained each UI frame. The test prompt is translated on the
main thread before calling `LlmClient`, which does not own prompt-prose fallbacks because its transport
code can run off-thread. (The **Fetch models** button on the same screen is still single-flight global
— only one fetch at a time across all rows.)

### 8.1 Error reporting (opt-out)

Optional, **on by default** crash telemetry that reports **only the errors the Pawn Diary mod family
raises** (the main mod and its first-party integration submods) so bugs can be found and fixed. The player turns it off with the **Send anonymous error reports** checkbox on
the main settings tab (`enableErrorReporting`, default `true`). A one-time informational notice
(`DiaryGameComponent.MaybeShowErrorReportingNotice`, shown from the first `LoadedGame`/`StartedNewGame`
and gated by the persisted `errorReportingNoticeShown` flag) tells the player it is on and offers a
**Turn it off** button, so "on by default" is disclosed rather than silent. Lives under
`Source/Diagnostics/`.

- **Capture.** `DiaryLogReportPatch` is a Harmony postfix on `Verse.Log.Error` / `Log.ErrorOnce`
  (registered via `DiaryPatchRegistrar`). It forwards a message only when it starts with a **Pawn Diary
  family** log prefix — the main mod (`[Pawn Diary]`) **or** a first-party integration submod
  (`[Pawn Diary: 1-2-3 Personalities]`, `[Pawn Diary: VSIE]`, `[PawnDiary: RimTalk bridge]`). The
  bridges log through the same `Verse.Log`, so this one global postfix captures the whole family; other
  mods, base-game lines, and the copy-me `PawnDiary.ExampleAdapter` template (a third-party starting
  point) are ignored. Which prefixes count as "ours" is the pure, unit-tested `ModErrorPrefixPolicy`,
  which matches by the two family name **roots** (`[Pawn Diary:` and `[PawnDiary`) so a new
  `[Pawn Diary: X]` bridge is covered without a code change. A `[ThreadStatic]` re-entrancy flag plus a total `try/catch` mean a fault in
  reporting can never crash the caller or loop. (Known limits: an error site without a family prefix is
  not captured until its prefix is added; and the report's `modVersion` is always the *main* mod's
  assembly version, so a submod error is identified by its message prefix rather than a separate field.)
- **Transport.** `DiaryErrorReporter` mirrors `LlmClient`'s shape — a shared static `HttpClient` and
  fire-and-forget `Task.Run` sends, so the game thread never blocks. It dedupes by fingerprint (each
  distinct error sent once per session), caps distinct reports per session and concurrent sends, and
  never re-logs its own failures. The `ErrorReportEndpoint` constant points at the deployed Cloudflare
  Worker (`services/error-endpoint/`); set it to `""` to make the reporter inert again. Per-session
  dedupe resets in the `DiaryGameComponent` ctor alongside `LlmClient.BeginSession()`.
- **Privacy (pure, tested).** `ErrorScrub` masks the username segment of any file path, redacts the
  configured API keys/endpoint URLs (passed in) plus `Bearer`/`key=`/`sk-` shapes (reusing
  `ApiLaneLabels.RedactSecrets`), and caps length. As defense in depth against a future error site that
  interpolates a name, `DiaryGameComponent.MaybeRefreshErrorRedactionNames` publishes the live colony
  and colonist names to the reporter on a coarse tick cadence (main-thread read, `volatile` array); the
  reporter feeds them through the same exact-substring redaction as the configured secrets. Today's
  Pawn Diary error sites (main mod and submods) embed exceptions/defNames, not names, so this is
  belt-and-suspenders.
  `ErrorFingerprint` is a deterministic FNV-1a over the normalized stack (line numbers/addresses
  blanked) so the same crash groups across machines. `ErrorReportPayload` builds the wire JSON by hand
  (no serializer in Mono). The payload carries only: schema version, mod version (stamped into the
  assembly from `About.xml` at build time by the `StampVersionFromAbout` target in `PawnDiary.csproj`,
  so `About.xml` is the single source of truth), RimWorld version, active DLC, coarse OS string, install
  source (`workshop`/`local`, classified once on the main thread in the mod ctor via
  `DiaryErrorReporter.CacheInstallSource` so the send thread never reads `ModContentPack`), an
  **anonymous random install GUID** (`errorReportInstallId`, generated **and persisted** once on the
  main thread by `EnsureErrorReportInstallIdPersisted` in the mod ctor, so it is stable across sessions
  and not a machine/hardware id), the fingerprint, a UTC timestamp, and the scrubbed message. Because
  the message comes from exception text, it is scrubbed best-effort — an unusual message could still
  carry an unanticipated in-game value. The pure helpers (scrub, fingerprint, payload, install-source)
  are covered by `tests/DiaryPipelineTests`.
- **Endpoint (deployed).** `POST {ErrorReportEndpoint}`, `Content-Type: application/json`, body =
  `ErrorReportPayload.ToJson`. The receiver is a Cloudflare Worker + D1 in `services/error-endpoint/`
  that validates/size-clamps the body and folds it into per-fingerprint and distinct-install
  aggregates (including `install_source`), with a daily cron retention prune; `schemaVersion` lets the
  shape evolve. The endpoint is public and unauthenticated, so a per-IP rate limiter binding
  (`INGEST_RATE_LIMITER`, 60/min) rejects floods with `429` before any DB write. The D1 schema is
  applied via **D1 migrations** (`migrations/`, `npm run db:migrate`) rather than a one-shot
  `schema.sql`, so re-running is idempotent and a database created before a column existed is upgraded
  in place (migration `0002` backfills `install_source`) — a fresh clone can no longer silently drop
  reports against an out-of-date DB.

## 9. Save Data And Compatibility

`DiaryGameComponent.ExposeData` owns the top-level save shape.

| Scribe key | Contents |
|---|---|
| `diaries` | per-pawn `PawnDiaryRecord` rows |
| `diaryEvents` | hot full `DiaryEvent` rows |
| `diaryArchiveEntries` | compact display-only `ArchivedDiaryEntry` rows |
| `activeEventWindows` | currently active XML event windows |
| `activeObservedConditions` | currently active live-state observed conditions |
| `observedConditionCooldownUntilTick` | saved restart cooldowns for ended observed-condition identities |
| `pendingBiotechGrowthMoments` | detached pawn/age/tick/before-snapshot ownership for postponed Biotech growth letters; no live Pawn or Letter reference; new owners use an XML admission limit of 256 by default, established owners survive config reductions/old-save overflow, and only the hard corruption ceiling of 2048 may truncate |
| `biotechFamilyArcs` | detached stable-ID family continuity, exact pregnancy/labor/birth facts, bounded supporter observations, and summarized growth ages |
| `pendingBiotechBirths` | detached exact birth snapshot, frozen adult writer order, and per-writer event-time prompt/display context while newborn naming is unresolved; no live Pawn, Thing, Corpse, or ritual reference; new owners use an XML admission limit of 256 by default, with established ownership preserved up to the hard 2048-row corruption ceiling. Pending rows, arcs, and growth rows are maintained (flush/fallback/prune) even when Biotech is later disabled, so a DLC-removed save shrinks instead of freezing |
| `familyObservationVersion` | additive family old-save baseline version; prevents invented historical pregnancy/birth/activity catch-up |
| `odysseyActiveJourney` | nullable detached active gravship journey (stable IDs, event-time labels, locations, qualitative launch state, and bounded writer facts); O1.3 commits it only after vanilla `TravelTo`, while an old save already in flight receives an explicitly incomplete baseline row |
| `odysseyTravelHistory` | versioned, bounded Odyssey novelty/home/cooldown history; schema 2 adds the last emitted launch-page tick plus the exact current-home key/tenure start used by launch policy. O1.3 records committed departure origins and successful landing observations, while missing old-save keys baseline silently with first/new and synthetic-duration claims disabled and never create a page |

Hot events and archive rows are separate on purpose. Hot `DiaryEvent` rows keep prompts, retry state,
raw/generated text, status, LLM metadata, titles, context, source ids, and per-role state. Compact
archive rows keep only what the Diary UI needs to render an old page.

History retention is per pawn:

- `maxActiveDiaryEvents` keeps each pawn's newest hot refs. The key name is historical and must not be
  renamed.
- `maxArchivedDiaryEvents` keeps each pawn's newest compact archive rows. A value of `0` purges old
  compact pages once they fall out of the hot set.
- Shared pair events remain hot until every linked pawn has either kept or archived its POV.
- Pending/not-generated hot refs are not destroyed just because the pawn is over the active cap.
- A death boundary is terminal for retention only while the same pawn load ID is not alive. After
  resurrection, post-death refs are retained/archived like ordinary in-bounds pages.

`DiaryTuningDef.activeScanEventWindow` is a separate XML-only global hot-event window. It controls
retry, title catch-up, orphan recovery, work cooldowns, prompt continuity, opener history, and
previous-ending history, and day/quadrum evidence scans. Compact archive rows never enter those
scans.

`PawnDiaryRecord` also owns nullable per-pawn progression state and arc schedule state. Old saves load
with those fields absent, then normalize to empty baseline-pending state. The progression state stores
only highest passion-skill milestones, last observed psylink/xenotype/royal-title values, the set
of known trait keys (`<defName>|<degree>`) used to detect newly gained traits, and an additive nested
`biotechProgressionState` whose Phase-1 `consumedGrowthAges` list is limited to 7/10/13 and whose
Phase-5 nested `geneIdentityObservationState` stores an explicit baseline version, bounded sorted
installed Def names, and current xenotype identity. The retained scalar xenotype fields are migration/
downgrade compatibility only; the nested row is authoritative for fallback diffing. The progression
row also owns additive `royaltyObservationState`: an explicit version,
`royaltyObservationAvailable` flag, and a bounded, sorted list of
plain highest-title observations keyed by faction ID. The older scalar Royalty title fields remain for
migration/downgrade compatibility and are not converted into an invented faction. Persona bonds live
once at component scope because weapon Thing ID plus bond epoch—not a pawn row—is their identity.
Phase 4 advances this existing row immediately for exact title hooks and scanner observations; schema
version 2 distinguishes a readable empty title set from temporarily unavailable Royalty data. A
Royalty-off `LoadedGame` invalidates availability immediately while retaining the saved rows and
psylink scalar, including when the player resaves before the first tick. The existing
`highestPsylinkLevelRecorded` scalar remains the psylink baseline. Cause scopes, pending ritual
mutations, and title-memory ownership are intentionally not Scribed. The pre-save pass consumes live
pending mutations into their selected fallback, reconciles live pending-memory pawns through the same
versioned observer, then flushes only unmatched title memories through ordinary Thought capture before
`events.ExposeEvents`. Remaining owner caches clear on `FinalizeInit`, so save/load cannot resurrect an
expired owner, lose a transient psylink-only batch, or serialize both Thought and rich title pages for
one scanner edge.
Phase 2 uses that same normalized deep-scribed ledger transactionally: lifecycle truth is committed
before optional page dispatch, and saved pending/separated state resumes through the independent
reconciliation deadline after load without inventing an old bond or a catch-up page. Bonds missed by
the first loaded-map baseline are silently adopted when they later become visible, and provider output
requires matching current coded-weapon truth even when an ambiguous cleanup intentionally preserves a
saved row. A late-visible bond's first scan only adopts its historical baseline; subsequent scans own
primary/not-primary inference. The arc
schedule stores only cadence bookkeeping (`lastArcEntryTick`, `lastArcEntryYear`,
`arcEntriesThisYear`, `forcedArcYear`, recently used memory ids, and the last retryable
memory-shortfall tick/year). Neither field is a history database; existing diary pages remain the
source of truth for reflections.

Failed/stale pages can be archived as displayable fallbacks. The fallback body/title is resolved
before compaction because the archive drops raw prompt data. Prompt-only dev capture rows stay hot for
the same reason.

Adding Pawn Diary to an existing save is safe; it records future events only. Removing the mod is
gameplay-safe because it does not attach custom components or gameplay defs to vanilla pawns/maps. The
diary UI/history disappears without the mod.

### 9a. Scribe-key stability contract

Every string passed to `Scribe_Values.Look` or `Scribe_Collections.Look` is stable save-format API.
Renaming a key silently makes old saves load defaults instead of player data.

Before touching save keys, read:

- `DiaryEvent.ExposeData`, `ScribePawnSlot`, and `ScribeNeutralSlot`.
- `ArchivedDiaryEntry.ExposeData`.
- `DiaryGameComponent.ExposeData`.
- `PawnDiarySettings.ExposeData`, plus `PersonaPresetStore` and `PromptOverrideDictionary`.

Do not rename historical flat POV keys such as `initiator*`, `recipient*`, or `neutral*`. Do not
rename `maxActiveDiaryEvents`; the meaning changed to per-pawn hot refs, but the saved key remains
the compatibility bridge.

Royalty Phase-3 `gameContext` markers are also a frozen soft contract for prompt selection and diary
decorations, including events already saved before an update. Keep the marker names and separators
stable: `persona_milestone`, `tale_source_def`, `tale_source_label`, `tale_killer_role`,
`tale_victim_role`, `persona_weapon_id`, `persona_weapon_def`, `persona_weapon_name`,
`bond_epoch`, `bond_previous_state`, `bond_new_state`, `bond_end_cause`, and the indexed
`persona_trait_<n>` / `persona_trait_description_<n>` keys. The separate Phase-2 standalone pages
may emit `persona_weapon=`; Phase-3 Tale/death pages intentionally do not. Adding a marker is
additive, but renaming/removing one requires a migration or compatibility handling in every parser
and template that consumes it.

### 9b. Post-load repair, and where it is tested

Loaded data is normalized in `LoadSaveMode.PostLoadInit`; code should not assume loaded strings,
lists, statuses, indexes, or refs are valid.

Pure repair lives in:

- `Source/Pipeline/DiarySaveNormalization.cs`
- `Source/Pipeline/DiaryGenerationStatus.cs`
- `tests/DiarySaveNormalizationTests/`
- related status fixtures in `tests/DiaryPipelineTests/`

Impure repair stays on save models: GUID minting, `DefDatabase`-based color-cue recovery, index
rebuilds, and the Scribe round trip. Run `tests/SAVE_COMPATIBILITY_SMOKETEST.md` when changing
`ExposeData`, saved settings, or Scribe keys.

### 9c. Allowed migration pattern

Only rename a Scribe key for an intentional format migration:

1. Keep reading the **old** key during a transition window so existing saves load.
2. On load, if the old key is present and the new key is absent, copy the value across.
3. Write only the new key on save.
4. Document the rename, the window, and the removal date in `CHANGELOG.md` and this section.

Never rename a key "for cleanliness" alone.

## 10. Runtime And DLC Constraints

- Runtime is RimWorld's Unity Mono. Use only assemblies available in `RimWorldWin64_Data/Managed`
  plus the declared Harmony dependency.
- JSON uses `Source/Util/MiniJson.cs`. Do not add `System.Web.Extensions` or external JSON libraries.
- Harmony is declared as a dependency on `brrainz.harmony` in `About/About.xml`; the Harmony runtime
  comes from that active mod at game-time. Pawn Diary compiles against `Source/Libs/0Harmony.dll`
  (`Private=False`, so it is never copied to the output), which must match the active brrainz Harmony
  DLL's assembly version (currently `2.4.1.0` in this checkout). It ships **only**
  `PawnDiary.dll` — it must never bundle `0Harmony.dll` in `1.6/Assemblies/`.
- No paid DLC is required. Optional DLC data must no-op cleanly when absent.
- Odyssey O1.0-O1.5 now provides the frozen contracts, XML policy, guarded live location/mobile-home
  projection, two additive detached save models, state-only takeoff/travel boundaries, and one
  canonical successful-landing event. Every
  live read returns before touching Odyssey
  state unless `ModsConfig.OdysseyActive`; the policy XML contains only primitive values and plain
  Def-name strings. Missing old-save keys baseline silently and distrust first/new claims. O1.2 adds
  localized mobile-home surroundings only for a pawn vanilla confirms is inside the exact gravship
  field. O1.3 captures `InitiateTakeoff` intent, commits an idempotent journey only from vanilla
  `GravshipUtility.TravelTo`; its prefix preserves the true origin tile before vanilla rewrites the
  by-value `oldTile` argument onto the destination layer. It snapshots `InitiateLanding` and defensively patches private
  `LandingEnded` to act only after successful completion. O1.4 adds `GravshipJourney`: meaningful
  landings create one solo or pair-shaped event with at most two deterministic pilot/copilot/crew
  POVs, while routine/disabled/duplicate/writerless landings update history only. Page tick and emitted
  journey ID are committed only after the event exists. Pre-feature mid-flight baselines may still
  write exact event-local major/rough facts, but never infer elapsed duration or `long_journey` from
  their synthetic load tick. The existing launch Ritual owner is departure-only,
  Odyssey-package-gated, capped at two XML-configured writers, and cooled against the prior emitted
  launch page. The cooldown marker is committed only after at least one ritual `DiaryEvent` exists;
  cancelling destination selection cannot create repeat pages. A verified current-home tenure may
  bypass cooldown once after `longHeldHomeMinimumTicks`, and travel commit clears that tenure until a
  later successful home landing establishes another. No takeoff or `TravelTo` page was added. O1.5 appends the landing schema
  to both important prompt templates and marks phase, primary/secondary reason, duration, per-POV
  journey role, ship, origin, and destination as required context, so Full/Balanced/Compact cannot erase the
  facts the landing instruction asks the model to use. Supporting biome/site/crew/quality/roughness
  fields remain available under budget. Pair events save both initiator/recipient journey-role
  mappings internally, then the pure prompt planner projects exactly one `pov_journey_role` for each
  prompt; the internal mapping keys never render. A localized Odyssey pair fixture exposes the same
  shape in the dev prompt suite. Stable journey/ship/location IDs and ticks have no template field and
  remain event-internal. Mobile-home surroundings also suppress a second biome fragment when the
  visible gravship location already uses that same biome label.
- Odyssey O2's XML-first environmental slice corrects seasonal flooding to observe the live
  `SeasonalFlood` ThingDef instead of the nonexistent `Flooding` mood identity. The package-gated,
  map-scoped condition is prompt-only with bounded hysteresis/cooldown. `GravNausea` is an exact
  string-matched prompt enchantment with XML-owned chance/weight/severity and cannot authorize a page.
  Generic visible `GameCondition` projection remains the owner for conditions it already covers.
  O2 also defensively discovers and postfix-patches every concrete `LandingOutcomeWorker.ApplyOutcome`
  override. A postfix runs only after the concrete worker succeeds, reads the exact applied
  `LandingOutcomeDef`, correlates it to the same transient pending ship, and adds its localized visible
  label as required `landing_outcome` context on the one canonical landing page. It adds no page and
  no save field; missing hooks merely omit the detail. Life support remains behind its feasibility gate.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks. This
  includes Biotech growth trait/skill/work-disabled snapshots and Ideology precept/role reads used by
  body-mod stance policy; other code consumes plain detached rows, labels, defNames, or booleans.
- `PawnDiaryDlcSafetyFixtureTests` exercises this boundary in both directions: absent/null state must
  disappear from the final prompt/public summary, while installed DLC uses disposable real xenotype,
  title, ideoligion/precept/eligible-role, and creepjoiner pawn state. The CreepJoiner positive path
  temporarily gives its isolated fixture pawn a vanilla `Pawn_CreepJoinerTracker` backed by a real
  loaded Anomaly form because `Pawn.IsCreepJoiner` tests tracker presence, not race or pawn kind. The
  original tracker is restored in a `finally` block. The same fixture freezes every
  official package-gated interaction group/event window, settings visibility, fragile DLC hook
  signature, and optional-adapter fail-open readiness contract. Specialized Anomaly ritual rows must
  remain exact-keyed; the later `PsychicRitual` token row is deliberately the future/modded fallback.
  Production code still never resolves a DLC Def by name.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use string matching or
  `GetNamedSilentFail`.

## 11. Localization

Player-facing UI strings and natural-language prompt text must be localizable.

Use:

- Keyed strings in `Languages/English/Keyed/PawnDiary.xml` for code-owned UI/prompt text.
- `.Translate()` only on the main thread.
- DefInjected text for XML Def labels, instructions, tones, prompts, personas, templates, and cues.

Do not translate or localize:

- internal prompt/context schema keys such as `thought=`;
- role/status/sentinel tokens such as `initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`;
- defNames, API model ids, and saved context keys;
- background-thread `LlmClient` strings, because `.Translate()` is not thread-safe there.

When editing XML Def text:

- Keep English DefInjected stubs in sync. This is load-bearing, not cosmetic: RimWorld applies the
  active language's DefInjected **over** the Def XML, so a stale English stub silently reverts a Def
  edit in English games (this once shifted every prompt-template field label by two after new fields
  were inserted mid-list, and dropped the first-person/anti-slop system-prompt lines).
- Use fully qualified custom-Def folders, such as
  `Languages/English/DefInjected/PawnDiary.DiaryInteractionGroupDef/`.
- Treat indexed DefInjected entries as positional schema. Inserting, removing, or reordering an XML
  `<li>` requires updating every affected language index, including keys such as
  `<Template.fields.N.label>`, `<group.instructions.N>`, and `<group.tones.N>`. Verify every source
  list length and index after the edit; appending a translation under the old numbering does not
  realign it.
- Avoid blank list entries that silently shift indexed translation keys.
- Add or update the matching Russian key/file at the same time.

The in-game Prompt policy editor keeps key-backed fields as blank literal override boxes until the
player types a replacement. Those per-player overrides live in `*Text`/cue fields only; XML/Keyed and
DefInjected entries remain the source that translators must keep aligned.

Russian lives in `Languages/Russian (Русский)/` and mirrors the English Keyed + DefInjected layout.
Use `Languages/Russian (Русский)/GLOSSARY.md` for game terms. Russian UI should stay compact for
RimWorld's narrow settings/tab surfaces, avoid unexplained English calques, and keep protocol/product
tokens such as `API`, `OpenAI`, `URL`, `Bearer`, `XML`, and `UTF-8` only where they name the actual
thing.

Russian prompt prose should be idiomatic, not literal English. For placeholders, avoid making a
dynamic pawn/target/work value the subject of gendered or numbered past-tense grammar unless the code
guarantees agreement. Writing styles and humor cues should be culturally rebuilt in Russian rather
than line-by-line translations.

Reflection/progression changes usually touch more than `Keyed/PawnDiary.xml`: keep the matching
`DiaryEventPromptDef`, `DiaryPromptTemplateDef`, and `DiaryInteractionGroupDef` DefInjected files in
English and Russian aligned so long LLM prompt text never falls back to English in Russian games.

## 12. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

If `MSBuild` is not on `PATH`, use Visual Studio Developer PowerShell or locate it with `vswhere`.
The Debug build writes `1.6/Assemblies/PawnDiary.dll`, which is committed on purpose.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryRetentionTests/DiaryRetentionTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
dotnet run --project tests/DiarySaveNormalizationTests/DiarySaveNormalizationTests.csproj
dotnet run --project tests/DiaryObservedConditionTests/DiaryObservedConditionTests.csproj
dotnet run --project tests/NarrativeContinuityTests/NarrativeContinuityTests.csproj
dotnet run --project tests/PawnMemoryTests/PawnMemoryTests.csproj
dotnet run --project tests/DiaryBiotechPolicyTests/DiaryBiotechPolicyTests.csproj
dotnet run --project tests/DiaryOdysseyPolicyTests/DiaryOdysseyPolicyTests.csproj
dotnet run --project tests/RoyaltyContextTests/RoyaltyContextTests.csproj
dotnet run --project tests/SpeakUpBridgeLogicTests/SpeakUpBridgeLogicTests.csproj
dotnet run --project tests/RimpsycheBridgeLogicTests/RimpsycheBridgeLogicTests.csproj
dotnet run --project tests/PowerfulAiBridgeLogicTests/PowerfulAiBridgeLogicTests.csproj
```

In-game smoke tests use the optional RimTest Redux development mod. Build the core first, then the
separate test assembly:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
MSBuild tests\PawnDiary.RimTest\PawnDiary.RimTest.csproj /t:Build /p:Configuration=Debug
```

The second command writes `tests/PawnDiary.RimTest/Assemblies/PawnDiary.RimTest.dll`.
`LoadFolders.xml` exposes that folder only when package `ilyvion.rimtestredux` is active, while the
optional `About.xml` load-order hint puts RimTest Redux first without making it a player dependency.
The project locates Workshop item `3762405308` relative to `RimWorldManaged`; use
`/p:RimTestReduxAssemblies=<path>` or the `RIMTEST_REDUX_ASSEMBLIES` environment variable when the
framework is elsewhere. In game, open **Mod Options → RimTest Redux → Open Test Runner**. The initial
`PawnDiaryDefSmokeTests` suite checks singleton policy Defs, representative base-game-safe interaction
groups, and prompt-template key/field integrity; these checks are read-only and can run at the main
menu. The companion `PawnDiaryEventReactionTests` suite requires a loaded game. It invokes the real
vanilla `PlayLog.Add`, `Pawn_RelationsTracker.AddDirectRelation`, and
`MentalStateHandler.TryStartMentalState` entry points, then verifies the `DiaryEvent` produced by Pawn
Diary's Harmony → ingestion-signal → persisted-event path (EVT-01/EVT-07/EVT-08 in the coverage
matrix).

Both loaded-game suites share the `PawnDiaryRimTestScope` harness (`TEST_COVERAGE_PLAN.md §2.1`),
which owns all of the fragile setup/teardown so a test body only fires a trigger and asserts an
outcome. Each scope generates isolated adult colonists whose diary generation is disabled before they
become eligible (so no LLM request is possible), snapshots the settings and RNG state it touches, and
in `[AfterEach]` restores every mutation — events, diary indexes, `diariesById`, Social-log rows,
relation/mental state, transient dedup/command keys, and the pawns themselves. Two properties make it
trustworthy: teardown runs every cleanup step through a failure accumulator (so a broken assertion
never skips cleanup), and a final no-leak audit fails the test if any state referencing a test pawn
survived. The suite README (`tests/PawnDiary.RimTest/README.md`) documents the harness helpers and how
to run the suites in-game.

Twenty EVT event-flow suites (`PawnDiary*FlowTests.cs`) now sit on this harness, one per event source in
`TEST_COVERAGE_PLAN.md §3` — EVT-01 through EVT-23 — plus the supplemental
`PawnDiaryBiotechGrowthFlowTests`, `PawnDiaryBiotechBirthFlowTests`,
`PawnDiaryBiotechMechanitorFlowTests`, and `PawnDiaryRoyaltyFlowTests` DLC suites. The Royalty fixture
drives a real `CompBladelinkWeapon.CodeFor`, silently recreates a missing historical row through
reconciliation, proves `UnCode` removes it from live narrative context, and audits all six exact
persona-hook signatures. The mechanitor fixture distinguishes silent unspawned starting-state callbacks
from real spawned install/removal pages and uses vanilla's mech-side Overseer relation direction. The B1
suites verify growth
once-only emission, progression consumption, ordinary fallback, both-groups-disabled observation,
the real vanilla `ConfigureGrowthLetter` → `MakeChoices` Harmony boundary at ages 7/10/13 for
`NoTrait`, multiple passions, nickname/responsibility changes, auto-resolution, and a Scribe-restored
postponed owner, canonical two-adult birth emission, child-subject/never-POV shape, exact context, source-owned
N1 evidence for both writers, frozen event-time context, chronological birth-before-death ordering,
durable birth replay rejection, and a live-child delayed naming flush using the original event time.
Both suites capture loaded Full/Balanced/Compact B1 prompts and require the central story facts while
rejecting Thing IDs, family-arc IDs, numeric tiers, and correlation tokens. Phase 4 also adds pure
prompt assertions with a deliberately exhausted optional budget, shipped XML/English/Russian indexed
label checks, invalid/exact-boundary/hard-ceiling pending-list tests, ownership-preserving admission
checks, component-Scribe and live-component pre-cap preservation/admission recovery, an About.xml
no-Biotech-dependency assertion, and localized growth/birth entries in the prompt fixture panel. A
base-only loaded fixture drains frozen growth/birth owners and rejects replay, while an optional-package
runtime fixture reflects over the real RimTalk bridge and verifies shared-memory injection without
recursive submission. That fixture resolves `diary_shared` through RimTalk's live
`ContextHookRegistry`, verifies the bridge-owned `{{diary_shared}}` entry in the active prompt preset,
and uses pair-owned growth-linked/birth-linked memories (canonical solo growth pages are not themselves
pair memory). SpeakUp/RimTalk pure suites plus both bridge builds remain supporting adapter
smoke. These new loaded fixtures must still be run in their matching game profiles; visual letter/
preview inspection and the real cross-launch DLC on/off/on transition remain manual. Sources whose real
trigger needs a loaded map, the
storyteller, or a periodic scanner (raid/mood/quest/reflection/window/observed-condition) are exercised
by submitting the exact per-unit production signal the scanner emits, which keeps the tests mapless and
deterministic; the suite README lists the two suites (death, raid) that still need a disposable colony
because their vanilla trigger has un-restorable side effects.

`PawnDiaryOdysseyJourneyFlowTests` covers the O1.2-O2 boundary without mutating a player's real
gravship: loaded XML policy and mappings, guarded active/inactive map projection, vanilla's exact
pawn-on-gravship predicate, prompt-safe mobile-home surroundings, both frozen Scribe keys,
missing/corrupt/oversized load repair, exact Harmony registration, and detached
  intent→commit→landing component transitions. The flow asserts both the origin-preserving TravelTo
  prefix and commit replay idempotence, landing
start does not touch novelty, an authorized landing without live writers advances observation history
without consuming a page marker, and a major landing with two live diary writers creates exactly one
  pair-shaped event, freezes per-POV role mappings plus exact outcome/roughness context, commits one
  emitted journey ID, and rejects replay. Real-Scribe coverage includes the additive launch-page and
  current-home-tenure fields and rejects separator-bearing stable ship IDs.
O1.5 adds an eligible-writer routine hop that must update observation history without a page, the
explicit no-pawn `TileSettled` drop, and a localized Odyssey prompt-suite render under Full,
Balanced, and Compact. The assembly-free pipeline suite repeats the preset check with a deliberately
exhausted optional budget and proves stable journey/ship/location IDs and ticks never cross the
template boundary. The loaded fixture passes its isolated copilot explicitly to the dev-only suite
entry helper, so a player's existing colonist order cannot change the asserted pair. The combined live
Odyssey runtime/save run completed on 2026-07-17 against RimWorld `1.6.4871 rev591` in English; a
focused loaded rerun of this broader component/prompt flow suite remains separate.
O2 adds loaded-Def checks for the Odyssey-active/inactive seasonal-flood package gate, exact
`ThingPresent` matching, prompt-only page policy, and the exact `GravNausea` matcher and tuning.
It also asserts that all four shipped concrete landing-outcome overrides carry Pawn Diary's
successful-return postfix; pure policy tests cover exact-ship correlation/rejection and sanitized,
bounded outcome prompt projection.

`PawnDiaryOdysseyRuntimeLifecycleTests` covers only the host behavior the detached suite cannot prove.
It enters the real `InitiateTakeoff` and `InitiateLanding` public methods with isolated live objects;
a last-priority test-assembly Harmony prefix suppresses their graphics/cutscene bodies only after Pawn
Diary's installed prefixes have received the payload. Real vanilla `GravshipUtility.TravelTo` then
performs its cross-layer tile rewrite/world-object add, and real private `LandingEnded` clears the
controller and calls Pawn Diary's successful postfix. The fixture asserts that the surface origin
survives engine/pilot despawn and tile rewriting, cancellation never commits, one landing creates one
event/marker, and callback replay is inert. It refuses to run when Odyssey is inactive or the loaded
map already owns a player gravship, and every temporary patch, world object, engine, pawn, component
row, controller field, event, diary index, time-speed change, and mask reference is restored in
failure-safe cleanup. RimTest may also discover or start the suite at the main menu; the fixture checks
for `Current.Game` and `DiaryGameComponent.Instance` before touching `Find` or instance reflection and
logs a visible skip when the loaded-game host is absent.

The same suite supplies a genuine three-run save fixture. Phase A writes a disposable save after real
`TravelTo`, preserving the frozen `odysseyActiveJourney` and `odysseyTravelHistory` keys plus bounded
history, trust, current-home tenure, and launch cooldown. A pending landing is captured before save but
is intentionally absent after load because intent/pending rows remain cutscene-local and are not frozen
save keys. Phase B, run after manually loading Phase A, verifies those contracts, completes the real
landing exactly once, and writes the completed save. Phase C, run after a second manual reload, verifies
one durable page/marker and no resurrected active/transient state, then deletes the two reserved saves.
RimTest Redux cannot safely automate the load calls inside one synchronous test because loading
disposes the current `Game` and runner; `tests/SAVE_COMPATIBILITY_SMOKETEST.md` records the exact
continuation. Building the RimTest DLL is reported separately from executing these phases in RimWorld.
The 2026-07-17 focused run passed the real cancellation/full-lifecycle tests and all three save phases;
Phase C left exactly one durable page/marker before cleanup, resurrected no active/transient state, and
deleted both reserved saves. A base-only profile separately produced all five explicit Odyssey-inactive
runtime skips without Pawn Diary Odyssey patch/XML/type-initializer errors. The post-hardening
Odyssey-enabled main-menu rerun also produced all five intended no-host skips with no Odyssey suite
error, closing the runtime-suite live gap.

The DLC-focused flows include installed-Royalty positive scanner fixtures for a real
`PsychicAmplifier` hediff and disposable real titles, plus Phase-4 loaded fixtures for exact hook
registration, real `SetTitle` promotion/loss and loss-label identity, per-faction scanner loss,
disabled/re-enabled observation, bestowing/anima ritual ownership, the real neuroformer item-comp
hook, disabled-ritual non-transfer, both title-memory callback orders, real `FinalizeInit` reset, and
 Royalty-off immediate-load invalidation. The earlier suite passed 252/252; the expanded suite's last
loaded run passed 256/256 in game. Two additional compiled fixtures cover attendee-first non-claim,
combined psylink/title-memory expiry, and scanner-title reconciliation across a production pre-save
Scribe round-trip; they bring the assembly to 258 tests and still require loaded execution. Ideology and Anomaly ritual tests use
internal copied-fact fixture seams because safely
constructing their live ritual job objects would start a real colony ritual; only that reflective
object extraction is bypassed. The fixtures still execute production fan-out ordering, pawn-ID
uniqueness, colony dedup, child capture decisions, persisted solo pages, diary references, and prompt
context for all four perspectives. `DiaryPipelineTests` separately pins Odyssey's package-gated,
departure-only gravship launch group; exact `GravshipJourney` landing group/domain; XML-enabled novelty
switch; orbital debris, vacuum exposure, volcanic ash/flooding, prompt-only volcanic atmosphere, and
vacuum enchantments. `DiaryCapturePolicyTests` pins the canonical solo/pair/drop route, while
`DiaryOdysseyPolicyTests` pins writer limits, novelty/history, baseline duration distrust, first-orbit
and rough-only positive paths, homecoming negatives, invalid-input drops, hidden-destination omission,
per-POV pair projection, transactional launch-page cooldown, and one-use long-held-home boundaries.

Canonical birth context begins with `tale=BiotechFamilyBirth`, then carries the B1 child, outcome,
method, and adult-role facts. The Tale marker is what makes saved-event/prompt classification recover
the important `biotechFamilyBirth` group and select PairImportant/SoloImportant; without it, the same
truthful facts exist on the event but ordinary interaction templates omit them. The birth prompt suite
pins both the domain/importance route and the loaded template fields before capturing prompts.

Release runs must also disable duplicate Workshop, Modmixer, or development copies and use one active
core package. RimWorld may otherwise report duplicate package IDs or combine stale Def XML with a
different `PawnDiary.RimTest.dll`; a missing loaded template field reports that mismatch explicitly.

Alongside the event flows, `PawnDiary*FixtureTests.cs` cover the prompt/policy layers on the same
harness: template/domain resolution and the Pair/Solo/reflection/neutral template matrices, the
context-detail presets, prompt enchantments, the writing-style and psychotype precedence chains, humor
cues (all captured through the real render pipeline via prompt-test mode), a Scribe save/load round trip
of the diary models and repository index rebuilds, the public `PawnDiaryApi` surface, the non-visual
diary-tab view-model contracts, and the DLC compatibility matrix. That matrix asserts base-only/null
omission, real installed-DLC pawn state through final summary/enchantment adapters, exact official
package/group/window/settings availability, fragile DLC hook signatures, and optional capture
capability fail-open behavior. Production invalidates the gene comparison baseline when a save loads
without Biotech. `PawnDiaryBiotechDlcOffMaintenanceTests` now proves frozen pending-owner maintenance
inside a genuinely base-only loaded profile; cross-launch DLC removal/re-enable acceptance remains
manual because `ModsConfig` cannot be safely rewritten inside one running game. `scripts/verify-coverage.ps1` is the one-command
audit that builds everything, runs the pure suites, and prints the EVT requirement matrix. The
transport/async-runtime layer (`§6.3`) is intentionally deferred — see the suite README — because
`LlmClient` is static and session-global and cannot be driven safely from an in-game test without a
reviewed request-executor seam or a loopback endpoint.

The API-surface fixture deliberately sends blank ids/null callbacks to four public guards. Those
guards use production `ErrorOnce` diagnostics to alert real integration authors, so running that
negative-test fixture emits one expected message each for `SetWritingStyleOverride`,
`SetPsychotypeOverride`, `RegisterEntryStatusListener`, and `RegisterPawnContextProvider`; they do not
mean a shipped adapter registered incorrectly.

`TEST_COVERAGE_PLAN.md` is the implementation roadmap for expanding this initial suite. It maps every
documented event source, prompt template/policy layer, asynchronous runtime branch, persistence path,
UI/view-model contract, and DLC/optional-mod configuration to a concrete pure, RimTest, transport, or
manual test and defines the completion gate for each staged phase.

Adapter assemblies are built from their own projects; notably SpeakUp stays reflection-only, while
the Powerful AI bridge is also reflection-only and Rimpsyche needs the installed `RimPsyche.dll`
compile reference:

```powershell
MSBuild integrations/PawnDiary.SpeakUp/Source/PawnDiarySpeakUp.csproj /t:Build /p:Configuration=Debug
MSBuild integrations/PawnDiary.RimpsycheBridge/Source/PawnDiaryRimpsyche.csproj /t:Build /p:Configuration=Debug
MSBuild integrations/PawnDiary.PowerfulAiBridge/Source/PawnDiaryPowerfulAiBridge.csproj /t:Build /p:Configuration=Debug
```

Prompt lab:

```powershell
cd prompt-lab
npm test
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

Live hook checks use a disposable save, dev mode, prompt-test mode, and RimBridge/GABS. RimWorld dev
mode's Debug Actions menu exposes `Pawn Diary > Event test panel...` for common real trigger paths
`Pawn Diary > Export all diary pages...` for UTF-8 export, and
`Pawn Diary > Purge archived entries for pawn...` for a direct pawn picker that clears only that
pawn's compact archived pages. In the event panel, select an eligible colonist and optionally select
a partner. Real event trigger buttons are not exposed in the panel; exercise those hook paths by
playing them out in-game. For Diary UI stress checks, use the panel's Diary section to fill mock
pages, switch personas, or open transient card previews. For prompt shape checks that do not need a
real gameplay trigger, use the Fixtures section and generate all or selected fixtures for an eligible
colonist. Prompt-test mode intercepts only after an event reaches
`QueuePrompt`; a successful capture logs:

```text
[PawnDiary debug] Captured prompt without generation event=<id> role=<role>
```

Release payloads are prepared with:

```powershell
scripts\publish.ps1
```

The publish script builds the example/API-explorer and SpeakUp adapter payloads by default; pass
`-PublishExampleAdapter:$false` or `-PublishSpeakUpAdapter:$false` to skip either. The four adapters
that compile against typed integration contracts are opt-in because RimTalk, Rimpsyche, and 1-2-3
Personalities need their target Workshop assembly installed on the release machine (VSIE does not).
The reflection-only Powerful AI bridge is opt-in as well. Use `-PublishAllAdapters` for the complete
seven-adapter release, or one of
`-PublishRimTalkAdapter`, `-PublishRimpsycheAdapter`, `-PublishPersonalitiesAdapter`, and
`-PublishVsieAdapter` to enable one typed adapter, or `-PublishPowerfulAiAdapter` for that bridge (the
default-on example/SpeakUp payloads can still be disabled separately).

The source `About/About.xml` carries the mod's `<modVersion>` (`0.5.0` for the current release). The publish
script stamps that value into the generated main and Russian localization `About.xml` files; pass
`-Version <value>` to override the release payload version without editing source metadata.

The script builds a throwaway Release DLL, copies runnable mod files, runtime textures, and reference
docs into `dist/<published packageId>`, and installs the payloads into the detected RimWorld `Mods`
folder through junctions by default. The published main payload keeps `README.md`,
`DOCUMENTATION.md`, `CHANGELOG.md`, `EVENT_PROMPT_MAP.md`, and any license file, but intentionally
excludes `Source/`, `tests/`, `prompt-lab/`, and other development-only files. Pass
`-InstallToMods:$false` to prepare `dist/` only.

Russian is packaged as a separate Workshop localization mod by default. The script produces the
normal main payload plus `dist/<published packageId>.russian`; the main payload excludes
`Languages/Russian (Русский)/`. The localization payload contains only its own translated Russian
`About/` metadata, `About/Preview-Russian.png` copied as the Workshop `Preview.png`, and the Russian
language folder. It declares a dependency/load-after on the main published packageId, uses packageId
`<published packageId>.russian` unless overridden with `-RussianLocalizationPackageId`, and installs
its own junction next to the main mod junction. Before updating an existing localization Workshop
item, either pass `-RussianLocalizationPublishedFileId <id>` or store that id in
`About/PublishedFileId-Russian.txt`; the script copies it into the localization payload as
`About/PublishedFileId.txt`. Use `-SplitRussianLocalization:$false` or
`-IncludeRussianInMainPayload` only for a legacy bundled-language payload.

The example adapter is packaged as a third payload by default. The script builds
`integrations/PawnDiary.ExampleAdapter/Source/PawnDiaryExampleAdapter.csproj` against the freshly
built core DLL, writes the runnable example mod to `dist/<example adapter packageId>`, rewrites its
dependency/load-after metadata to the published core packageId, adds the core Workshop URL from
`About/PublishedFileId.txt` when present, and installs a matching Mods-folder junction. Unlike the
main Workshop payload, this example payload intentionally ships its `Source/` folder plus
`API_EXPLORER.md`, `INTEGRATIONS.md`, and `EXTERNAL_API.md`, so adapter authors can open the mod and
copy the integration pattern directly. Pass `-PublishExampleAdapter:$false` to skip the example
payload, `-ExampleAdapterPackageId` or `-ExampleAdapterOutDir` to override its identity or location,
and `-ExampleAdapterPublishedFileId` or `About/PublishedFileId-ExampleAdapter.txt` when updating an
existing example-adapter Workshop item.

Each opt-in runtime adapter is rebuilt against the same throwaway core DLL as the main payload and staged
as a clean runtime mod (About, Defs/Patches, Languages, its own fresh DLL/PDB, integration docs, and
license; no checked-in assembly copy). Runtime payloads omit `Source/` except the Powerful AI bridge,
which deliberately publishes its complete bridge source. Release prep rewrites the development core packageId
in both `modDependencies` and `loadAfter`, preserves target-mod dependency rows, stamps the release
version, and refreshes the core Workshop URL. Every integration source manifest directly declares the
published core package id (`aimmlegate.pawndiary`) and Workshop URL; each target-specific adapter also
declares its target mod as a dependency with a Workshop URL (optional secondary modules remain
`loadAfter` hints only). Adapter Workshop ids can be stored in
`About/PublishedFileId-RimTalk.txt`, `About/PublishedFileId-Rimpsyche.txt`,
`About/PublishedFileId-Personalities123.txt`, and `About/PublishedFileId-Vsie.txt`; each is copied to
the matching payload's `About/PublishedFileId.txt`; the Powerful AI bridge uses
`About/PublishedFileId-PowerfulAi.txt`. Release prep validates that staged integration
payloads retain the published core id and never contain `aimmlegate.pawndiary.development`.

All seven integration source `About.xml` descriptions use short, natural English Workshop copy; Russian
in-game localization remains in each submod's `Languages/Russian (Русский)/` tree rather than being
duplicated inside the metadata description.

## 13. When Changing The Mod

- Follow `AGENTS.md` for detailed architecture, DLC, localization, and validation rules.
- Keep tunable policy in XML when possible.
- Keep live RimWorld objects at adapter/UI/transport edges; pure helpers should use DTOs/primitives.
- Add or update focused pure tests when changing pure logic.
- Update `DOCUMENTATION.md` and `CHANGELOG.md` for behavior, structure, release, or workflow changes.
