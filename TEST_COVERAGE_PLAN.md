# Pawn Diary — Comprehensive Test Coverage Plan

This plan defines how to cover Pawn Diary's documented behavior from a real RimWorld event through
capture, prompt planning, generation, persistence, and display. It is organized around observable
contracts rather than line coverage: every documented route must have a deterministic test at the
lowest useful layer, plus an in-game wiring test wherever RimWorld or Harmony is part of the contract.

The authoritative behavior maps are `DOCUMENTATION.md` (§3–§10) and `EVENT_PROMPT_MAP.md` (§1–§6).
When either document gains a new event source, prompt shape, policy layer, or runtime branch, this
plan's coverage matrix must gain a matching row before the feature is considered complete.

## 1. Coverage Standard

“Fully covered” means:

1. Every event source in `DOCUMENTATION.md §4` has:
   - a pure decision/context-format test;
   - an in-game test that invokes the real vanilla/API entry point or scanner;
   - a negative gate test and a dedup/batching/fan-out assertion where applicable;
   - save-state cleanup that runs even after failure.
2. Every shipped prompt template key has a selection test and a rendered-field contract test.
3. Every prompt-policy layer has precedence, fallback, override, and omission tests.
4. The asynchronous runtime has success, retry, failover, cancellation, reload, and error paths.
5. Persistence has both pure normalization tests and a real disposable-save round trip.
6. Base-game-only and optional-DLC/mod configurations are explicitly exercised.
7. No automated test calls a real external LLM, changes the player's colony, or leaves test rows in
   a save, PlayLog, relation tracker, world-pawn list, transient queue, or settings object.

This does not mean one in-game test for every XML row. Large XML catalogs use exhaustive data-driven
contract tests plus representative live-state tests for each distinct source/behavior. For example,
all prompt-enchantment Defs get schema/matcher coverage, while live RimTest cases exercise each
collector kind (hediff, capacity, Royal title, Ideology role, event window, observed condition).

## 2. Test Layers And Shared Harness

| Layer | Home | Responsibility |
|---|---|---|
| Pure policy | Existing standalone projects under `tests/` | Decisions, ordering, weights, parsing, formatting, retention, normalization, routing, and deterministic randomness without RimWorld assemblies. |
| Loaded-Def contracts | `tests/PawnDiary.RimTest/PawnDiaryDefContractTests.cs` | Exhaustive loaded XML integrity, references, uniqueness, template capabilities, fallback availability, and optional-package gates. Safe at the main menu. |
| In-game integration | `tests/PawnDiary.RimTest/PawnDiary*FlowTests.cs` | Real Harmony hooks/scanners, live pawn/map adapters, repository refs, prompt capture, and runtime lifecycle. Requires a loaded disposable game. |
| Transport integration | `PawnDiaryRuntimeFlowTests` with a loopback provider | Actual `LlmClient` request JSON, background execution, retries/failover, response parsing, and main-thread application without internet access. |
| Save/load scenario | Disposable test save plus two-phase RimTest suite | Real Scribe round trip, index rebuilds, pending-state normalization, archive persistence, and session reset. |
| Manual visual checks | `tests/SAVE_COMPATIBILITY_SMOKETEST.md` plus a new UI checklist | Immediate-mode rendering, resolution/accessibility layouts, scroll behavior, and screenshots that are not stable enough for unit assertions. |

### 2.1 `PawnDiaryRimTestScope`

Before adding more loaded-game cases, extract the current reaction-test setup/teardown into one
internal test-assembly harness. Each test scope should own and restore:

- isolated adult/child test pawns, optionally spawned on a disposable map;
- per-pawn generation gates and all mutated settings dictionaries/fields;
- the pre-test event/archive/diary/PlayLog snapshots;
- relations, mental states, inspirations, hediffs, jobs, conditions, tales, quests, and spawned things;
- event-window/observed-condition rows, pending batches, delayed generation, dedup keys, command caches,
  LLM queues, lane cooldowns, listener registrations, and any world-pawn membership;
- `Rand` state and deliberate game-tick changes;
- a failure accumulator so every cleanup step runs before teardown reports an error.

The harness exposes helpers such as `FireAndRequireEvent`, `RequireNoNewEvent`,
`CapturePromptOnly`, `RequirePairRefs`, and `RequireSoloRef`. Test code should assert outcomes, not
repeat reflection-based cleanup.

### 2.2 Deterministic LLM substitute

Prompt-only mode covers planning but not runtime transport. Runtime tests should host a bounded local
loopback HTTP endpoint on a background thread and temporarily point test-only API lanes at it. The
endpoint returns scripted OpenAI-compatible responses and records requests. This exercises the real
HTTP, JSON, queue, parser, and completion paths without provider credentials.

Each transport test has a five-second maximum, restores API settings and `LlmClient` session state,
and manually drains `GameComponentUpdate` while RimTest owns the main thread. If the target Mono build
cannot reliably host a loopback listener, introduce a narrow internal request-executor interface in
`LlmClient`; the production implementation remains HTTP and the test implementation remains inside
the separate RimTest assembly. The core assembly must never reference RimTest Redux.

## 3. End-To-End Diary Item Flow

Every row below needs a real-trigger RimTest. The “core assertions” are in addition to the common
checks: exact event count, correct participant refs, correct status, no external request, and complete
teardown.

