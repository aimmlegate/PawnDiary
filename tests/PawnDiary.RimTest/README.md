# Pawn Diary — in-game RimTest suite

This folder is the **loaded-game** test assembly for Pawn Diary. Unlike the standalone pure-logic
projects under `tests/`, these suites run *inside RimWorld* through the optional
[RimTest Redux](https://steamcommunity.com/sharedfiles/filedetails/?id=3762405308) development mod, so
they can drive real vanilla APIs and Harmony hooks and assert the `DiaryEvent` the production pipeline
actually persists.

The local, untracked `design/TEST_COVERAGE_PLAN.md` is the long-form roadmap. This tracked README is
the operator's guide for the assembly itself.

## Why a separate assembly

`PawnDiary.dll` must never take a runtime dependency on a test framework. So these tests compile into a
standalone `PawnDiary.RimTest.dll` that:

- is exposed to the game by `LoadFolders.xml` **only when** package `ilyvion.rimtestredux` is active;
- references the real `PawnDiary.dll` and reaches its `internal` members via
  `[assembly: InternalsVisibleTo("PawnDiary.RimTest")]` (see `Source/Properties/AssemblyInfo.cs`);
- is excluded from the shipped Workshop payload along with the rest of `tests/`.

## Build

Build the core mod first, then this assembly:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
MSBuild tests\PawnDiary.RimTest\PawnDiary.RimTest.csproj /t:Build /p:Configuration=Debug
```

The second command writes `tests/PawnDiary.RimTest/Assemblies/PawnDiary.RimTest.dll`. The project
finds RimTest Redux relative to `RimWorldManaged`; if the framework lives elsewhere pass
`/p:RimTestReduxAssemblies=<path>` or set the `RIMTEST_REDUX_ASSEMBLIES` environment variable.

## Run (manual, in-game)

1. Enable **Harmony**, **RimTest Redux**, and **Pawn Diary** (the `About.xml` load-order hint already
   asks the game to load RimTest Redux first; it is not a player dependency).
2. Launch RimWorld. Open **Mod Options → RimTest Redux → Open Test Runner**.
3. `PawnDiaryDefSmokeTests` is read-only and can run at the **main menu**.
4. `PawnDiaryEventReactionTests` needs a **loaded game** — start or load any colony (a throwaway one is
   fine; the suite never touches the player's colonists) and run it there.

## Suites

`PawnDiaryDefSmokeTests` (read-only, main-menu safe) checks Def registration. Loaded-game event-flow
suites map to `design/TEST_COVERAGE_PLAN.md §3` EVT rows; supplemental B1 suites exercise Biotech composite
owners. Both use the shared harness:

| Suite | EVT | Covers |
|---|---|---|
| `PawnDiaryEventReactionTests` | 01/07/08 | Interaction pair (PlayLog), romance (relation), mental state. |
| `PawnDiaryQualityWavePhase2FlowTests` | Quality Wave A5 | Event-time per-POV identity context, prompt projection, partner exclusion, and historical-boundary safety. |
| `PawnDiaryQualityWaveMoodFlowTests` | Quality Wave B2 | Eligible event-time mood capture and real SoloInternalState prompt projection. |
| `PawnDiaryInteractionBatchFlowTests` | 02 / Quality Wave B2 | Interaction batch/ambient accumulation + flush and most-extreme event-time mood retention. |
| `PawnDiaryThoughtFlowTests` | 03 | Thought memory immediate/ambient route. |
| `PawnDiaryThoughtProgressionFlowTests` | 04 | Thought-stage progression baseline/worsen/repeat. |
| `PawnDiaryInspirationFlowTests` | 05 | Inspiration solo page + group gate. |
| `PawnDiaryAbilityFlowTests` | 06 | Ability activation caster/target facts + chance gate + dedup. |
| `PawnDiaryTaleFlowTests` | 09 | Tale single/pair shape + participant extraction + group toggle. |
| `PawnDiaryDeathFlowTests` | 10 | Neutral death page + cross-source dedup. |
| `PawnDiaryHediffFlowTests` | 11 | Hediff immediate vs day-signal + body-part markers. |
| `PawnDiaryWorkFlowTests` | 12 | Work passion/chore/dark-study facts + same-work suppression. |
| `PawnDiaryRaidFlowTests` | 13 | Raid per-colonist fan-out + colony dedup + bypass classes. |
| `PawnDiaryMoodConditionFlowTests` | 14 | GameCondition mood fan-out + classification + group gate. |
| `PawnDiaryPawnProgressionFlowTests` | 15 | Skill/trait milestone baseline + upward-only + arc request; installed-Royalty psylink/title scanners; guarded/versioned Biotech gene projection, rich fallback, real implant/reimplant hooks, same-call Ability ownership, replay silence, and the N3-B salient identity lens/stable repetition key. |
| `PawnDiaryBiotechMechanitorFlowTests` | B6 | Real spawned mechlink install/removal pages, silent unspawned starting-state callbacks, vanilla mech-side Overseer ownership, combat gating, and Harmony registration audits for mechlink, relation, Tale, pre-cleanup death, boss call, and boss defeat seams. |
| `PawnDiaryRoyaltyFlowTests` | R2/R3/R5/R8 | Real persona-weapon coding/formation, silent late-visible baseline adoption, live-context invalidation after `UnCode`, exact persona/title/succession/appointment Harmony targets plus caught-failure diagnostics, synthetic structural/malformed modded traits through the live adapter, policy-isolated reversible long-time-skip reconciliation, real persona-kill cache reset, major-threat milestone ownership, delayed companion-Tale flush, one-shot kill-Thought fallback, disabled output, and primary/non-primary wielder death. |
| `PawnDiaryRoyaltyProgressionFlowTests` | R4/R5/R8 | Real title/psylink owner and fallback flows plus repeat-safe direct-mutation title-loss fallback, strict one-page inheritance, titleless instant-intermediate ownership, delayed terminal claim/retirement, equal-or-higher silence, bestowing/title dedup, and explicit `ChangeRoyalHeir` appointment with automatic assignment silent. Fixtures that exercise succession ID resolution or the component-wide pre-save scanner spawn their disposable heir/writer so production's live-colonist roster can find it. |
| `PawnDiaryRoyaltyStateFixtureTests` | R1/R3/R5 save | Real-Scribe persona/title/committed-succession state, nonempty component-ledger and old-expiry migration, legacy/missing markers, distinct observed/recorded milestone flags, transient load reset, and guarded no-Royalty collectors/hooks/scopes. |
| `PawnDiaryRoyaltyPermitFlowTests` | R6/R8 | Exact permit/incident hook audits, real reviewed-family success and routine exclusions, target/cancel/repeat silence, quick-aid ownership/fallback/reset, cap-safe owner lookup, and synthetic unknown/malformed permit Defs through the real tracker and success callback. |
| `PawnDiaryRoyalAscentFlowTests` | R7 | Real Royal Ascent Accept/End ownership, stable-witness/default fanout, prompt pressure and journey evidence, Scribe migration, reset, package visibility, and master/Royalty-off no-op behavior. |
| `PawnDiaryBiotechGrowthFlowTests` | B1 | Family-keyed canonical growth/N1 evidence, baseline/fallback, real age-7/10/13 growth-letter hooks, multiple passions, nickname/responsibility changes, auto-resolution, postponed-owner Scribe recovery, live pre-cap admission, and loaded detail-preset prompts. |
| `PawnDiaryBiotechBirthFlowTests` | B1 | Canonical two-adult birth, Tale-domain/important-group routing, child-never-POV shape, delayed naming flush, replay rejection, live pre-cap admission recovery, loaded detail-preset prompts, and a loaded-template preflight. |
| `PawnDiaryBiotechComponentStateFixtureTests` | B1 | Real-Scribe component keys, old/malformed/oversized rows, hard ceiling, and pre-cap admission recovery. |
| `PawnDiaryBiotechDlcOffMaintenanceTests` | B1 | Base-only loaded maintenance of frozen growth/birth owners, ordinary Birthday/canonical birth release, pruning, and replay silence. |
| `PawnDiaryAnomalyStateFixtureTests` | A1.1/A2.0 save | Actual seven-key component Scribe round-trip, missing-key legacy defaults, independent deep monolith/creepjoiner rows, guarded active-DLC baselines, DLC-off deferral, and transient load reset. |
| `PawnDiaryCreepJoinerFlowTests` | A2.0/A2.1 | Exact optional hook registration, canonical/repeated arrival continuity, real rejection/aggression/departure and surgical-inspection calls, exact Tale ownership, writer roles, repeat/no-op silence, and lifecycle reset. |
| `PawnDiaryGhoulTransformationFlowTests` | A2.2 | Exact optional recipe registration/no-DLC gate, real successful/failed infusion, already-ghoul and disabled-output fallback, exact pair/solo POVs and preverified diary refs, exception/unscoped Tale release, A2.1 scope exclusion, save/no-replay, and later ordinary injury batching. |
| `PawnDiaryRimTalkBridgeRuntimeTests` | B1 adapter | Reflection-only smoke against the actually loaded RimTalk + bridge assemblies: registered context-variable resolution, active-preset auto-entry attachment, and pair-owned growth/birth-linked shared memory without duplicate or recursive Pawn Diary submission. |
| `PawnDiaryDlcSafetyFixtureTests` | 7.3 | Null/base-only omission, installed-DLC positive pawn state (including a temporary vanilla CreepJoiner tracker with a real loaded form), exact specialized/generic-fallback classifier policy, official package/group/window/settings matrix, fragile hook signatures, and optional-adapter fail-open readiness. |
| `PawnDiaryOdysseyJourneyFlowTests` | O1.2–O1.5/N2-O | Loaded Odyssey policy/context/Scribe repair, exact lifecycle hooks, idempotent intent→travel state, exact-onboard provider snapshots, writerless landing rollback, one canonical two-POV major landing, eligible-writer routine-hop suppression, `TileSettled` non-ownership, and Full/Balanced/Compact localized prompt fixtures. |
| `PawnDiaryOdysseyRuntimeLifecycleTests` | O1 runtime/save | Real Harmony payloads through the public takeoff/landing entry points, real vanilla cross-layer `TravelTo` and successful `LandingEnded`, cancellation/replay cleanup, plus the manual-boundary Phase A/B/C disposable-save flow. |
| `PawnDiaryQuestFlowTests` | 16 | Quest accept/complete/fail fan-out + label sanitation + dedup. |
| `PawnDiaryRitualFlowTests` | 17 / Ideology P2 ritual | Ideology/Anomaly four-perspective production fan-out through internal fact fixtures, pawn-ID uniqueness, colony dedup, context/localization, and DLC-safe fields; exact completed-conversion classification/settings, installed fully-qualified worker identities, real patched target mutation, POV isolation, event-time/Scribe retention, quality non-proof, role-only missing evidence, and spectator-safe fail-open behavior. |
| `PawnDiaryIdeologyPhase1FixtureTests` | Ideology P1/N3-I | Exact source-precept/history evidence, deterministic typed-precept thought capture through real `TryGainMemory`, cached policy, enrichment failure isolation, and real approved/disapproved body-mod situational workers plus canonical N3-I re-stamping through `AddHediff`. |
| `PawnDiaryIdeologyPhase2InfrastructureTests` | Ideology P2 | Real mutation hooks/cache, exact conversion/reassurance/crisis consumers, both deterministic real Counsel success subbranches plus failure, one-page ability/thought ownership and RNG preservation, XML context/prompt selection, legacy-setting inheritance, failure isolation, and DLC-off ordinary fallback. |
| `PawnDiaryIdeologyPhase3BeliefStateTests` | Ideology P3 | Page-silent first baseline, real tracker certainty accumulation, XML scan work cap, inactive-Ideology reset, and dev-safe mechanical diagnostics. |
| `PawnDiaryDiaryTabFilterFixtureTests` | UI lifecycle | Hidden-panel pawn reset and year-specific tag reset without invoking immediate-mode rendering. |
| `PawnDiaryOnThisDayDividerFixtureTests` | UI lifecycle / Quality Wave H5-UI | "On this day" divider against the live clock: `DayIndexForGameTick` offset semantics, tick/printed-date year agreement, anniversary match plus fail-closed dev-mock/corrupt-tick/wrong-year variants, current-year and undated gating, and both localized label forms. |
| `PawnDiaryArrivalFlowTests` | 18 | Neutral arrival page + first-ordering + bootstrap resilience. |
| `PawnDiaryDayReflectionFlowTests` | 19 / Quality Wave H3 + H5-prompt | Day/quadrum reflection highlight, once-per-day guard, evidence consumption, arrival-bounded colony-news ownership, and deduplicated same-season memory across hot/archive history. |
| `PawnDiaryArcReflectionFlowTests` | 20 | Arc reflection year/gap limits + memory filter/dedup + backoff. |
| `PawnDiaryExternalApiFlowTests` | 21 | `PawnDiaryApi` submit solo/pair, group gate, budget, listener notify. |
| `PawnDiaryEventWindowFlowTests` | 22 | Event-window start/end/one-shot/timeout + prompt-bias state; exact monolith ownership when Anomaly is active and inert loaded Defs when it is absent. |
| `PawnDiaryObservedConditionFlowTests` | 23 | Observed-condition start/end debounce + scope identity + restart cooldown. |
| `PawnDiaryArtImmortalizationFixtureTests` | 24 | Patched art/reflection surface, exact colony-wide ownership, diary fallback, and Def/localization wiring. |
| `PawnDiaryAnniversaryFlowTests` / `PawnDiaryAnniversaryFixtureTests` | 25 | Birthday/arrival/loss/record milestones, silent baseline and ownership, save normalization, live relation ordering, and Def/localization wiring. |
| `PawnDiaryDigestPacingFlowTests` / `PawnDiaryDigestPacingFixtureTests` | 26 | Low-salience daily allowance, pair semantics, digest buffering/consumption, rollover, save normalization, shipped classification, and tunables. |
| `PawnDiaryRepositoryRebuildFixtureTests` | save/index | Real-Scribe event/archive/memory round trips, transient-index rebuilds, detached component re-ID/retention/reference-prune integration, memory-row repair/removal, and first-post-load replay idempotency. |
| `PawnDiaryRngIsolationFixtureTests` | RNG boundary | Stable generation and one-shot capture adapters preserve the outer `Verse.Rand` stream; stable seeds reproduce while reroll salt can change the candidate. |

The first A2.2-expanded loaded run reached 342/347. Its two production failures shared the corrected
post-transformation subject-indexing gap; the other ghoul failures expected a pair from surgeon-only
exceptional fallback and an immediate page from delayed `Wounded` batching. The remaining Biotech
failure assumed the implanted GeneDef must always be N3-B's leading salient changed gene. The fixtures
now follow the actual production contracts; the user-confirmed corrected rerun passed 347/347.

The reimplant replay fixture deliberately completes vanilla's temporary loss-shock, gene-regrowth,
and recipient-coma cooldowns before calling the real public method again. An immediate raw replay is
not a valid silence check: vanilla makes a second `XenogermReplicating` application lethal, which
correctly routes through Pawn Diary's death fallback instead of indicating a duplicate gene page.

Do not run the prompt suites with two copies of Pawn Diary active. RimWorld can load Def XML from one
copy and `PawnDiary.RimTest.dll` from another, producing a test binary/XML contract that no single
checkout contains. The Biotech birth prompt suite checks its loaded `PairImportant` fields up front
and reports this condition explicitly; disable stale Workshop/Modmixer/development copies before
rerunning.

## Suite-owned cleanup & known limitations

The harness auto-cleans and leak-audits every **pawn-id-keyed** store (events, diaries, dedup/command
keys, interaction-batch/ambient stores, thought-progression, day-hediff, day-reflection). Some event
sources use stores that are **not** pawn-scoped, so the suites that touch them clean their own state via
`scope.RegisterCleanup` and the harness audit does not cover them. Folding these into the harness is a
tracked follow-up:

- **Colony fan-out dedup keys** in `recentEvents` (raid/quest), e.g. `raid|<def>|<mapIndex>|…` — no pawn id.
- **`activeEventWindows`** rows (window/map-scoped) — EVT-22 cleans by `windowDefName`.
- **`activeObservedConditions`** + `observedConditionCooldownUntilTick` — EVT-23 cleans by `subjectPawnId`.
- **`knownAcceptedQuestIds`** (`HashSet<int>` by quest id) — EVT-16 cleans the ids it added.
- **`delayedRaidGenerationReadyTicks`** — EVT-13 avoids it by forcing the generation delay to 0.

**Run these two on a disposable colony:** their real trigger has vanilla side effects no test can undo.
- `PawnDiaryDeathFlowTests` (EVT-10): a real `Pawn.Kill` gives *other* colonists `ColonistLost`/`KnowColonistDied` mood memories and may raise a death letter.
- `PawnDiaryRaidFlowTests` (EVT-13): drives the per-colonist raid signal for the isolated test pawn, but the colony-dedup and fan-out contract are checked against the live map; a full end-to-end fan-out would write pages into real colonists' diaries, so it is intentionally not driven.

Run `PawnDiaryRoyaltyFlowTests` on a disposable Royalty colony as well. Its death fixtures call real
`Pawn.Kill`, and its adversarial ownership assertions explicitly flush all pending Tale batches.

Run `PawnDiaryOdysseyRuntimeLifecycleTests` on a disposable Odyssey colony too. Its test objects and
component/controller mutations are failure-safely removed, and the launch/landing visual originals are
suppressed, but real `TravelTo` and `LandingEnded` briefly mutate vanilla world/controller state. The
suite refuses to compete with an active or parked player gravship. Its Phase A/B/C process-boundary
steps and reserved save names are documented in `tests/SAVE_COMPATIBILITY_SMOKETEST.md`. If RimTest's
run-at-startup option enters this loaded-game suite at the main menu, every runtime/phase fixture logs
an explicit skip before dereferencing `Find` or the absent `DiaryGameComponent`.

### Writing fixtures against a live colony

These suites run inside whatever colony the developer loaded, at whatever tick it is paused on. Three
assumptions look safe and are not — each one cost a red suite on the 2026-07-25 run:

- **`AddDirectRelation` silently refuses implied relation defs.** Among blood relations only `Parent`
  has `implied=false`; `Sibling`, `Child`, `Grandparent`, … are derived by workers from shared parents.
  Passing one logs a *Warning* (which the runner does not fail on) and leaves the pawns unrelated, so
  the test fails much later with a confusing message. Build a named-relation fixture from
  `PawnRelationDefOf.Parent`, or give both pawns the same mother *and* father.
- **Never assume a large `Find.TickManager.TicksGame`.** A fresh test colony can be on day 0 below tick
  200, where `GameTickForDayIndex(today)` is at or under 0. Lay fixture ticks out as a compact ordered
  ladder just below `now` — the collectors compare order, never distance — instead of `now - 100`. The
  compact ladder also keeps the rows inside RimWorld's `scanBack` archive cap on a long save.
- **The colony-news collector reads the developer's real `Find.Archive`.** The scope scrubs a fixture
  pawn's automatic arrival page *and* its `status.faction.joined` knowledge record, so until a test
  seeds a boundary that pawn's news window is the whole day and a letter the colony filed today becomes
  a legitimate extra candidate. Two consequences: a test asserting an exact `highlights=`/`candidates=`
  count must switch the collector off (`DiaryContextReactions.ForKey(ColonyNews).enabled = false`,
  restored in cleanup) rather than hope the archive is empty; and a test about *exclusion* must assert
  its own seeded label is absent, never that the page is news-free. Seed the boundary, then assert the
  production reader actually returns it (`FirstArrivalTickFor` takes the **minimum** across the hot
  events, the archive, and the knowledge record — any survivor silently widens the window).
- **The clock does not advance while the runner works, so every letter an earlier suite raised is
  archived at exactly the current `TicksGame`.** A real psylink gain, inspiration, or death from
  another fixture therefore lands *newer* than any tick this run can seed, and `Archive.Add` keeps the
  list sorted by creation tick — so a newest-first scan reaches that leftover before the seeded row.
  A fixture that asserts *which* letter won must first lift the non-pinned letters at or after its
  window start out of `Find.Archive` and restore them in cleanup (`ReserveNewestLetterSlot`).

Related: assert optional enrichment conditionally. The N3-I narrative fact on a belief reflection only
exists when the pawn's live ideoligion resolves a high-confidence precept stance;
`PawnDiaryIdeologyPhase1FixtureTests` owns the deterministic coverage by building its own `Ideo` with a
`PreceptComp_Thought`.

### Transport / async runtime (plan §6.3) — deferred by design

The `LlmClient` queue/retry/failover/`Retry-After`/session/result-apply suite is **not** implemented here,
on purpose:

- Its socket-free boundary is already covered — the prompt-capture fixtures assert that Prompt Test Mode
  renders and stores the prompt and marks the POV `prompt_only`, i.e. the pipeline stops before any
  `LlmClient.Enqueue`.
- The Harmony wiring `§6.1` asks about is proven transitively — the 20 event suites cannot produce a
  `DiaryEvent` unless the base-game choke-point patches (`PlayLog.Add`, `TryStartMentalState`,
  `RecordTale`, `RegisterCondition`, …) are live.
- The remaining transport internals cannot be exercised **safely** from an in-game test: `LlmClient` is
  static and session-global, so calling `BeginSession()` / `Enqueue()` cancels or races the *player's*
  real in-flight generation (an unrestorable side effect), and there is no injectable request-executor.

Doing it right needs a separate, reviewed production change: either a bounded in-game loopback HTTP
endpoint pointed at by test-only lanes, or a narrow internal request-executor interface in `LlmClient`
(production stays HTTP; the test double lives in this assembly). That belongs in its own PR, not a
blind edit to the transport core — see `design/TEST_COVERAGE_PLAN.md §2.2 / §6.3`.

## The shared harness — `PawnDiaryRimTestScope`

Every loaded-game test needs the same fragile scaffolding, and getting the cleanup wrong strands test
pawns and diary rows in the developer's live colony. `PawnDiaryRimTestScope` owns all of it so a test
body only fires a trigger and asserts an outcome:

```csharp
[BeforeEach] SetUp()  → scope = PawnDiaryRimTestScope.Begin("heartfelt", ...);
                        firstPawn = scope.CreateAdultColonist();
[Test]                → var e = scope.FireAndRequireEvent(() => Find.PlayLog.Add(row), "DeepTalk", a, b);
                        scope.RequirePairRefs(e, a, b);
[AfterEach] TearDown()→ scope.TearDown();   // restores everything, then audits for leaks
```

What a scope owns and restores:

- **Isolated pawns** that can never fire an LLM request: each is created factionless, has its diary
  record made with generation **disabled**, and is only then turned into an eligible colonist.
- **Settings** it changed (the per-group enable flags), snapshotted and restored verbatim.
- **RNG**: `Rand.PushState()`/`PopState()` bracket the whole scope so nothing the fired events roll
  perturbs the player's game stream.
- **All test-owned diary state**: events, archive rows, per-pawn diary indexes, `diariesById`, tracked
  Social-log rows, transient dedup/command keys — and the pawns themselves, destroyed with `Vanish`.
- **Generically for every test pawn**: any mental state is recovered and all direct relations cleared,
  so a test never has to write its own reflection cleanup.

Two guarantees make this safe:

1. **Failure-accumulating teardown.** Every cleanup step runs even if an earlier one throws; the first
   failure is re-thrown only after all steps have been attempted. A broken assertion mid-test can never
   skip cleanup.
2. **No-leak audit.** After cleanup, `TearDown` asserts that *no* event, diary index, Social-log row,
   or transient key referencing a test pawn survived — turning a silent leak into a visible test
   failure. This is the machine check behind `design/TEST_COVERAGE_PLAN.md §9`'s "zero marked state" gate.

### Helpers

| Helper | Use |
|---|---|
| `Begin(params groups)` | Start a scope; validate the loaded-game preconditions; enable the named groups. |
| `CreateAdultColonist()` | Isolated, non-generating, diary-eligible adult colonist. |
| `FireAndRequireEvent(fire, defName, initiator, recipient)` | Run a real trigger; assert exactly one matching new event; return it. |
| `RequireNoNewEvent(fire)` | Negative gate: assert the trigger produced no new event for a test pawn. |
| `OwnDiaryEventsCreatedAfterThisPoint()` | Before a synchronous colony-witness trigger, make the scope remove and audit every exact new event even when a real loaded pawn owns it. |
| `SuppressDiaryGenerationForTest(pawn)` | Temporarily gate transport for a real loaded witness and restore the exact prior diary-row/flag state without queueing work. |
| `RequirePairRefs(event, a, b)` / `RequireSoloRef(event, a)` | Assert shape, participant ids, and per-pawn diary refs. |
| `TrackPlayLogEntry(entry)` | Mark a Social-log row the test added for removal + audit. |
| `RegisterCleanup(action)` | Register extra per-test cleanup (spawned thing, job, hediff) run failure-isolated before the core steps. |
| `Require(condition, message)` | Assertion shorthand shared by tests and the harness. |

### Extending for later phases

The harness deliberately restores only what today's suites touch. Later `design/TEST_COVERAGE_PLAN.md` phases
add more state (hediffs, jobs, conditions, quests, event/observed-condition windows, LLM queues and
lane cooldowns, map spawns, deliberate tick changes, child pawns behind a Biotech gate). Add each as a
new snapshot/restore pair in `PawnDiaryRimTestScope` — or, for one-off per-test state, via
`RegisterCleanup` — and extend the no-leak audit to cover it. Keep test bodies assertion-only.

## Coverage audit

When the local developer script `scripts/verify-coverage.ps1` is present, it validates all XML, runs
every standalone pure test project, builds the core mod, builds this RimTest assembly when RimTest
Redux is available, and prints the EVT-01…EVT-26 requirement matrix.

```powershell
scripts\verify-coverage.ps1              # full audit (build + pure tests + matrix)
scripts\verify-coverage.ps1 -MatrixOnly  # just print the EVT coverage matrix
```

It is intentionally separate from `.githooks/verify.ps1` because it also prints the full matrix.
Both scripts build the RimTest assembly when `RimTestRedux.dll` is available, and skip that one
optional build only when the dependency is genuinely absent; the mandatory hook additionally
freshness-checks the committed test DLL.

## Guarantees for every test here (plan §9)

- Never calls a real external LLM (isolated pawns have generation disabled).
- Never changes the player's colony, and leaves no test pawn/event/log/settings/queue state — even
  after a deliberately failing assertion.
- Pure logic stays in the standalone `tests/` projects; this assembly is only for what needs the game.
