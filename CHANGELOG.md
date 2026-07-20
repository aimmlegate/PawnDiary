# Changelog

- **2026-07-20 — Corrected two false-negative A2.0 loaded creepjoiner fixtures.** The first
  user-provided active 323-test run passed 321 and failed the rejection/aggression assertions after
  each test had already verified that exactly one new test-pawn event existed. Those outcomes are
  intentionally single-writer pages: the visible creepjoiner is frozen in prompt-safe context, not
  stored as a pairwise `recipientPawnId`. The fixtures now match a blank recipient role and explicitly
  assert `creepjoiner_subject_id`, preserving both solo ownership and subject-identity coverage. No
  production behavior, save schema, prompt text, Def, or localization changed. The 323-test RimTest
  assembly rebuilds; the corrected in-game rerun remains pending.

- **2026-07-20 — Implemented Master Wave 7 / Anomaly Phase A2.0 visible creepjoiner state.** The
  existing canonical arrival page now initializes one visible-only creepjoiner arc and attaches its
  event ID only after that page exists; repeat arrivals create neither a second arc nor a second page.
  Save schema 2 deep-scribes the frozen `anomalyCreepJoinerArcs` key with only pawn/arrival/joined/
  visible-phase/visible-event/terminal/version primitives. Pure normalization deterministically handles
  null, malformed, duplicate, oversized, negative-tick, invalid-phase, and future-schema rows under
  4,096-input/512-output caps. Anomaly-active old saves silently baseline current joined player
  creepjoiners; Anomaly-inactive loads defer without catch-up pages.

  The installed 1.6 assembly was reconfirmed before implementation. Independent fail-open Harmony
  registrations now target the exact public parameterless `Pawn_CreepJoinerTracker.DoRejection()`,
  `DoAggressive()`, and `DoLeave()` methods and cache their private committed-transition markers once.
  Detached before/after verification preserves vanilla behavior on missing targets, exceptions, no-op/
  repeated calls, or unverifiable state. `DoDownside()` remains unpatched; its nested exact method owns
  naturally, while a bounded lifecycle-cleared scope lets outer rejection own its nested aggressive/
  departure response exactly once.

  Pure policy commits terminal visible history independently of settings and chooses at most one
  truthful event-time POV: exact eligible speaker before joining, eligible subject captured before a
  joined departure, or the closest bounded nearby eligible witness. Context and localized English/
  Russian fallback expose only generic visible phase/result, stable subject identity/visible label,
  verified role, and terminal state—never benefit/downside/response Defs, private triggers, hidden host/
  infection, worker type, future form, motive, or causality. A2.1 surgical disclosure, A2.2 ghoul
  transformation, A3 terminal outcomes, and N3-A expansion remain untouched.

  Focused suites pass 472 Anomaly and 115 save-normalization assertions. Seven new loaded fixtures
  bring the RimTest assembly to 323 compiled tests and cover registration/no-`DoDownside`, canonical/
  repeated arrival, live rejection/aggression/departure, nested ownership, writer roles, repeat/no-op
  silence, and lifecycle reset; the actual component Scribe fixture now covers the seventh deep list.
  These new fixtures are compiled but not claimed as executed in-game. The user-confirmed automated
  A1.4 Anomaly-active 316-fixture run is recorded green as aggregate evidence without implying a
  preserved per-method log. Deferred verification remains the separate Anomaly-inactive profile,
  disposable missing study/containment-hook profiles, and real process-boundary save/reload.

- **2026-07-20 — Completed the Anomaly A1.4 adversarial hardening and automated delivery pass.** Blank
  study labels now use a localized neutral subject in player-facing fallback instead of exposing a raw
  Def name. Containment capture resolves recent studiers with one bounded cache scan, ranks the full
  eligible affected-map roster once, caps the retained cascade pool at 512, and derives writers from
  that pool. Malformed saved Anomaly histories now inspect at most 4,096 source rows as well as retaining
  at most 4,096 normalized rows.

  Focused suites pass 404 Anomaly and 84 save-normalization assertions; new cases cover batched recent-
  study matching, 600-row candidate ordering equivalence across the cap, and corrupt-history input
  bounds. A loaded fallback fixture raises RimTest to 316 compiled tests. Runtime and RimTest assemblies
  build with 0 warnings and 0 errors; the full verifier passes whitespace/XML, all 14 pure projects,
  runtime rebuild, and committed-DLL freshness. The prior user-confirmed corrected Anomaly-active
  315/315 full-suite result corresponds to the unchanged assembly containing all ten A1.2 and all ten
  A1.3 fixtures; it closes the old A1.2 row as aggregate user-confirmed evidence, while no preserved
  per-method run artifact independently enumerates them. At this delivery boundary it did not cover the
  new fixture; the later A2.0 entry records the user's green 316-fixture confirmation. The separate
  Anomaly-inactive profile, disposable missing-hook profiles, and a real process-boundary save/reload
  remain explicit manual rows; none is claimed from compilation. No A2 creepjoiner/ghoul behavior or
  DLC dependency was added in A1.4 itself.

- **2026-07-20 — Confirmed the corrected A1.3 loaded suite all green.** The user-confirmed
  Anomaly-active rerun passed all 315 compiled RimTest fixtures, including all ten containment
  fixtures and the corrected exact localized visible-label fallback assertion. The separate
  Anomaly-inactive profile remains a manual DLC-off matrix row. No runtime, save, schema, or
  DLC-dependency behavior changed.

- **2026-07-20 — Corrected the A1.3 visible-label RimTest after its first full loaded run.** The
  Anomaly-active run executed all 315 compiled fixtures: 314 passed, including the new direct
  containment scope-state fixture, and one containment fallback assertion failed. Production had
  emitted the intended localized visible label; the test incorrectly treated that label as leaked
  whenever it happened to contain the entity's stable Def name. The fixture now requires exact
  equality with the localized visible-label-only fallback instead. The RimTest assembly was rebuilt;
  a confirmation loaded rerun and the separate Anomaly-inactive profile remain manual. No runtime,
  save, schema, or DLC-dependency behavior changed.

- **2026-07-20 — Hardened A1.3 after combined adversarial review.** Localized containment fallback
  text now says the selected pawn recorded the breach instead of claiming every nearby, recent
  studier, or colony fallback personally witnessed it; player-visible fallback uses bounded entity
  labels only, while stable Def names remain confined to structured prompt context. Missing labels
  use a localized neutral subject. English/Russian Keyed and DefInjected policy text were updated.

  The outer adapter now filters eligible pawns before retention, applies the pure role/distance order
  before its 512-row defensive cap, and gives that bounded roster to nested same-room calls. Cascades
  therefore avoid repeated full-map copies/sorts and nested surroundings work. Optional modded
  surroundings failures warn once and omit only setting enrichment. Recent-study rows normalize IDs
  once on insertion/restore, and writer-resolution drift now warns once instead of disappearing
  silently. Cross-source breach suppression remains deliberately fail-open because vanilla escape
  supplies no entity-bearing Tale/raid row and current generic raid/thought contracts cannot prove
  exact escaped-entity/start identity; the plan now records that boundary instead of implying broad
  suppression.

  Pure coverage now passes 398 assertions, including priority-before-cap, cache normalization,
  visible/raw entity separation, and real truncation. A new compiled RimTest pins idempotent abort and
  unhealthy close cleanup; the exception fixture proves the subject remained held before retry, and
  fallback pins visible-label-only text. Core and the 315-test RimTest assemblies rebuild with no warnings.
  The original user-confirmed active-DLC run remains 9/9; the new tenth scope fixture and separate
  DLC-off profile still require in-game execution. No Scribe key, schema version, save migration, or
  paid-DLC dependency changed.

- **2026-07-20 — Hardened A1.3 RimTest map setup after its first loaded runs.** The containment
  fixture now selects any clean vanilla `Room`, including open terrain, matching the real
  `CompHoldingPlatformTarget.Escape(bool)` implementation's non-null-room requirement. It validates
  the holding platform's complete loaded-Def footprint rather than only its center, keeps a one-cell
  margin between simultaneous platforms, and refuses any placement that could cross the map edge,
  overlap an edifice or pawn, wipe a thing, or involve an existing occupied platform. The loaded
  witness-radius fixture now places its radius-two witness outside the real 3x3 platform footprint.
  Failed main-menu setup also preserves the truthful loaded-game assertion instead of letting a null
  cache snapshot fail in teardown. RimTest Redux exposes no room/map-construction fixture helper, so
  using the live map's room graph avoids mutating player structures. The corrected user-confirmed
  Anomaly-active loaded rerun passed 9/9 containment fixtures with 0 failures; the separate
  Anomaly-inactive profile remains a manual DLC-off row.

- **2026-07-20 — Implemented Anomaly Phase A1.3 containment-breach capture.** Added an
  Anomaly-active-only defensive registration for the exact public
  `CompHoldingPlatformTarget.Escape(bool initiator)` method, including prefix/postfix/finalizer
  ownership. A bounded reentrancy-safe frame/scope stack snapshots exact entity/platform/map/cell,
  writer evidence, and visible surroundings before ejection; nested same-room `Escape(false)` calls
  append and verify without emitting, while the healthy outer close submits at most one canonical
  containment event. Verification requires a live, spawned, no-longer-held entity in vanilla's visible
  escape state. Exception and lifecycle cleanup cannot strand live references. Deterministic pure
  policy ranks nearby witnesses, a separate non-consuming exact recent-studier cache, then free-colony
  fallbacks, with stable IDs and no `Verse.Rand`. Context is bounded to visible entity labels/defNames,
  counts, exact POV roles, pre-ejection setting, and cascade state; it excludes cells/platforms,
  containment rolls, hidden abilities/codex, inventories, guessed causes, and invented blame. Exact
  map/tick/outer-entity dedup leaves later consequences and atmosphere untouched. Intentional ejection,
  release notifications, held deaths, capture, and reconstruction remain unpatched and silent; no
  production UI trigger was added. The standalone Anomaly suite passes 387 assertions. Nine new loaded
  fixtures compile against the real escape seam, raising the RimTest assembly to 314 tests; after
  fixture hardening, the user-confirmed Anomaly-active loaded run passed all 9 with 0 failures. Core
  and RimTest DLLs were rebuilt for adversarial review.

- **2026-07-20 — Hardened Anomaly Phase A1.2 after combined adversarial review.** Correlated a
  dedicated study page and its later `StudiedEntity` fallback with RimWorld's exact non-reused
  `Job_<id>` as well as researcher/entity identity. A slow five-interaction study job can therefore
  retain ownership beyond the short compatibility tick window, while another job on the same pair
  remains fail-open. Exact-job rows stay unsaved, lifecycle-cleared, and bounded by the existing
  64-row cache cap. The study prefix now captures only cheap identity/progress/activation facts;
  visible labels, discovered codex/category data, eligibility, containment, and surroundings are read
  once and only after a real transition. Removed the unused monolith-level read from every study call.

  Void-monolith activation registration now requires the exact `Find.Anomaly.LevelDef` getter, and
  the component refuses an empty reached level without advancing its baseline or consuming remembered
  study context. Added runtime-signature pins for `Building_VoidMonolith.CanActivate(out,out)` and
  `Job.GetUniqueLoadID()`, pure completion-vs-promotion and delayed/different-job ownership cases, and
  loaded fixtures for slow-job consumption plus missing-level state preservation. The focused pure
  suite passes 362 assertions; core and the 305-test RimTest assembly build cleanly. The two new
  loaded fixtures, like the original eight A1.2 fixtures, are compiled but not yet executed in-game.
  No Scribe key, schema version, DLC dependency, or localization string changed.

- **2026-07-20 — Implemented Anomaly Phase A1.2 researcher-owned study capture.** Added one
  defensive, Anomaly-active-only registration for the exact public
  `CompStudyUnlocks.OnStudied(Pawn,float,KnowledgeCategoryDef)` seam. Guarded before/after reads now
  stay in `DlcContext`, detach exact subject/studier/codex/category/containment/monolith/setting facts,
  and commit pure first/completed-kind/XML-promotion history even when output is disabled or the
  researcher is ineligible. Only an accepted exact-author study page claims the matching
  `StudiedEntity` Tale; mismatches, expiry, disabled rows, rejected dispatch, and missing hooks retain
  the generic fallback. Monolith study remains page-silent and can enrich only the next exact
  activation; automatic activation consumes/baselines the snapshot without inventing pawn agency.

  The assembly-free Anomaly suite passes 353 assertions (up from 320). Eight focused in-game tests
  add hook-health/inertness, no-threshold, one-threshold, multi-note jump, completion, disabled-group,
  real-Scribe/no-replay, and exact consume-once Tale cases, bringing the RimTest assembly to 303
  compiled tests. Core and RimTest DLLs rebuild cleanly. These new A1.2 fixtures are compiled but
  have not been executed inside RimWorld; manual execution remains pending.

- **2026-07-20 — Recorded the completed Anomaly Phase A1.1 in-game matrix.** The no-Anomaly
  main-menu exact-classifier fixture passed 1/1 with 0 failures. The user-confirmed manual loaded
  no-Anomaly state run passed 3/3 with 0 failures, and the user-confirmed manual loaded
  Anomaly-active state run passed 3/3 with 0 failures. The resulting 7/7 focused executions cover
  exact classifier routing, the real six-key Scribe round-trip, missing-key defaults, DLC-off
  deferred migration, DLC-on one-time baseline, and transient reset. This runtime result supersedes
  the earlier A1.1 “compiled but pending” notes below; it does not count the new A1.2 fixtures.

- **2026-07-20 — Hardened Anomaly Phase A1.1 after adversarial review.** Replaced the shared
  non-catch-all classifier path with an Anomaly-specific exact-name, runtime-available lookup, so the
  broad order-70 `Anomaly` token group cannot claim unknown synthetic names and package-gated rows
  return null without the DLC. Preserved the six other required-match callers unchanged. An eligible
  fallback writer now remains solo when an ineligible first-role slot carries the same pawn ID.
  Oversized persisted identities now drop atomically instead of truncating into collision-prone
  replay keys, and live monolith/study reads moved into the guarded `DlcContext.Anomaly` adapter.

  Pure regressions pass 708 capture and 83 save-normalization assertions. Three new loaded RimTests
  drive the actual component's six Scribe keys, deep monolith snapshot, missing-key defaults,
  package-absent deferred migration, package-active one-time baseline, and transient reset; the
  RimTest assembly now contains 295 tests and builds cleanly with the runtime DLL. The save runbook
  now covers pre-A1 active, disable/resave, and re-enable transitions. These new in-game fixtures are
  compiled but have not yet been executed inside RimWorld.

- **2026-07-20 — Implemented Master Wave 7 / Anomaly Phase A1.1 catalog and persistence
  foundation.** Registered the common fail-closed `AnomalyEvent` catalog envelope for the five frozen
  study, containment, visible creepjoiner, ghoul, and void kind/Def pairs. It requires verified
  player-visible source ownership, enabled policy, a safe stable source key, non-replay, and one or
  two distinct eligible writers; no live source is registered yet, so this route cannot create a page.
  Added six additive study/monolith save keys, bounded pure normalization, trustworthy new-game state,
  and a conservative pre-A1 loaded-map baseline which retains completed study kinds but treats
  incomplete history as already observed rather than inventing a new “first.” All live Anomaly reads
  are package-gated and the package-absent migration remains pending for a later eligible load.

  Added a bounded unsaved consume-once study/Tale claim cache with constructor/new-game/load/finalize
  cleanup, plus an explicit zero-candidate N3-A provider seam. Five exact package-gated XML
  groups/settings at orders 61–65 ship with English/Russian DefInjected guidance and Keyed fallbacks;
  their classifier requires an available exact match and never falls through to any broader
  Interaction matcher. No Harmony registration, signal, page emission, tick work, hidden state, or paid-DLC Def
  reference was added. Focused suites pass 320 Anomaly, 708 catalog, 83 save-normalization, and 135
  Narrative assertions. Runtime and 295-test RimTest assemblies rebuild; compiled smoke coverage pins
  the five Def/package routes and exact study-comp API, while the new in-game/no-DLC execution remains
  pending.

- **2026-07-20 — Hardened Anomaly Phase A1.0 after adversarial review.** Centralized policy-Def
  normalization in an assembly-free boundary that preserves switches, applies conservative numeric
  fallbacks, trims/de-duplicates promotion rows, and caps them at 128. The loaded adapter deliberately
  returns a fresh mutable snapshot on every call, preventing one consumer from leaking mutations into
  another. Study planning now preserves a same-call monolith false-to-true activatable transition even
  when note progress is unchanged, emits a promotion token only when promotion is the selected semantic
  reason, and names the note field as the exact per-call threshold delta. Tale ownership now uses the
  defName fallback only when both sides lack stable entity IDs, so partial identity evidence fails open
  instead of suppressing an unrelated Tale. Containment breach de-duplication now includes the source
  tick as well as escape and map identity.

  `DiaryAnomalyPolicyTests` now passes 211 assertions, adding equality-boundary, simultaneous-stage,
  no-progress monolith, asymmetric Tale-ID, malformed-policy normalization/cap, negative-window, and
  tick-aware breach-key regressions. The RimTest singleton smoke fixture now also exercises normalized
  defaults and the fresh-snapshot contract. A manual main-menu RimTest run passed all 46 tests that do
  not require a loaded colony, including that singleton fixture; the other 245 fixtures rejected the
  missing loaded game as designed, so the full 291-test loaded-colony run remains pending. The hook-
  equivalent verifier, runtime DLL, and RimTest assembly rebuild are clean. Runtime behavior and save
  format remain unchanged: A1.0 still adds no live hook, page, static state, Scribe key, migration, or
  paid-DLC dependency.

- **2026-07-20 — Implemented Master Wave 7 / Anomaly Phase A1.0 pure policy foundation.** Added the
  primitive-only `DiaryAnomalyPolicyDef`/XML defaults, detached study/containment/Tale DTOs, pure
  milestone and witness planners, exact fail-open `StudiedEntity` ownership, and the planned stable
  synthetic event/prompt token vocabulary. Study observation advances independently from output,
  monolith study remains state-only, one multi-threshold jump observes every crossed promotion while
  authorizing at most one semantic reason, containment keeps only verified unique escapes and ranks
  truthful nearby/recent-studier/colony witnesses deterministically, and Tale suppression requires one
  exact researcher/entity pair inside its configured consume-once window. No planner consumes
  `Verse.Rand` or holds a live Pawn/Thing/Def/Map/settings object.

  The policy XML makes no paid-DLC Def reference and its loaded singleton smoke check is read-only, so
  a no-Anomaly game can load it inertly without a new dependency. This slice adds no catalog route,
  Harmony hook, live DLC read, tick work, signal/page/prompt group, static colony state, Scribe key, or
  migration; A1.1–A1.3 retain those scheduled responsibilities. The new assembly-free
  `DiaryAnomalyPolicyTests` passes 167 assertions; the focused capture, pipeline, save-normalization,
  observed-condition, and text-decoration suites pass 680, 2,734, 46, 68, and 67 assertions. Runtime
  and the unchanged 291-test RimTest assemblies rebuild cleanly. The new loaded-Def assertion and the
  full 291-test in-game run have not been executed and remain pending.

- **2026-07-20 — Closed adversarial-review gaps in Royalty Phase 8 hardening.** Royalty persona
  reconciliation now calculates due/next deadlines through a pure scheduling boundary and stores its
  unscribed deadline as `long`, so even an `Int32.MaxValue` compatibility cadence cannot wrap negative
  and turn the collector into a per-tick scan. Eight standalone assertions cover the cadence floor,
  due boundary, current-time rebase, malformed inputs, and maximum-tick overflow, raising
  `RoyaltyContextTests` from 463 to 471 assertions. This preserves the existing one-current-state-pass
  time-skip contract; it does not introduce historical cadence replay.

  Caught Harmony registration failures still emit only their bounded feature-specific `WarningOnce`,
  but now retain the exception type/message (or exact missing-target reason) needed to diagnose a
  compatibility break. The loaded hook audit exercises that caught-exception branch without installing
  a patch. `RoyaltyTransientState.Reset()` isolates every cache owner so one failed cleanup cannot stop
  later cross-colony state from clearing. Phase-8 loaded fixtures now pin and restore the mutable cached
  Royalty master/caps/cadence/threshold plus the initial-arrival gate, eliminating dependence on the
  active compatibility profile or suite order. Runtime and the unchanged 291-test RimTest assemblies
  rebuild with no warnings. No prompt/XML/Scribe key or DLC dependency changed; the deadline remains
  unscribed, so old saves require no migration. The 291-test loaded run remains pending.

- **2026-07-19 — Hardened pawn-fact and mood-thought reads against modded getter crashes.**
  Player logs showed three third-party NREs escaping through Pawn Diary call sites:
  `VPE-Veincaster`'s bloodlink hediff throwing inside `Hediff.Label` (via `get_LabelInBrackets`),
  `InspirationTweaks`' soothe thought throwing inside `ThoughtHandler.GetAllMoodThoughts`, and a
  room-stat recalculation throwing inside `Room.GetRoomRoleLabel` (already guarded by an earlier
  commit). `PawnFactCapture` now reads hediff labels through a guarded helper used by both
  text-decoration fact capture and intoxication detection, so a broken modded hediff degrades to
  "no label" instead of aborting the diary event. The `GetAllMoodThoughts` enumeration call —
  which itself invokes every thought's `MoodOffset()` before the existing per-thought guards can
  run — is now wrapped in the thought-progression scan, the top-thoughts summary, and the
  game-condition mood classifier, degrading to an empty scan/summary or zero offset for that pass.
  All fallbacks stay silent because the broken modded state rethrows on every sample while it
  persists. No prompt field, XML, Scribe key, or gameplay behavior changed.

- **2026-07-19 — Implemented Master Wave 13 / Royalty Phase 8 compatibility and release
  hardening.** Coverage-first review confirmed the existing pure Royalty matrix already owns
  localized-wording independence, structural/override persona-trait ranking, unsafe/malformed rows,
  deterministic caps, exact permit allowlists, routine/unknown exclusions, and fallback decisions;
  no duplicate policy or XML rule was added. Hook-registration exceptions now defer to one bounded
  feature-specific warning instead of emitting an unbounded duplicate, while each optional seam keeps
  its documented scanner fallback or feature-local fail-closed behavior. Phase 8 documents and pins
  the existing persona time-skip contract: at most one current-state catch-up, then an elapsed deadline
  rebased from the current tick rather than a loop over every missed cadence.

  Four reversible loaded fixtures raise the RimTest assembly to 291 tests. Synthetic modded persona
  traits pass through the real `DlcContext` adapter and prove structural facts—not localized labels—
  control selection; synthetic unknown/malformed permit Defs pass through the real tracker and
  `Notify_Used` while remaining silent; a real persona-kill scope is cleared by `FinalizeInit`; and a
  long game-time skip proves one reconciliation/page with global tick/deadline restoration. The
  direct-mutation title-loss fallback now also asserts a repeated scan is silent. The cache audit
  records process-static Royalty owners cleared by `RoyaltyTransientState.Reset`, general death
  context cleared by the component constructor, and component Quest/source/window dedup cleared by
  `StartedNewGame` / `LoadedGame`. No prompt field, XML, Scribe key, DLC dependency, or gameplay Def
  reference changed.

  Runtime and RimTest DLLs rebuild. Focused suites pass 463 Royalty, 680 capture, 2,734 pipeline, 22
  prompt-variant, and 46 save-normalization assertions. The new 291-test loaded run, actual exit-to-
  menu/second-colony boundary, corrected Phase-7 loaded rows, Royalty-off profile, localization, and
  all hands-on release matrices remain explicitly pending; 278/278 is still the last fully green
  loaded baseline.

- **2026-07-19 — Hardened Royalty Phase 7 after consolidated adversarial review.** Saved Quest
  display and arc-reflection recovery now use the exact root before lifecycle fallback, so Royal
  Ascent keeps its own label, royalty color/tone, and reflection diversity bucket after save/load.
  The generic event-window prompt path now reuses `RoyalAscentPolicy.ActivePressureApplies`, making
  the Royalty master and the bounded saved correlation/arc mandatory before Ascent can alter prompt
  candidates or normal weighting. An identified active window now rejects both mismatched and empty
  terminal identities; only identity-less legacy rows retain the conservative root-based close path.
  Exact-only group lookup explicitly excludes catch-alls, and the Quest adapter now stops after the
  accepted start-window edge instead of relying solely on the downstream pure accepted-signal drop.
  Loaded fixtures isolate and restore any real active Ascent row and now pin first-accept cardinality,
  exact display/reflection recovery, prompt master/identity gating, empty-correlation rejection, and
  future Quest catch-all behavior. A conflicting review claim that acceptance currently created two
  pages was disproved by tracing `DiaryGameComponent.Dispatch` through `QuestEventData.Decide`; the
  new assertion preserves that already-correct behavior. Runtime and the unchanged 287-test RimTest
  assembly rebuild cleanly; the corrected loaded rerun and hands-on matrix remain pending.