| ID | Source/route | Real trigger or driver | Core assertions |
|---|---|---|---|
| EVT-01 | Interaction pair/solo | `PlayLog.Add` with eligible combinations | Pair vs solo shape, rendered fallback, source LogID, group instruction, disabled-group drop. |
| EVT-02 | Interaction batch/ambient | Repeated `Chitchat`/`Insult` rows plus bounded tick advance | Pending accumulation, threshold/quiet-window flush, sampled evidence, no premature page, promotion route. |
| EVT-03 | Thought immediate/ambient | `TryGainMemory` with configured thought Defs | Vanilla-accepted memory only, solo vs ambient route, mood facts, duplicate rejection. |
| EVT-04 | Thought progression | Set a need/thought stage and invoke scheduled scan | Baseline suppression, worsening-stage page, recovery/repeat behavior, saved progression key. |
| EVT-05 | Inspiration | `TryStartInspiration` | Solo page, reason/context, disabled group, ending inspiration leaves no test state. |
| EVT-06 | Ability | Successful local and global `Ability.Activate` paths | Caster/target facts, cooldown-weighted gate, failed activation drop, dedup. DLC cases conditional. |
| EVT-07 | Romance | `AddDirectRelation` for Lover/Spouse/Ex relations | One canonical pair event despite mirrored relation state; unlisted relation drops. |
| EVT-08 | Mental state | Forced Berserk and SocialFighting | Solo break vs pair fight, target/reason context, failed start drops, recovery cleanup. |
| EVT-09 | Tale | `TaleRecorder.RecordTale` representative single/pair/combat/death tales | Shape, participant extraction, combat batch, missing participant handling, XML group toggle. |
| EVT-10 | Death | `Pawn.Kill` with and without a recorded death Tale | One neutral death page, cross-source dedup, final-boundary refs, no first-person voice. |
| EVT-11 | Hediff | `AddHediff`, severity change, natural part loss, added part | Immediate vs day-signal route, severity steps, body-part markers, ignored/no-policy hediff. |
| EVT-12 | Work | Assign controlled job and invoke work scan | Eligible work only, chance/cooldown route, same-work suppression, passion/chore/dark-study facts. |
| EVT-13 | Raid | Execute a minimal test incident or invoke a controlled `IncidentWorker` | Map-colonist fan-out, ordinary delay, drop-pod/infestation bypass, colony dedup, other-map exclusion. |
| EVT-14 | Mood event | Register a controlled `GameCondition` | Affected-map fan-out, mood classification, unaffected-map exclusion, disabled group. |
| EVT-15 | Pawn progression | Mutate skill passion milestone, trait, and optional DLC state then scan | First-scan baseline, only upward/new milestones, exact context, major-change arc request; installed-Royalty real psylink/title positive paths and repeat suppression. |
| EVT-16 | Quest | Accept/end controlled quest and run recovery scan | Accepted bookkeeping/no page, completed/failed fan-out, placeholder-label sanitation, duplicate scan drop. |
| EVT-17 | Ritual | Submit production Ideology/psychic fan-outs through internal copied-fact fixtures | Organizer/invoker, target, participant, and spectator pages; duplicate-role pawn-ID collapse, colony dedup, per-perspective context/localization, all 16 Anomaly classifier keys and fallback, and clean no-DLC fields. Live DLC constructor signatures are checked separately. |
| EVT-18 | Arrival | `SetFaction` plus founding-arrival bootstrap | Neutral page, scenario/backstory facts, exactly-first ordering, one bad pawn cannot wedge bootstrap. |
| EVT-19 | Day/quadrum reflection | Seed day signals and invoke sleep/rest flush | Highlight selection, once-per-day guard, quadrum substitution, evidence consumption, token limits. |
| EVT-20 | Arc reflection | Seed hot/archive memories and invoke scheduled/major-event path | Year/gap limits, memory filtering/dedup, forced retry backoff, recent-memory tracking. |
| EVT-21 | External API | `SubmitEvent`, `SubmitPromptEntry`, and direct-entry API | Solo/pair, group gate, protected instruction, attribution, budget rejection, listener notification. |
| EVT-22 | Event windows | Controlled incident/thing/letter/birthday/hediff/prison-break/monolith signals | Start/end/one-shot page, scope, dedup, timeout, prompt-bias state, no-DLC string matching; exact Stirring/Waking/Void Awakened ownership, Gleaming silence, and source-owned monolith evidence/reference with no provider-created prompt context. |
| EVT-23 | Observed conditions | Seed each observer kind and invoke scan | Start/end debounce, state refresh, scope identity, optional page, prompt-only state, restart cooldown. |

For chance-driven routes, pure tests cover distributions and formulas. In-game tests inject a known
seed or temporarily set XML-backed effective chance to `0`/`1`; they never retry until random success.

## 4. Prompt Policy And Template Selection

### 4.1 Domain and policy resolution

Add table-driven pure tests covering every marker/domain in `DiaryEventDomainClassifier` and every
classifier-key specialization: quest signals, ritual/psychic ritual, ability category, body-part
hediffs, reflections, arrival, and death.

Master Wave 2 adds shipped-XML contract coverage in `DiaryPipelineTests`: every installed Anomaly
psychic-ritual key resolves exactly once under case-insensitive `matchDefNames`, all exact groups and
the fallback require the official package, Ritual orders are unique, DefInjected fields are complete
in English and Russian, unknown/modded keys keep the fallback, and an Ideology ritual cannot enter an
Anomaly family. The same test parses event-window triggers to prove one reached monolith level maps to
one window, `Gleaming` maps to none, localized fallbacks exist, and the three exact windows declare
bounded source evidence. The EVT-22 RimTest fixture exercises the live package-gated page/evidence
path when Anomaly is active and the no-op branch otherwise.

Master Waves 5 and 9 / Royalty Phases 0–5 plus N3-R evidence use `RoyaltyContextTests`, now a
346-assertion assembly-free suite. It
freezes the `royalty-persona|<weaponThingId>|<bondEpoch>` grammar and its mapping to the existing
Narrative Continuity `bond_lifecycle`/`weapon` contract; formation/load-baseline/re-equip,
pending/threshold/unobservable separation, recorded/unrecorded recovery, destruction/death/transfer,
map-removal/unknown-uncode, and disabled-output/no-catch-up lifecycle rows; structural trait rank,
worker mapping, exact override/exclusion, stable ordering, two-trait/candidate/text caps, sanitization,
and malformed input; first consequential Tale qualification, exact initiator/recipient and death-
context ownership, disabled/rejected consumption, solo-killer ownership, and victim-route preservation;
first title/promotion/demotion/loss/no-op, faction identity, seniority, bounded duty deltas, claimed and
disabled observation advancement; and exact bestowing/anima/neuroformer/succession title/psylink
matching, inclusive correlation boundaries, pending/claim/expiry, one-fallback dedup, mismatched pawn/
level/correlation, and malformed/null behavior. The shipped policy XML is parsed separately and its
detached `CreateDefault` path is exercised. Phase 1 additionally covers conservative persona
baselines, malformed phase/tick/cause repair, duplicate weapon ownership, trait/row caps, faction-title
dedup, highest-seniority selection, stable ordering, missing rows, corruption ceilings, exact live
weapon validation for provider facts, and persistence of steady-primary observation ticks. The new
N3-R adds exact pawn title-transition evidence, authority/status/duty/death topic boundaries, major
loss salience, and rejection of no-change/malformed rows. `PawnDiaryRoyaltyStateFixtureTests` compiles
against RimTest Redux and exercises deep persona Scribe
round-trip, every nested per-pawn faction/title/label/seniority/tick field plus psylink preservation,
the component's actual persona-ledger keys for old-save version-zero versus initialized-empty markers,
observation availability, transient ownership reset, and guarded no-Royalty
persona/title/psylink collection. Building that fixture is not a claim
that it was executed inside a loaded game; that acceptance remains explicit.

