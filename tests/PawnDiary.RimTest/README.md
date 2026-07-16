# Pawn Diary — in-game RimTest suite

This folder is the **loaded-game** test assembly for Pawn Diary. Unlike the standalone pure-logic
projects under `tests/`, these suites run *inside RimWorld* through the optional
[RimTest Redux](https://steamcommunity.com/sharedfiles/filedetails/?id=3762405308) development mod, so
they can drive real vanilla APIs and Harmony hooks and assert the `DiaryEvent` the production pipeline
actually persists.

`TEST_COVERAGE_PLAN.md` (repo root) is the roadmap this suite is being built out against. This README
is the operator's guide for the assembly itself.

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
suites map to `TEST_COVERAGE_PLAN.md §3` EVT rows; supplemental B1 suites exercise Biotech composite
owners. Both use the shared harness:

| Suite | EVT | Covers |
|---|---|---|
| `PawnDiaryEventReactionTests` | 01/07/08 | Interaction pair (PlayLog), romance (relation), mental state. |
| `PawnDiaryInteractionBatchFlowTests` | 02 | Interaction batch/ambient accumulation + flush. |
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
| `PawnDiaryPawnProgressionFlowTests` | 15 | Skill/trait milestone baseline + upward-only + arc request. |
| `PawnDiaryBiotechGrowthFlowTests` | B1 | Family-keyed canonical growth/N1 evidence, baseline/fallback, real growth-letter hooks, and loaded detail-preset prompts. |
| `PawnDiaryBiotechBirthFlowTests` | B1 | Canonical two-adult birth, Tale-domain/important-group routing, child-never-POV shape, delayed naming flush, replay rejection, loaded detail-preset prompts, and a loaded-template preflight. |
| `PawnDiaryBiotechComponentStateFixtureTests` | B1 | Real-Scribe component keys, old/malformed/oversized rows, hard ceiling, and pre-cap admission recovery. |
| `PawnDiaryDlcSafetyFixtureTests` | 7.3 | Null/base-only omission, installed-DLC positive pawn state (including a temporary vanilla CreepJoiner tracker with a real loaded form), exact specialized/generic-fallback classifier policy, official package/group/window/settings matrix, fragile hook signatures, and optional-adapter fail-open readiness. |
| `PawnDiaryQuestFlowTests` | 16 | Quest accept/complete/fail fan-out + label sanitation + dedup. |
| `PawnDiaryRitualFlowTests` | 17 | Ritual participant fan-out (DLC-gated, clean no-op without Ideology). |
| `PawnDiaryArrivalFlowTests` | 18 | Neutral arrival page + first-ordering + bootstrap resilience. |
| `PawnDiaryDayReflectionFlowTests` | 19 | Day/quadrum reflection highlight + once-per-day guard + evidence consumption. |
| `PawnDiaryArcReflectionFlowTests` | 20 | Arc reflection year/gap limits + memory filter/dedup + backoff. |
| `PawnDiaryExternalApiFlowTests` | 21 | `PawnDiaryApi` submit solo/pair, group gate, budget, listener notify. |
| `PawnDiaryEventWindowFlowTests` | 22 | Event-window start/end/one-shot/timeout + prompt-bias state. |
| `PawnDiaryObservedConditionFlowTests` | 23 | Observed-condition start/end debounce + scope identity + restart cooldown. |

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
blind edit to the transport core — see `TEST_COVERAGE_PLAN.md §2.2 / §6.3`.

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
   failure. This is the machine check behind `TEST_COVERAGE_PLAN.md §9`'s "zero marked state" gate.

### Helpers

| Helper | Use |
|---|---|
| `Begin(params groups)` | Start a scope; validate the loaded-game preconditions; enable the named groups. |
| `CreateAdultColonist()` | Isolated, non-generating, diary-eligible adult colonist. |
| `FireAndRequireEvent(fire, defName, initiator, recipient)` | Run a real trigger; assert exactly one matching new event; return it. |
| `RequireNoNewEvent(fire)` | Negative gate: assert the trigger produced no new event for a test pawn. |
| `RequirePairRefs(event, a, b)` / `RequireSoloRef(event, a)` | Assert shape, participant ids, and per-pawn diary refs. |
| `TrackPlayLogEntry(entry)` | Mark a Social-log row the test added for removal + audit. |
| `RegisterCleanup(action)` | Register extra per-test cleanup (spawned thing, job, hediff) run failure-isolated before the core steps. |
| `Require(condition, message)` | Assertion shorthand shared by tests and the harness. |

### Extending for later phases

The harness deliberately restores only what today's suites touch. Later `TEST_COVERAGE_PLAN.md` phases
add more state (hediffs, jobs, conditions, quests, event/observed-condition windows, LLM queues and
lane cooldowns, map spawns, deliberate tick changes, child pawns behind a Biotech gate). Add each as a
new snapshot/restore pair in `PawnDiaryRimTestScope` — or, for one-off per-test state, via
`RegisterCleanup` — and extend the no-leak audit to cover it. Keep test bodies assertion-only.

## Coverage audit

`scripts/verify-coverage.ps1` is the one-command audit (`TEST_COVERAGE_PLAN.md §8`): it validates all
XML, runs every standalone pure test project, builds the core mod, builds this RimTest assembly when
RimTest Redux is available (skips with a warning otherwise — the assembly is optional), and prints the
EVT-01…EVT-23 requirement matrix, exiting non-zero if any row is uncovered.

```powershell
scripts\verify-coverage.ps1              # full audit (build + pure tests + matrix)
scripts\verify-coverage.ps1 -MatrixOnly  # just print the EVT coverage matrix
```

It is intentionally separate from `.githooks/verify.ps1`, which stays lean and must not depend on the
optional Workshop RimTest Redux DLL.

## Guarantees for every test here (plan §9)

- Never calls a real external LLM (isolated pawns have generation disabled).
- Never changes the player's colony, and leaves no test pawn/event/log/settings/queue state — even
  after a deliberately failing assertion.
- Pure logic stays in the standalone `tests/` projects; this assembly is only for what needs the game.