- **2026-07-19 — Corrected three fixture defects exposed by the first Royalty Phase-7 loaded run.**
  The expanded suite executed 287 tests: 284 passed and three failed across two suites. The official-
  DLC catalog fixture had not added the intentionally Royalty-gated `questRoyalAscent` group and
  `RoyalAscent` event window; the stable-witness fanout fixture supplied the deliberately rejected
  `unknown` lifecycle signal; and the real Accept/End fixture did not isolate a loaded witness's
  pre-existing generic Quest dedup entry. The matrices now include both Phase-7 rows, fanout uses the
  proven `completed` edge, and the real-hook fixture snapshots/restores only its exact generic and
  colony Quest dedup keys. It also temporarily suppresses generation for the selected witness without
  queueing on restore, and makes the shared scope remove/audit every exact page assigned there, so no
  network request or test page survives teardown. No
  production behavior, save schema, DLC dependency, Def policy, or manual acceptance row changed.
  Runtime and 287-test RimTest assemblies rebuild cleanly; a corrected loaded rerun is pending, so the
  last fully green executed baseline remains 278/278.

- **2026-07-19 — Implemented Master Wave 9 / Royalty Phase 7 Royal Ascent.** Adversarial inspection
  of installed RimWorld 1.6 code and Royalty XML established the truthful lifecycle: accepting
  `EndGame_RoyalAscent` proves the colony's commitment but not the asynchronously scheduled Stellarch
  arrival; `Quest.End(Success|Fail)` proves only the hosting quest's terminal outcome; the later
  `SentWithExtraColonists` signal owns escape/credits. The existing exact Accept/End hooks now require
  real state transitions, direct/UI acceptance paths share one admission owner, and the legacy
  acceptance scanner explicitly excludes Ascent rather than inferring a missed start from later state.

  Added pure exact-root/correlation/arc/expiry/migration policy; root-first Quest routing at live and
  saved-payload boundaries; XML-owned one-witness fanout, start-only twenty-day event window, prompt
  pressure, exact group/prompt, bounds, and truth rules; and one stable eligible witness across loaded
  colony maps. Acceptance creates one preparation page/window, active pressure can enrich only a
  relevant already-authorized page, and matching completion/failure closes silently before one exact
  terminal Quest page. Shared journey evidence never exposes quest IDs/arcs to prompts or claims
  arrival, exact failure cause, boarding, or escape. XML-master-off and Royalty-off behavior fail
  closed, and package rows stay hidden without adding a paid-DLC dependency.

  Save schema changes are append-only primitive strings on `ActiveEventWindowState`
  (`startCorrelationId`, `startNarrativeArcKey`); missing old-save fields remain empty and cannot infer
  pressure and malformed rows fail closed. New-game/load callbacks reset component Quest/window
  transients, while `FinalizeInit` resets process-static Royalty ownership. English/
  Russian DefInjected and Keyed text plus three Prompt Studio fixtures ship. Pure suites pass 463
  Royalty, 2,734 pipeline, and 132 Narrative assertions; runtime and 287-test RimTest assemblies
  build. Eight flow fixtures plus one Def smoke check compile real-hook start/terminal/cardinality,
  fanout, pressure/evidence, Scribe migration, reset, package/Prompt Studio, master-off, and no-DLC coverage. The
  first expanded loaded run subsequently reached 284/287; the fixture-only corrections are recorded
  above. Until their rerun, 278/278 remains the last fully green baseline, and no manual or separate
  Royalty-inactive acceptance row is closed.

- **2026-07-19 — Confirmed Royalty Phase 6 automated loaded coverage green at 278/278.** The full
  compiled RimTest suite is now user-confirmed passing in a loaded game, including the eleven Phase-6
  dramatic-permit fixtures and the prior runtime regression matrix. This records automated execution
  only: no Phase-2–6 hands-on acceptance row is closed, and a separately recorded Royalty-inactive
  profile remains pending.

- **2026-07-19 — Hardened Royalty Phase 6 after combined adversarial review.** The quick-aid raid
  branch now stages only while the Royalty policy master is enabled: master-off preserves the mature
  generic raid route, while group-off deliberately consumes the matched source without producing
  duplicate pages. Compatibility fallback ownership reads exact `AllFactionPermits` references
  without re-entering the patched lookup and fails closed if the bounded scan is truncated. The
  successful-use patch is fail-soft, its hot lookup postfix avoids captured closures, the correlation
  window is consistently half-open, and unknown family tokens can no longer fall through as orbital
  salvo.

  All five Phase-6 prompt Defs now carry Royalty package metadata. They stay loaded for pending-event
  and old-save recovery but are hidden from Prompt Studio when Royalty is inactive. Pure coverage now
  passes 431 Royalty, 2,650 pipeline, and 680 capture assertions; the runtime and 278-test RimTest
  assemblies build. The eleven compiled Phase-6 fixtures now drive the production raid postfix and
  cover master/group switching, non-reentrant cap-safe fallback lookup, and Prompt Studio package
  visibility. Loaded execution remains pending, so the confirmed in-game baseline is still 267/267.

- **2026-07-19 — Implemented Master Wave 9 / Royalty Phase 6 dramatic permits.** Adversarial
  inspection of RimWorld 1.6's installed assembly and Royalty XML confirmed the exact six dramatic
  permits, nine routine exclusions, `Pawn_RoyaltyTracker.GetPermit(RoyalTitlePermitDef, Faction)` as
  the exact-instance owner seam, and `FactionPermit.Notify_Used()` as the shared post-success edge;
  targeting/cancellation and failed military aid never reach it. Vanilla quick military aid completes
  its flagged `RaidFriendly` before notifying the permit, so the existing raid owner now stages that
  exact successful faction+map signal briefly and losslessly returns unmatched, expired, overflowed,
  backwards-clock, or pre-save signals to the ordinary fan-out route. Matching permit success claims
  the raid in vanilla or reverse callback order even when visible permit output is disabled.
  Bounded weak owner/correlation state resets at `FinalizeInit`; no save key, unsafe DLC Def lookup,
  direct XML DLC reference, paid-DLC dependency, or hot-path polling was added.

  Pure permit contracts/policy, XML allowlist/mappings/windows/caps, guarded main-thread collection,
  exact manual Harmony registration, the `RoyalPermit` catalog/signal/domain/group, four invocation-
  only prompts, append-only SoloImportant fields 117–122, and English/Russian UI/prompt/fixture text
  now ship. `RoyaltyContextTests` passes 421 assertions, `DiaryPipelineTests` 2,645, and
  `DiaryCapturePolicyTests` 680; runtime and 275-test RimTest assemblies build. Eight compiled loaded
  fixtures cover exact targets, all four families, real invalid-target/cancelled intent, every
  reviewed exclusion, repeats, quick-aid ownership/expiry/overflow, pre-save/load reset, and
  Royalty-inactive silence. They have not yet had a loaded run: Phase 5's confirmed 267/267 remains
  the executed baseline, Phase 6/R2 acceptance remains open, and Phase 2–5 hands-on matrices remain
  unchanged and open.

- **2026-07-19 — Confirmed Royalty Phase 5 automated loaded coverage fully green.** The corrected
  disposable-pawn fixtures passed the complete loaded suite at 267/267, confirming strict inheritance
  cardinality, titleless instant-intermediate ownership, delayed succession retirement, pre-save title
  reconciliation, nonempty component-ledger migration, Royalty-off silence, and the existing DLC/runtime
  regression matrix together. Pure baselines remain green at 358 Royalty and 2,493 pipeline assertions,
  and repository verification passes. This closes Phase 5's automated loaded gate only; the recorded
  Phase 2–5 hands-on acceptance matrices remain open.

- **2026-07-19 — Repaired the first Royalty Phase-5 loaded-run fixture failures.** The loaded run
  reached 264/267; all three failures shared one harness precondition rather than three runtime
  defects. Generated test colonists are normally unspawned, but committed succession and pre-save
  title reconciliation intentionally resolve detached pawn IDs through RimWorld's live map/caravan
  roster. The two inheritance fixtures now spawn their disposable heir and the scanner/save fixture
  spawns its disposable writer before driving those production paths. The main-menu 222-failure block
  in the same log is the suite's expected loaded-game precondition when startup execution runs before
  a save is loaded. The corrected loaded rerun subsequently passed 267/267.

- **2026-07-19 — Adversarially hardened Royalty Phase 5 succession and save compatibility.** Verified
  vanilla's titleless inheritance order against the installed RimWorld assembly/XML: positive inherited
  favor synchronously awards instant Freeholder before the outer death tracker records `wasInherited`,
  while the offered bestowing quest has no acceptance deadline. Replaced the incorrect one-hour saved
  succession timeout with an XML-capped terminal state machine that advances only through exact-
  predecessor monotonic title steps, persists across arbitrarily delayed ceremonies, retires at the
  inherited target, and invalidates on contradictory same-pawn/faction title evidence. The existing
  2,500-tick XML value now cleans only a bounded transient same-action exact-edge duplicate cache;
  first-version pending rows migrate, already claimed rows prune, and additive current-title cursor
  fields round-trip through the actual component ledger. Titleless candidate capture now uses vanilla's
  authoritative pre-inheritance title snapshot, rejects a self-heir, and accepts the instant intermediate
  callback. Scope completion/cancellation releases every unclaimed staged mutation through isolated
  patch safety, preserves richer title owners, and advances saved title observation even after
  succession removes a competing cause-owned title. Fixed loaded inheritance helpers to use RimWorld
  1.6's real `canBeInherited`/`GetInheritanceWorker` API. Pure Royalty coverage passes 358 assertions;
  strict loaded fixtures now cover total event cardinality, titleless inheritance, equal-title silence,
  delayed terminal claim/retirement, nonempty production-component Scribe/old-expiry migration, and
  Royalty-inactive hook/scope silence. The runtime and 267-test RimTest assemblies build cleanly; the
  first expanded loaded run reached 264/267 and exposed three corrected live-pawn fixture setup gaps;
  the user-confirmed corrected rerun passed 267/267.

- **2026-07-19 — Implemented Royalty Phase 5 succession under the explicit Master-Wave-9 scheduling
  exception.** Added pure detached candidate/commit/fact/appointment contracts and
  `RoyalSuccessionPolicy`: only an exact `TryInherit` candidate plus the matching outer deceased
  title's `wasInherited` commit authorizes a succession; equal-or-higher heirs, candidate-only,
  mismatched, malformed, interrupted, and expired edges stay silent. Defensively registered the exact
  installed RimWorld 1.6 `RoyalTitleDefExt.TryInherit`,
  `Pawn_RoyaltyTracker.Notify_PawnKilled`, and
  `QuestPart_ChangeHeir.Notify_QuestSignalReceived(Signal)` seams with balanced prefix/postfix/finalizer
  failure isolation. Title callbacks that vanilla fires before its outer commit are staged and either
  claimed by the committed heir/faction/title edge or released to ordinary title flow; committed
  facts also suppress exact delayed title, bestowing, scanner, and title-memory duplicates without
  swallowing independent ritual/psylink truth. Direct and automatic `SetHeir` calls remain unpatched
  and silent; the proven explicit quest edge emits one heir-POV appointment. Only committed detached
  facts are deep-scribed under additive `royaltyPendingSuccessions`, with XML-owned 2,500-tick expiry,
  64-row cap, normalization/dedup, old-save empty baseline, pre-save pruning, and load-reset transient
  scopes. Added exact `RoyalSuccession`/`RoyalHeirAppointed` prompts, the four truthful context keys,
  SoloImportant fields 113–116, identity-transition evidence, and English/Russian UI, DefInjected,
  and prompt-test fixtures. Pure suites pass 346 Royalty and 2,493 pipeline assertions; the runtime
  and expanded 264-test RimTest assemblies build cleanly. New loaded fixtures drive real inheritance,
  equal-or-higher silence, title/bestowing dedup, explicit appointment, exact hook audit, Scribe/old
  save, and transient reset, but have not yet executed in game; the last confirmed loaded baseline
  remains 256/256 and Phase-5 hands-on acceptance remains open. Phase 6 permits, Phase 7 ascent, and
  unrelated work were not started.

- **2026-07-19 — Hardened and documented the still-inert pawn memory layer after adversarial
  review.** Player-visible behavior remains unchanged because capture/prompt/eviction/settings
  wiring still has no production caller. Blank-text fragments are now excluded before the recall
  store-size gate and scoring, so they cannot hide a renderable direct memory or leave an unanchored
  associative pick. Pawn identity tokens now bypass prose-only stopword/minimum-length filters, so
  names such as `Will`, `Bo`, and one-character CJK names remain association handles. Fixed
  `PawnMemoryRepository.Register` to initialize its lazy indexes before enforcing deposit
  idempotency, corrected the Russian quadrum age phrase, clarified the XML-only localized prompt
  instruction fallback, and added an explicit future-integration contract covering main-thread
  policy snapshots, recall-before-deposit, snapshot/live-row ownership, blank deposit rejection,
  save/load rebuilds, elapsed eviction scheduling, and per-pawn-then-global cap order. Expanded
  `PawnMemoryTests` with XML/default parity and regression coverage, and added real-Scribe RimTest
  fixtures for memory row repair, index rebuild/removal, and first-post-load replay idempotency.

- **2026-07-19 — Implemented the pawn memory subsystem from `design/MEMORY_SYSTEM_DESIGN.md` as an
  inert, unwired layer.** Player-visible behavior is unchanged: nothing calls the new code yet
  (capture hooks, prompt plumbing, eviction scheduling, and the settings toggle are design §14
  steps 4–7 and land separately). Added the pure `Source/Pipeline/Memory/` layer — `MemoryContracts`
  (18-token closed tag vocabulary, query/result/snapshot DTOs, `MemoryPolicySnapshot` whose
  behavioral defaults match the shipped XML while prompt guidance stays XML-only), `MemoryExtraction`
  (the single tag/keyword/importance/
  excerpt mechanism shared by deposit and recall, with ~60-word stopword filtering and
  none/n/a/unknown sentinel handling so `royal_title=none` never tags or keywords a memory),
  `MemoryRecallSelector` (seeded recall gate, saturated tag/keyword similarity with half-life decay
  and a floor, same-day and self-event guards, quarter-strength anti-repetition cooldown, 1-hop
  spreading activation that excludes direct matches and same-source fragments, age-band rendering
  that drops whole picks rather than truncating them), `MemoryEvictionPlanner` (stale rule, core
  exemption plus core cap, per-pawn and colony-wide caps with deterministic retention/createdTick/
  recallCount/id ordering, never mutating input), and `MemoryContextPrompt` (`MemoryContext` source
  token + Compose mirroring the narrative field). Added the persistence building blocks — the
  saved `MemoryFragment` model (additive Scribe keys, PostLoadInit repair) and
  `PawnMemoryRepository` (saved master list plus rebuilt per-pawn/deposit-key indexes, idempotent
  per pawn+source-event deposit) — and the tuning surface: `DiaryMemoryTuningDef` +
  `1.6/Defs/DiaryMemoryTuningDef.xml` with `DiaryMemoryPolicy.Snapshot()` copying it into the pure
  snapshot, plus English and Russian DefInjected files for the label, age bands, and prompt
  instruction. New `tests/PawnMemoryTests` pure suite (264 assertions, no RimWorld/Verse/Unity/DLC
  assemblies) covers the extraction tables and keyword normalization, golden scoring math,
  saturation/decay/floor, min-age and cooldown boundaries, tie-breaks and self-recall, the Yorick
  spreading-activation bridge with hop exclusions, seeded gate flips, rendering age bands and
  character-budget drop order, determinism/no-mutation, every eviction rule, the prompt composer,
  and the default policy surface; the suite is registered in `.githooks/verify.ps1`. Debug rebuild
  is clean and the committed DLL carries the new (unused) types.

- **2026-07-19 — Closed the remaining Royalty Phase-4 correlation lifecycle defects found in review.**
  Title-memory release is now an ordered second phase after map and exact off-map mutation/title
  observers, so equal expiry windows cannot publish an ordinary Thought immediately before a richer
  fallback claims it. Pre-save handling consumes live unscribed mutation batches into their selected
  fallback, reconciles scanner-owned title changes before flushing unmatched memories, and remains
  before `diaryEvents` serialization; this prevents both save-and-continue duplicates and save/reload
  loss, including psylink-only owners. Combined psylink fallbacks claim their batch's title memory.
  Live-owner pruning now includes eligible pawns in caravans and travelling transporters. Ritual
  attendees cannot claim target-only mutation context, duplicate boundary completion is rejected, and
  saved observation fields roll back if bookkeeping fails before the active owner is consumed. The
  canonical bestowing token uses the shared sanitized selector and redundant mutation-kind work was
  removed. `RoyaltyContextTests` passes 318 assertions; the main and expanded 258-test RimTest
  assemblies build with zero warnings. The last executed loaded result remains 256/256; the two new
  save/expiry fixtures still require an in-game run. No save key, schema, DLC dependency, or localized
  player-facing string changed.

- **2026-07-19 — Hardened Royalty Phase 4 after the consolidated adversarial review.** Royalty-off
  loads now invalidate the saved availability marker synchronously, so a paused immediate resave
  cannot preserve stale DLC-readable state. The Royalty policy master switch and the canonical
  `ritualRoyal` setting now suppress the canonical ceremony page and mutation output without freezing,
  transferring, or replaying truth; combined title+psylink fallbacks choose the first independently
  enabled route, and disabled
  rituals also consume their matching delayed title memory. Unmatched title memories flush to the
  ordinary Thought pipeline before save instead of disappearing at the transient load reset. Ritual
  fanout still writes intended attendee perspectives, but only the mutated pawn's page receives the
  personal title/psylink context. Title-loss headers name the title that was lost. Expired ritual
  batches for dead/departed pawns are globally pruned on the coarse scanner
  cadence, while eligible pawns retain their one fallback. Harmony completion is retry-safe after
  partial downstream failure, pre-consumption exceptions cancel their active scope for scanner
  recovery, and empty Royalty thought-correlation hot paths avoid unnecessary ID work/list
  allocation. No save key or schema changed; transient mutation/thought owners remain
  deliberately unscribed and reset at `FinalizeInit`. `RoyaltyContextTests` now passes 316 assertions,
  and the expanded 256-test RimTest suite passes in game with real neuroformer-comp, disabled-ritual,
  reverse-order and production pre-save/Scribe-reload title-memory, actual component-key/nested-title,
  lifecycle-reset, corrected pre-mutation psylink baseline, same-tick loss-label, and safe DLC-off
  immediate-load-seam fixtures. Legacy test-only title/psylink scanners
  were removed; their loaded fixtures now drive the versioned production observer. The user-confirmed
  expanded loaded rerun passed 256/256; hands-on Phase-2/3/4 matrices remain pending.

- **2026-07-19 — Fixed the two blockers from Royalty Phase 4's second loaded run.** The rerun reached
  250/252 and confirmed the title-hook signature plus six-token ritual-catalog fixes. Its remaining
  title loss was a real same-tick dedup defect: promotion and loss used only pawn/faction/opened-tick,
  so the accepted promotion suppressed the distinct loss. Title keys now include transition and exact
  before/after Def names, preserving duplicate suppression for one repeated edge without merging two
  real mutations in a paused tick; pure Royalty coverage is 287 assertions. The other failure was a
  Phase-3 fixture collision with the loaded mod profile's equipment-removal patches, not a runtime
  Pawn Diary failure. That death-enrichment fixture now establishes its non-primary pending bond
  through Pawn Diary's exact observer and reports tracker/row diagnostics. Automated build verification
  is green, and the subsequent user-confirmed loaded rerun passed 252/252. Phase 4's automated loaded
  coverage is therefore green; the hands-on Phase-2/3/4 rows remain before R1 acceptance is complete.

- **2026-07-19 — Fixed the two blockers from Royalty Phase 4's first loaded run.** The loaded-game
  suite reached 249/252: Harmony rejected the private title postfix because its previous-title
  argument was named `currentTitle` instead of RimWorld 1.6's exact `prevTitle`, which also caused
  the real `SetTitle` promotion fixture to emit no page. The postfix now binds the exact vanilla
  parameter name. The third failure was fixture-only: the official-DLC catalog still expected the
  pre-Phase-4 four-token Royalty ritual family, so it now freezes all six narrow throne-speech,
  bestowing, and anima-linking tokens. Automated build verification is green; a loaded rerun remains
  required before Phase 4 or R1 acceptance can be claimed.

- **2026-07-19 — Implemented Royalty Phase 4 title and psylink correctness.** Defensively registered
  the exact private faction-title callback plus bestowing, anima-linking, and neuroformer cause
  boundaries; added exact gained/promoted/demoted/lost pages, per-faction immediate observation,
  title-loss scanner fallback, disabled-output advancement, and Royalty-inactive preservation.
  Bestowing/anima rituals now claim bounded before/after mutations and suppress matching progression
  duplicates; neuroformers own one cause-aware progression page; expired owners fail open once; and
  exact royal-title memories stage/claim/release without global ThoughtDef suppression. Added XML-owned
  tuning and prompt policy, append-only SoloImportant fields 107–112, English/Russian localization,
  prompt fixtures, Def/save smoke coverage, and focused loaded fixtures. Pure suites pass 283 Royalty,
  2,437 pipeline, 665 capture-policy, and 125 Narrative Continuity assertions; both runtime and
  RimTest assemblies build cleanly and were rebuilt. Phase-4 loaded execution and all hands-on
  matrices remain open, so R1 is code-complete but not yet acceptance-complete.

- **2026-07-19 — Confirmed the Royalty Phase-3 loaded suite fully green.** The corrected focused
  rerun passed all 244/244 loaded RimTests, including the real first-kill owner, delayed companion
  Tale release, disabled-output fallback, bonded-wielder death enrichment, and saved milestone flags.
  Pure baselines remain green at 245 Royalty, 2,290 pipeline, 665 capture-policy, and 125 Narrative
  Continuity assertions. This closes Phase 3's automated loaded coverage only: its hands-on acceptance
  matrix remains open, and Royalty R1 is not complete until Phase 4 passes.

- **2026-07-19 — Fixed two false-negative Royalty Phase-3 loaded assertions.** The expanded loaded
  run reached 242/244: milestone ownership, Thought fallback, save flags, and death enrichment all
  passed, while two combat-batch checks incorrectly searched `interactionDefName=talecombat`.
  Flushed Tale batches actually keep either the one source Tale Def or the XML `syntheticDefName` in
  that field and store `group=talecombat; batch=tale` in saved context. The fixture now queries those
  exact context fields, so it verifies the production batch contract without depending on batch size.
  Runtime behavior is unchanged; the subsequent focused loaded rerun passed 244/244.

- **2026-07-18 — Hardened Royalty Phase 3 after a consolidated adversarial review.** Vanilla's real
  `KilledMajorThreat` path can also record an ordinary melee/ranged/capacity combat Tale in the same
  `Pawn.Kill`; those eight exact double-pawn companion Tale shapes are now XML-owned, staged only in
  that matching kill call, claimed by an accepted canonical milestone, and released unchanged when
  the milestone is disabled/rejected. The nonexistent vanilla `KilledMan` policy row was removed.
  Persona kill-Thought correlation is now lossless and one-shot: it de-duplicates exact Def signals,
  fails open at its defensive 128-signal cap, carries only still-missing expected Defs while the
  60-tick active scope remains open, consumes each once, and failure-isolates every fallback submission.
  Wielder death now retains a still-coded live separated/non-primary bond; saved `recorded=true`
  repairs the implied `observed=true` invariant; no-Royalty kills avoid a needless list allocation;
  and the prompt fixture now mirrors the real Tale class/source label. Pure coverage is 245 Royalty
  and 2,290 pipeline assertions. RimTests now exercise delayed Tale flush, a same-tick second kill,
  disabled-output fallback, non-primary wielder death, and both save-flag states; they compile, with a
  fresh loaded run still pending.
  The developer prompt fixture now uses the same `tale; label; taleClass` ordering and capitalization
  as production context output; neutral death templates document why trait meanings remain omitted.

- **2026-07-18 — Fixed the first loaded Royalty Phase-3 milestone failure.** The supplied loaded run
  passed 240/241 tests and exposed that vanilla records `KilledMajorThreat` inside
  `Pawn.DoKillSideEffects`, before `Pawn.Kill` reaches `health.SetDead()`. Persona qualification now
  treats the already-matched, balanced `Pawn.Kill` scope as the death proof at that pre-dead Tale
  callback instead of reading the necessarily-false later pawn state. The existing real
  major-threat RimTest remains the regression; a fresh loaded rerun is pending.