Phases 2–3 add pure ownership and shipped-contract coverage across the other suites. Capture tests
require the registered `PersonaWeapon` catalog/Spec, lifecycle solo/drop decisions, stable
`persona-weapon|weapon|epoch|phase` dedup, exact phase/Def/trait tokens, and forced solo killer-POV
planning for a canonical first-kill page; the suite now passes 665 assertions. Pipeline tests pin the
dedicated lifecycle domain, the Royalty-package-gated no-catch-all lifecycle and exact Tale milestone
groups, five exact event-prompt rows, append-only `SoloImportant` lifecycle fields `90–101` and
milestone fields `102–106`, enriched `DeathDescription` fields, English/Russian DefInjected and Keyed
coverage, all prompt fixtures, required compact facts, optional trait behavior, and exclusion of
internal IDs, epoch, ticks, and correlation keys; the suite now passes 2,290 assertions.

Runtime Phase 2 commits saved lifecycle state before optional dispatch and registers defensive
Royalty-only seams. Vanilla `bondedThought` remains situational and outside the memory hook. The user
confirmed the repaired Phase-2 loaded automated suite green on 2026-07-18; that result is recorded
separately from the still-unchecked short-swap, separation, recovery, destruction, map-removal,
transfer, save/load, localization, and no-Royalty manual rows in
`tests/SAVE_COMPATIBILITY_SMOKETEST.md`.

Phase 3 projects an exact active `Pawn.Kill` scope into qualifying Tale capture. The first truth is
consumed even when the milestone group is disabled or the page is rejected, while an accepted page
is forced to one solo killer POV and retains the source Tale marker/roles. Exact persona `killThought`
signals and the eight exact same-call companion Tales are staged in bounded fail-open buffers, claimed
only by the durable milestone, and otherwise released independently through ordinary capture. The
60-tick inverse-order owner carries only missing ThoughtDefs and consumes each once. Wielder death
snapshots pre-`UnCode` persona facts into the existing death page—including a live coded non-primary
bond—and never creates a standalone bond-ending page. Save tests cover recorded+observed and
observed-without-recorded round trips plus recorded-implies-observed repair.

`PawnDiaryRoyaltyFlowTests` now compiles real coding/formation, late-visible baseline, hook audit,
guaranteed vanilla major-threat kill, delayed companion-batch flush, same-tick ordinary second kill,
disabled-milestone fallback, primary and non-primary bonded-wielder deaths, and Scribe flag fixtures.
Pure coverage is 245 Royalty and 2,290 pipeline assertions. The first loaded Phase-3 run passed 240/241
tests and exposed that production read `victim.Dead` before vanilla's later `health.SetDead()`; the
exact active scope fixes that timing. The expanded run reached 242/244; its two failures were
false-negative fixture queries that searched for `interactionDefName=talecombat`, even though a
flushed batch stores either its source/synthetic Def there and identifies the group through exact
`group=talecombat; batch=tale` context fields. After those queries were corrected, the focused loaded
rerun passed 244/244 on 2026-07-19. Phase 3 automated loaded coverage is therefore green. All manual
rows remain required; neither Phase 2 nor Phase 3 is manually acceptance-complete, and R1 remains
open until Phase 4 passes.

Phase 4 expands the pure Royalty matrix to 316 passing assertions: exact gained/promoted/demoted/lost/
no-op classification, same-label faction distinction, hook/scanner observation advancement, vanished-
faction loss, disabled-output no replay, bestowing/anima/neuroformer/unknown routing, wrong-owner
rejection, bounded active/pending admission, missing-pawn expiry, master/group-disabled consumption,
one-shot expiry, title-thought ownership, combined title/psylink selection, and compact/malformed/
oversized context. Pipeline
coverage passes 2,437 assertions and pins exact groups/prompts, append-only SoloImportant fields
107–112, English/Russian labels and fixtures, required/optional context-detail routing, and compact
projection. Capture-policy and Narrative Continuity remain green at 665 and 125 assertions.
`PawnDiaryRoyaltyProgressionFlowTests` now compiles loaded fixtures for real `SetTitle` promotion/loss,
scanner loss/no-catch-up, target-only bestowing/anima ritual ownership, real neuroformer-comp use,
group/policy-master suppression, unmatched title-memory expiry and production pre-save Scribe reload,
and no-Royalty silence; `PawnDiaryRoyaltyFlowTests` audits all four exact new Harmony seams.
The first Phase-4 loaded-game run reached 249/252. One exact Harmony postfix parameter-name mismatch
disabled the title callback and caused both the hook-audit and real-promotion failures; the remaining
failure was an outdated four-token ritual-catalog expectation after Phase 4 added two bestowing tokens.
The second run reached 250/252 and confirmed those corrections. It exposed one production regression:
promotion and loss in the fixture's same paused tick shared the old pawn/faction/tick dedup key, so
loss was suppressed. Pure tests now pin stable identical-edge keys and distinct same-tick edge keys.
The other failure was the Phase-3 non-primary death fixture depending on a loaded profile's patched
equipment-removal semantics; it now arranges the pending bond through the exact Pawn Diary observer.
The subsequent user-confirmed loaded rerun passed 252/252. Adversarial hardening then strengthened
existing fixtures and expanded the suite; the user-confirmed loaded rerun passed 256/256. Every
Phase-2/3/4 hands-on row remains open; R1 is
therefore code-complete but not yet acceptance-complete.

Phase 5 adds 28 pure Royalty assertions for exact candidate+outer-commit authorization,
candidate-only/equal-or-higher silence, deceased/heir/faction/title/correlation mismatches, inclusive
expiry, callback-before-commit staging, delayed matching, deterministic normalization/dedup/caps,
distinct same-tick edges, malformed rows, prompt-context redaction, and explicit-quest versus
automatic heir assignment. Pipeline coverage now passes 2,493 assertions and pins the two exact event
prompts, Royalty-gated group classification, XML window/cap, SoloImportant fields 113–116,
English/Russian labels/UI/dev fixtures, Compact-required succession facts, and the omission of internal
IDs/ticks/correlation/commit flags. The runtime and 264-test RimTest assemblies build.

