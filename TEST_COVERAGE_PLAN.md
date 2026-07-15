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
| EVT-15 | Pawn progression | Mutate skill passion milestone, trait, and optional DLC state then scan | First-scan baseline, only upward/new milestones, exact context, major-change arc request. |
| EVT-16 | Quest | Accept/end controlled quest and run recovery scan | Accepted bookkeeping/no page, completed/failed fan-out, placeholder-label sanitation, duplicate scan drop. |
| EVT-17 | Ritual | Complete controlled Ideology/psychic ritual when DLC active | Participant role/perspective fan-out and context; all 16 installed Anomaly classifier keys resolve to six exact package-gated families, unknown psychic rituals reach the generic fallback, and the no-DLC path is clean. |
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