- **2026-07-18 — Implemented Master Wave 5 / Royalty Phase 3 persona combat and death integration.**
  The first configured qualifying Tale now becomes one canonical solo killer-POV
  `PersonaWeaponFirstConsequentialKill` page only when the exact coded persona weapon is the killer's
  current primary inside the matching `Pawn.Kill` scope. Observed truth advances independently from
  durable page acceptance, so disabled/rejected output cannot retell a later kill as the first. Exact
  structural persona-trait `killThought` signals are staged across an XML-owned 60-tick callback-order
  window, claimed only by the durable milestone, and otherwise released once through ordinary Thought
  capture. Bonded-wielder death now snapshots persona context before vanilla `UnCode` and enriches the
  existing Tale/fallback death page without a standalone bond-ending duplicate. Added the exact
  Royalty-gated Tale group/prompt, append-only solo/death template fields, English/Russian localization,
  prompt fixture, pure regressions, and compiling real major-threat/repeat/death RimTests. Pure suites
  pass 226 Royalty, 665 capture, and 2,265 pipeline assertions. The user also confirmed the Phase-2
  automated loaded suite green; its manual rows remain explicitly open, while Phase-3 loaded execution
  and manual acceptance remain pending. Royalty Phase 4 and the R1 release gate were not started.

- **2026-07-18 — Repaired the Royalty loaded fixture's paused-tick simulation.** The late-visibility
  test now clears the production one-tick free-colonist snapshot before simulating the next scheduled
  reconciliation. Previously the pawn was spawned and reconciled in one paused tick, so the fixture
  kept reading the legitimate pre-spawn cache and falsely reported that the historical bond was not
  adopted. Failure output now includes the observed row phase, kill-consumption flag, and pawn ID.

- **2026-07-18 — Fixed the loaded Royalty late-visibility acceptance failure.** Persona
  reconciliation no longer baselines a newly visible historical bond and immediately reuses that
  same first observation as not-primary separation evidence. The first sight now remains an active,
  consumed historical baseline; a later reconciliation can begin the normal XML-timed separation
  path. The focused loaded fixture now covers both halves of that cadence.

- **2026-07-18 — Adversarially hardened Royalty Phase 2 and mechanitor lifecycle edges.** Persona
  reconciliation now silently adopts historical coded weapons first exposed after the global old-save
  baseline, persists steady-primary observation ticks, and requires the exact live coded weapon before
  N3-R labels a saved bond current. Persona collection now follows vanilla's retained
  `equipment.bondedWeapon` pointer instead of scanning every equipment/inventory item, Royalty policy
  snapshots are cached per active language, and malformed modded psylink getters fail soft. Removed the
  dead bonded-thought correlation path and XML window because vanilla bonded thoughts are situational,
  not `MemoryThoughtHandler` memories; the pure owner contract remains for Phase-3 kill memories.
  Mechlink add/remove hooks now baseline unspawned generation/restoration silently while preserving
  real spawned first operations, and the Overseer fixture uses vanilla's mech-to-controller direction.
  Added pure regressions for live bond visibility and primary timestamps plus focused RimTests for real
  persona coding, late visibility, `UnCode`, all six Royalty hooks, and unspawned mechlinks. Royalty
  tests pass 211 assertions, pipeline tests pass 2,195, and both core/RimTest Debug DLLs build; the full
  loaded Royalty acceptance matrix remains pending.

- **2026-07-18 — Repaired failures exposed by the supplied all-DLC RimTest runs.** Exact
  mechlink installation and removal contexts now include their canonical
  `mechanitor_moment=mechlink_installed` / `mechanitor_moment=mechlink_removed` fields instead of
  exposing only the controller label. The frozen official-DLC interaction-group matrix now includes
  all four Royalty-gated rows: `ritualRoyal`, `progressionRoyalTitle`, `progressionPsylink`, and
  `personaWeaponLifecycle`. Reflection prompt fixtures no longer mistake legitimate voice rules that
  mention `[[speech]]` while forbidding it for an appended direct-speech instruction; they assert the
  actual adapter/template channel with an injected sentinel. The follow-up run confirmed the
  mechanitor repair but stopped at 232/236 before those corrections; the next run reached 235/236 and
  exposed one last fixture assumption that every official DLC group has exact def-name keys. The
  catalog fixture now explicitly validates `ritualRoyal`'s intended four-token throne-speech/anima
  classifier. A fresh loaded rerun remains pending, and no Biotech B1 or Royalty Phase 2 manual
  acceptance row has been marked passed.

- **2026-07-18 — Implemented Master Wave 5 / Royalty Phase 2 (persona lifecycle pages; loaded
  acceptance pending).** Added defensive Royalty-only persona coding, equipment, destruction,
  map-removal, and cleanup registration; exact formation/transfer epochs; silent short-swap handling;
  independent elapsed separation/recovery reconciliation; destruction ownership; and state-first
  persistence that prevents disabled-group catch-up. Exact bonded-thought side effects are now staged
  and claimed only by an accepted lifecycle page, with ordinary Thought fallback otherwise. Added the
  `PersonaWeapon` catalog/Spec/signal/domain, bounded prompt-safe context, the package-gated exact
  lifecycle group, four exact event prompts, append-only `SoloImportant` fields, English/Russian text,
  four prompt fixtures, and N3-R bond evidence attachment. Pure suites pass 205 Royalty, 660 capture,
  and 2,196 pipeline assertions; all XML parses and the Debug DLL builds. The new loaded-game matrix
  remains unchecked, so Phase 2 is not acceptance-complete; Royalty Phases 3–4, the R1 release gate,
  Wave 5, and every previously open Biotech B1 manual acceptance row remain open.

- **2026-07-18 — Implemented Master Wave 5 / Narrative N3-R core (provider/evidence dependency).**
  Replaced the fixed Royalty provider stub with pure bounded persona-bond and faction-title snapshot
  contracts. Persona candidates require the exact frozen bond arc or weapon subject; title candidates
  require the exact POV pawn plus Royalty title-domain or authority/status/duty evidence, preventing
  generic rank context on unrelated Biotech identity pages. Added shared title-transition evidence,
  guarded current Phase-1 bond/title snapshot projection, deterministic ordering/caps, and XML-owned
  DefInjected English/Russian provider prose with empty safe fallbacks. Existing event-time builders
  accept the snapshot, but no existing source emits Royalty evidence, so behavior remains inert until
  Royalty Phase 2/4 attaches a canonical owner. `RoyaltyContextTests` passes 196 and
  `NarrativeContinuityTests` 125 assembly-free assertions; Debug builds cleanly. This slice adds no
  Harmony hook, page source, setting, save field, court-pressure provider, or loaded-game acceptance
  claim. Wave 5, Royalty Phases 2–4, and the earlier Biotech B1 manual rows remain open.

- **2026-07-18 — Implemented Master Wave 5 / Royalty Phase 1 (page-silent foundation).** Added
  guarded persona weapon/structural trait, per-faction title/duty, and psylink projections; versioned
  deep-scribed global persona and nested per-pawn title observation state; deterministic malformed,
  duplicate, ordering, and cap normalization; conservative silent old-save bond/title/psylink
  baselines; Royalty-inactive baseline preservation; and resettable plain future-correlation shells.
  Existing persona bonds mark historical first-kill ownership consumed, so the upgrade cannot invent
  a later “first.” `RoyaltyContextTests` now passes 187 assembly-free assertions, and the focused
  Scribe/no-DLC RimTest fixture compiles. This slice adds no Royalty Harmony hook, page, provider,
  prompt, setting, dependency, or player-visible behavior, and does not claim a loaded-game fixture
  run. Wave 5, the R1 gate, and the earlier Biotech B1 manual rows all remain open.

- **2026-07-18 — Implemented Master Wave 5 / Royalty Phase 0 (contract-only).** Added plain R1
  persona weapon/trait/bond, faction-title, psylink, cause-scope, and ownership contracts; mapped
  persona continuity to the frozen `royalty-persona|<weaponThingId>|<bondEpoch>` shared arc grammar;
  added an XML-owned `DiaryRoyaltyPolicyDef` with safe detached fallbacks; and implemented pure
  deterministic lifecycle, meaningful separation/recovery/end, structural trait selection, first
  consequential milestone, title transition, and title/psylink dedup decisions. The new assembly-free
  `RoyaltyContextTests` suite passes 164 assertions. This slice adds no hooks, live Royalty reads,
  Scribe state, provider, page, prompt, setting, dependency, or player-visible behavior; Royalty
  Phase 1 and loaded-game acceptance remain pending, and the earlier Biotech B1 manual gates remain
  open under the explicit scheduling exception.

- **2026-07-18 — Corrected the loaded mechanitor combat regression fixture.** The real RimWorld 1.6
  `KilledMelee` Tale requires killer, victim, and weapon arguments; the adversarial friendly/hostile
  combat RimTest now supplies the base-game club Def instead of failing during Tale construction.

- **2026-07-18 — Closed the Phase-6 adversarial review and test gaps.** First control/combat truth now
  advances even while output is disabled; combat requires a hostile target; old-save mech tenure begins
  at Pawn Diary's observation instead of vanilla's older relation tick. Completed loss rows recycle at
  the cap without evicting active ownership, repeated scans normalize once per controller, and the death
  hot path type-narrows before mechanitor work while nested scope releases balance only in the finalizer.
  Boss calls now save the exact spawned pawn ID, so overlapping same-kind calls resolve one controller;
  ambiguous legacy deaths fail closed. Added pure cap/tenure/cross-controller regressions, real disabled-
  output/friendly-fire/death-scope/patch RimTests, exact boss-ID Scribe coverage, young-colony DLC-off
  execution, and RimTalk registry plus active-preset attachment assertions; rebuilt both committed DLLs.

- **2026-07-18 — Implemented Biotech Phase 6 mechanitor lifecycle (live acceptance pending).** Added
  silent versioned old-save baselines and bounded per-controller mech tenure/boss-call state; exact
  mechlink install/removal, first Overseer relation, first configured controlled-mech combat, custom-
  named or 15-day mech loss, and confirmed boss call/defeat chapters. Combat Tale ownership fails open,
  death-driven mechlink removal stays silent, and boss defeat never invents the final attacker. Added
  XML-owned Tale roles/tenure/caps, one Biotech-gated Progression group, English/Russian localization,
  67 new standalone assertions, real-mechlink/patch-audit RimTests, Scribe coverage, docs, and rebuilt DLLs.

- **2026-07-17 — Synced the loaded official-DLC catalog fixture after the Phase-5 package gate.** An
  all-DLC RimWorld 1.6.4871 run passed 227/228 loaded tests; the sole failure showed that the fixture's
  expected official-group set omitted the intentionally Biotech-gated `progressionXenotype` row.
  Added that existing production row to the matrix. The same run confirms the corrected reimplant
  replay and the new N3-B salient identity context/key/evidence assertions; no production behavior
  changed. Base-only, RimTalk, and Odyssey save Phase B/C branches remain separate runs.

- **2026-07-17 — Added the first N3-B salient-gene continuity lens (loaded acceptance passed).**
  Existing `GeneIdentityChanged` pages now attach exact-subject identity evidence and may select one
  XML-worded lens from Phase 5's already-chosen leading gene theme. A stable gene Def candidate key is
  persisted through ordinary Narrative Continuity history, activating the shared repetition penalty
  without another gene scan, membership list, hook, page owner, writer, or save field. Extended pure
  provider/policy coverage and the real implant RimTest assertion; rebuilt both committed DLLs. The
  supplied all-DLC loaded run passed the new assertion.

- **2026-07-17 — Automated the remaining Biotech B1 acceptance behavior.** Expanded loaded growth
  coverage through real age-7/10/13 letters, multiple passions, nickname/responsibility changes,
  auto-resolution, postponed-owner Scribe recovery, and live pre-cap rejection/recovery. Added a
  reflection-only loaded RimTalk shared-memory smoke and a base-only frozen growth/birth maintenance
  fixture, both with duplicate/replay assertions. Split the acceptance record into in-game automated
  runs and the genuinely manual visual UI/cross-launch checks. No production behavior changed; rebuilt
  the committed RimTest assembly, with the new in-game profile runs still pending.

- **2026-07-17 — Fixed adversarial-review findings in Biotech gene compatibility.** Exact live gene
  membership now remains complete up to the fixed 2048-row defensive ceiling; only the persisted XML-
  bounded baseline is truncated, carries a new frozen `geneObservedMembershipTruncated` key, and never
  infers membership deltas from an incomplete window. Bumped that baseline to version 2 so old bounded
  rows silently rebaseline, and invalidated comparison state on DLC-off load so save-without-Biotech
  then re-enable cannot create a stale catch-up page. Fixed nested Ability ownership to continue past a
  nonmatching inner scope, contained every local Ability patch phase, made exact-event dedup keys
  tick-independent, simplified active-fact admission, and package-gated the gene-identity group. Added
  pure cap/version/dedup/XML regressions plus compiled RimTests for nested scopes, disabled-output
  advancement, DLC-baseline invalidation, independent live membership, explicit no-DLC skips, the new
  Scribe marker, and populated legacy progression rows. Rebuilt both committed DLLs.

- **2026-07-17 — Hardened the Biotech reimplant replay fixture from live evidence.** A RimWorld
  1.6.4871 rev591 loaded-game run passed 217/218 RimTests and reached the real
  `GeneUtility.ReimplantXenogerm` hook, canonical recipient page, bounded context, and enclosing
  Ability claim. Its immediate replay then correctly produced `PawnDiary_DeathFallback`: vanilla
  makes a second `XenogermReplicating` hediff lethal while the caster's genes are still regrowing.
  The fixture now models the normal cooldown completing by removing only vanilla's temporary
  reimplant hediffs before replay, preserving the strict no-new-event assertion. Production behavior
  and assertions are unchanged. The later 227/228 all-DLC run confirmed the corrected replay; its
  only failure was the now-synced official-DLC catalog expectation.

- **2026-07-17 — Finished Biotech Phase 5 salient-gene integration (live acceptance pending).** Added
  pure exact membership diff/fallback-significance policy and separator-safe bounded context formatting;
  the Progression prompt now receives at most four selected gene themes and never full membership.
  Defensively registered the verified RimWorld 1.6 `GeneUtility.ImplantXenogermItem(Pawn, Xenogerm)`
  and `ReimplantXenogerm(Pawn, Pawn)` boundaries, with before/after snapshots, immediate baseline
  advancement, recipient-only `GeneIdentityChanged` ownership, and no-op replay rejection. A same-call
  ability scope suppresses the generic reimplant Ability page only after canonical dispatch commits and
  is reset at every Game boundary. The slow observer now emits stable xenotype changes or XML-significant
  membership changes (`geneMinimumFallbackChanges=2`) while old-save initialization, disabled output,
  one-gene churn, suppression recalculation, and localized label changes remain silent. Added localized
  page/group prose, an enriched prompt fixture, pure transition/context/XML assertions, and compiled
  real-body RimTests for both exact vanilla methods plus ability ownership. Updated architecture,
  coverage, roadmap, and manual save/DLC matrices; the new live fixtures are not yet executed.

- **2026-07-17 — Added Biotech Phase 5 guarded gene snapshots and silent saved baselines.** Added
  `DlcContext.TryCaptureGeneIdentity`, which double-gates live reads, separates bounded installed
  membership from active salience facts, uses direct `GeneDef` fields/base descriptions, and remains
  inert without Biotech. Added nested `GeneIdentityObservationState` with frozen version/xenotype/
  membership Scribe keys, deterministic normalization, an XML-owned 512-row configured cap and fixed 2048-row
  corruption caps. The progression cadence advances this observation even when Progression output is
  disabled while retaining the old scalar xenotype keys for migration, and initially emitted no gene page. Added
  pure baseline tests plus compiled RimTests for guarded projection, inactive-DLC silence, old-save
  initialization, disabled-output advancement, and real Scribe round-trip. The later Phase 5 completion
  entry above adds exact mutation hooks, fallback output, and prompt projection.

- **2026-07-17 — Started Biotech Phase 5 with the pure salient-gene foundation.** Under an explicit
  user-directed scheduling exception, Phase 5 may proceed while Phase 4 manual rows 3, 4, 7, 9, and
  10 remain open; those rows are not waived and B1/Wave 3 is not marked complete. Added detached gene
  identity and exact mutation contracts plus an XML-driven deterministic selector that prioritizes
  added/removed facts, favors category diversity, supports force/exclude corrections, filters hidden
  or inactive bookkeeping, caps output at four themes, and bounds cleaned text. Extended the Biotech
  policy Def/snapshot and assembly-free suite to 410 passing assertions. This first step adds no live
  gene reads, save keys, Harmony hooks, prompt fields, pages, or DLC dependency.

- **2026-07-17 — Reconciled Odyssey O1 acceptance status before continuing Biotech Phase 4.** Updated
  the Odyssey and master plans plus the coverage record to reflect the completed live evidence already
  recorded in the save-compatibility runbook: clean base-only skips, passing real cancellation and
  cross-layer landing, passing Phase A/B/C reloads, exactly-once output, no lifecycle resurrection,
  and removal of both reserved saves. The broader Odyssey component/prompt-flow rerun remains a
  separate follow-up, O2/O3 remain unstarted, and Biotech Phase 4 rows 3, 4, 7, 9, and 10 remain open.
  No production behavior or DLC dependency changed.

- **2026-07-17 — Closed the live main-menu rerun and inactive-Anomaly fixture failure.** The rebuilt
  Odyssey runtime suite produced all five intended no-host skips at the main menu with no Odyssey
  error, closing the final live rerun gap from the prior hardening. The same all-suite log exposed one
  separate test-only contradiction: EVT-22 tried to verify the Anomaly-inactive monolith no-op only
  after a helper had already required Anomaly to be active. Split loaded/enabled Def validation from
  package availability so the fixture now tests exact monolith chapters with Anomaly and inert loaded
  Defs without it. Production capture behavior and DLC dependencies are unchanged.

- **2026-07-17 — Executed and hardened the Odyssey live acceptance fixtures.** Ran RimWorld
  `1.6.4871 rev591` in English with isolated base-only and Odyssey-only RimTest profiles. The base
  profile produced all five explicit Odyssey-inactive runtime skips without Pawn Diary Odyssey
  patch/XML/type-initializer errors. The Odyssey profile passed real takeoff cancellation,
  cross-layer travel/landing exactly-once behavior, and the full Phase A/B/C save/reload sequence;
  Phase C proved no lifecycle resurrection and removed both reserved saves. The live run also exposed
  test-only null-target failures when RimTest started the loaded-game suite at the main menu, so the
  runtime fixture now checks for `Current.Game` and `DiaryGameComponent.Instance` before using `Find`
  or instance-field reflection and logs a visible skip instead. Production behavior and DLC
  dependencies are unchanged; a post-fix live main-menu rerun remains pending.

- **2026-07-17 — Added real-runtime Odyssey lifecycle and phased save/reload hardening.** Added a
  focused RimTest suite that enters the actual `InitiateTakeoff`/`InitiateLanding` methods so Pawn
  Diary's installed Harmony prefixes receive live payloads, executes vanilla cross-layer `TravelTo`
  and successful private `LandingEnded`, and verifies pre-rewrite origin survival, cancellation,
  exactly-one landing page/marker, replay rejection, and failure-safe cleanup. Added a genuine
  Phase A/B/C disposable-save flow for the frozen active-journey/history keys, bounded history, trust,
  home tenure, launch cooldown, transient pending-state omission, post-reload landing completion, and
  second-reload non-resurrection. RimTest cannot keep one synchronous test alive while RimWorld
  disposes/reloads the `Game`, so the two exact manual continuation steps are documented. No production
  behavior or DLC dependency changed; rebuilt the committed RimTest DLL.

- **2026-07-17 — Closed the Odyssey adversarial-review findings.** Preserved the true pre-rewrite
  `TravelTo` origin tile, prevented pre-feature mid-flight baselines from inventing elapsed duration or
  `long_journey`, and made pair landing prompts project the correct pilot/copilot/crew role separately
  for each POV. Launch policy now persists schema-2 `lastLaunchPageTick` and current-home tenure,
  commits cooldown only after a ritual page exists, permits the XML long-held-home bypass once per
  tenure, and cannot spam pages after destination cancellation. Hidden destination detail and duplicate
  mobile-home biome text are suppressed, Russian gravship terminology now follows the glossary, and
  stable ship IDs are separator-hardened on load. Expanded pure coverage to 158 Odyssey, 651 capture,
  and 2,028 pipeline assertions; expanded RimTests for the TravelTo prefix, exact outcome enrichment,
  pair-role mappings, and additive Scribe fields. Rebuilt both committed DLLs.

- **2026-07-17 — Corrected two loaded Odyssey RimTest fixture assumptions.** The official-DLC catalog
  now recognizes `ritualGravship`'s intentional narrow token classifier instead of requiring an exact
  ritual defName, and the localized Odyssey prompt fixture supplies its isolated copilot through a
  dev-only explicit-partner seam rather than depending on the player's live colonist ordering. Normal
  prompt-suite UI partner selection and gameplay behavior are unchanged.

- **2026-07-17 — Implemented Odyssey O2's XML-first environmental slice.** Removed the ineffective
  `Flooding` MoodEvent matcher and added an Odyssey-gated, map-scoped `ThingPresent` observed
  condition for the installed `SeasonalFlood` ThingDef. It uses XML-owned scan/end hysteresis,
  restart cooldown, decay, and weight; it only shades authorized prompts and never creates start/end
  pages. Added exact-string `GravNausea` prompt context with XML chance/weight/severity, plus English
  and Russian Keyed/DefInjected text. Assembly-free contracts lock the package gate, matcher,
  no-page policy, tuning, and localization; the Odyssey loaded fixture locks active/inactive Def
  loading and exact projected policy. Existing visible-condition and vacuum owners remain unchanged.
  Also verified and implemented exact negative landing consequences: startup defensively discovers
  and postfix-patches concrete `LandingOutcomeWorker.ApplyOutcome` overrides, then a successful worker
  correlates its exact Def and localized visible label into the same transient landing transaction.
  `landing_outcome` is required when present across Full/Balanced/Compact, no second page or save field
  is added, and a changed hook fails soft. Pure Odyssey coverage now passes 126 assertions, pipeline
  coverage 2,019, and the loaded fixture asserts all four shipped overrides. Life support remains
  behind its documented O2 feasibility gate.

- **2026-07-17 — Completed Odyssey O1.5 automated hardening and delivery prep.** Found and closed the
  prompt-boundary gap that left O1.4's frozen landing schema off the model-facing templates. Appended
  English/Russian Odyssey fields to the important pair/solo templates; made phase, reasons, duration,
  solo journey role, ship, origin, and destination mandatory under Full/Balanced/Compact; retained
  optional site/biome/crew/roughness/launch-quality evidence; and kept stable IDs, location keys, and
  ticks event-internal. Added a localized Odyssey landing entry to the dev prompt suite plus pure and
  loaded preset assertions, an explicit no-pawn `TileSettled` regression, and an eligible-writer
  routine-hop no-page fixture. Existing tests cover intent-only cancellation, idempotent TravelTo,
  major-site/writerless ownership, mid-flight Scribe, old-save distrust, and guarded no-DLC reads.
  Automated XML/localization/pure/build verification is complete; the combined in-game Odyssey save
  matrix remains intentionally deferred until the batch is complete.

- **2026-07-17 — Completed Master Wave 4 / Odyssey O1.4 landing event and launch truth.** Added the
  concrete `GravshipJourney` catalog payload/Spec/signal/domain and Odyssey-gated landing group with
  English/Russian role and fallback prose. Successful vanilla `LandingEnded` now creates exactly one
  novelty-authorized solo or pair-shaped landing event (maximum two POVs), freezes the bounded landing
  schema plus source-owned ship/place Narrative Continuity evidence, and commits page cooldown/dedup
  history only after the event exists; routine, disabled, duplicate, or writerless landings still
  advance observation history without a page. Retuned `ritualGravship` to departure-only truth,
  package-gated its settings row, and applied the Odyssey-only XML writer cap/prior-departure cooldown
  without changing other rituals. Added pure catalog/launch policy, XML/domain, transaction, and
  loaded pair-event fixtures. The focused RimTest assembly builds; its in-game run is intentionally
  deferred to the combined Odyssey acceptance pass. O1.5 hardening is next.

- **2026-07-17 — Completed Master Wave 4 / Narrative N2-O provider and reference dependency.** Replaced
  the Odyssey provider stub with one pure bounded mobile-home candidate, backed by a guarded event-time
  snapshot that requires vanilla's exact pawn-on-gravship predicate plus visible ship/location facts.
  Existing canonical Biotech growth and birth pages can now select that frozen home lens without new
  page ownership or cross-DLC live objects; matching committed journeys carry exact-arc relevance.
  Added DefInjected English/Russian provider prose and pure `departed`/`arrived`/`returned` ship/place
  evidence factories for O1.4. Narrative Continuity now passes 104 assembly-free assertions, and
  loaded RimTests cover the fixed provider list, knowledge/connection gates, and exact-onboard live
  boundary. Landing emission remains XML-disabled; O1.4 is next.