`PawnDiaryRoyaltyProgressionFlowTests` adds real `Notify_PawnKilled` inheritance, a surrounding
bestowing/title duplicate owner, equal-or-higher silence, and a real
`QuestPart_ChangeHeir.Notify_QuestSignalReceived` appointment after proving direct `SetHeir` is silent.
`PawnDiaryRoyaltyFlowTests` audits all three exact new Harmony targets.
`PawnDiaryRoyaltyStateFixtureTests` adds detached committed-fact Scribe, the actual component's missing
and initialized-empty `royaltyPendingSuccessions` key, and transient-scope reset. These four new loaded
tests compile but have not yet executed in RimWorld; 256/256 remains the last confirmed loaded result,
and the Phase-5 hands-on matrix remains open.

Odyssey has a focused shipped-XML contract in `DiaryPipelineTests`: the departure-only launch ritual
and exact `GravshipJourney` landing group are Odyssey-package-gated, landing pages are novelty-enabled,
and saved landing context recovers the new domain. `OrbitalDebris`/`VacuumExposureRevealed`, weather,
prompt-only volcanic atmosphere, and both vacuum hediff spellings retain their generic routes.
O2 removes the stale `Flooding` mood identity and freezes the package-gated `SeasonalFlood`
`ThingPresent` observer, prompt-only page policy, hysteresis/cooldown, exact `GravNausea` matcher,
XML tuning, and English/Russian Keyed plus DefInjected coverage.

Odyssey O1.1-O1.5 uses `DiaryOdysseyPolicyTests`, an assembly-free suite with 158 assertions freezing the
journey/landing tokens and IDs, exact location mapping, deterministic pilot/copilot/crew selection,
quality and duration bands, landing reason priority, cooldown exceptions, bounded idempotent history,
silent old-save baselining, prompt-safe bounded context, exact intent/landing correlation, conservative
fallback commits, transient expiry, departure-history mutation, transactional launch-page cooldown,
one-use long-held-home bypass, old-save synthetic-duration suppression, positive first-orbit and
rough-only paths, homecoming negatives, invalid-input drops, hidden-destination omission, and per-POV
pair-role projection. `DiaryCapturePolicyTests` additionally pins all registered `GravshipJourney`
enablement/identity/writer guards, the solo/pair/drop decision, and frozen landing dedup key. O1.2 adds
`PawnDiaryOdysseyJourneyFlowTests`: loaded XML projection/mappings, Odyssey-active/inactive guarded
map capture, exact vanilla onboard scoping, mobile-home prompt safety, real-Scribe round trips under
both frozen component keys, missing-key untrusted baselining, corrupt journey rejection, and bounded
newest-history retention. O1.3 extends it with exact public/private Harmony registration and detached
state-only intent/TravelTo/landing-start ownership. O1.4 adds an authorized-without-live-writer
transaction fixture plus a major two-writer finish that creates one pair event, commits one page/ID,
and rejects replay. The major path also attaches an exact landing outcome before component completion
and asserts roughness/outcome plus both pair-role mappings. The focused O1 runtime/save acceptance is
complete; a loaded rerun of this broader component/prompt-flow fixture remains a separate follow-up.
O1.5 adds a loaded eligible-writer routine landing/no-page fixture,
an explicit `TileSettled` no-pawn drop, and localized Full/Balanced/Compact prompt-suite rendering.
`DiaryPipelineTests` also exhausts the optional context budget and proves the required Odyssey
phase/reason/duration/role/ship/origin/destination facts survive while stable IDs and ticks do not.
O2 extends the loaded fixture with active/inactive package-gate checks for `SeasonalFloodActive` and
loaded exact matcher/tuning checks for `DiaryEnchant_GravNausea`; their live execution remains an
O2-specific future gap and is not part of the completed O1 runtime/save acceptance. The prompt-suite
fixture supplies its isolated copilot explicitly,
so its dual-POV assertions are independent of the player's live colonist ordering.
The same fixture asserts fail-soft registration on all four shipped `LandingOutcomeWorker` overrides.
`DiaryOdysseyPolicyTests` now has 158 assertions, including wrong-ship/empty-outcome rejection, exact
outcome attachment, Def-identity omission, visible-label sanitization, and bounded context. Pipeline
coverage now passes 2,195 assertions, keeps `landing_outcome` and the correct pilot/copilot POV role in
Full/Balanced/Compact, and locks both localized indexed labels.

Odyssey runtime hardening adds `PawnDiaryOdysseyRuntimeLifecycleTests`, deliberately limited to the
facts that detached/component fixtures cannot prove. `InitiateTakeoff` and `InitiateLanding` are entered
through their real public methods so the installed Pawn Diary Harmony prefixes receive live engine,
tile, gravship, map, and pawn payloads; a last-priority test-only prefix suppresses only their unsafe
graphics/cutscene bodies. `GravshipUtility.TravelTo` and private `LandingEnded` execute their real
vanilla bodies. The suite therefore proves a cross-layer trip keeps the pre-rewrite surface origin
after the engine/pilot are despawned, takeoff without `TravelTo` stays transient, successful landing
creates one canonical rough-landing event/marker, and a repeated finish callback creates neither.
Every fixture explicitly logs `SKIP` when Odyssey is inactive or a real player gravship is present,
and failure-safe cleanup restores component/controller state, time speed, the mask engine, world
objects, spawned things, pawns, events, diary indexes, and temporary Harmony patches.

Narrative N2-O, N3-B identity, and N3-R core extend `NarrativeContinuityTests` to 125 assembly-free
assertions. N3-R covers exact persona arc/weapon and title POV/domain/topic applicability, cross-DLC
identity isolation, absent/unknown/unverified/null silence, stable candidate keys and ordering,
defensive persona/duty caps, and deterministic duty-topic normalization. N2-O covers the one-row
Odyssey home candidate, exact ship/journey/location identity, family-event ambient selection, landing
exact-arc selection, inactive-provider/unknown-knowledge/disconnected-POV/unknown-location silence, and
the frozen departure plus arrived/returned ship/place reference factories. Loaded RimTests exercise the
fixed provider list and Def-backed format, while `PawnDiaryOdysseyJourneyFlowTests` proves the live
snapshot exists iff vanilla's exact onboard predicate succeeds. The existing growth/birth owner passes
that snapshot at event time; N2-O adds no hook, event, writer fan-out, or new save field.

Master Wave 3 / Biotech Phases 0–3 plus Phase 4 automated hardening use
`DiaryBiotechPolicyTests`, an assembly-free suite that freezes
the complete family arc grammar, additive save/context keys, actual before/after growth diffing,
NoTrait and ambiguous auto-trait handling, passion verification, opportunity boundaries,
deterministic supporter tiers/ties, child/supporter writer shapes, capped birth writer ordering,
legacy settings inheritance, bounded context formatting, XML policy coverage, exact classifier
groups/package gates, pending-row normalization/expiry/grace, exact/invalid/hard cap boundaries,
deterministic defensive truncation, and ownership-preserving admission limits,
the absence of a Biotech mod dependency, pregnancy/labor correlation,
lesson/play/memory classification, unsummarized evidence, duplicate repair, compaction/retention,
exact birth-arc attachment/idempotence, naming/writer-availability flush and durable-record policy,
labor-to-pushing single-arc continuity, multiple-birth evidence cloning, exact-parent precedence,
XML-owned mature-birth/miscarriage classification, tick-zero load repair, frozen pending birth context,
malformed birth-token repair, and English/Russian DefInjected/Keyed
text. `DiaryCapturePolicyTests` also requires both catalog types/Specs and their
solo/pair/drop/dedup decisions. `PawnDiaryBiotechGrowthFlowTests` covers the live growth
component boundary: canonical page creation with family context and N1 identity evidence, durable-event/consumed-age
dedup repair, trait/skill baseline consumption, Birthday fallback when canonical growth is disabled,
zero pages with both groups disabled, and the real vanilla `ConfigureGrowthLetter` → `MakeChoices`
Harmony boundary at ages 7/10/13 for `NoTrait`, multiple passions, nickname/responsibility changes,
auto-resolution, and a Scribe-restored postponed owner. It also exercises the live pre-cap rejection/
recovery boundary, captures loaded Full/Balanced/Compact growth prompts, and rejects private
IDs/tier/correlation fields. `PawnDiaryBiotechBirthFlowTests` covers
final canonical pair dispatch, child-subject/never-POV shape, original tick, exact context, per-role bond
evidence, hot-event replay rejection, frozen event-time prompt/display facts, chronological insertion before
a final-death boundary, a live-child delayed naming flush, and loaded Full/Balanced/Compact birth prompts.
The Scribe fixtures round-trip detached pending growth and birth rows, deep family arc/supporter state,
nested consumed ages, component-level missing/corrupt/oversized lists, and pre-cap ownership/admission recovery,
while the DLC-safety fixture drives the guarded growth/family/birth snapshot accessors and pins exact runtime
method signatures. `PawnDiaryBiotechDlcOffMaintenanceTests` constructs frozen plain-DTO owners in a
base-only loaded game and proves growth fallback, frozen canonical birth flush, removal, and replay
silence. `PawnDiaryRimTalkBridgeRuntimeTests` reflects over the actually loaded optional assemblies and
proves pair-owned growth-linked/birth-linked memories enter RimTalk shared memory without recursive
submission. It resolves the provider through RimTalk's registered `diary_shared` context variable and
verifies the bridge-owned `{{diary_shared}}` entry exists in the active prompt preset.
`DiaryPipelineTests` additionally renders B1 growth and birth through the important pair/solo shapes
at Full, Balanced, and Compact with an exhausted optional budget, requiring the central qualitative
facts while rejecting IDs/numeric tiers/ticks/correlation tokens. It parses the shipped templates to
pin both context-key projections and every English/Russian indexed label; the dev prompt fixture panel
ships localized synthetic growth/birth cases. The SpeakUp and RimTalk pure suites and bridge builds,
plus the loaded RimTalk runtime fixture, are the automated adapter smoke.
Vanilla letter and localized-preview visual presentation, the real cross-launch DLC on/off/on sequence,
live healthy/ill/stillborn/vat/surrogacy/ritual/naming/fail-open birth paths, a base-only launch, an
oldest-supported save, and one loaded-adapter UI observation remain manual acceptance items in
`tests/SAVE_COMPATIBILITY_SMOKETEST.md`. The postponed-owner, pre-cap, and DLC-off maintenance behavior
is automated but still requires its indicated in-game RimTest profile to be recorded as passed.

Biotech Phase 5 extends `DiaryBiotechPolicyTests` with assembly-free gene selection, transition, context,
and observation coverage.
It pins zero/one/two/four/many cardinality, the four-theme ceiling, added/removed delta priority over
unchanged membership, deterministic Def-name ties independent of input order, category diversity and
its XML exception, force/exclude corrections, duplicate post-mutation membership collapse, filtering
of hidden/inactive/suppressed/malformed rows, whitespace cleaning, and per-field/total text caps. The
suite also pins the explicit empty-baseline version, membership de-duplication/sorting/capping,
malformed-row normalization without accidental version upgrade, exact deterministic before/after
membership deltas, retained removed-gene facts, stable-Def-versus-localized-label identity rules,
XML minimum fallback significance, separator-injection resistance, bounded outer labels, frozen gene Scribe keys, and parses
the shipped category weights and primitive policy caps while asserting the empty
correction lists contain no unconditional DLC Def reference. New RimTest source exercises guarded real
`Gene`/`GeneDef` projection in both DLC branches, silent first/current observation, disabled-output
advancement, nested Scribe round-trip, missing-key version-zero normalization, a real vanilla
`GeneUtility.ReimplantXenogerm` mutation with same-call Ability ownership and replay silence, and a
real vanilla `GeneUtility.ImplantXenogermItem` mutation with bounded event-time context. The dev prompt
suite includes an exact reimplant fixture. An initial loaded RimWorld 1.6.4871 rev591 all-suite run
passed 217/218 fixtures and exposed the fixture's invalid immediate reimplant during vanilla's lethal
`XenogermReplicating` cooldown. The corrected fixture removes only the three temporary vanilla
reimplant hediffs before its unchanged replay. A later all-DLC run passed that replay and 227/228 tests
overall; its sole failure was a stale official-DLC expected-group matrix, now updated to include the
intentionally Biotech-gated `progressionXenotype` row.
Live no-DLC, minimal-mod Player.log, prompt, fallback, and save/reload evidence remains the separate
Phase 5 manual matrix.