- **2026-07-17 — Completed Master Wave 4 / Odyssey O1.3 state-only lifecycle hooks.** Added guarded
  exact-signature capture for takeoff intent, vanilla `GravshipUtility.TravelTo` commit, and landing
  start, plus defensive manual registration of private `LandingEnded`. Travel commit now creates one
  detached saved journey, establishes bounded trustworthy departure history, correlates optional
  launch information through cached reflection, and rejects replay by journey ID. Successful landing
  advances history only after vanilla returns and then clears transient/active state. The XML-owned
  `landingPageEnabled` switch remains false and no Odyssey event sink exists, so O1.3 creates no page.
  Pure Odyssey coverage now passes 112 assertions; RimTests pin runtime signatures, actual Harmony
  registration, idempotent component flow, and zero event/page mutations. Narrative N2-O followed.

- **2026-07-17 — Completed Master Wave 4 / Odyssey O1.2 guarded context and persistence.** Added the
  XML-owned `Diary_Odyssey` policy with exact string-only biome/site mappings, a DLC-gated live
  map/gravship adapter, detached versioned active-journey and bounded travel-history Scribe state under
  `odysseyActiveJourney` / `odysseyTravelHistory`, silent untrusted old-save baselining (including an
  incomplete row for a ship already travelling), and English/Russian mobile-home surroundings only
  when vanilla confirms the exact pawn is onboard. Added `PawnDiaryOdysseyJourneyFlowTests` for loaded
  policy, active/inactive collection, onboard scoping, real-Scribe round trips, missing/corrupt keys,
  and history caps. O1.2 adds no lifecycle Harmony hook, event type, settings row, or Odyssey page;
  O1.3 is next.

- **2026-07-17 — Completed Odyssey O1.0-O1.1's inert journey-policy foundation.** Reconfirmed the
  installed RimWorld 1.6.4871 gravship signatures and froze Odyssey package/gate names, save keys,
  arc/dedup grammar, event/group names, schema tokens, page ownership, and silent old-save semantics.
  Added DLC-free contracts plus pure exact location classification, landing reason/cooldown planning,
  bounded idempotent history, deterministic pilot/copilot/crew selection, and prompt-safe context;
  `DiaryOdysseyPolicyTests` passes 88 assertions and is wired into the verification hook. No Def,
  save field, settings row, Harmony patch, or page source exists yet; O1.2 is next. The Biotech B1
  acceptance runbook now records user-confirmed passes for rows 1, 2, 5, 6, and 8, with rows 3, 4,
  7, 9, and 10 retained as explicit TODOs, so Biotech Phase 4 remains open.

- **2026-07-17 — Added focused automated coverage for the remaining shipped DLC integrations.**
  Loaded-game progression fixtures now drive real Royalty psylink and title state through the private
  scanners, assert exact pages, and reject repeats. Ideology and Anomaly gained internal copied-fact
  ritual fixture seams plus four-perspective production fan-out, duplicate-role collapse, and colony
  dedup assertions without starting a real player ritual. The pure pipeline suite now freezes the
  existing pre-O1 Odyssey gravship/orbital/vacuum/weather/observed-condition/enchantment XML routes.
  Core runtime behavior is unchanged outside the internal test seams.

- **2026-07-17 — Corrected the multi-preset birth prompt RimTest fixture.** The loaded-game suite's
  final 192/193 run showed that its Full/Balanced/Compact loop assigned three different children one
  birther-owned family arc, so production correctly deduplicated the second birth. The fixture now
  mirrors the child-owned family arc contract: each preset exercises a distinct canonical birth while
  the existing same-child replay test continues to prove once-only suppression.

- **2026-07-16 — Fixed canonical birth prompt routing and exact CreepJoiner fixture state.** The
  second all-DLC runner reached 191/193 and proved the loaded template fields were current: canonical
  birth context carried `birth_outcome=healthy` but lacked its Tale-domain marker, so policy recovery
  misclassified the page as an ordinary interaction and selected `PairDefault`, dropping all B1 prompt
  fields. `BirthContextFormatter` now emits `tale=BiotechFamilyBirth`, with pure and loaded-game
  assertions pinning the domain/importance route. The Anomaly fixture now installs a temporary vanilla
  `Pawn_CreepJoinerTracker` backed by a real loaded form, matching the actual `Pawn.IsCreepJoiner`
  predicate, and restores the original tracker in `finally`. The duplicate Pawn Diary package reported
  by RimWorld remains an independent test-environment error and must be removed before the acceptance
  rerun.

- **2026-07-16 — Corrected first all-DLC RimTest findings.** The first loaded-game B1/DLC run passed
  190 of 193 tests and exposed two fixture assumptions plus a birth prompt-routing failure initially
  confounded by a mixed-install problem. The Anomaly positive fixture stopped sending the specialized
  pawn kind through invalid generic pawn generation. The official-DLC catalog now preserves exact keys
  for the six specialized Anomaly
  ritual families while explicitly pinning the intentional `PsychicRitual` token fallback. Birth
  prompt coverage still requires child/outcome/method/role facts at every detail preset, but now first
  verifies that the loaded `PairImportant` template contains those fields and explains a missing-field
  XML/DLL mismatch. The later 191/193 run isolated the remaining domain-routing defect above.

- **2026-07-16 — Expanded automated DLC compatibility matrix.** The loaded-game DLC-safety fixture
  now covers the compatibility layer beyond absence-only guards: null pawns remain safe even with DLC
  flags active; absent DLC fields are rejected at the final prompt/public-summary boundary; installed
  Biotech, Royalty, and Ideology exercise disposable real xenotype/title/ideoligion/precept state and
  an eligible role where the colony permits one; an installed Anomaly run temporarily installs a
  vanilla creepjoiner tracker with a real loaded form on a disposable pawn; title/role prompt-enchantment collectors get
  positive coverage; and the exact
  official-DLC interaction-group/event-window catalog is pinned to `ModsConfig`, package helpers, and
  settings visibility. Fragile growth, birth, monolith, unnatural-corpse, Ideology ritual, and psychic
  ritual runtime signatures are now release assertions. A public capture-capability fixture proves an
  optional adapter suppresses XML fallback only while ready and restores fail-open capture when
  cleared. True DLC disable/re-enable save transitions remain a cross-launch smoketest.

- **2026-07-16 — Biotech B1 loaded-game checkpoint coverage.** Five focused RimTests now replace
  large synthetic parts of the manual release matrix: the growth suite invokes vanilla
  `ChoiceLetter_GrowthMoment.ConfigureGrowthLetter` and `MakeChoices` through the installed Harmony
  hooks for a committed `NoTrait`/passion choice; growth and birth both capture the shipped loaded
  templates at Full, Balanced, and Compact while rejecting private Thing/arc/tier/correlation data;
  the birth suite flushes a saved unnamed owner through the real live-child naming poll and proves
  original tick/frozen context/once-only removal; and the component-Scribe suite preserves rows above
  the XML admission limit, rejects new ownership without eviction, then admits again after the count
  falls below the limit. The runbook's impossible “resolve one row above a full limit” wording now
  correctly requires enough resolutions to return below the admission threshold. RimTest and core
  assemblies build cleanly; the new loaded-game cases still need an in-game runner pass, and genuine
  mod-list/save transitions, UI, optional-mod gameplay, remaining birth routes, and speed-4 perception
  remain manual.

- **2026-07-16 — Second-wave adversarial bughunt fixes (Biotech ownership lifecycles).**
  `FindLivePawnByLoadId` and the newborn naming lookup now search caravans and travelling
  transporters (the same universe the loaded-save bootstrap uses) — previously a family that
  caravanned during the naming window read as nonexistent, silently discarding the promised
  canonical birth page after one day's grace, hiding caravanning supporters, and mis-reading the
  child as gone. A live pregnancy arc can no longer be closed `ended_unknown` while the birther is
  merely unresolvable, and baseline child arcs (which never observed a pregnancy hediff) are no
  longer mislabeled closed on their first maintenance pass — the silent close now requires that the
  arc actually observed a pregnancy/labor hediff. A growth letter postponed past the pawn's NEXT
  birthday no longer forfeits its canonical page: the open-letter hold now also covers the age-flip
  release, and `BeginBiotechGrowthChoice` falls back to the pawn's newest pending claim when the
  live age no longer matches (new pure `FindNewestForPawn`); the hold also ignores letters for dead
  pawns. Re-configuring an already-claimed growth letter at a full admission table now replaces its
  own row instead of destroying the claim and rejecting the replacement. Family arcs whose child
  died or grew up with unconsumable lesson evidence follow the ordinary retention countdown instead
  of living forever. The cached Biotech policy snapshot is language-keyed so a mid-session language
  switch cannot serve stale localized prose. New pure pins: repetition penalty/exact-arc
  exemption/ordinal-comparison/sole-candidate selection (`NarrativeContinuityTests` 74→82) and
  dead-child evidence retention (`DiaryBiotechPolicyTests` 379→381). Both DLLs rebuilt; all pure
  suites pass.

- **2026-07-16 — Four-axis DLC-work code review fixes (plan / lore / save-compat / correctness).**
  Save-compat: pending Biotech births, family arcs, and the naming poll are no longer gated on
  `ModsConfig.BiotechActive` (mirroring the growth side), so a save whose Biotech was later disabled
  flushes its promised canonical birth pages from the frozen event-time facts (birth-time name after
  the normal grace) and keeps compacting/pruning arcs instead of freezing them forever;
  `PendingBiotechBirthState` gained the same PostLoadInit self-repair as its siblings and
  `NarrativeEvidenceState.salience` now scribes with a `minor` default. Correctness: pending growth
  ownership no longer tick-expires while the pawn's vanilla growth letter is still open (growth
  letters never time out — expiring forfeited the canonical page and emitted a mistimed ordinary
  birthday), and the event-window settings gate no longer NREs when `PawnDiaryMod.Settings` is null.
  Narrative: the persisted anti-repetition history is now actually consulted — growth, birth, and
  event-window sources feed the selector the POV pawn's recent selection keys (newest hot pages, then
  archive), activating the XML `repetitionPenalty` that was previously inert. Performance:
  `DiaryBiotechPolicy.Snapshot()` is cached (it rebuilt ~12 lists on every social interaction and
  gained thought), and family-arc retention collects saved arc references in one pass instead of
  re-parsing every page per arc. Coverage: `NarrativeContinuityTests` is wired into
  `.githooks/verify.ps1` (it was passing but ungated); new `PawnDiaryBiotechComponentStateFixtureTests`
  RimTest suite round-trips the component's three Biotech lists under their production save keys
  (missing-key baseline, corrupt-row drop, and >2048 hard-ceiling truncation against real Scribe); the
  save-compat runbook gained the new scribed files in its touch list and a row 10 "Biotech removed
  mid-save" acceptance scenario. Pure suites and both DLL builds pass; RimTest suites need an in-game
  rerun.

- **2026-07-16 — Fixed the three remaining loaded-game RimTest failures.** (1) Writing-style,
  psychotype, and humor voice blocks now splice their rule into the localized frame verbatim
  (`DiaryPipelineAdapters.InjectVoiceRule`); the previous args-`Translate` path ran vanilla's
  `GrammarResolverSimple`, whose sentence-casing treats `:` as a sentence break and silently rewrote
  player/XML-authored rules ("sentinel: end…" → "sentinel: End…"). (2) Re-synced every stale English
  DefInjected entry with its Def XML (171 entries): prompt-template field labels had shifted by two
  after the external-prompt fields were inserted mid-list (mislabeling most English prompt fields,
  e.g. the setting rendered under "my last opener"), and the base/reflection system prompts had
  dropped the 2026-07-06 first-person-anchor and anti-slop lines; also restored the Title template's
  "diary entry to title" label. (3) Event-window pages now honor their settings Events row:
  `RecordEventWindowPhase` skips the page when the Interaction group matching the window defName
  (e.g. `eventWindowBirthday`) is disabled, which also lets the Biotech growth fallback consume
  baselines without releasing a Birthday page when both rows are off. Pure suites pass
  (`DiaryPipelineTests` 1,766; `LlmResponseParserTests` 94; `DiaryTextDecorationTests` 67);
  runtime DLL rebuilt.

- **2026-07-16 — Fixed Biotech Phase 4 adversarial-review findings.** Pending growth and birth caps
  now act as admission limits: normal load/maintenance preserves established ownership (including
  pre-cap saves and later XML reductions), while a full list rejects only the incoming owner so its
  ordinary Birthday/mature-birth source fails open. Centralized the shared 256-row default and 2048-row
  corruption ceiling, added exact-boundary/invalid-cap/hard-ceiling/admission regressions, and expanded
  the save-compat matrix for pre-cap overflow. The localized dev growth preview now uses production
  `broad`/`teacher` tokens instead of impossible `high`/`observed_teacher` values.

- **2026-07-16 — Continued DLC integration: Biotech Phase 4 automated hardening.** Important pair and
  solo prompt templates now project verified growth opportunity/choice, family-role, child, birth
  outcome/method, and exact participant facts; Full keeps all supplied details while Balanced/Compact
  retain the central B1 facts even with no optional budget. Stable IDs, numeric tiers, ticks, and
  correlation tokens remain out of model prompts. Added localized English/Russian growth and birth
  fixtures to the prompt test panel, repaired stale English indexed labels on both important templates,
  and pinned every important-template field index in both languages. Persisted pending growth and
  birth owners now have XML-owned 256-row defaults, a hard 2048-row ceiling, deterministic newest-row
  retention, and live overflow fail-open behavior. Added no-Biotech dependency, prompt-mode, projection,
  localization, and cap regressions (`DiaryBiotechPolicyTests`: 355 assertions;
  `DiaryPipelineTests`: 1,766 assertions), rebuilt and smoke-tested the SpeakUp/RimTalk bridges, updated
  the loaded-game acceptance runbook/plans/docs, and rebuilt the runtime DLL. The manual no-DLC,
  old-save, live growth/birth, and loaded-adapter matrix remains before Phase 4 can be marked complete;
  B2 has not begun.

- **2026-07-16 — Fixed Biotech Phase 3 adversarial review findings.** Normal labor now keeps
  `PregnancyLabor` and `PregnancyLaborPushing` on one family arc; same-birth siblings keep separate
  child arcs while inheriting the shared pregnancy/supporter evidence. Known incoming parents now
  require exact saved-ID matches; wildcard correlation is limited to missing incoming parents. Ritual
  classification now requires the performed `LordJob_Ritual`, and canonical
  ownership survives exceptions thrown by later third-party postfixes. Released fail-open mature
  signals retain their captured tick and insert before a same-call death boundary. Mature birth and
  miscarriage Def-name classifiers moved into `DiaryBiotechPolicyDefs.xml` with pure role/classifier coverage.
  Pending naming rows now deep-save event-time writer names, prompt context, calendar date, handwriting,
  and generation eligibility; delayed pages use those frozen facts and insert chronologically before a
  same-call final-death boundary. Tick-zero load normalization no longer discards valid pending births,
  the transient activity cache is game-component scoped, and the incorrect RimTest context assertion
  was repaired. Added labor/twin/parent/classifier/persistence/context/death-boundary regressions (332
  pure assertions), updated docs/coverage, and rebuilt runtime/RimTest DLLs.

- **2026-07-16 — Continued DLC integration: Master Wave 3 / Biotech Phase 3.** Added canonical
  family-birth ownership at RimWorld 1.6's exact `PregnancyUtility.ApplyBirthOutcome` boundary with a
  stack-safe prefix/postfix/finalizer correlation scope. Exact child/corpse outcome, pregnancy/
  surrogacy/growth-vat method, participant roles, birther death, and stable family identity now feed
  one localized solo or two-adult `BiotechFamilyBirth` page; the child is always the subject, never a
  POV. Mature `GaveBirth` and `BabyBorn`/`Stillbirth` signals are staged and either claimed or released
  in original order; an exact ritual-job reservation arbitrates the later childbirth-ritual postfix.
  Disabled, invalid, writerless, missing-hook, or thrown ownership fails open. Detached
  `pendingBiotechBirths` rows survive naming/save/load, refresh the current
  newborn/corpse name, preserve the original birth tick, normalize malformed/duplicate rows, and use
  hot/archive family+child context for durable replay rejection. Exact miscarriage closes/enriches its
  matched family arc; unexplained pregnancy disappearance records only silent `ended_unknown` state.
  Added source-owned bond-lifecycle evidence, English/Russian fallback text, pure policy/XML coverage
  (307 assertions), Scribe/no-DLC/signature fixtures, a loaded-component birth-flow suite, updated
  plans/docs, and rebuilt runtime/RimTest DLLs. Biotech Phase 4 compatibility and release hardening is
  next.

- **2026-07-16 — Fixed Narrative N2-B adversarial findings.** Family continuity now treats exact
  zero-count `Parent`/`ParentBirth` baseline rows as valid family connections, while prior recorded
  growth ages alone no longer invent directly observed upbringing for child-only arcs. Live growth
  snapshots now apply the player's global Full/Balanced/Compact context-detail preset before freezing
  selected lenses. Added pure family-state regressions, consolidated the duplicate Biotech policy
  documentation row, and rebuilt the runtime DLL.

- **2026-07-16 — Continued DLC integration: Narrative N2-B Biotech provider.** Added the first real
  guarded Narrative Continuity provider behind a fixed deterministic provider list; Royalty,
  Ideology, Anomaly, and Odyssey remain explicit empty stubs until their scheduled waves. Canonical
  Biotech growth pages can now select exact saved family continuity (known birth, directly observed
  childhood, or an exact current parent baseline) and the child's visible current non-Baseliner
  xenotype as optional frozen prompt lenses. The provider rejects inactive Biotech, child-only arcs,
  unrelated evidence, and unconnected POVs; it never lists genes, predicts xenotypes, infers feelings,
  creates fan-out, or adds a save field/hook. Candidate formats are XML/DefInjected-owned in English
  and Russian, malformed formats fail empty, pure provider coverage reaches 74 assertions, and the
  runtime/RimTest DLLs were rebuilt. Biotech Phase 3 canonical birth/naming is now the next Wave 3 slice.

- **2026-07-16 — Hardened optional-DLC capture after adversarial review.** Corrected the Biotech
  family observer to RimWorld 1.6's exact `HediffWithParents.SetParents(Pawn, Pawn, GeneSet)`
  signature, added a local Biotech gate to family-arc maintenance, and tied unresolved
  pregnancy/labor retention to the exact live hediff so interrupted or completed arcs can compact
  and expire. Canonical growth replay
  detection now searches hot and archived context by stable child ID plus age, including
  supporter-solo pages stored in an adult's diary. Anomaly monolith capture now distinguishes the
  private timer-driven call before vanilla clears its scheduled tick and suppresses that transition,
  avoiding false attribution to vanilla's random colonist while failing closed if the field changes.
  Added pure provenance/growth/retention regressions, a RimTest signature fixture, and rebuilt the
  runtime and RimTest DLLs.

- **2026-07-15 — Continued DLC integration: Master Wave 3 / Biotech Phase 2.** Added deep-scribed,
  stable-ID-only family arcs with exact pregnancy/labor correlation, living-child old-save baselines,
  and bounded deterministic normalization/retention. Exact `BabyPlay`/`Lesson*` PlayLog observations
  and accepted `GaveLesson`/`WasTaught` memories now update per-child/adult upbringing evidence before
  normal page settings, with cross-source pair/kind de-duplication. Canonical growth now attaches the
  family key, ranks exact parents/birth parents and demonstrably involved teachers, emits child solo,
  supporter solo, or child/supporter pair POVs, includes only localized qualitative upbringing bands,
  and advances summarized counters so later growth ages do not repeat old observations. Pregnancy/labor
  health context gains the same family key when exact Hediff identity matches. Added family Scribe and
  no-DLC fixture coverage, pregnancy/labor/activity/memory/retention pure coverage (247 assertions),
  updated plans/docs, and rebuilt runtime/RimTest DLLs. Narrative N2-B is next; canonical birth/naming
  remains Phase 3.

- **2026-07-15 — Continued DLC integration: Master Wave 3 / Biotech Phase 1.** Activated canonical
  age-7/10/13 growth ownership by extending the existing birthday patch and atomically registering
  the stable `ChoiceLetter_GrowthMoment.ConfigureGrowthLetter`/`MakeChoices` lifecycle hooks. Exact
  before/after snapshots now cover committed and auto-resolved growth; postponed letters persist as
  detached, normalized `pendingBiotechGrowthMoments` rows across save/load; missing hooks,
  correlation, mutation, or settings fail open to the mature Birthday route without scanning the
  letter UI. Completion emits at most one child-solo `BiotechGrowthMoment` with qualitative
  XML/DefInjected context and source-owned N1 identity evidence, then consumes all current trait keys,
  newly passionate skill milestones, and the saved 7/10/13 marker even when pages are disabled.
  Durable canonical pages repair damaged consumed markers rather than replaying/falling back. Added
  English/Russian growth fallback text, Scribe round-trip and no-DLC accessor coverage, a three-case
  loaded-component RimTest fixture, pending normalization/grace tests (218 pure assertions), current
  architecture/prompt/coverage plans, and the rebuilt runtime DLL. Family arcs/supporter POVs remain
  Phase 2; canonical birth remains Phase 3.

- **2026-07-15 — Began DLC integration: Master Wave 3 / Biotech Phase 0.** Froze the
  assembly-free B1 growth/family snapshot and mutation contracts; stable synthetic event Def names,
  complete `biotech-family|...` arc grammar, additive Scribe/context keys, and inert Event Catalog
  types; pure before/after growth diff, qualitative opportunity banding, deterministic supporter and
  birth-writer selection, bounded context formatting, and explicit legacy settings inheritance.
  Added the XML-owned B1 policy, exact package-gated growth/birth groups, English/Russian
  DefInjected and Keyed text, a 180-assertion standalone Biotech policy/XML suite, catalog contract
  coverage, documentation, and the rebuilt runtime DLL. No Biotech hook, saved state, provider,
  signal, or new page behavior is active; Phase 1 remains the first live growth slice.

- **2026-07-15 — Completed DLC integration: Master Wave 2 / Anomaly A0.0–A0.2.** Reconfirmed the
  installed RimWorld 1.6 catalog and split all 16 Anomaly psychic rituals into six exact,
  package-gated semantic families (invitation, flesh/weather, predation, mind, abduction, and death
  refusal), retaining the truth-bounded generic fallback for future/modded rituals. Split the
  existing monolith activation route into mutually exclusive Stirring, Waking, and Void Awakened
  one-shot chapters while leaving discovery separate and automatic Gleaming silent. Exact monolith
  pages now attach visible N1 `journey_chapter` evidence/references on `anomaly-monolith|0` through an
  optional XML event-window template; no provider, new Harmony hook, hidden state, or extra page
  source was added. Added exhaustive shipped-XML classification/localization tests, a package-aware
  RimTest page/evidence fixture, English and Russian text, prompt/coverage documentation, and rebuilt
  the committed core/RimTest DLLs.

- **2026-07-15 — Completed DLC integration: Master Wave 1 / Narrative Continuity N1.** Added
  additive per-first-person-POV Scribe state for knowledge-gated evidence, compact references,
  selected candidate keys, and frozen narrative context; old saves normalize to empty state. Archive
  compaction now copies bounded references/key history and rebuilds pawn-scoped arc/subject indexes
  after load and retention. The prompt payload carries optional context to appended first-person
  template fields only, with DefInjected wording/localization and complete-fact Full/Balanced/Compact
  selection budgets. Added `NarrativeContextBuilder` with synthetic/core fixture candidates only—no
  real DLC provider, Harmony hook, tracker read, or source page behavior changed. Extended pure,
  pipeline, Scribe, archive-index, and RimTest fixture coverage; updated the narrative documentation,
  coverage plan, and Russian localization; rebuilt the committed DLL.

- **2026-07-15 — Began DLC integration: Master Wave 0 / Narrative Continuity N0.** Added the
  DLC-neutral, pure Narrative Continuity foundation: frozen evidence/reference/lens/reflection DTOs
  and tokens; ordinal reference equality; deterministic relevance, category, repetition, age, and
  complete-fact budget policy; deferred reflection priority and disabled-group debt instructions; and
  the XML-backed `Diary_NarrativeContinuity` policy snapshot. Added the standalone
  `NarrativeContinuityTests` suite (49 assertions, no RimWorld/Verse/Unity/DLC assemblies) plus the
  Def and localization entries. This intentionally adds no live provider, Harmony hook, save/archive
  field, or prompt field, so current gameplay and generated prompts are unchanged. Documented the
  frozen N0 seam and rebuilt the committed DLL.