The first Narrative N3-B identity slice proves the N2 xenotype-key fallback, the exact stable
`gene|<geneDefName>` identity key, bounded identity/gene topics, exact-subject selection, and ordinary
recent-history repetition reordering against a fresh applicable lens. `DiaryBiotechPolicyTests` pins
the new XML and English/Russian DefInjected prose. The real implant loaded fixture now also requires
the canonical page to persist the salient gene label, stable candidate key, exact pawn subject, and
`biotech_gene` source domain. The supplied all-DLC loaded run passed this real implant fixture.

Biotech Phase 6 adds pure mechanitor coverage for old-save versus empty baselines, numerical versus
custom mech names, the inclusive long-service boundary, significant-loss admission, first/second Tale
instigator roles, observation-time old-save tenure, completed-row cap recycling without active-row
eviction, and cross-controller exact boss-pawn assignment/defeat (including ambiguous legacy failure).
Shipped-XML assertions
pin all seven synthetic Def names, the Biotech package gate, combat role lists, tenure/caps, and every
English/Russian group and event key. `PawnDiaryBiotechMechanitorFlowTests` calls the real
`HediffSet.AddHediff/RemoveHediff` lifecycle and audits the verified relation, Tale, death, boss-call,
boss-spawn, and boss-defeat Harmony targets. It also drives a real disabled-output Overseer relation,
rejects same-faction combat as the first hostile milestone, verifies balanced nested death scope, and
proves unspawned starting mechlink add/remove callbacks update truth without emitting pages. The
relation fixture now uses vanilla's mech-side `Overseer` direction before asserting controller state.
The nested Scribe round-trip includes consumed flags, one mech tenure/loss row, and one boss
call/defeat row with its exact spawned pawn ID. The loaded fixture and manual matrix remain TODO.
The first user-supplied RimWorld 1.6.4871 all-DLC run reached 234/236 tests and exposed a missing exact
mechlink `mechanitor_moment` context field plus stale Royalty expectations in the official-DLC catalog
matrix. A follow-up 232/236 run confirmed the mechlink repair and showed that the frozen matrix needed
all four Royalty-gated groups (`ritualRoyal`, `progressionRoyalTitle`, `progressionPsylink`, and
`personaWeaponLifecycle`). It also exposed three false reflection failures: a pawn voice rule may
legitimately mention `[[speech]]` while forbidding it, so scanning the entire prompt could not prove
whether the dedicated instruction channel was appended. The fixture now asserts the empty adapter
channel, disabled template flag, and rejection of an injected instruction sentinel instead. The next
loaded run reached 235/236, confirming those corrections and exposing one final matrix-fixture
assumption: `ritualRoyal` intentionally classifies throne-speech and anima-linking rituals through four
narrow runtime tokens rather than exact def-name keys. Its token-only contract is now asserted beside
the equivalent Odyssey and Anomaly exceptions. The user subsequently confirmed the Phase-2 automated
loaded suite green. No Biotech B1 or Royalty Phase-2 manual acceptance row is closed by that result.
The first Phase-3 in-game run reached 240/241 and exposed the pre-dead Tale timing gate. After the
timing fix and two fixture-only Tale-batch query corrections, the focused rerun passed 244/244 on
2026-07-19. This closes automated loaded coverage, not the Phase-3 hands-on matrix.

Narrative N2-B extends `NarrativeContinuityTests` with assembly-free checks for family-continuity
classification, exact arc/subject candidate construction, fixed provider ordering, inactive-DLC and
unconnected-POV silence, unrelated-evidence rejection, and empty future-provider stubs. The normal
growth flow/RimTest path remains the adapter-level check that only the existing canonical page receives
the frozen context; no new hook, page owner, or save field is introduced. `DiaryBiotechPolicyTests`
also pins the family-state projection: zero-count exact parent rows supply baseline continuity, exact
activity supplies observed upbringing, and a prior recorded growth age on a child-only arc supplies
neither.

For `DiaryEventPromptKeys` and runtime Def resolution, test:

- precedence: exact source defName → matched group → classifier key → fallback domain;
- independent inheritance of `prompt`, `enhancement`, and `forcedModel`;
- Prompt Studio override winning only for the stable resolved key;
- blank override/XML fields falling through without erasing broader policy;
- unknown/modded defNames landing on a safe group/domain fallback.

### 4.2 Template matrix

Every shipped template receives one pure planner test and one prompt-only RimTest fixture:

| Templates | Required cases |
|---|---|
| `PairDefault`, `PairImportant`, `PairCombat`, `PairBatched` | Shape priority, relationship/weapon fields, hidden initiator text only where allowed, recipient follow-up, important/combat 200-token rules. |
| `SoloDefault`, `SoloImportant`, `SoloInternalState`, `SoloBatched` | Missing-group important fallback, internal marker selection, batch evidence, important/combat token rules. |
| `SoloDayReflection`, `SoloQuadrumReflection`, `SoloArcReflection` | Marker priority, reflection fields, direct-speech omission, 350/420-token limits, current intentional absence of `important context` for quadrum/arc. |
| `ArrivalDescription`, `DeathDescription` | Neutral role, boundary facts, no voice/enchantment/humor, safe fallback text. |
| `Title` | Entry-only body, no voice/context side channels, 40-token cap, bad-title fallback. |

### 4.3 Rendered prompt contract

For each template, snapshot normalized `systemPrompt`, `userPrompt`, response rules, template key, and
forced model. Assert fields by semantic labels rather than whole translated paragraphs where possible.
Whole-prompt golden files are limited to one canonical case per template and normalized for pawn names,
dates, and IDs.

Required cross-template assertions:

- empty and `none`/`n/a`/`unknown` fields are omitted;
- XML field order is preserved and duplicate labels are rejected;
- group instruction/tone stable variants are deterministic per event but rotate across event IDs;
- direct-speech instruction appears only for eligible social shapes;
- quest/imported text is sanitized and capped while owned XML/Keyed prompt text is not sentence-capped;
- `Full`, `Balanced`, and `Compact` keep mandatory fields, cut whole optional fields, report reasons,
  and honor per-lane context-detail overrides;
- prompt-only mode stores exactly the prompt the runtime would send and never enters `LlmClient`.

## 5. Prompt Enchantments, Voice Overrides, Humor, And Forced Models

### 5.1 Prompt enchantments

1. Exhaustively validate every loaded enchantment Def: unique identity, valid source, matchers,
   chance/weight ranges, severity-tier inheritance, nonblank rule, and DLC-safe string references.
2. Pure tests cover chance, live-severity weight, capacity weight, normal multipliers, weighted pick,
   cue cap/order, zero/negative candidates, and deterministic seeds.
3. Live collection tests cover representative hediff, capacity, Royal title, Ideology role,
   event-window, and observed-condition candidates.
4. Verify normal candidates are multiplied before extra candidates are added.
5. Verify every hediff matched by the active hediff writing-style override is suppressed from the
   enchantment pool, but suppression stops when an external style override wins.
6. Verify template gates and missing template fields: neutral/title produce no candidate; quadrum/arc
   may select a candidate but intentionally cannot render `important context` in current XML.

### 5.2 Writing style and psychotype

Cover the complete writing-style precedence chain with real pawn state:

`external override > hediff override > pawn custom > base Def`.

At every transition, capture a prompt and assert the effective rule and source metadata, then remove
the winning layer and assert the next layer becomes effective without changing the saved base picker.
Also cover sanitization, source-owned reset, load-order arbitration, missing Def fallback, child/adult
catalogs, pinned vs unpinned crystallization, and the psychotype chain
`external override > pawn custom > base type`.

The combined system block must always order psychotype lens before writing-style mechanics. Labels and
rules must never leak into the user prompt. Neutral arrival/death and title prompts must include none
of these voice layers.

### 5.3 Humor

Test the base/elevated/reduced/conflicting temperament multipliers, Social passion, light vs gallows
cue selection, template eligibility, cue weighting, stable event+POV seed, and `Rand` non-mutation.
Live tests add representative traits/passions to a temporary pawn and capture prompts at forced
effective chance `0` and `1`; no probabilistic retry loops are allowed.

### 5.4 Forced models and lane-specific prompts

Test exact model matching, trimming/case contract, blank/unknown/disabled forced models, and normal
routing fallback. Then test the full runtime behavior with three temporary lanes:

- a forced active model wins regardless of normal routing mode;
- a failed/cooling forced lane falls through to normal/failover lanes;
- pair recipient and title follow-ups pin to the previous successful lane when available;
- each lane receives the pre-rendered `Full`/`Balanced`/`Compact` variant configured for it;
- retries within a lane reuse the same prompt variant, while failover switches variants;
- recorded endpoint/model metadata matches the lane that actually succeeded.

## 6. Runtime Flow

### 6.1 Startup and Harmony

- Assert required patches are present once with Pawn Diary's Harmony owner ID.
- Assert fragile optional targets either patch successfully or log one warning and remain inert.
- Assert the hidden Diary tab is registered on humanlike pawn/corpse defs without duplicates.
- Run a base-game-only mod list and an optional-mod matrix to catch type-load failures.

### 6.2 Dispatch, storage, and retention

- Guard rejects main-menu/world-generation events.
- Starting arrivals flush before the first non-arrival signal.
- Source and generic dedup check/mark ordering is preserved, including zero-window behavior.
- Solo, pair, neutral, batch, ambient, and fan-out shapes create the correct repository row and
  per-pawn refs atomically.
- Event limits compact only eligible completed/stale POVs; pending rows survive; shared pair events
  remain hot until every POV is safely archived.
- Continuity/opener/previous-ending scans use hot bounds as documented and ignore compact archive
  rows except arc-memory selection.

### 6.3 Queue, transport, and result application

Using scripted loopback responses, test:

1. Solo success: event → queued → HTTP → parsed prose → completed status → unread flag → title.
2. Pair success: initiator completes first; recipient receives hidden initiator text and same lane.
3. Neutral arrival/death success and title-free/voice-free behavior.
4. Prompt-only mode stops before any socket request.
5. Transient error retry, `Retry-After`, exponential lane cooldown, and eventual success.
6. Permanent error, malformed JSON, empty response, timeout, and all-lanes-failed status/error text.
7. Failover order, Failover-only routing, Prefer-top, Balanced/round-robin, per-lane concurrency.
8. Duplicate enqueue suppression and event-role in-flight tracking.
9. `BeginSession` cancels stale work, clears result queues/cooldowns as specified, and ignores late
   results from the previous game.
10. Orphaned pending work requires two scans before requeue and never duplicates a real in-flight request.
11. `GameComponentUpdate` applies completed work while paused; no background thread touches Verse,
    translation, pawn, Def, or saved event state.
12. Title rejection/fallback and missing-title catch-up run only on documented triggers.

### 6.4 Save/load

Create a disposable save fixture containing:

- completed solo and pair pages;
- pending, failed, prompt-only, neutral arrival/death, and titled/untitled pages;
- hot and compact archived POVs;
- N1 per-POV Narrative Continuity evidence, references, selected keys, and frozen optional context;
- active event window and observed condition state;
- progression and arc schedule state;
- generated Social-log speech compatibility fields.

Phase A writes and saves the fixture. Phase B reloads it and verifies Scribe-key preservation,
non-null normalization, repository/diary/archive index rebuilds, pending-status normalization,
generation catch-up bounds, no duplicate arrivals/conditions, retention, and successful removal of all
test markers. N1 additionally proves old-save empty defaults, archive arc/subject index rebuilding and
retention, Full/Balanced/Compact core-fixture selection, and prompt-field omission when no context was
selected. Run the same fixture with a copy of the oldest supported save in
`SAVE_COMPATIBILITY_SMOKETEST.md`.

The Odyssey process-boundary subset is implemented as three named RimTests because RimTest Redux's
synchronous runner is disposed with the current `Game` and cannot safely continue one test method
through `GameDataSaveLoader.LoadGame`. Phase A uses real `TravelTo`, seeds bounded history/trust/home
tenure/launch cooldown, captures one transient pending landing, and writes
`PawnDiary_Odyssey_RimTest_PhaseA_Active`. Phase B must be run after manually loading that save; it
verifies the two frozen save keys, newest-row retention, and the intentional absence of non-scribed
takeoff/pending state, completes the real Harmony landing finish exactly once, then writes
`PawnDiary_Odyssey_RimTest_PhaseB_Completed`. Phase C must be run after loading the completed save; it
proves one page/marker survived the second reload, no transient/active journey resurrected, and stale
completion cannot add output. Phase C deletes both named disposable saves and all fixture-owned live
state. The exact continuation is in `tests/SAVE_COMPATIBILITY_SMOKETEST.md`.