- **2026-07-15 — Made surroundings snapshots tolerate broken room-role recalculation.** Room-role
  labels are optional prompt flavor, but RimWorld's label getter can lazily recalculate room stats and
  throw when a room/performance mod leaves the live room graph transiently inconsistent. Pawn Diary
  now omits only that label and continues recording the event and collecting the remaining context,
  preventing `PlayLogAddPatch` and `GameComponentTick` from being skipped. Documented that the four
  missing-id/callback integration messages emitted by the API-surface RimTest fixture are intentional
  negative-test diagnostics, not failing shipped adapters. Rebuilt the committed DLL.

- **2026-07-15 — Added the authoritative DLC master implementation order.** Added one strict Wave
  0–13 execution schedule above the Narrative Continuity and five source-specific plans. It resolves
  roadmap priority conflicts, assigns every subordinate phase to a release wave, freezes dependency
  and non-reordering rules, defines per-wave acceptance/release gates, defers reflection until all
  producers share one coordinator, and makes final combination hardening the last step. Linked every
  subordinate plan and the atmosphere roadmap back to the scheduling authority. Planning only; no
  runtime behavior changed. The committed DLL was rebuilt solely for repository freshness.

- **2026-07-15 — Planned the shared cross-DLC Narrative Continuity Layer.** Added the canonical
  implementation plan for per-POV narrative evidence, guarded DLC lens providers, deterministic
  XML-tuned selection, persistent arc/subject references, bounded prompt context, and one reflection
  coordinator. Updated the overall atmosphere roadmap and the Royalty, Ideology, Biotech, Anomaly,
  and Odyssey plans with required shared emissions, provider boundaries, dependency gates, and
  cross-DLC acceptance cases. Planning/documentation only; no runtime behavior changed. The committed
  DLL was rebuilt solely for repository freshness.

- **2026-07-15 — RimTest save/load + API/UI/DLC + coverage audit (plan Phases 6–8), Phase 5 scoped.**
  Save/load (`§6.4`): two RimTest fixtures round-trip the diary model objects (`DiaryEvent` in every
  status, `ArchivedDiaryEntry`, `PawnDiaryRecord`) and the repository/archive lookup indexes through a
  real Scribe temp-file save+load, asserting Scribe-key preservation, PostLoadInit non-null
  normalization, pending→not_generated collapse, and index rebuilds (the index is never serialized).
  API/UI/DLC (`§7`): three fixtures cover the public `PawnDiaryApi` surface (null/blank/ineligible/valid
  + master-toggle gating for every call), the non-visual diary-tab view-model contracts (year bucketing,
  hot-vs-archive dedup, visibility filters, unread/pending status, POV/entry-role selection, death-page
  lookup), and DLC-safety (base-game `DlcContext` accessors return empty and never `GetNamed`, match
  strings stay inert, DLC-gated collectors no-op without the DLC; each DLC branch reports a result rather
  than silently skipping). Verification (`§8`): `scripts/verify-coverage.ps1` builds the core mod, runs
  every pure test project, builds the optional RimTest assembly when RimTest Redux is present, validates
  all XML, and prints the EVT-01…EVT-23 requirement matrix — exiting non-zero on any uncovered row (kept
  separate from `.githooks/verify.ps1`, which must not depend on the optional Workshop DLL). Transport
  (`§6.3`, Phase 5) is intentionally deferred: its socket-free boundary is already covered by the
  prompt-capture fixtures, the Harmony wiring is proven transitively by the 20 event suites, and the
  remaining `LlmClient` internals cannot be exercised safely from an in-game test (static session-global
  state; no injectable executor) — a bounded loopback endpoint or a narrow request-executor seam is
  needed and belongs in its own reviewed PR (documented in the suite README). Full in-game suite now:
  37 suites / 162 tests across EVT-01…EVT-23 plus prompt/voice/save-load/API/UI/DLC; core unchanged.

- **2026-07-15 — RimTest prompt/voice coverage (plan Phases 3–4).** Added a Prompt Test Mode capture
  path to the `PawnDiaryRimTestScope` harness — `EnablePromptCapture(level)` +
  `CreateGeneratingAdultColonist()` + `CapturedPrompt(event, role)` — which flips `Prefs.DevMode` +
  `promptTestMode` (snapshotted/restored in teardown) so a fired event runs the real resolve-template →
  render-prompt pipeline, stamps the assembled system+user prompt on the event, and stops before any
  `LlmClient` enqueue: exactly what the runtime would send, with zero network calls. The
  generating-colonist factory is gated on capture being enabled, so a test can never send a real
  request. On this seam: 10 prompt/voice fixtures (43 tests). Phase 3 (`§4`) — policy/domain + candidate-
  key precedence, the Pair/Solo/reflection/neutral template matrices, and the Full/Compact context-detail
  presets. Phase 4 (`§5`) — exhaustive loaded prompt-enchantment Def validation + a forced first-person
  cue + neutral-template suppression; the writing-style precedence chain (external > hediff > custom >
  base) and psychotype chain (external > custom > base) with the psychotype-before-style ordering; and
  humor cue presence/absence under forced 0/1 effective chance. All resolution/rendering is exercised
  through the live pipeline with deterministic (non-RNG-luck) forcing; no production code changed.

- **2026-07-15 — RimTest event coverage (plan Phases 1–2).** Added 20 in-game RimTest suites on the
  `PawnDiaryRimTestScope` harness covering every event source in `TEST_COVERAGE_PLAN.md §3`, EVT-02
  through EVT-23 (interaction batch/ambient, thought immediate + progression, inspiration, ability,
  tale, death, hediff, work, raid fan-out, mood condition, pawn progression, quest, ritual, arrival,
  day/quadrum + arc reflection, external API, event windows, observed conditions) — 77 new test
  methods. Each suite drives the real production trigger, or (for map/scanner/fan-out sources) the exact
  per-unit production signal the scanner submits, keeping tests mapless and deterministic; chance-gated
  routes force the XML effective chance to 0/1 rather than retrying. The harness's failure-safe cleanup
  and no-leak audit were extended to the pawn-scoped accumulator stores (`pendingInteractionBatches`,
  `pendingAmbientInteractionNotes`, `writtenAmbientInteractionNotes`, `activeThoughtProgressions`,
  `pendingDayHediffs`, `writtenDayReflections`); suites that touch non-pawn-scoped stores (raid/quest
  colony keys, event-window and observed-condition rows) clean them per-suite and are documented in the
  suite README. Two suites need a disposable colony because their real trigger has un-restorable vanilla
  side effects (EVT-10 death gives other colonists mood memories; EVT-13 raid fan-out targets the live
  map) — flagged in the README. No production code changed; `PawnDiary.RimTest.dll` builds clean.

- **2026-07-15 — RimTest harness (coverage plan Phase 0).** Extracted the reaction suite's setup and
  teardown into a shared `PawnDiary.RimTest` harness, `PawnDiaryRimTestScope`, per
  `TEST_COVERAGE_PLAN.md §2.1/§8`. One scope now owns isolated non-generating colonists, the settings
  and RNG state a test touches, and failure-safe teardown of events, diary indexes, `diariesById`,
  Social-log rows, relation/mental state, transient dedup/command keys, and the pawns — plus a no-leak
  audit that fails the test if any test-owned state survives cleanup (the plan's "zero marked state"
  gate). `PawnDiaryEventReactionTests` was rewritten onto the harness so each test only fires a trigger
  and asserts an outcome (`FireAndRequireEvent`/`RequirePairRefs`/`RequireSoloRef`), and its three
  cases are tagged EVT-01/EVT-07/EVT-08. Added `tests/PawnDiary.RimTest/README.md` as the suite's
  operator guide. No production code or runtime behavior changed; this is a test-assembly refactor.

- **2026-07-15 — Planned comprehensive automated coverage.** Added `TEST_COVERAGE_PLAN.md`, a staged
  requirement-to-test roadmap covering every documented event source, prompt policy/template,
  enchantment and voice layer, humor/forced-model route, asynchronous LLM lifecycle, save/load path,
  integration/UI contract, and base-only/DLC configuration. The plan defines a shared failure-safe
  RimTest harness, deterministic loopback provider, two-phase disposable-save fixture, implementation
  order, and release gates; no runtime behavior changed.

- **2026-07-15 — Added initial RimTest Redux in-game tests.** Added a separate, conditionally loaded
  `PawnDiary.RimTest.dll` development assembly with three registration/integrity smoke checks and
  three event-reaction integration tests. The reaction suite now drives real vanilla choke points —
  `PlayLog.Add`, `Pawn_RelationsTracker.AddDirectRelation`, and
  `MentalStateHandler.TryStartMentalState` — and verifies the pair/solo `DiaryEvent` persisted by the
  production Harmony-to-signal path, including the originating Social-log ID. It uses isolated adult
  test colonists with generation disabled and removes their events, indexes, log rows, relations,
  mental state, dedup keys, and pawn objects after every test. RimTest Redux remains optional:
  `LoadFolders.xml` exposes the test assembly only when the framework is active, the main
  `PawnDiary.dll` has no test-framework reference, and release packaging continues to exclude
  `tests/`.

- **2026-07-14 — Psychotype roll tuning moved to XML.** The ~19 numeric weights, bonuses, thresholds,
  and odds (family bases, zero-passion/creepjoiner/burning/focus leans, wildcard chance, jitter range,
  duplicate penalty, etc.) that drove the two-stage psychotype roll were compile-time constants in
  `Source/Pipeline/PsychotypeRollPolicy.cs`, contrary to AGENTS.md rule #3. They now live in the new
  `1.6/Defs/DiaryPsychotypeRollPolicyDefs.xml` and flow into the pure algorithm through a new
  `PsychotypeRollWeights` DTO + `DiaryPsychotypeRollPolicyDef.cs` XML boundary — mirroring the sibling
  `DiaryPsychotypeTraitPolicyDef` shape exactly. Behavior is unchanged: the shipped XML reproduces the
  old constants, the DTO defaults match them, and the `DiaryPipelineTests` psychotype suite (1006
  assertions) passes unmodified. `WeightFloor` stays a code constant (a defensive floor, not a
  tunable) and the combo-signature count thresholds stay in the policy (structural matching rules
  deferred per `design/PSYCHOTYPE_PLAN.md` "Out of scope"). Updated `DiaryPsychotypeDefs.xml` and
  `DOCUMENTATION.md` to point at the new owner.

- **2026-07-14 — Powerful AI Integration persona bridge.** Added the separate reflection-only
  `Pawn Diary: Powerful AI Integration` adapter, which reads the full enabled, pawn-bound PAI persona
  surface and synchronizes it one way into a reversible Pawn Diary psychotype override. Its basic mode
  setting offers Disabled, Direct, and LLM-assisted transfer with lane selection, exact data disclosure,
  stable change detection, direct fallback, cancellation, and bridge-owned cleanup. Added XML-owned
  transform policy, English localization, pure formatting/fingerprint tests, short Workshop metadata,
  and an opt-in `-PublishPowerfulAiAdapter` release path; that payload deliberately ships the complete
  bridge source and is also included by `-PublishAllAdapters`.

- **2026-07-14 — Integration requirements completed.** Pointed every integration manifest's dependency
  and load-order metadata at the published `aimmlegate.pawndiary` package and verified its Pawn Diary
  Workshop URL. Added RimTalk's missing dependency URL and made Vanilla Social Interactions Expanded
  an explicit dependency of its integration instead of only a load-order hint.

- **2026-07-13 — Transparent LLM persona payloads in every personality bridge.** Audited the actual
  `ExternalLlmCompletionRequest.userText` paths and added conditional English/Russian settings disclosures
  whenever an LLM persona/psychotype transform is enabled. RimTalk now distinguishes full RimTalk-persona
  import from psychotype-rule-only export and documents its narrow local modifier; Rimpsyche shows its
  `psyche=`/`interests=`/`base outlook:` schema with live caps; 1-2-3 shows its nonblank style, trait,
  outlook, and raw target-owned `details:` fields. Each disclosure also lists important data that is not
  added. Made the Rimpsyche and 1-2-3 settings pages scrollable so localized disclosures stay reachable.

- **2026-07-13 — Audited persona → diary LLM transforms across all bridge mods.** RimTalk import now
  preserves every supported durable persona tendency and contradiction, omitting only surface speech
  mechanics that carry no outlook meaning; its independent import-prompt cache revision refreshes old
  vague results once. Rimpsyche now treats its deterministic, Rimpsyche-derived `base outlook:` as
  authoritative and its psyche/interests as secondary evidence. 1-2-3 Personalities likewise preserves
  every Enneagram-derived built-in outlook tendency while allowing style/main-trait data to adjust only
  supported emphasis. Both prompts now state explicitly that this external-personality result fully
  replaces the pawn's previous Pawn Diary psychotype rather than blending with or extending it. Updated
  English and Russian prompts/settings copy and documented how each bridge's existing effective-prompt
  identity refreshes default-generated results without changing player-customized prompts.

- **2026-07-13 — Source-faithful Pawn Diary → RimTalk persona transforms.** Reworked the export LLM
  prompt so every psychotype tendency remains recognizable, wording and length stay close to the
  source, and concrete outlooks are not replaced with vague speech/personality traits. Removed the
  forced 200-character minimum, updated the English and Russian settings/prompt copy, and versioned
  transformed-export cache keys so already-saved vague results regenerate automatically. Added pure
  regression assertions for prompt-version invalidation without disturbing direct synchronization.

- **2026-07-13 — Release 0.5.0 prepared.** Bumped the source-of-truth mod version, rebuilt the core
  and all six integration submods in Release mode, generated the split Russian localization payload,
  preserved Workshop item `3753779334` for Russian localization and `3758735054` for the Example
  Adapter, and refreshed all eight RimWorld Mods-folder junctions.

- **2026-07-13 — Hardened thought labels and pairwise continuity snapshots** (telemetry refs
  `88B0AB8A`, `D74961A6`). Thought capture now catches failures inside `ThoughtDef.LabelCap` and uses
  the stable `defName` as a last-resort label instead of dropping the event. Pairwise prompt continuity
  now routes `OpinionOf` through the same fail-soft `TryReadOpinion` guard already used by day summaries
  and interaction promotion, so a transiently inconsistent social-thought list contributes neutral
  opinion rather than aborting the interaction-batch `GameComponentTick`. Both paths warn at most once
  per exception type.

- **2026-07-13 — Short, English-only integration Workshop descriptions.** Rewrote all six integration
  submods' `About.xml` descriptions as concise, natural player-facing copy and removed their duplicated
  Russian halves and EN/RU dividers. Dependencies, package ids, and full in-game Russian `Languages/`
  localizations are unchanged.

- **2026-07-13 — RimTalk capture-health fallback.** Added API v8's thread-safe capture-capability
  registry and the XML `disableWhenCaptureCapabilitiesReady` availability gate. The RimTalk bridge
  now installs its displayed-chat postfix independently and reports readiness only after Harmony
  succeeds; if a future RimTalk rename breaks that Level-2 hook, core ambient RimTalk XML stays active
  instead of losing all chat capture. A separate live policy claim preserves intentional Off and
  Shared-context behavior, so the fallback does not create duplicates or bypass the selected level.

- **2026-07-13 — Completed Example Adapter API coverage.** Added explorer forms and copyable facade
  wrappers for the full psychotype surface and the one-shot LLM completion request/poll/cancel
  lifecycle. Prompt-entry and direct-entry builders now default to their own XML-claimed event keys,
  making the previously dead `exampleadapter_prompt_idea` and `exampleadapter_direct_note` settings
  rows reachable. Localized the new developer-facing fields, help, warnings, and sample prompt text.

- **2026-07-13 — Integration capture and release sanity fixes.** Core now supplies Russian-localized,
  adapter-disabled Rimpsyche conversation and afterfeel fallbacks, so Rimpsyche-only installs keep the
  same themed ambient/Thought capture instead of dropping into generic groups. `publish.ps1` can build
  and stage all six integrations (`-PublishAllAdapters` or four focused typed-adapter switches), rebuilds
  each typed bridge against the fresh release core, strips development sources, and rewrites both core
  dependency and load-order ids in every payload. Typed adapter projects now accept the release core
  reference. Removed the unreachable RimTalk direct-engine client and two orphan Rimpsyche setting
  labels while retaining their saved migration keys. RimTalk v1.0.14 inspection reconfirmed that every
  live displayed-talk route reaches the existing `TalkService.CreateInteraction` hook.

- **2026-07-13 — Russian localization parity and native-language cleanup.** Realigned all eight
  indexed prompt-template field lists after the external-prompt fields were inserted, restored the
  28 missing Keyed settings/event-filter strings and seven missing interaction-group translations,
  and brought quest guidance back in sync with the colony-wide source policy. Replaced glossary
  violations and UI anglicisms, corrected the false `CorpseTorment` meaning, rewrote literal-English
  prompt artifacts, normalized visible typography and hyphenated style/cue labels, and documented indexed
  DefInjected lists as positional schema.

- **2026-07-13 — Adversarial hardening for persona bridges and humor cues.** One-shot integration LLM
  requests are now caller-cancellable (`ApiVersion 6 → 7`) and both RimTalk/Rimpsyche persist stable
  source keys plus resolved targets, preventing reload churn, target drift, repeated paid transforms
  after a rejected local write, and orphaned 64-slot handles. RimTalk persona sync now defaults to Off,
  migrates old opt-in/opt-out saves without silently enabling imports, dynamically registers its optional
  editor patch, restores both the original persona and talk weight, retries failed reflection setters,
  and rejects invalid chattiness floats. Rimpsyche modes now use reversible source-owned overrides
  without overwriting player base/custom outlooks, release ownership even after the master switch turns
  off, and keep an inherited localized transform prompt blank in the editor. Filled all 32 previously
  missing Russian bridge keys. Humor trait policy moved to XML/Advanced tuning and a pure-tested private
  deterministic RNG scope, so cue selection no longer advances RimWorld's shared `Rand` state.

- **2026-07-13 — Psychotype-driven RimTalk chattiness.** While Pawn Diary controls RimTalk's persona,
  it now also applies an XML-authored talk-initiation baseline for the selected psychotype with stable
  per-pawn relative ±15% variation. The 250-tick sync maintains the inferred value, `silent-focus`
  remains an absolute zero override, and ending Pawn Diary authority restores the player's saved
  pre-sync RimTalk value (including across reloads).