The focused 2026-07-17 runtime run used RimWorld `1.6.4871 rev591`, English, and isolated base-only
and Odyssey-only mod profiles. Base-only startup produced all five explicit
`ModsConfig.OdysseyActive == false` runtime skips without Pawn Diary patch/XML/type-initializer
errors. The Odyssey profile passed the real cancellation/full-lifecycle fixtures and Phase A/B/C
continuation; Phase C confirmed one durable page/marker, no resurrected lifecycle state, and deleted
both reserved saves. Startup also demonstrated that an Odyssey-enabled main menu has no
`Current.Game`/component, so the runtime fixture now checks those hosts before using `Find` or
instance-field reflection. The post-fix live main-menu rerun produced all five intended skips with no
Odyssey suite error, closing the runtime-suite acceptance gap. That all-suite run separately exposed
EVT-22's Anomaly-inactive helper contradiction; the monolith fixture now requires loaded/enabled Defs
without requiring their package before it asserts the package-gated no-op branch.

## 7. Settings, Integration API, UI, And Compatibility

### 7.1 Settings and external API

- Every master/group toggle blocks the matching capture route and restores without writing settings.
- Prompt/persona/psychotype preset overrides round-trip and keep stable keys.
- API lane import/normalization, authentication modes, key redaction, and capability snapshots agree
  with actual queue behavior.
- External context providers/listeners are ordered, bounded, exception-isolated, and session-safe.
- Every public `PawnDiaryApi` call has null/ineligible/valid cases and an in-game persisted outcome.

### 7.2 UI/view model

Automate non-visual UI contracts: year indexing, hot/archive dedup, visibility filters, unread/pending
command status, link navigation, Social-log click routing, corpse/resurrection lookup, and entry-role
selection. Extract any deterministic sizing/paging decision into pure helpers before testing it.

Keep a short manual matrix for actual rendering at small/large resolutions, UI scale changes, Tiny
text accessibility toggle, long translated labels, large histories, scroll boundaries, prompt debug
details, writing-style editor state, and right-to-left/long-string stress. Manual checks must use mock
pages in a disposable save and attach screenshots to the release checklist.

### 7.3 DLC and optional mods

Run at least these configurations:

1. Base game + Harmony + RimTest Redux + Pawn Diary only.
2. All official DLC active.
3. Each official DLC independently where practical.
4. Optional adapter capability present/ready and present/not-ready.
5. Representative compatibility XML mods for package gating.

The base-only run is release-blocking. It verifies all DLC trackers are absent/null, DLC match strings
stay inert, no unsafe `GetNamed` lookup occurs, and every conditional test reports “not applicable” as
a separate suite result rather than failing or silently pretending to execute DLC behavior.

`PawnDiaryDlcSafetyFixtureTests` automates the shared compatibility boundary in every configuration:
null-pawn guards even when DLC flags are active; final prompt/public-summary omission when a DLC is
absent; positive non-Baseliner, royal-title, player-ideoligion/precept/eligible-role, and temporary
vanilla CreepJoiner-tracker state backed by a real loaded form when installed; exact set equality for all official-DLC interaction
groups and event windows; exact classifier keys for specialized ritual families plus the deliberate
`PsychicRitual` and gravship-launch token classifiers; `ModsConfig`/package-helper/settings-visibility
agreement; fragile
Biotech growth/birth,
Anomaly monolith/corpse, and Ideology/Anomaly ritual signatures; and optional-adapter capability
ready/not-ready fail-open behavior. A new official package-gated Def must update that exact catalog
fixture in the same change. True disable/re-enable save transitions still require separate game
launches and remain in `tests/SAVE_COMPATIBILITY_SMOKETEST.md`.

Loaded prompt tests also preflight the shipped template contract. A missing required B1 field is
reported as a stale/mixed-install failure because duplicate Pawn Diary packages can combine Def XML
from one copy with the current RimTest DLL from another; release runs must have exactly one core copy
active.

Canonical B1 birth coverage pins `tale=BiotechFamilyBirth` in the formatted context and requires the
persisted page to resolve as important. This guards the domain-classification boundary that selects
the PairImportant/SoloImportant templates; retaining outcome fields in XML alone is insufficient if
the saved event is accidentally recovered through the Interaction domain.

## 8. Implementation Order

| Phase | Deliverable | Exit gate |
|---|---|---|
| 0 | Shared `PawnDiaryRimTestScope`, test IDs, cleanup auditing, and test-suite README | Existing three reaction tests use the harness and leave zero marked state after forced failure. |
| 1 | EVT-01–EVT-12: direct pawn/social/health event flows | Every non-map base-game event has positive, negative, and dedup coverage. |
| 2 | EVT-13–EVT-23: map fan-out, scanners, reflections, windows, external API | Every `DiaryEventType` and every bus-bypass page source has a real loaded-game test. |
| 3 | Prompt policy, all template fixtures, field/golden contracts, context-detail matrix | Every shipped template key and policy fallback resolves and renders in pure + prompt-only tests. |
| 4 | Enchantments, voice precedence, psychotypes, humor, forced-model routing | Every side-channel layer has pure, live collection, precedence, and omission coverage. |
| 5 | Loopback transport and asynchronous runtime suite | Queue/retry/failover/apply/title/session branches pass without internet. |
| 6 | Two-phase save/load, retention, old-save compatibility | Disposable save round trip and oldest-supported fixture pass with no orphan refs. |
| 7 | Integration API, UI/view model, DLC/optional-mod matrix | Base-only and all-DLC runs pass; manual visual checklist signed off. |
| 8 | Verification integration and coverage audit | One command builds all test projects, builds RimTest, checks XML, and prints the requirement matrix with no uncovered rows. |

Do not implement all phases in one change. Each phase should be a reviewable change with the smallest
necessary production seam, corresponding docs/changelog updates, core + RimTest builds, relevant pure
tests, and an in-game result log. Event tests come before transport/UI tests because every later suite
depends on trustworthy event fixtures and cleanup.

## 9. Completion Gate For Every Phase

- Tests fail when the production hook/policy branch is deliberately disabled.
- No test depends on a real-time storyteller event, random retry-until-success, or internet service.
- A deliberately failing assertion still leaves no test pawn/event/log/settings/queue state.
- Pure tests remain free of RimWorld/Verse/Unity references.
- RimTest stays in the separate conditionally loaded assembly.
- Core Debug build and RimTest build succeed.
- Relevant standalone tests and XML parsing succeed.
- The in-game runner log records suite/test names, durations, and pass/fail details.
- `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, `CHANGELOG.md`, and this matrix agree.