- **2026-07-13 — Temperament-biased hidden humor chance.** The hidden humor-cue system now shifts
  `humorChance` by the writer's temperament with one flat, non-cumulative multiplier. An upbeat
  temperament (Optimist, Sanguine, or Anomaly's Joyous trait) or a Social skill passion (minor or
  burning) raises it (`DiaryTuningDef.humorElevatedChanceMultiplier`, default `2`); a dour, anxious,
  or unfeeling temperament (Pessimist, Depressive, Nervous, Neurotic, Very neurotic, Psychopath, or
  Anomaly's Disturbing trait) lowers it (`humorReducedChanceMultiplier`, default `0.5`). The two
  directions are mutually exclusive: within a direction several qualifiers still count once, and a
  writer who qualifies for both offsets back to the base rate. Cue insertion stays hidden and
  always-on; its rates and trait-key lists are editable through Advanced tuning.

- **2026-07-13 — Fixed RimTalk bridge startup translation errors.** Localized RimTalk variable and
  injected-section registration now runs after language loading instead of from the background mod
  constructor, eliminating the `No active language` errors for diary, persona, colony, and shared context.
  Corrected template guidance to RimTalk's real `pawn`/`recipient` roots; numbered `pawn1`/`pawn2`
  variables do not exist, and `recipient` must be null-guarded for monologues.

- **2026-07-13 — Directional RimTalk persona synchronization.** The RimTalk bridge now lets players
  choose Pawn Diary → RimTalk (publish psychotype only) or Pawn Diary ← RimTalk (import the chat
  persona as diary outlook), with a strict optional 200–300-character LLM rewrite, periodic updates,
  ownership notices, and regenerate buttons in the controlling editor. Writing-style prose never
  crosses the bridge: only the five listed hediff styles and five child styles select bounded prompt
  modifiers; `silent-focus` instead maintains RimTalk chattiness at zero and restores the saved prior
  value afterward. Added opt-in `{{ pawn.diary_persona }}` / `{{ recipient.diary_persona }}` variables
  without automatic prompt insertion, pure policy/length tests, and retained the legacy toggle key.

## 2026-07-13

- **Clearer per-pawn writing-style status.** The voice editor now leads with a prominent, live
  “Currently used” panel that distinguishes the base style, the pawn's custom prompt, a temporary
  health-condition override, and an external-mod override by source. It explicitly says when saved
  custom text is waiting behind an override, and removes the redundant effective-prompt preview.

## 2026-07-12

- **Rimpsyche persona sync now matches the 1-2-3 workflow.** The old on/off outlook toggle is now Off
  plus three active modes: map the dominant psyche family to a built-in psychotype, apply a reversible
  source-owned direct-text outlook, or create a source-owned LLM-assisted outlook on a selectable lane
  with deterministic direct-text fallback. The 250-tick rounded-vector detection refreshes after
  Rimpsyche persona changes, LLM mode exposes Regenerate in the pawn voice editor, old settings migrate
  safely, and the pure mapping/input policy is covered by the Rimpsyche bridge harness.

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state. The public integration
contract starts at `PawnDiaryApi.ApiVersion == 1`; older entries below preserve the internal
pre-release version ladder for project history.

## 2026-07-12

- **Rewritten About descriptions for all six integration mods.** Every `integrations/*/About/About.xml`
  description was regenerated as a short, player-facing text (what the adapter does, what it needs,
  what happens when the target mod is absent), dropping internal deploy/tuning details, and each now
  carries a matching native-Russian half after an EN/RU divider, using the established localization
  terms (мост, психотип, взгляд, поселенец; group names like «Наболевшее» / «глубокие разговоры»).
  Package ids, dependencies, and the dev comments in each About.xml are unchanged; publish scripts
  pass the new descriptions through as-is.
- **Adversarial review follow-up for the new integrations.** The SpeakUp prisoner group now lists the
  21 exact `Prisoner*` conversation defNames instead of a bare `Prisoner` prefix, so it no longer
  steals Anomaly DLC's `PrisonerStudyAnomaly` from the core anomaly group; unlisted future SpeakUp
  prisoner defs fall through to the package catch-all. All five SpeakUp ambient groups opt into
  `allowSingleEligiblePawn`, so a colonist's talk with a prisoner or guest batches into one day note
  instead of dead-configuring the batch and emitting solo pages. The Tier-2 observer refuses to attach
  to a `Talk` first seen past its opening reply (via SpeakUp's `Statement.Iteration`), preventing
  inverted subject/partner roles and partial line sampling when capture is toggled on mid-conversation.
  The Rimpsyche mod constructor now wraps `new Harmony(...)` in the same try/catch the SpeakUp adapter
  uses, so a Harmony static-init failure can never abort play-data load. `EVENT_PROMPT_MAP.md` corrects
  the Tier-C alignment gate to strictly `> 0.55`. Both adapter DLLs were rebuilt; all pure suites pass
  (SpeakUp 21, Rimpsyche 121, core 605).
- **Six source-verified, no-hard-dependency compatibility packs.** Alpha Memes gets funeral/ritual,
  thought, baptism, and visible-hediff voices; VIE Memes gets severe/general rites, afterthoughts,
  and interrogation; Way Better Romance gets its 3 real interactions and 14 memories; Vanilla Traits
  Expanded gets recordable memories, 3 actual mental states, and 20 XML-patched psychotype affinities;
  Hospitality gets colonist-owned guest work/scrounging plus one-witness arrival/join-request pages;
  and Vanilla Events Expanded gets purple raids, visible ambient hediffs, 6 lasting-condition tints,
  and 4 one-witness incident families. All groups/windows are package- or exact-string-gated and carry
  native-register Russian text. Upstream audit corrections deliberately omit guest-held Hospitality
  thoughts, VEE's neutral visitor incident, hidden traitor state, and situational-not-memory thoughts.
- **Reusable compatibility routing fixes.** `MapWitness` gives map-level incidents one deterministic
  diary owner and event windows now support target-package gates across start, restored state, prompt,
  timeout, and recording paths. Interaction batches can explicitly allow one eligible colonist for
  guest/prisoner pairings without changing existing groups. Live Thought classification now preserves
  the source Def for package matching, while saved recovery remains name-based. Ritual pages combine
  the matched XML theme with localized pawn-role guidance, making mod-specific ritual instructions
  reachable. Pure capture-policy coverage and the committed core DLL were updated.
- **Pawn Diary: SpeakUp adapter.** Five gated XML families replace the core fallback only while the
  adapter is loaded, and a default-on reflection-only observer can submit one sampled whole-conversation
  event after a configurable 1–5 reply threshold. SpeakUp alone keeps the frozen
  `speakup_chitchat`/`SpeakUpAmbientDay` behavior and saved toggle; force-loading the adapter without
  SpeakUp is inert. EN/RU text, 21 pure assertions, a rebuilt adapter DLL, and default-on
  `scripts/publish.ps1` payload/build/install wiring are included.
- **Pawn Diary: Rimpsyche adapter.** Gated conversation/thought groups, a cached localized `psyche=`
  context line, a default-on six-family source-owned psychotype outlook, and signature-checked charged
  conversation capture now bridge Rimpsyche v1.0.41 without changing the public API. The XML `0.55`
  threshold and saved 60,000-tick pair cooldown are tunable/persistent; toggle/new-game sweeps release
  owned overrides. EN/RU text, 121 pure assertions, installed 34-node/hook verification, and the rebuilt
  adapter DLL are included.
- **Fixed the RimTalk bridge settings window appearing empty.** Its scrollable Verse listing could
  overflow into invisible side-by-side columns, then repeatedly shrink its measured canvas until only
  the section heading remained. The settings now stay in one vertical column and the scroll canvas
  never becomes shorter than its viewport; the bridge DLL was rebuilt.
- **RimTalk funnel hardening.** Assessment limits and the retry gap now survive save/load, while
  queues and requests remain transient. Failed or skipped outcomes clean up pending event context;
  late charged/user/keyword evidence is retained; pre-0.3 zero-cap settings migrate safely; and
  in-flight completions can still be polled for cleanup when integration is disabled. The JSON schema
  prefix is protected from overrides and translations, and reaction-editor limits count Unicode text
  elements consistently. Pure tests, EN/RU text, and both committed DLLs were updated.
- **RimTalk bridge 0.3.1.** Accepted conversations put both POV pawns on a persistent 60,000-tick
  cooldown, with race-safe reservation and refund on rejected diary submissions. The legacy per-pawn
  setting is now 0/1, and the settings window supports localized, Unicode-aware reaction-term editing
  plus an editable semantic-assessment prompt. XML owns the limits and defaults; semantic mode remains
  optional and its off state uses the stricter local-only path. EN/RU text, tests, XML, and the bridge
  DLL were updated.

## 2026-07-11

- **RimTalk bridge 0.3.0: bounded editorial conversation funnel replaces the “important kind or four
  lines” rule.** Finished reply chains now pass through pure Unicode-aware local scoring, a deterministic
  12-candidate ranked queue (pair limit 2), and small batches of at most 6 using the existing
  `RequestLlmCompletion` handle API. The XML/DefInjected policy owns weights, thresholds, English/Russian
  keyword categories and assessor prompt, overlap/event windows, formatter/token caps, and the default
  ceiling of 2 assessment requests per in-game day with a 15,000-tick gap. A locked third status-listener
  cache merges pending/completed native or other-adapter events with subject/partner context snapshots,
  excludes this bridge's output, and supplies event-repetition context. The strict JSON parser accepts
  only active conversation/event aliases plus frozen decision/reason tokens and fails closed. Only
  `related`/`standalone` results reserve existing pawn/colony/pair throttle space and call one normal
  pairwise `SubmitPromptEntry`; related results carry the actual event id/focus and a no-recap guard, so
  one accepted conversation yields one `DiaryEvent` with two POV pages. No-lane/admission failure keeps
  only the bounded expiring queue; transport/malformed/blank/toggle-off/save-load paths create nothing.
  New saved settings select semantic vs stricter zero-extra-request local mode and an Automatic/active
  Pawn Diary lane. Frozen `minRepliesForImportant` and `useRimTalkEngine` keys still read but are hidden
  and ignored; engine writing is temporarily retired. Core `rimtalk_chatter` is now fallback-only and
  disables itself whenever the bridge package is loaded, eliminating ambient/promotion duplication at
  Levels 0/1/2. Added 150 passing pure assertions, EN/RU Keyed + DefInjected text, updated bridge About/
  integration/design docs, validated XML, and rebuilt both Debug DLLs.
- **Adversarial hardening of today’s trait, integration-API, and 1-2-3 bridge work.** One-shot external
  completions now require a live game, reserve the existing per-source/global prompt budget, share the
  normal lane semaphore/cooldown/session cancellation, reject admission at 64 tracked handles instead
  of evicting still-paid work, clear handles between games, and redact public failure text. The public
  API is now v6: `DiaryPsychotypeSnapshot.savedCustomRule` exposes the player-owned custom layer so an
  adapter can remove its legacy locked override without erasing text underneath it. The Personalities
  bridge persists a secret-free fingerprint of its effective mode/prompt and Pawn Diary lane setup,
  reacts to changes across restarts, preserves old `usePersonalityOutlook=false` opt-outs and existing
  custom rules, runs legacy cleanup even when 1-2-3 is inactive, retries temporary request/write
  rejection without re-spending a successful completion, and no longer lets async Regenerate overwrite
  text typed meanwhile or remain waiting after immediate rejection. Trait-gain prompts now explicitly
  include the trait name/description fields. Psychotype trait mappings, family/member bonuses, and the
  gated takeover rate moved from hardcoded C# into `DiaryPsychotypeTraitPolicyDefs.xml`, projected into
  the same pure tested roll policy. Both committed DLLs rebuilt.
- **Error reporter now covers the integration submods, not just the main mod.** The opt-out crash
  reporter's capture filter (`DiaryLogReportPatch`) matched only the main mod's `[Pawn Diary]` log
  prefix, so a bridge tagging with the spaced family name — e.g. the 1-2-3 Personalities bridge's
  `[Pawn Diary: 1-2-3 Personalities]` — was never reported (the RimTalk bridge's `[PawnDiary: …]` lines
  already were, by luck of the shared root). The "is this one of ours?" test moved out of the Harmony
  patch into a new pure, unit-tested `ModErrorPrefixPolicy` (`Source/Diagnostics/Pure/`) that matches
  the whole Pawn Diary family by its two log-prefix **roots** (`[Pawn Diary:` and `[PawnDiary`), so
  every first-party bridge — 1-2-3 Personalities, RimTalk, and the new VSIE adapter (`[Pawn Diary: VSIE]`)
  — is captured with no per-submod entry, while other mods, base-game lines, and the copy-me
  `ExampleAdapter` template stay ignored. The bridges log through the same `Verse.Log`, so the one
  existing global postfix now captures them all; no wire-schema or endpoint change, and a submod error
  is identified by its message prefix (`modVersion` stays the main mod's).
- **VSIE adapter now captures group gatherings (birthdays & funerals).** The `Pawn Diary: Vanilla Social
  Interactions Expanded` adapter — previously XML-only — gains a tiny assembly (`PawnDiaryVsie.dll`) that
  records VSIE's two colony-important gatherings as their own diary entries. VSIE gatherings emit no
  InteractionDef/TaleDef, so a Harmony postfix on the **base-game** `RimWorld.GatheringWorker.TryExecute`
  (matched by `def.defName` **string**, so the adapter references no VSIE type and cannot TypeLoad-fail
  without it) forwards `VSIE_BirthdayParty` and `VSIE_Funeral` to the public API as External events
  (`vsie_birthday` / `vsie_funeral`), claimed by two new `domain=External` groups in
  `1.6/Defs/DiaryExternalGroups_Vsie.xml` (EN + RU localized). One entry per gathering, from the
  organizer's point of view — the colony **moment** — while each attendee's private feeling keeps arriving
  through the existing `vsie_thoughts` group. Birthdays and funerals each have their **own on/off toggle**
  in the adapter's mod settings (new `VsieBridgeMod`/`VsieBridgeSettings`, both default on): the gathering
  entries are External-domain, which Pawn Diary's Events tab excludes, so the adapter owns its settings
  entry like the RimTalk and 1-2-3 Personalities bridges. VSIE's four non-External XML groups stay
  toggleable in Pawn Diary's own Events tab. VSIE's flavor gatherings (dates, movie night, skygazing,
  snowmen, beer/binge/outdoor parties) are intentionally left to that thought capture. Pure defName→plan
  map in `Source/Pure/VsieGatheringMap.cs`, covered by the new `tests/VsieBridgeLogicTests/`. The mod
  stays fully inert without VSIE (groups `enableWhenPackageIdsLoaded`-gated; `PatchAll` skipped unless
  VSIE is active). Verified against the installed VSIE 1.6 source: neither gathering worker overrides
  `TryExecute`, so the base-method hook fires for both.
- **New "important" diary page when a colonist gains a personality trait.** The pawn-progression scanner
  now snapshots each colonist's trait set (`<defName>|<degree>`) and, on a later scan, writes a page for
  any newly gained trait. It feeds the trait's **own** character-card description — resolved for the pawn
  and stripped of stat/skill/mechanic lines — into the prompt, so any trait (vanilla or modded) is voiced
  as a felt personality shift with no hardcoded per-trait table. The prompt instructs a first-person,
  from-the-inside account and forbids naming the game trait/stat/number. Lives in the existing
  `Progression` domain: new XML group `progressionTraitGained` (important, toggleable, EN+RU localized),
  baselined on first scan so a pawn's starting traits never record, and a per-trait dedup key so gaining
  more than one trait at once yields one page each. Pure key/diff logic in `TraitProgressionPolicy` with
  tests in `DiaryCapturePolicyTests`.
- **1-2-3 Personalities bridge redesigned around one setting + an experimental LLM transform; public
  API → `ApiVersion 4`.** The bridge's two toggles and its always-on `personality=` context line are
  gone, replaced by a single **mode** selector with three escalating tiers of how a colonist's Enneagram
  shapes their **editable** Pawn Diary psychotype:
  - *Map to a built-in psychotype* — sets the pawn's base psychotype to the closest built-in
    `DiaryPsychotype_*` (pinned), a real swappable type in the Psychotype Studio.
  - *Override from personality* — seeds the pawn's editable custom rule from the built-in root→outlook
    text (default mode; the old locked-override behavior, now player-editable).
  - *Experimental LLM transform* — rewrites the pawn's 1-2-3 data into a compact outlook via the player's
    language model, on a lane they pick (styled like the main mod's connection UI) with an editable,
    small-model-friendly prompt; falls back to the override text when no lane is set or the call fails.
  Seeding is change-detected by `<mode>:<root>` and **saved with the game**, so a reload never re-seeds
  over a hand-edit; it re-seeds on a mode or root change, and re-seeds the whole colony whenever the
  effective bridge or selected core-lane configuration changes (tracked by a saved secret-free
  fingerprint). A one-time first-tick sweep releases the
  locked overrides earlier bridge versions placed. Read-only toward 1-2-3 Personalities; SP_Module1 reads
  stay `[NoInlining]` behind the `SimplePersonalitiesActive` guard.
- **Public integration API v4** (additive; existing members unchanged). `SetPsychotype(pawn, defName,
  pin)` and `SetPsychotypeCustomRule(pawn, rule)` write the pawn's **player-editable** psychotype layers
  (base type / custom rule) — the Psychotype Studio's own slots — so an adapter can *seed* an outlook the
  player then owns, rather than the source-locked override. `RequestLlmCompletion(request)` +
  `GetLlmCompletionResult(handle)` run one prompt on a chosen (or first-active) lane and poll the result;
  they wrap a new one-shot `LlmClient.SendSingleCompletion` behind `ExternalLlmCompletionService`, are
  master-toggle-gated + sourceId-attributed + one-shot + input/output-capped, and never throw. The bridge
  no longer writes the external psychotype override, so it no longer participates in override arbitration.
- **Settings migration:** the retired `provideContextLine` key is dropped; saves without the new `mode`
  read the old `usePersonalityOutlook` key so an explicit false remains *Off*, while a missing/default-on
  value becomes *Override* (now editable). The pure mapper gains root→built-in-psychotype and transform-input helpers; the bridge test
  suite drops the retired context-line checks and adds new ones
  (`tests/Personalities123BridgeLogicTests/`, 90 checks green). Both committed DLLs (`PawnDiary.dll`,
  `PawnDiaryPersonalities123.dll`) rebuilt.
- **Per-pawn Regenerate button + loading status for the LLM tier (`ApiVersion 4 → 5`).** New public
  `RegisterExternalPsychotypeGenerator(ExternalPsychotypeGenerator)`: an adapter registers `canReroll` /
  `isBusy` / `reroll` callbacks and the per-pawn voice editor (`Dialog_PawnWritingStyle`) shows a
  **Regenerate** button and a live **generating…** status for pawns it owns, refreshing the editable
  custom rule when the new outlook lands (new `ExternalPsychotypeGenerators` registry, mirroring the
  context-provider hook). The 1-2-3 Personalities bridge registers one while in the LLM tier and gains
  `Personalities123GameComponent.RerollTransform` / `IsTransformInFlight`; the button re-fires the
  transform for that colonist on demand. No Harmony. Registration is thread-safe and requires no main
  thread, so it works from the mod constructor that RimWorld 1.6 runs off the main thread (the bridge's
  one-time override-migration sweep moved from `FinalizeInit` to the first tick for the same reason).
  Main-mod EN/RU strings added; both DLLs rebuilt.
- **LLM transform tuned for small models: rewrite a base outlook instead of inventing one.** The
  transform input now carries the pawn's LOCALIZED built-in outlook (`base outlook:` — the same text
  Tier 2 would seed) instead of a bare `enneagram type: N`, so the model's job becomes "reword this
  known-good text so the style and main trait show through" — and its worst failure (copying it
  verbatim) is still correct, on-register text. The default prompt (EN + native RU) was reauthored to
  match: numbered rules, an output-only guard, an anti-echo rule for type/trait labels, an
  ignore-code/IDs rule for the raw `details:` blob, and two contrasting micro-examples. Transform
  output budget raised 200 → 300 tokens for reasoning-model headroom. Pure mapper drops the
  type-number line (`tests/Personalities123BridgeLogicTests/`, 91 checks green).
- **Traits now steer the psychotype roll — and dominate it.** A new XML-owned weight policy
  (`1.6/Defs/DiaryPsychotypeTraitPolicyDefs.xml`, projected through the pure
  `Source/Pipeline/PsychotypeTraitAffinities.cs`) adds a deliberate trait channel on top of the
  skill-passion signals, scaled above them so the trait wins against even a contrary passion profile
  (pinned by a seeded dominance test: a Sanguine pawn with burning violence passions still rolls
  Content more than anything else): each supported trait adds stage-1 family weight and stage-2
  member weight toward its compatible psychotypes — Jealous → Resentful/Narcissistic, Greedy → Ruthless/Ambitious,
  Too smart → Detached, Tortured artist → Resentful/Nostalgic/Theatrical, Kind → Content/Dutiful,
  Abrasive → Wry/Pragmatic, Recluse → Detached/Avoidant, Depressive → Nostalgic/Avoidant, Pessimist →
  Resentful/Paranoid/Wry, Sanguine/Optimist → Content, Nervous → Avoidant/Dependent, Volatile →
  Volatile, Neurotic/Very neurotic → Perfectionist/Paranoid. Spectrum traits map per degree
  (NaturalMood / Nerves / Neurotic); unknown or modded traits contribute nothing; the two existing
  vetoes (Psychopath never rolls Dependent, Kind never rolls Ruthless) are unchanged.
- **Three new trait-gated psychotypes for the extreme traits: Hollow (Psychopath), Ravenous
  (Cannibal), Bloodthirsty (Bloodlust)** — all intense-family, each rollable ONLY by pawns carrying
  the named trait via the new `<requiredTrait>` def field (the gate holds on every roll branch; the
  manual per-pawn picker still allows hand-assigning anything). A pawn with such a trait adopts its
  gated psychotype outright 45% of the time (`<gatedTakeoverChance>` in the trait policy Def) and keeps a strong weight bonus
  toward it in the normal roll, so a Psychopath usually — but not always — reads as Hollow. English +
  native Russian rule texts shipped. Pure policy covered in `DiaryPipelineTests` (973 assertions
  green: canonical trait keys, family/member bonuses, trait-over-profile dominance, the gate holding
  without the trait across profile/wildcard/child branches, and a dominant-but-not-total share with it).

## 2026-07-10

- **External override collisions are now arbitrated by mod load order (later mod wins).** When two
  integration adapters both target the same pawn's external writing-style or psychotype override slot
  (e.g. the 1-2-3 Personalities bridge and the RimTalk bridge's Tier B persona voice, both enabled),
  `Set*Override` no longer silently last-writer-wins between them: if both `sourceId`s are packageIds
  of active mods, the later-loading mod keeps the slot and the earlier mod's write returns `false`
  with one quiet log line — the standard RimWorld "lower in the list overrides" convention, so the
  player picks the winner by reordering. SourceIds that are not active-mod packageIds keep the old
  last-writer-wins behavior, and a stale override from an uninstalled mod is always displaceable;
  `Reset*Override` stays owner-guarded. Guardrail-only change, no `ApiVersion` bump. New pure policy
  `Source/Pipeline/ExternalOverrideArbitration.cs` (tested in `DiaryPipelineTests`, 945 assertions
  green) + impure load-order resolver `Source/Util/ExternalSourceLoadOrder.cs`; both setters in
  `DiaryGameComponent` arbitrate before writing.

- **New mod-compatibility adapters for 1-2-3 Personalities and Vanilla Social Interactions Expanded,
  each shipped as its own standalone mod** under `integrations/` (deployed by
  `scripts/deploy-integrations.ps1`), rather than as compat groups inside the core mod — so a player
  installs only the ones matching their mod list. Both are inert without their target mod.
  - **`Pawn Diary: Vanilla Social Interactions Expanded`** (`PawnDiary.Vsie`, XML only) — teaches the
    diary about VSIE's new social moments in their own voice: venting (ambient day-note that can
    promote a charged moment to its own confiding entry), teaching/learning (ambient day-note),
    becoming best friends (`VSIE_BestFriend`, a relationship milestone like the vanilla romance
    milestones), and VSIE's extra mood thoughts. VSIE's `Discord` interaction — an anger-driven insult
    despite the name, not co-working chatter — is routed into the core `insults` group via a VSIE-gated
    patch so it batches with the social fight it usually starts. All groups gated by
    `enableWhenPackageIdsLoaded`.
  - **`Pawn Diary: 1-2-3 Personalities`** (`PawnDiary.PersonalitiesBridge`, XML + assembly) — Tier 1
    (XML) routes Module 2's compatibility interactions (Harmonious/Diversive/Turmoil) and its
    personality mood thoughts into their own diary voice. Tier 2 (`PawnDiaryPersonalities123.dll`,
    net472, reads 1-2-3 Personalities' public Enneagram API) adds a `personality=<variant>, <trait>`
    context line and a default-on option that turns each colonist's Enneagram root into their diary
    **outlook** via `SetPsychotypeOverride` — the same pattern the RimTalk bridge proved, now shown to
    generalize. Read-only toward 1-2-3 Personalities; overrides clear on toggle-off and new-game. The
    root→outlook mapping is pure and unit-tested (`tests/Personalities123BridgeLogicTests/`, 74 checks).
  - **Both mods are localized** (English source + native Russian, matching the repo's RU-prompt
    rule): DefInjected for every group's label/instruction/tone and Keyed for the Personalities
    settings. The initial RU draft was re-edited the same day to the core RU register — «поселенец»
    (never «пешка»), capitalized group labels, the core's `тема: пиши…` instruction pattern — with
    calqued lines rewritten as native idiom; key sets and instruction/tone indices verified
    unchanged against the English sources. The 9 Enneagram→outlook rules were made
    localizable — the pure mapper now exposes a per-root translation key that the bridge resolves
    through RimWorld's Keyed system (native Russian shipped), falling back to the English source rule
    — so a Russian player no longer gets English outlook text injected into their prompts.
  - Verify note: the plan's Thought-domain "ambient batch" does not exist — `<batch>` is
    Interaction-only, so both `*_thoughts` groups theme instruction/tone and rely on the global
    mood-offset threshold as the flood guard. Psychology compatibility is deliberately not included in
    this pass.
- **Russian proofread pass over the RimTalk bridge** (same register sweep as the new compat mods).
  Fixes in `PawnDiary.RimTalkBridge` RU files: ungrammatical "Разговор с кое с кем"
  (`Event.SomeonePartner` → «кем-то»), «троттлинг» → «ограничения частоты», quest → «задание» to
  match the core's «Задание» groups, the shared-memory sample date "(5-е весны)" → "(5-й день
  квадрума)" (RimWorld has quadrums, not seasons), the diary-entry label capitalized like core event
  labels, gendered «его характером» in the legacy `Persona.VoiceRule` made gender-neutral, and
  smoother settings labels (core's «в тиках» convention). Text-only change — no keys added or
  removed, no behavior change.
- **Fixed an opinion-read crash that skipped the day-summary tick** (telemetry ref `E335F9E6`).
  Vanilla `Pawn_RelationsTracker.OpinionOf` walks the other pawn's social thoughts, and that walk can
  throw an `ArgumentOutOfRangeException` from `ThoughtHandler.OpinionOffsetOfGroup` when a pawn's
  memory list is momentarily inconsistent. The throw propagated out of `CollectOpinionSignals` and
  aborted the whole `GameComponentTick`, so *every* sleeping pawn's ambient/day-summary flush was
  skipped that tick. A shared `TryReadOpinion` guard now wraps the fragile read — one bad pawn costs
  only its own opinion signal, not the tick — and the two identical reads in the day-start opinion
  snapshot (`SnapshotDayStartOpinions`) and the interaction-promotion scorer (`PromotionChance`) route
  through it too, so a broken read degrades gracefully (no baseline recorded / a neutral `0`) instead
  of crashing. Warns at most once per session per exception type.
- **RimTalk bridge: review fixes for the colony-situation + shared-memory context (bridge modVersion
  0.2.0).** Follow-up correctness/consistency fixes from an adversarial review of the 2026-07-09 change,
  no new features:
  - **Shared memory now reads whichever pawn is diary-eligible.** `SharedMemoryInjector` picked the
    lower-load-id pawn as the subject and read only that pawn's diary; for a colonist↔non-colonist pair
    (prisoner/guest) where the ineligible pawn sorted first, the block cached empty in *both*
    directions. It now falls back to the other pawn when the primary is unreadable (unspawned or
    diary-ineligible), and a null snapshot is distinguished from "readable but shares nothing".
  - **Transient empties no longer stick.** A block built while a pawn was away (caravan) was cached
    non-stale and served until an unrelated diary edit; a build where neither pawn can be read now
    retries on a later pass instead of caching a permanent empty.
  - **Settings changes invalidate the shared-memory cache.** `Mod.WriteSettings` now marks all cached
    pairs stale, so changing `sharedMemoryCount`/toggles takes effect immediately instead of after the
    next diary edit.
  - **Auto-inject no longer disables itself silently.** If `RimTalkPromptAPI.CreatePromptEntry` returns
    null (rather than throwing), `SyncAutoInject` no longer records success — it warns once and retries,
    instead of leaving `{{diary_shared}}` unregistered for the rest of the process.
  - **Colony-situation cache no longer serves stale lines.** The refresh pass now clears a map's cached
    block when it has no free colonists (so e.g. an old "under serious attack" line stops), and evicts
    blocks for maps that no longer exist.
  - **One shared feature gate.** The `level + toggle + external-API-enabled` predicate was hand-copied
    in several places (two omitted the external-API check); the injectors and the game-component refresh
    now route through a single `FeatureActive()` each. Doc comment on `SharedMemoryPromptEntryName`
    corrected (cleanup is keyed on the mod id, not the entry name).

## 2026-07-09

- **RimTalk bridge: colony situation + pair shared memory context.** Two new Level-1 outbound context
  variables for the `PawnDiary: RimTalk bridge` adapter (`design/RIMTALK_BRIDGE_CONTEXT_EXTENSION_PLAN.md`),
  both cache-read-only on RimTalk's background prompt thread and refreshed on the main-thread tick pass:
  - **`{{colony_events}}` — colony situation (`ColonyContextInjector`, default OFF).** A short curated,
    weighted line about the colony now — threat level, active game conditions, an atmospheric Anomaly
    note (DLC-gated), and top ongoing quests; weather/season left to RimTalk. Registered as an
    environment variable + an injected section after Weather; per-map cache keyed by `map.uniqueID`.
    Off by default (overlaps RimTalk's live-event mods). Pure `ColonyEventsFormat` orders/trims it
    (settings `colonyEventCount`, hard cap 400 chars).
  - **`{{diary_shared}}` — pair shared memory (`SharedMemoryInjector`, default ON).** When two colonists
    talk, the diary moments they share (filtered by `DiaryEntryTitleQuery.partnerPawnId`) are injected as
    "previous interactions", picked weighted-randomly by the pure `SharedMemorySelection` (recency ×
    importance, seeded from a stable per-pair value — never `Verse.Rand`; settings `sharedMemoryCount`,
    hard cap 500 chars). A `context` variable (sees all participants), so it uses a lazy request queue:
    the provider serves the cached block or enqueues the pair; the pass builds it and caches by pair key,
    with a second entry-status listener invalidating a pair when either pawn's diary changes. Optional
    zero-config auto prompt-entry embedding `{{diary_shared}}` (`autoInjectSharedMemory`, default ON),
    reconciled idempotently and removed on toggle-off (prompt entries persist in the RimTalk preset).
  - New Advanced settings (frozen Scribe keys, defensive clamps): `injectSharedMemory`,
    `autoInjectSharedMemory`, `injectColonyContext`, `sharedMemoryCount` (0–4), `colonyEventCount`
    (0–6), with the shared-memory sub-rows gated on the parent toggle. The bridge settings window is
    now wrapped in a scroll view so the fuller list stays reachable (it otherwise overflowed the fixed
    mod-settings rect at larger UI scales / longer translations, clipping the bottom controls).
  - Verified against the **installed cj.rimtalk v1.0.13** DLL (`RegisterContextVariable`, `PromptContext`,
    the prompt-entry API all present — no degrade needed). Full EN + native RU strings (RU flagged for a
    human pass); new pure tests in `RimTalkBridgeLogicTests` (98 assertions green). Bridge builds clean.
    **In-game matrix + the `InjectEnvironmentSection` render question (U1) are pending a maintainer
    playtest** — see DOCUMENTATION.md.

## 2026-07-08

- **Edit psychotypes from settings (Styles tab).** The Styles settings tab now hosts a *Psychotypes*
  catalog editor beside *Writing styles* (`PawnDiaryMod.PsychotypeStudio`, backed by the new
  `PsychotypePresetStore`, Scribe key `psychotypePresets`): retune a built-in psychotype's
  label/rule/family or add your own (label + rule + family). `DiaryPsychotypes.All` now merges those
  edits over the XML defs and caches the result like `DiaryPersonas.All`, so overrides reach the roll,
  the per-pawn picker, and generation from one place. Custom psychotypes are **manual-only** — pickable
  per pawn but never auto-rolled (`RollCandidates` skips `custom` rows); built-in overrides still roll
  with their edited family. Full EN + RU strings; new pure `PsychotypeRollPolicy.NormalizeFamily`
  (test-covered in `DiaryPipelineTests`).
- **Strip leaked format placeholders from generated diary text.** The LLM output sanitizer
  (`LlmResponseParser.SanitizeGeneratedMarkup`) now removes stray numbered format placeholders —
  `{0}`, `{1}`, `{0:D2}`, and empty `{}` — that a model echoed after an unfilled
  `.Translate()`/`string.Format` template (e.g. "favor of {0}") reached the prompt, or that a small
  model copied from the template shape. Numeric/empty braces only; a brace run with letters
  (`{spawn}`) is kept as prose, and a stranded space before clause punctuation is healed so
  "favor of {0}." saves as "favor of.". Covered by new `LlmResponseParserTests` cases.
- **New second voice layer: pawn psychotypes (outlook).** Each pawn now has a *psychotype* — a short
  semantic lens describing what they notice, value, and fear, and how they judge events — folded into
  first-person prompts alongside their writing style. The two layers roll **independently** (style from
  traits/backstory, psychotype from skill passions with wildcard + jitter), so they multiply into many
  distinct diary voices. 17 adult psychotypes across four families (grounded/inward/intense/anxious)
  plus 5 child ones, in `1.6/Defs/DiaryPsychotypeDefs.xml`; the pure roll/resolution/sanitizers live in
  `Source/Pipeline/Psychotype*.cs` (test-covered). The psychotype **label is never shown to the model**
  (only the rule), and the block is placed *before* the writing style so the style stays the final
  mechanical instruction. Master toggle **Use pawn psychotypes** in settings (default on); off omits the
  block and defers rolls. Edited from the same per-pawn editor as the writing style (Diary tab header
  icon → two sections), with a picker, re-roll, custom rule, pin/unpin, and override explanation; the
  tab tooltip shows both layers. Public API gains `GetPsychotype` / `SetPsychotypeOverride` /
  `ResetPsychotypeOverride` (mirrors the writing-style pair); the RimTalk bridge's Tier B now supplies
  the **psychotype** override (who the pawn is) instead of the writing style (how they write), and every
  bridge reset sweeps the old style override so existing saves migrate cleanly.
- **Children can keep a diary, and voices crystallize at adulthood.** The first-person minimum age drops
  to 7 (`minimumFirstPersonAgeYears`, XML-tunable back to 13); children below 13 roll from naive
  child-voice catalogs (both layers), and when a pawn crosses `psychotypeCrystallizationAgeYears`
  (default 13, the final vanilla growth moment) both unpinned layers re-roll onto the adult catalogs
  with a small continuity nudge. Player-picked layers are pinned and never auto-re-rolled.
- **Save-compatible with no migration step.** Loading an older save never fails: records that already
  have generated entries adopt an empty-rule **Neutral** psychotype so established voices do not shift,
  and entry-less records roll a fresh one lazily on their next entry — the psychotype is always created
  if missing. New per-pawn fields (`psychotypeDefName`, custom/external psychotype rules, `voiceStageBand`,
  pin flags) default safely on old saves.
- **Russian localization pass for the psychotype layer (native re-authoring, not translation).** All 23
  RU psychotype rules rewritten as idiomatic Russian: fixes one inverted meaning (Detached's «общество
  стоит усилий» read as *worth* the effort, the opposite of "company costs effort") plus grammar and
  calque cleanups across the catalog; the Volatile / Wide-eyed picker labels are now «неровный» /
  «восторженный». Child writing-style rules unify on the established «переданные факты» fact-anchor
  phrasing. The per-pawn voice editor is now fully localized — 45 previously English-only Keyed strings
  (`PawnDiary.Tab.WritingStyle*`, `PawnDiary.WritingStyle.*`, `PawnDiary.Psychotype.*`,
  `PawnDiary.Tab.Psychotype`) translated. The psychotypes settings toggle/tip,
  `PawnDiary.Prompt.PsychotypeLens`, and the RimTalk bridge's `Persona.LensRule` (now phrased as a rule
  so it reads naturally inside the lens wrapper) were reworked natively as well.
- **Adversarial-review hardening pass on the new voice layers.** A prompt **preview** no longer mutates
  saved state: `PersonaRuleFor`/`PsychotypeRuleFor` take an `ensureVoiceStage` flag so the read-only
  `PreviewExternalEventPrompt` path never rolls, stamps, or crystallizes a real pawn's voice. The per-pawn
  editor no longer clobbers a not-yet-recorded pawn's first-time rolls — **Save** writes the base
  style/outlook only when the player actually changed them (opening + saving an untouched editor used to
  overwrite the fresh trait/passion rolls with Default/Neutral). The psychotype custom-rule box no longer
  auto-pins on a keystroke (typing then clearing left the layer pinned); pinning is decided from final
  state at Save, matching the writing-style field. The "established pre-feature voice freezes to Neutral"
  guarantee now survives a disable→enable of the layer (the band stamp no longer masks the pre-feature
  signal while the layer is off). An external psychotype **override** (e.g. the RimTalk bridge's Tier B)
  still applies while the automatic layer is toggled off, instead of being silently dropped. The
  psychotype roll is wrapped in `Rand.PushState/PopState` so it no longer perturbs the seeded gameplay
  RNG. "Reset to base" now reports clearing **both** voice layers. Smaller guards: the thought-progression
  scan tolerates a throwing `MoodOffset()` (same stale-thought NRE class already fixed in the pawn
  summary), the VEF backstory fallback strips unresolved `[PAWN_*]` grammar tokens, and skipped-thought
  summaries log once per thought def instead of silently.
- **Fixed a mod-conflict NRE that silenced the whole diary on new games** (telemetry: every capture
  patch reporting the same `BackstoryDef.FullDescriptionFor` failure, e.g. refs `81AA8488`,
  `58B1F970`). With Vanilla Expanded Framework's transpiler on that vanilla method, certain modded
  backstories throw while the founding-arrival scan builds its backstory context. That scan gates
  all capture in a new-game session (`EnsureStartingArrivalsBefore` plus the tick-scan guards), so
  the pending flag never cleared: no entry was ever recorded and every signal re-threw the same
  error. Two layers of hardening: `SafeBackstoryDescription` falls back to the raw backstory
  template on failure (one warning per backstory def), and `TryRecordStartingColonistArrivals`
  isolates each colonist so an unexpected per-pawn failure costs only that pawn's arrival page
  instead of wedging the gate. Same-day follow-up for saves the wedge already damaged: `LoadedGame`
  now re-arms the founding-arrival bootstrap when any diary-eligible free colonist is missing an
  arrival page (the wedge aborted the scan mid-loop, so pawns after the broken backstory never got
  theirs), letting such saves backfill the missing pages on next load. Also covers mid-game
  installs and joins recorded while capture was off; pawns with existing pages dedup, so healthy
  saves see no change — the "already has an arrival" check (`HasArrivalEventFor`) now also consults the
  compact **archive**, so a founding colonist whose arrival page has aged out of the hot store (past the
  100-page cap) is no longer re-detected as "missing" and re-minted as a duplicate on every load.
- **Fixed a stale-lover-thought NRE that dropped interaction captures** (telemetry ref `237F2575`).
  Vanilla `Thought_OpinionOfMyLover.BaseMoodOffset` dereferences the lover relation, which can be
  gone by the time the pawn-summary top-thoughts picker calls `MoodOffset()`. `BuildTopThoughtsSummary`
  now snapshots each thought's offset once inside a per-thought guard (skipping ones that throw),
  and the weighted pick and label formatting reuse the snapshot instead of re-reading the fragile
  getter.
- **Fixed a thought-capture NRE on vanilla-rejected memories** (telemetry ref `475B9A13`; reported
  under Vanilla Psycasts Expanded but reproducible without it). A postfix on
  `MemoryThoughtHandler.TryGainMemory` also fires when vanilla's accept-gates (`CanGetThought`,
  the social-thought filters) early-return — *before* `thought.pawn` is assigned — and
  `Thought_Memory.MoodOffset()` then dereferences that null pawn inside
  `ThoughtUtility.NullifyingHediff` (the trace's VPE `NullDarkness` line is just Harmony's
  patched-method annotation, not the culprit). `ThoughtGainPatch` and `ThoughtSignal` now skip
  memories with a null `thought.pawn`. Also a data fix: a rejected memory was never actually
  gained, so it no longer produces a diary entry.

## 2026-07-07

- **Fixed the Diary tab running off the top of the screen** (user-reported; title/close button
  colliding with the top-center message stack). The responsive height clamp measured against the
  screen bottom, but vanilla hangs inspect tabs above the inspect pane's tab strip — 230px of
  chrome (165 pane + 35 bottom bar + 30 strip, verified against decompiled
  `InspectTabBase.TabRect` / `MainTabWindow_Inspect.PaneTopY`) the clamp never subtracted, so any
  UI-scaled screen height under ~1030 pushed the tab top off-screen. The clamp now anchors to the
  live `PaneTopY` (correct under pane-moving mods) via a new `UpdateSize()` override — vanilla's
  pre-layout hook, replacing the one-frame-late refresh in `FillTab` — with the constant fallback
  only for construction time. `<tabScreenHeightMargin>` now truly means "clear screen above the
  tab".
- **Integration API v2–v3 — LLM connection + event filters.** `PawnDiaryApi.ApiVersion` is now `3`.
  v2 adds `GetApiSetup()` / `AddApiLane(...)` to read and extend the player's LLM API lanes; v3 adds
  `GetEventFilters()` / `IsEventFilterEnabled` / `SetEventFilterEnabled` over the same saved flags as
  the settings Events tab. All are gated by the master integration toggle + main thread but not by a
  loaded game, so adapters can configure lanes at the main menu; the example adapter gains Connection
  and Events groups. Same-day hardening made raw lane keys opt-in via the new default-**off**
  *Share API keys with other mods* switch (`enableExternalKeySharing`; `hasApiKey` stays for
  presence-only checks) and records the requesting mod's `sourceId` on API-added lanes.
- **Opt-out error reporter shipped, wired live, and hardened.** New `Source/Diagnostics/` layer sends
  the mod's own `[Pawn Diary]` errors to a Cloudflare Worker ingest endpoint (D1-backed, per-IP
  rate-limited, real migrations via `npm run db:migrate`), opt-out through *Send anonymous error
  reports* (`enableErrorReporting`, default on) with a one-time in-game disclosure and *Turn it off*
  button. A pure, unit-tested privacy layer (`ErrorScrub`/`ErrorFingerprint`/`ErrorReportPayload`)
  masks paths/keys and live colony/colonist names; reports carry a stable anonymous install id, the
  real `About.xml` version (new `StampVersionFromAbout` MSBuild target replaces the hardcoded
  `1.0.0.0`), and a Workshop-vs-local `installSource`. EN/RU strings; schema in
  `DOCUMENTATION.md §8.1`.
- **Fixed a thought-capture NRE on animals / not-fully-built pawns** (user-reported; log refs
  `A2D21F2A`, `74E0E9CE`). `ThoughtSignal` called `Thought_Memory.MoodOffset()` before checking
  eligibility, throwing inside RimWorld's nullifier checks for pawns without `story`/`health`;
  `DiaryPatchSafety` contained it (no save broke), but the memory was dropped. The constructor now
  gates on `IsDiaryEligible(pawn)` up front — behavior-preserving, and it skips wasted work on one of
  the hottest hooks in the game.
- **RimTalk bridge implemented (Steps 2–8) and review-hardened.** The scaffold became the real
  two-way bridge mod (`design/RIMTALK_BRIDGE_PLAN.md`), built entirely on public API v1: an
  integration-level dropdown (Off / Shared context / + Conversations), Level-1 diary-memory injection
  into RimTalk prompts plus persona sync back (Tier A on, experimental Tier B off), Level-2
  conversation capture under the new `rimtalkbridgeConversation` External group, and an
  off-by-default engine mode that routes an entry through RimTalk's own AI with fallback to the
  normal path. An adversarial pass (PR #62) then fixed engine-mode timeouts, partner-POV parity on
  engine success, pawn-liveness re-checks, off-map Tier-B override cleanup, `transcriptLineCap = 0`,
  the Level-1 cache-refresh TTL floor, constructor-registration isolation, and throttle refunds for
  rejected submissions (`ThrottlePolicy.Release`). Open: H1 — Level-1 injection may not render under
  some RimTalk presets, pending in-game verification. Tests in `tests/RimTalkBridgeLogicTests`; EN/RU
  localization; RimTalk-typed code isolated behind a `cj.rimtalk` guard.
- **Diary tab height clamps to the screen** (scaled UI height minus the XML-owned
  `<tabScreenHeightMargin>`, `<tabMinHeight>` as the normal floor), so short screens no longer let
  the tab run off-screen.

## 2026-07-06

- **RimTalk groundwork.** Added the `rimtalk_chatter` ambient compat group (gated on `cj.rimtalk`) so
  ordinary RimTalk chat batches into `AmbientDayNote` instead of one diary candidate per line; reset
  the old log-only bridge to an adapter scaffold (`RimTalkBridgeGameComponent` +
  `PawnDiaryRimTalkBridgeApi`); and recorded the approved implementation plan
  (`design/RIMTALK_BRIDGE_PLAN.md`, open decisions U1–U9).
- **Release 0.3.1 prepared** (main + split Russian payloads, Mods-folder junctions refreshed; no
  runtime change).
- **Prompt context detail levels.** Global `Full`/`Balanced`/`Compact` presets plus per-API-lane
  overrides: lower presets run a pure, deterministic field selector over XML-tunable budgets
  (`DiaryContextDetailDef`), and failover lanes get their own pre-rendered prompt variant. A
  follow-up retune (Balanced 650/1000/600, Compact 350/600/400) made the presets visibly trim
  ordinary events; `Full` stays a faithful pass-through. Settings were reorganized around it:
  automatic event filters moved to a dedicated Events tab, the selector got its own Main-tab section
  with a cut/add comparison, and the experimental prompt-policy drawer was removed.
- **Per-pawn custom writing-style prompt.** Each pawn's Diary tab now opens an editor to pick the
  base style and write a pawn-specific custom prompt (`PawnDiaryRecord.customWritingStyleRule`,
  additive save key); effective priority (External override > Hediff override > Pawn custom > Base)
  is resolved by a new pure `WritingStyleResolutionPolicy`, and `GetWritingStyle` still returns the
  base style only. The full-width opener row was later compacted to a small header icon.
- **Advanced tab: removed 22 dead duplicate tuning rows** whose `DiaryTuningDef` copies were silently
  masked by `DiarySignalPolicyDef` (the policy def is read first and ships every value); the live
  editors remain in the "Signal:" groups, `TuningOverrideMigration` prunes saved overrides for the
  dead rows by exact key, and the tuning fields stay as the documented XML fallback. No save-format
  or runtime change.
- **Prompt diversity pass, batches 1–3 (English prompt text only).** Anti-sameness rules in the
  shared system prompts (vary the opener, ban stock phrases), default-no-speech interaction
  instructions, 2–5-sentence important/combat templates with `maxTokens=200`, 3-lens instruction
  pools extended to 40 of 87 groups (every frequent one), six new syntax-varying `DiaryPersonaDef`
  styles, fixes for the two raw event lines models echoed badly (`Event.Interaction`, `Event.Raid`),
  title-flow guards, and model-facing field-label hygiene.
- **Russian parity + naturalization.** Brought the Russian patch to parity with batches 1–3 (40/40
  instruction pools, a Russian stock-phrase ban, the six syntax personas authored natively), reworked
  ~60 calqued prompt lines into natural Russian (response verb unified to «Выведи»), and re-anchored
  the humor cues and new personas in native Russian patterns.
- **Writing-style catalog fixes (both languages, Def/localization only).** Fixed five weak or
  duplicate rule mechanics, de-clustered over-used example domains (walls/doors/meals), and added the
  hostile `verdict-first` style (RU «сперва-приговор») — the catalog is now 46 styles in both
  languages.
- **Integration API validation pass.** Budget reservation released if `Dispatch` throws; reflection
  signals honor the Reflection filter row; inert package-gated External groups treated as unclaimed;
  `defaultEnabled=false` groups now visible in the filter UI; stale external-API docs corrected.
- **Example adapter publish payload** (+ developer-themed preview art): `scripts/publish.ps1` now
  builds and packages the example adapter alongside the main and Russian payloads, with its own
  Workshop id and junction install.

## 2026-07-05

- **Public integration API v1 — first release numbering.** `PawnDiaryApi.ApiVersion` reset to `1` for
  the first public release, covering the full surface built during the internal pre-release ladder
  below; future additions increment from v2. Non-contract helper types across capture, ingestion,
  generation, pipeline, UI, and settings were internalized so adapters compile only against
  `PawnDiaryApi` + DTOs, and the *Allow external mod integrations* setting is the player-facing
  master switch (`IsExternalApiEnabled`, exposed separately from `IsReady`).
- **Pre-release API ladder v6–v22 (internal numbering, superseded by public v1).** In order: pawn
  summary + prompt-enchantment reads (v6), recent generated-entry context snapshots (v7), tracked
  entry handles + status snapshots (v8), direct text injection (v9), entry lifecycle listeners (v10),
  eligibility preflight (v11), writing-style catalog read (v12), external writing-style overrides
  (v13), per-pawn generation controls (v14), prompt preview (v15), external prompt
  fragments/enchantment inputs (v16), one-entry snapshot read (v17), external source attribution
  (v18), richer title/context query filters (v19), cheap entry stats (v20), context bundle snapshot
  (v21), and wrapped prompt entries (`SubmitPromptEntry`, v22), plus rolling token-spend budget
  guardrails (MT-7, XML-tunable `integrationPromptBudget*`).
- **Forced external recording.** `forceRecord` on the event/direct-entry requests skips the external
  budget, group/user toggles, and dedup so adapter-owned triggers can guarantee an entry — while
  still respecting validation, main-thread/game readiness, the master toggle, and diary-owner
  eligibility.
- **In-game API Explorer + docs.** The example adapter became a Dev-mode three-pane tool driving
  every public `PawnDiaryApi` method (searchable method tree, prefilled forms, color-coded result
  log, `[DebugAction]` quick actions, three demo External groups covering all submit paths), with
  `Source/PawnDiaryExampleApi.cs` as the single copyable facade and its pure parsing helpers under
  test. Added the one-page `EXTERNAL_API.md` guide and collapsed the API docs to `INTEGRATIONS.md`
  (shipped contract) + `design/EXTERNAL_API_CAPABILITIES.md` (planned).
- **Integration hardening (review follow-ups).** Adapter input can no longer forge internal
  `gameContext` (reserved-key rejection; flattened, length-capped `eventKey`/`sourceId`); budget
  reservations are refunded when the dispatcher drops an event; added `SubmitEventOutcome`, a
  64-field request-context ceiling, a 32-id provider-registration cap, a `GetEntryStats` archive-scan
  cap, an `(eventId, povRole)` archive index, provider-iteration snapshots + RNG isolation, true
  min/max snapshot ticks, thread-safe off-thread diagnostics, and a direct integration gate in the
  preview helper; the pure pipeline helpers went `internal`. An agent-guidance sweep also aligned the
  codebase with documented gotchas (`DiaryGameComponent.Instance` accessor, `DlcContext` precept
  reads, cheap pre-`DiaryPatchSafety` exits, Harmony `2.4.1.0` reference match).
- **Advanced automatic event filters.** New Advanced-tab list to disable per player which
  automatic-capture `DiaryInteractionGroupDef` groups record; external API submissions intentionally
  bypass these filters.

## 2026-07-04

- **Integration API v2–v5 (internal pre-release numbering).** Title snapshots
  (`GetRecentEntryTitles`, v2), the writing-style publish read (`GetWritingStyle` →
  `DiaryWritingStyleSnapshot`, side-effect free, v3), pawn-context providers
  (`RegisterPawnContextProvider(id, Func<Pawn,string>)` with sanitized one-line output, gated by the
  new default-on `allowExternalIntegrations` setting, v4), and filtered title reads via
  `DiaryEntryTitleQuery` + a pure `DiaryEntryTitleFilter` (v5). Regression tests pin that a pawn's
  writing style is injected into the LLM *system* prompt, never a user field.
- **External-API design consolidated (docs only).** New `design/EXTERNAL_API_CAPABILITIES.md` is the
  authority on the public surface's shape — every requested/proposed capability with status, internal
  hook, and open decision. The cross-cutting forks were closed (consent = single master toggle,
  default on; injection gating = optional claiming group; capabilities ship one at a time, each with
  its own `ApiVersion` bump), the outbound "machinery as a service" read surface and a v4 provider
  brief were added, and the scattered planning markdown moved into `design/` with an index.
- **Minimal RimTalk bridge scaffold.** `integrations/PawnDiary.RimTalkBridge/` shipped as a separate
  mod that Harmony-patches RimTalk's accepted-chat boundary and logs chat facts + recent title
  snapshots — diagnostic only. A persona-alignment note records that writing styles and RimTalk
  personas share pawn identity/memory while staying separate private-writing vs spoken-behavior
  surfaces.
- **LLM/network adversarial-review fixes.** Response cleanup no longer silently empties or truncates
  valid entries (mismatched reasoning tags recovered, stray closers dropped, over-stripping
  narrowed); `Retry-After` is honored on 429/503 (the lane cools for the longer of server wait and
  local backoff, capped at 1h); the capability refresh re-runs for the player's final edit; redaction
  masks whole `Bearer` tokens and settings-window errors; non-finite temperatures are coerced. The
  pure parser also unwraps whole-response Markdown fences, strips leading final-answer labels, and
  reads `output_text` typed parts.
- **Advanced overrides UX pass.** Raw XML Def editing is gated behind an experimental opt-in and
  marked as a testing escape hatch, with All/Changed/Raw filters, collapsed "using XML/default"
  drawer rows, live parse validation with a colored preview (malformed edits stay unapplied),
  per-tab/drawer reset, and a copyable plain-text changed-settings summary.
- **Tooling + polish.** `scripts/publish.ps1` gained `-AutoBump` (patch bump of `About.xml
  <modVersion>`, written back) and a Russian payload Workshop id; bumped to `0.2.2`. Dev event panel
  buttons fixed (no pause, normal left-click handling); hidden humor cues default raised to a 20%
  chance.

## 2026-07-03

- **Release 0.2.0 prepared.** Source `modVersion` `0.2.0`, split Russian payload, refreshed
  junctions; a localization audit aligned EN/RU coverage and localized the arrival/dev mock prompt
  labels; release hardening kept explicit reasoning `none` off even when a provider omits it, kept
  external pages in the External domain despite native-looking context keys, clamped invalid reflow
  tuning, and made startup patching tolerate partial assembly type-load failures.
- **Event-coverage pass (XML-only, Anomaly focus).** New `EVENT_COVERAGE_PLAN.md` inventoried every
  reacted-to moment, then Tiers 1–2 shipped: retone groups (Anomaly entity raids, weather hardship,
  three mental-break registers), new prompt enchantments (malnutrition, heatstroke/hypothermia,
  anesthetic, psychic shock, carcinoma, mechanites, and Biotech/Anomaly/Odyssey conditions),
  drunk/fading-memory writing-style overrides, new observed conditions with active-time caps and
  cooldowns, and new event windows (`MechClusterLanded`, `ShortCircuitAftermath`, `SelfTameJoined`) —
  English + native Russian. A review pass corrected silently-dead `ThingPresent` defNames (obelisks,
  unnatural corpse, harbinger tree) and added companion display groups for the new page-recording
  defs.
- **Body-part diary events.** Immediate Hediff-domain entries for artificial-part installs, anomalous
  body changes, and living-pawn part losses (synthetic `addedpart`/`organicpart`/`missingpart` keys,
  XML-owned tier/stance tuning, EN/RU strings), with new
  `bodyPartAnomalous`/`bodyPartArtificial`/`bodyPartLost` color accents, new `psychic`/`royalty` cues
  split off the shared ones (`colorCue` is saved per-event, so old entries keep their colors), and an
  Anomaly/body-horror prompt-atmosphere pass.
- **Observed-condition / event-window lifecycle hardening.** Event windows can early-cancel via an
  XML still-present probe (`MechClusterLanded` ends once no mechanoids remain); three Anomaly
  presences gained `maxActiveTicks`/`restartCooldownTicks`; the unnatural corpse became a pawn-scoped
  observer that haunts only the imitated colonist and ends when the corpse is destroyed; and a dev
  "Force-close active event window" action was added.
- **Render-time paragraph reflow.** Long single-line entries now split into readable paragraphs at
  render time via the pure `DiaryParagraphReflow.ReflowLine` (sentence ends, year mentions,
  semicolons, em-dashes, hard length cap) — saved `GeneratedText` is never mutated. Tuning in
  `DiaryUiStyleDef.xml`; new pure test project `tests/DiaryParagraphReflowTests`.
- **Mod-compatibility target survey (docs only).** Popular social-interaction mods surveyed and
  tiered — Tier A XML-only compat groups behind `enableWhenPackageIdsLoaded` (VSIE, Hospitality,
  SpeakUp, Way Better Romance, …), Tier B personality mods → context providers (RimPsyche,
  Psychology), Tier C LLM chat → outbound context (RimTalk) — with verified packageIds/defNames per
  mod.
- **Fixes and polish.** Generic short-window event-type dedup (`genericEventTypeDedupTicks` plus a
  shared death-description key so death pages cannot double-emit); title fallback rejects answer
  labels, instruction echoes, and out-of-contract titles; reasoning capability auto-refreshes on four
  settings-window triggers; API-row label/button alignment + missing RU reasoning strings; arc
  reflections honor `arcReflectionMaxEntriesPerYear` across 1–10; the dev panel's non-functional
  Events section is hidden (code kept for re-enabling); stale memory-decay translation refs removed.

## 2026-07-02

- **Public mod-integration API v1 (inbound events; internal numbering).** Other mods push moments via
  the stable `PawnDiary.Integration.PawnDiaryApi.SubmitEvent(ExternalEventRequest)` facade —
  validated, crash-isolated, main-thread-guarded, routed through the normal bus as a new `External`
  source. Prompt policy stays in XML (an External-domain group claims the `eventKey`); new
  `externalEventDedupTicks` knob and `enableWhenPackageIdsLoaded` group flag, EN/RU strings, the
  `INTEGRATIONS.md` contract, and a buildable adapter template in
  `integrations/PawnDiary.ExampleAdapter/`.
- **Reasoning handling matured.** A per-lane "Reasoning tag" dropdown pins the wrapper tag a model
  emits; endpoints advertising reasoning capability constrain the effort dropdown and clamp outgoing
  `reasoning_effort`, and the request omits `reasoning_effort` when effort is `none` (both fixing
  400s on non-reasoning models like Gemma); the built-in Auto stripper covers seven tag names;
  capability auto-fetches at model pick; and per-row Test connection runs concurrently (Fetch models
  stays single-flight).
- **Metalhorror tone handling improved.** New `MetalhorrorInfection` observed condition keeps a soft
  hidden-infection dread alive while any home-map colonist is infected (tone-only, never names the
  hediff or host), and `MetalhorrorEmergence` now lasts until the live `Metalhorror` leaves the map
  instead of a blunt 2-day cap.
- **Pre-release performance pass.** Diary-tab name-highlight refresh re-measures only on real
  highlight changes, the hottest Harmony hooks use a closure-free `DiaryPatchSafety.Run` overload,
  and batch scanners allocate keys lazily.
- **Packaging/build.** No longer bundles Harmony — runtime comes from the `brrainz.harmony`
  dependency (verify hook fails on a bundled copy; compile-time reference kept) — and the project
  builds from any checkout via the `RimWorldManaged` MSBuild property / `RIMWORLD_MANAGED` env var.
- **Capture/robustness fixes.** Single-item interaction batches become normal standalone entries;
  lasting prompt overrides gained a stale-state guard; malformed (`ERR:`/blank) quest descriptions
  are filtered from prompts; arrival capture no longer risks an early-worldgen faction log; Harmony
  patching is per-class so one fragile target cannot block the rest; the quest-UI accept fallback is
  silent on clean boot.
- **UI/dev polish.** Diary export moved to Debug Actions (hot + archived pages + backing records);
  the inspect-tab Diary button dropped its unread markers (the command overlay still signals); the
  prompt-enchantment editor exposes `chance` (legacy `frequency` accepted but pruned); key-backed
  override boxes stay blank when XML owns the Keyed default; a Russian localization review; UI
  text-clipping hardening; destructive dev buttons tinted red.

## 2026-07-01

- **Tuning and prompt settings tabs for XML overrides.** Mod settings reorganized into
  Main/Prompts/Styles/Tuning tabs. Prompts holds shared/event prompts plus a full prompt-policy and
  weights editor; Styles edits writing-style rules/tags; Tuning exposes scalar/list/table knobs from
  `DiaryTuningDef`, `DiarySignalPolicyDef`, and `DiaryContextReactionDef`. Tuning and Prompt policy
  use a two-pane editor with per-field widgets, reset, accent coloring, filtering, and tooltips.
  Overrides persist per player (`TuningOverrideStore`) and apply immediately to live Def fields;
  follow-ups register Def-backed groups lazily, replace translation-key editors with literal-text
  override fields, and make literal overrides win over Keyed at generation time.
- **Pawn arc reflections.** Passion/psylink/xenotype/title progression pages plus rare yearly arc
  reflections from de-duplicated hot/archive memories. XML owns templates/cadence/policy; fixtures,
  pure tests, and `PAWN_ARC_REFLECTION_IMPLEMENTATION.md` cover the flow.
- **Gray-flesh suspicion hands off to emerged metalhorrors.** Observed conditions gained
  `suppressWhenThingDefNames`, age-based decay (`promptDecayTicks`/`promptDecayMinMultiplier`),
  `maxActiveTicks`, and restart cooldowns. `AnomalyGrayFleshEvidence` tracks gray-flesh samples and
  stops once a visible metalhorror exists; `MetalhorrorEmergence` now observes the spawned ThingDef.
  Long-lived style-override hediffs (inhumanization, joywire, etc.) are suppressed from prompt
  enchantments; settings expose the decay/cooldown/suppression knobs.
- **Diary arcs start with arrival and continue from the prior ending.** Starting-colonist arrivals
  flush first on new games, arrival refs insert at the front of the index, and prompts gain an
  XML-owned `PreviousEntryEnding` field. Pure tests cover the new source token.
- **Resurrected pawns keep writing.** A death page stays a historical boundary but no longer ends
  the diary if RimWorld revives the same load ID; capture, generation, rendering, export, and
  retention all ignore the old cutoff while alive.
- **Archived-page purge moved to a direct Debug Action** (`Pawn Diary > Purge archived entries for
  pawn...`): a pawn picker clears only that pawn's compact archive rows, leaving active hot events
  untouched.
- **Diary tab pagination no longer flickers over quiet refreshes.** A year-index build is full
  blocking only when no cached index exists.
- **Dev event panel click split.** Def-backed rows left-click to fire, right-click to open the Def
  selector; button titles mirror the selected menu label.
- **Generated-text sanitizer hardened.** Handles incomplete speech markers, `speach` typos,
  unfinished bracket/reasoning tags, and leaked Unity rich-text tags before save/UI.
- **Prompt settings menu labels shortened:** compact system-prompt names, internal event keys hidden.
- **Russian localization caught up for reflections**, rewritten idiomatically.

## 2026-06-30

- **Mod versioning added.** `About/About.xml` now carries `modVersion` (`0.1.0` initially), and
  `scripts/publish.ps1` stamps that version into both the main and Russian localization payloads.
  `-Version <value>` can override the payload version for a release.
- **Workshop/docs cleanup.** `scripts/publish.ps1` no longer ships `Source/` or other
  development-only folders to Workshop, while maintainer docs remain included. `DOCUMENTATION.md` was
  shortened around current architecture and policy, and `EVENT_PROMPT_MAP.md` now uses current-state
  Mermaid diagrams for capture, prompt policy, overrides, and active weights.
- **Generation and diary UI polish.** Quest acceptance now only marks quests seen; completed/failed
  outcomes generate colony-effort pages. Rare quadrum reflections add long end-of-quadrum pages from
  dated highlights with XML tuning and optional `forcedModel`. The Diary tab unread marker moved to
  the top center, rewrite moved to a subdued footer icon, and the work/social random-generation
  sliders were merged into one migrated weight.
- **Starting arrival context improved.** Founding-colonist arrival prompts now receive each pawn's
  childhood/adulthood title, full in-game backstory description, and compact backstory effects
  (skill bonuses, disabled work/tasks/tags, required tags, and forced/disallowed traits), and the
  neutral arrival instruction asks the model to connect those facts with the starting scenario.
- **Dev event test panel.** Dev mode now exposes `Pawn Diary > Event test panel...` with real vanilla
  triggers for registered diary sources, prompt fixtures, former Diary tab dev tools, persisted
  selections/sections, Def selection for trigger types, and a purge action for compact archived pages.
- **Retention and archive controls.** Active hot pages and compact archive rows now have separate
  per-pawn caps (`maxActiveDiaryEvents`, default/max 100; `maxArchivedDiaryEvents`, default 10000).
  Old oversized hot caps are clamped and older hot rows compact into archive rows on load/save.
- **Review hardening pass.** Tightened API-key masking/log redaction, memoized interaction
  classification and tagged grammar checks, added O(1) diary lookup by pawn id, shared same-tick
  colonist snapshots, snapshotted failover lanes, fixed arrival/death tie ordering, stripped all stray
  reasoning closing tags, validated conflicting observed-condition map flags, made text truncation
  surrogate-safe, aligned event-id lookup casing, prefiltered kill fallback work, and localized
  dev-only LLM debug labels.
- **Observed conditions.** Added the scan-backed system for lasting colony states (`MapDanger`,
  `GameCondition`, `ThingPresent`, `PawnHediff`) with pure lifecycle planning, debounce-aware
  persistence, prompt candidates, XML defs for map threats/toxic fallout/solar flare/Anomaly gray
  flesh evidence, EN/RU strings, 62 pure tests, docs, and a rebuilt DLL. Retired the fixed-timeout
  `MetalhorrorSuspicion` event window; `MetalhorrorEmergence` ships disabled pending observable-state
  verification. Follow-up hardening made recording transactional/retryable, skipped empty pawn-hediff
  scans, added invalid subject-pawn scope validation, and corrected save comments/header dates.
- **Renderer/save-compat extraction.** Continued Plan 9 by moving expanded-card measurement and
  rendering into helper classes while leaving behavior/visuals unchanged. Plan 6 moved load-repair
  normalization into `DiarySaveNormalization`, removed dead wrappers, added 46 pure
  save-normalization assertions, documented Scribe compatibility/runbook coverage, and rebuilt the
  DLL.
- **Archive compaction fixes.** Hardened compacted failed/stale page fallback text/title, cleared
  matching archive rows from dev prompt-suite reset, prevented hidden-hot/archive duplicate cards,
  moved overflow selection into `DiaryArchiveCompactionPlanner`, added coverage, and rebuilt the DLL.
  Same-year diary refreshes now update selected-year layout in place so generation/title completion no
  longer flickers to the loading panel.

## 2026-06-29

- **Archive compaction landed.** Added the compact `diaryArchiveEntries` save store and
  `DiaryArchiveRepository`, so old completed/stale/failed displayable pages compact before hot refs
  are dropped while active pending rows stay hot. The Diary tab, Social-log links, dev export,
  archive eligibility tests, and the earlier Plan 3 design doc were updated around the new archive
  flow.
- **Large-history Diary tab performance improved.** `DiaryContextFields` now scans context strings
  without repeated `Split` allocations, and sliced year/card builds tolerate generation-side state
  ticks without restarting unless the visible structure changes. Large histories load much faster
  while keeping save data and parsing semantics unchanged.
- **Thought classification tightened.** Risky broad positive/negative thought substring tokens were
  replaced with exact defName lists plus precise prefix/suffix/segment matchers for mod coverage,
  with pure `GroupNameMatcher` tests covering opinion-flipped death/loss regressions.
- **Event ingestion bus completed and hardened.** All 17 catalog sources now submit uniform
  `DiarySignal` payloads through one dispatch/dedup/emit path with no save-format changes. Review
  fixes made dedup pruning respect each key's own window, made zero/negative windows opt out cleanly,
  restored pre-roll dedup ordering, and updated stale comments/docs.
- **Gray flesh event-window monitor disabled.** `MetalhorrorSuspicion` is disabled in XML because
  the gray-flesh spawn signal could leave the status effectively always active; the row remains as a
  documented template until a safer monitor exists.

## 2026-06-28

- **Prompt and condition style cleanup.** Memory-decay hediffs now stay prompt context instead of
  forcing a lost-thread style, hediff style overrides suppress duplicate matching prompt guidance,
  external game/mod text included in prompts is flattened and capped, and bad title/quest follow-up
  text now falls back to safe humanized excerpts.
- **Russian localization shipped and polished.** A full Russian Keyed/DefInjected set now ships with
  glossary-aligned RimWorld terms, rewritten writing styles, localized humor cues, neutral
  placeholder guidance, UI/prompt copyedits, complete DefInjected coverage, and key/placeholder
  parity checks.
- **Russian Workshop payload support.** `scripts/publish.ps1` now builds Russian as a separate
  language-mod payload with translated metadata, localized preview art, separate Workshop id support,
  and local junction installs alongside the main payload.
- **Dev/UI access fixes.** Dev mode can export all saved diary pages and backing records to a UTF-8
  text file, prompt-suite fixtures ignore live prompt enchantments, and Diary tab registration/opening
  is retried defensively after startup.
- **Condition and event-window features.** XML hediff style overrides can temporarily force writing
  styles for active conditions; event windows now cover birthdays, heart attacks, and prison breaks.
  Review hardening added trigger-rule prefilters, isolated optional event-window failures from other
  capture paths, and restored missing English DefInjected stubs.

## 2026-06-27

- **XML event-window support expanded.** `DiaryEventWindowDef` can now create start/end/timeout
  diary entries from incident, quest lifecycle, spawned-thing, and letter signals, with built-in
  windows for metalhorror suspicion, ancient dangers, and void monolith discovery or activation.

- **Anomaly and hediff prompt routing improved.** `DeathPall` and `UnnaturalDarkness` now route
  through more specific mood groups, Anomaly hediffs such as revenant hypnosis and cube effects can
  trigger immediate/progression entries, and common drug hediffs gained localized prompt condition
  overrides.

- **Long-history retention and UI performance reworked.** The history cap is now per pawn, the
  retention plan is covered by pure tests, background maintenance uses a bounded hot window, and the
  Diary tab virtualizes long lists with sliced loading, unread flags, pawn-view reuse, stale-entry
  handling, archived-pending fallbacks, and dev-mode stress history.

## 2026-06-26

- **Localization, packaging, and maintainer docs cleaned up.** DefInjected folder names were fixed,
  the Workshop preview was replaced with human-made art, publish output now includes source and
  reference docs, and `DOCUMENTATION.md` was condensed into a current-state guide.

- **Generation and retention reliability improved.** Catch-up scans became demand-driven, orphan
  recovery moved to its own pass, retained diary events gained a settings cap, and completed LLM
  results now drain while the game is paused.

- **Prompt and compatibility policy expanded.** Event prompt lookup now falls back through several
  XML keys for modded compatibility, SpeakUp rows route as promoted chitchat, tagged social-log
  grammar uses a reply-suppression guard, and stock writing styles were retuned around distinct
  mechanics.

- **Runtime and UI hardening landed.** Diary hooks, ticks, save/load work, startup tab injection,
  and immediate-mode drawing now isolate failures; mental-break card styling was softened; unread
  markers skip world inspect panes; and thinking-model self-edit cleanup gained parser coverage.

## 2026-06-25

- **Diary tab surfaced as the default.** Fresh settings now use the pawn inspect tab, selected
  humanlike corpses can show Diary too, unread markers appear in tab mode, and command mode remains
  available with its underline/writing dots.
- **Generation controls expanded.** Event prompts can prefer a configured model while keeping normal
  failover, dev-mode cards can regenerate a saved POV page, raids gained timing/prompt tuning, and
  eligible first-person entries can receive subtle XML-tuned humor cues.
- **Large structural extraction pass.** Per-POV state collapsed into `PovSlot`, saved events moved
  into `DiaryEventRepository`, generation code split by stage, Harmony patches split by capture
  domain, settings/UI code moved into focused files, and dead diary-bound helpers were removed while
  preserving existing Scribe keys and behavior.
- **Diary tab performance and cache work.** Long-history drawing now memoizes counts by render token,
  culls offscreen cards, caches expanded-card measurements, and routes visible-entry reuse/year
  ordering through `DiaryTabVisibleEntriesCache`.
- **Pure helpers and tests broadened.** Prompt enchantment planning, text decorations, LLM request
  JSON, parser speech-marker guards, API lane identity, and context-builder substeps moved into
  focused pure helpers with targeted coverage.
- **XML/localization boundaries tightened.** Consciousness/stagger/mood/intoxication policy moved to
  XML fallbacks, localized colony naming moved out of saved models, instruction resolution moved out
  of settings DTOs, live pawn fact capture moved to `PawnFactCapture`, and English DefInjected stubs
  were filled for new prompt/event text.
- **Release and review cleanup.** Workshop metadata, README/publish output, and package-id handling
  were prepared for public release; review fixes addressed stale card-height caching, unknown
  decoration validation, shared API pinning, and small dead facade surface.

## 2026-06-24

- **Native Ollama API mode removed.** API lanes now use OpenAI-compatible Chat Completions or
  OpenAI Responses only; local Ollama remains usable through its OpenAI-compatible endpoint.

- **Harmony declared as a mod dependency.** `About/About.xml` now declares `brrainz.harmony` for
  RimWorld 1.6 while keeping the bundled DLL as a run-from-clone fallback.

- **API lanes hardened for free-tier pools.** Routing modes, lane reordering, auth modes, Gemini
  reasoning support, exponential cooldowns, failover snapshots, settings cleanup, byte-capped reads,
  and prompt-lab auth options were added.

- **Pawn ability-use events added.** Successful ability activations can create cooldown-weighted solo
  entries with ability, category, target, and cooldown context.

- **Anomaly psychic ritual events added.** Completed psychic rituals fan out to relevant pawns with
  quality/context text and darker XML-guided prompt rules.

- **Ideology ritual events added.** Finished non-canceled rituals create role-aware entries for
  authors, targets, participants, and spectators, with DLC-safe edge-group policy.

- **Important-event status context added.** Prompt enchantments can add one weighted Royalty title or
  Ideology role cue for important events.

- **Personas reworked into writing styles.** Settings, picker text, prompt defaults, and presets now
  describe writing mechanics instead of chat personas while keeping internal save keys stable.

- **Per-event-type prompt variation.** Interaction groups can define instruction and tone variant
  pools, with persisted instruction rolls and deterministic tone picks.

- **Diary UI tweaks.** Older cards collapse by default after the first three entries, and save-time
  cleanup strips echoed schema punctuation while preserving prose.

- **Reliability fixes.** Startup patching, cooldown failover, no-auth lane identity, query-key URL
  fragments, and prompt-variant seeds were hardened.

- **Docs and live-smoke coverage added.** RimBridge/GABS hook-validation notes, an auto-test
  scenario, and a live-smoke Lua fixture were added.

- **Save compatibility documented.** Player metadata and persistence docs now state that diary
  history is self-contained and does not attach gameplay defs/components to pawns or maps.

## 2026-06-23

- **Settings window compacted.** Request tuning, Prompt Studio, writing-style editing, and the hidden
  generated-speech Social-log toggle were reorganized into a smaller settings surface.

- **Diary entry point moved to pawn selection.** Selecting one eligible colonist or corpse now shows
  a Diary command button instead of the old inspector tab-strip button.

- **Mod icon added.** `About/ModIcon.png`, `modIconPath`, and publish-script texture copying were
  added for the mod icon and runtime command icons.

- **Save-time tag sanitizer improved.** Valid speech blocks are preserved while malformed closers and
  hallucinated bracket tags are repaired or stripped.

- **Prompt-lab coverage expanded.** Static prompt fields, generated Romance/Raid/Quest contexts, and
  XML template/event prompt checks are now covered.

- **Thinking-model response parsing fixed.** Typed visible output now wins over flattened reasoning,
  more reasoning part types are skipped, and blank Ollama messages fall back to root `response`.

- **Prompt Studio can edit event-source guidance.** XML catalog prompt and enhancement text can now
  be overridden per event source.

- **Review hardening pass landed.** Save/load dedup, endpoint editing, capped HTTP reads, deferred
  PlayLog grammar rendering, reflection warnings, diary lookup null tolerance, name-highlight
  throttling, DLC context reads, and death-context wording were tightened.

- **Day reflections require an XML-controlled important signal.** End-of-day summaries now skip
  filler-only days by default, with XML able to re-enable quiet summaries.

## 2026-06-22

- **Quest event source added.** Accepted and ended quests now create lifecycle entries with compact
  quest, issuer, reward, and result context.

- **Raid event source added.** Raid incidents fan out solo entries to eligible colonists with
  incident/faction/points payloads and colony-level deduplication.

- **Prompt structure split.** Shared system prompts were shortened, with event-source guidance moved
  into `DiaryEventPromptDef` rows before group instruction/tone flavor.

- **Diary prose nudged toward immediacy.** Prompt guidance now asks for one sensory detail, one
  emotional beat, and an implied consequence from supplied facts.

- **Diary tab and prompt suite updated.** The tab is taller, dev cards gained copy support, and the
  Prompt suite became a data-driven dropdown with clearable prompt-only cards.

## 2026-06-21

- **Formatting system matured.** Dev previews, stronger staggered speech, conflict/mental-break page
  washes, dark/strange speech variants, and status-aware pawn-name highlighting were added.

- **Event Catalog completed for current live sources.** Capture decisions now cover solo, pair,
  batch, ambient, day-reflection, and neutral arrival/death routes.

- **Romance relation events added.** Lover, spouse, ex-lover, and ex-spouse changes now route through
  the Romance domain.

- **Hardening pass landed.** Work sampling, catalog dispatch tests, recorders, age/consciousness
  gating, save/load behavior, parser handling, caches, MiniJson, and provider support were tightened.

## 2026-06-19 - 2026-06-20

- **Generated speech Social-log injection prototyped.** Direct-speech rows can be generated for
  initiator entries, but Social tab display remains unreliable, so the setting stays hidden and off.

## 2026-06-17 - 2026-06-18

- **Workshop release and publishing flow prepared.** Workshop metadata, preview art, publish script,
  package-id cleanup, and verification hooks were added.

- **LLM compatibility broadened.** Chat Completions, OpenAI Responses, native Ollama Chat, reasoning
  cleanup, and raw-response debug views were added; native Ollama was later removed.

- **Pipeline extracted into pure contracts.** Prompt planning, response parsing, text decorations,
  domain recovery, and diary architecture moved into focused helpers with tests.

- **Health, combat, and social capture expanded.** Hediff progression, combat tale batching, insult
  batching, direct-speech POV rules, and important health/capacity cues were added.

## 2026-06-16

- **Core experience matured.** Work entries, prompt-lab support, title generation, UI readability,
  generation weights, pending recovery, LLM retries/failover, batching, day reflections, and
  save/load robustness improved.

## 2026-06-14 - 2026-06-15

- **Diary gameplay coverage broadened.** Arrival/death chronicles, writing personas, DLC-safe
  context patterns, broader event routing, localization coverage, the pawn Diary tab, context
  builders, and early event display/generation flow were established.

## 2026-06-13

- **Initial diary system.** Added the base diary event model, generation path, UI surface, and
  RimWorld integration.
