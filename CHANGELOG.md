# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [repowiki/README.md](repowiki/README.md) describes the current state. The public integration
contract starts at `PawnDiaryApi.ApiVersion == 1`; older entries below preserve the internal
pre-release version ladder for project history.

## 2026-07-23

- **Lore-memory prompt trim.** Removed the world-lore primer and its setting; authored lore now reaches prompts only through relevant memory recall.
- **Odyssey Phase O3 Mechhive ending.** Verified and patched the exact private Cerebrex-core destroy/scavenge owner, transactionally defers the matching generic Quest page, commits normalized replay state, and writes one operator-only EN/RU ending while failing open on every missing gate.
- **Odyssey Phase O3 exploration.** Extended exact visible asteroid/station/Mechhive destination categories and enriched the existing Quest route for all nine installed gravcore roots without guessing recovery or the terminal choice.
- **Lore memory — post-review fixes.** Retargeted the `LoreSeed_Prog_Xenotype` progression seed onto
  the `GeneIdentityChanged` token the runtime actually emits (Biotech Phase 5 consolidated xenotype
  transitions into it); it was keyed only on the retired `XenotypeChanged` token and could never fire
  from live gameplay. Dropped the unreachable `MechCluster` keyword from the two mechanoid seeds (mech
  clusters land via `IncidentWorker_MechCluster`, which is not raid-like, so `raid=MechCluster` is
  never emitted; those seeds still reach recall via `faction=Mechanoid`). Added a `DiaryLoreSeedDef`
  config-error and a `MemoryExtraction.IsQueryReachableToken` guard so an authored keyword the prose
  query tokenizer would drop (under three chars or a stopword) is reported at load instead of shipped
  dead. New pure regression tests: progression seeds must target a runtime-emitted token, and every
  catalog keyword token must be query-reachable.
- **Master Wave 11 / Anomaly Phase A3.0.** Added the DLC-safe terminal void answer: defensively patches both public `VoidAwakeningUtility.EmbraceTheVoid`/`DisruptTheLink` methods, verifies the reached monolith level, defers and owns the single-pawn `EmbracedTheVoid`/`ClosedTheVoid` Tale, commits a saved terminal token, writes exactly one actor-authored ending page (with an optional non-recap arc reflection), suppresses the duplicate monolith quest-success page, and fails open (never zero, never two) on patch/verification failure, old saves, or repeat calls — EN/RU prose already shipped.
- **Master Wave 11 / Biotech Phase 8.** Added DLC-safe psychic-bond lifecycle pages and XML-tuned severe interrupted-deathrest capture with exact nested-signal ownership and EN/RU parity.
- **Master Wave 11 / Biotech Phase 7.** Added DLC-safe pollution thresholds, strongest-band bounded transitions, reload-safe reclamation/decay, qualitative ambient-pressure context, and EN/RU parity.
- **Lore memory L5 — progression lore seeds.** Identity-changing events (psylink, xenotype, gene
  identity, mechlink, royal titles — the six audited registered event Def tokens, XML-owned in
  `progressionLoreSeedEventDefNames`) now attach at most one owner-only authored memory
  immediately after the ordinary lived deposit succeeds: pure deterministic
  `LoreSeedPlanner.PlanProgression` with exact-Def lifetime uniqueness, the four-seed progression
  and two-core lifetime caps, synthetic non-colliding `loreseed-progression:` sentinels, real
  ticks with zero narrative offset, and six new EN/RU catalog seeds (catalog now 40). Witnesses
  never receive one; replays and noise-gated events never trigger. The full
  LORE_MEMORY_SEED_PLAN is now implemented.
- **Lore memory L3 — the full EN/RU seed catalog.** Shipped all four §11 passes as one gate:
  the stable-keyword audit added five Def-name-valued context keys (`raid`, `faction`, `hediff`,
  `mood_event`, `observed_condition`) ahead of the localized keys in `contextKeywordKeys`; a
  34-seed `DiaryLoreSeedDefs.xml` catalog across 18 mutex groups built on verified base-game
  backstory categories/defNames, xenotypes, hediffs, and incident/condition tokens (6 core
  identity seeds with exact evidence); an independently authored native-Russian DefInjected pass;
  and QA fixtures enforcing the frozen vocabulary, per-seed reachability, the reserved specific
  slot on 15 supported pawn fixtures, EN/RU parity, and three end-to-end recall smokes. The lore
  seed layer is now live rather than a no-op; with L1/L2/L4 this completes the releasable feature.
- **Lore memory L4 — context-detail lore primer.** New independent default-true `enableLorePrimer`
  setting (EN/RU) appends a localized world-model paragraph — no FTL, no aliens, mixed tech,
  political isolation, each tier opening with the explicit-facts override — as the LAST
  system-prompt paragraph of all 11 first-person templates, chosen by the effective (lane-resolved)
  context-detail level (Compact 3 / Balanced 4 / Full 5 clauses); neutral death/arrival and title
  requests never receive it.
- **Lore memory L2 — initial planner, persistence, settings, and wiring.** Added `DiaryLoreSeedDef`
  (closed usage/tier tokens, config-error validation, core-requires-exact-evidence), the pure
  deterministic `LoreSeedPlanner.PlanInitial` (eligibility matrices, one reserved pawn-specific
  slot with core-first preference, weighted sampling, plan-local mutex, lifetime core cap),
  persisted per-pawn rosters/histories, the default-true `enableLoreSeeds` setting (EN/RU), lazy
  pre-recall deposits with `loreseed:` sentinels and live DefInjected prose, the 20-day hard core
  recall cooldown, and disabled-lore recall/eviction exclusion that preserves rows. Catalog ships
  in L3; the layer no-ops until then.
- **Lore memory L1 — contracts, truthfulness, and bounded prompt projection.** Memory fragments
  gained optional lore provenance and a narrative-age offset (age band + minimum-age guard only;
  real ticks keep driving decay/cooldown/eviction); recall is now gated on the finally chosen
  template's `MemoryContext` projection so neutral death/arrival/title pages never bump recall
  metadata; the three reflection templates project bounded memory; a non-empty memory field is
  required in every context-detail preset under the universal two-line/500-char whole-pick cap.
- **Ideology Phase 3 review hardening.** Cancelled net-zero belief changes, made diagnostics read-only,
  skipped no-DLC scans, and repaired load-time tick and quadrum normalization.
- **Documentation restructured into a repository wiki.** Migrated human docs into a browsable
  `repowiki/`, split oversized architecture/event/API files into focused wiki pages plus committed
  agent `skills/`, made the README a wiki landing page, and moved development-only plans and tooling
  to ignored local folders.
- **Exact insect-meat belief evidence.** Added exact insect-meat food-doctrine evidence with loaded
  coverage, hardening the exact-food hooks after deduplicating two adversarial reviews.

## 2026-07-22

- **Ideology Phase 2 exact belief enrichment.** Implemented exact, page-silent enrichment for
  completed conversion rituals, the Counsel slice, Crisis of Belief (folding IdeoChange's companion
  transition into its page), and throne/leader authority-speech, plus Narrative N3-I — closing the
  combined Master Wave 10 / Ideology Phase 2 review.
- **Exact humanlike-meat food evidence** added to existing thought pages.
- **Diary favorites and filters wired for real.** Favorites now persist and journal filters work; the
  seasonal window wash became an experimental default-off setting; Diary button icons were thickened
  with a native-style dark contour.

## 2026-07-21

- **Ideology Phases 0–2.** Landed the pure-contract foundation (Phase 0), guarded event-time belief
  context (Phase 1), and the first mutation/ownership infrastructure plus an exact PlayLog-interaction
  consumer (Phase 2), with an EN+RU belief prompt-text tuning pass.
- **Narrative Continuity N3-A/N3-O/N3-R.** Implemented visible-Anomaly (N3-A) and environmental-
  pressure (N3-O) continuity, extended N3-R, and added a queue-time prompt anti-repetition guard.
- **Associative memory wired into the diary.** Connected the associative-memory subsystem into the
  generation pipeline (steps W1–W3).
- **Diary UI rounds 3–5.** Replaced the tab button glyphs with CoreUI icons and a favorite star, added
  season-divider glyphs and a subtle seasonal background wash, and fixed four filter-panel bugs.
- **Anomaly A2.2 and Royalty Phase 5 loaded acceptance closed.**

## 2026-07-20

- **Anomaly Master Wave 7 (A1.0–A2.2).** Built the full Anomaly arc: pure-policy foundation (A1.0),
  catalog + persistence (A1.1), researcher-owned study capture (A1.2), containment-breach capture
  (A1.3), delivery hardening (A1.4), and visible creepjoiner state through surgical creepjoiner to
  ghoul transformation (A2.0–A2.2).
- **Diary filter/controls panel + Wave C1 reading treatments.** Added a toggleable filter panel (year
  selector, moved-in dev tools, chip tag filters) and reading-quality treatments (season dividers,
  player-visible copy).
- **Royalty Phase 8 review gaps closed.**

## 2026-07-19

- **Royalty Phases 4–8.** Closed Phase 4 correlation-lifecycle defects, then implemented succession
  (Phase 5), dramatic permits (Phase 6), Royal Ascent (Phase 7), and compatibility/release (Phase 8)
  across Master Waves 9 and 13.
- **Pawn memory subsystem (inert).** Implemented the memory layer from
  `design/MEMORY_SYSTEM_DESIGN.md` as a still-inert foundation, hardened and documented after review.
- **Modded-getter crash hardening.** Guarded pawn-fact and mood-thought reads against crashes from
  other mods' property getters.

## 2026-07-18

- **Royalty Phases 0–3.** Landed the contract-only foundation (Phase 0), page-silent foundation
  (Phase 1), persona lifecycle pages (Phase 2), and persona combat + death integration (Phase 3)
  under Master Wave 5.
- **Narrative N3-R core** provider/evidence dependency.
- **Biotech Phase 6 mechanitor lifecycle.** Added mechanitor control/combat truth (live acceptance
  pending), with Phase-6 review and test gaps closed.

## 2026-07-17

- **Odyssey Master Wave 4 (O1.0–O2 + N2-O).** Built the Odyssey arc: inert journey-policy foundation
  (O1.0–O1.1), guarded context/persistence (O1.2), state-only lifecycle hooks (O1.3), landing event +
  launch truth (O1.4), hardening (O1.5), the XML-first environmental slice (O2), and Narrative N2-O
  provider/reference dependency; live acceptance executed and hardened.
- **Biotech Phase 5 salient genes.** Integrated guarded salient-gene snapshots with silent saved
  baselines and reimplant-replay handling, plus the first N3-B salient-gene continuity lens.

## 2026-07-16

- **Biotech Phases 3–4.** Continued Master Wave 3 Biotech integration with ownership-lifecycle
  hardening and multi-axis (plan / lore / save-compat / correctness) review fixes.
- **Narrative N2-B Biotech provider.** Added the first family-continuity lens over Biotech evidence.
- **DLC compatibility matrix expanded.** Broadened automated all-DLC coverage, added Biotech B1 loaded
  checkpoints, and fixed canonical birth-prompt routing.

## 2026-07-15

- **DLC integration kickoff.** Authored the authoritative DLC master implementation order and shared
  cross-DLC Narrative Continuity Layer, then began the waves — Narrative N0/N1 (Waves 0–1), Anomaly
  A0.0–A0.2 (Wave 2), and Biotech Phases 0–2 (Wave 3).
- **Automated RimTest coverage.** Added the in-game RimTest Redux harness and built out the staged
  coverage plan (Phases 0–8: events, prompt/voice, save/load, API/UI/DLC), with Phase 5 transport
  scoped for later.

## 2026-07-14

- **Psychotype roll tuning moved to XML.** Exposed the ~19 numeric weights, bonuses, and thresholds
  behind the psychotype roll as XML tuning.
- **Powerful AI Integration persona bridge.** Added the separate reflection-only bridge and completed
  the integration-manifest dependency requirements.

## 2026-07-13

- **Release 0.5.0.**
- **Directional persona bridges.** Added directional Pawn Diary ↔ RimTalk persona synchronization with
  transparent, source-faithful LLM transforms across every bridge mod, an API v8 capture-health
  fallback, and psychotype-driven RimTalk chattiness.
- **Temperament-biased hidden humor cues** and a clearer per-pawn writing-style status panel.
- **Russian localization parity** and native-language cleanup across all eight localized surfaces.

## 2026-07-12

- **SpeakUp and Rimpsyche adapters.** Added gated conversation/thought adapters for both mods, with
  Rimpsyche persona sync matching the 1-2-3 workflow.
- **Six no-hard-dependency compatibility packs.** Source-verified voices for Alpha Memes, VIE Memes,
  Way Better Romance, Vanilla Traits Expanded, Hospitality, and Vanilla Events Expanded, on reusable
  routing (`MapWitness`, target-package event windows).
- **RimTalk bridge 0.3.1.** Persistent pair cooldowns, funnel save/load hardening, and a settings-
  window layout fix.
- **Rewritten About descriptions** (EN + RU) for all six integration mods.

## 2026-07-11

- **RimTalk bridge 0.3.0.** Replaced the "important kind or four lines" rule with a bounded editorial
  conversation funnel (pure scoring, ranked queue, small batches).
- **Integration API v4→v5.** Added additive API members, a per-pawn Regenerate button with loading
  status, and redesigned the 1-2-3 Personalities bridge around one setting plus an experimental
  small-model LLM transform.
- **Trait-driven psychotypes.** Traits now steer and dominate the psychotype roll, with three new
  trait-gated psychotypes and a retired `provideContextLine` setting.
- **VSIE group-gathering capture** (birthdays & funerals), a new "important" page when a colonist
  gains a personality trait, and error-reporter coverage extended to the integration submods.

## 2026-07-10

- **Load-order override arbitration.** External `Set*Override` collisions are now resolved by mod load
  order (later mod wins).
- **1-2-3 Personalities and VSIE adapters** shipped, plus RimTalk bridge context fixes, a day-summary
  opinion-read crash fix, and a Russian proofread pass.

## 2026-07-09

- **RimTalk bridge colony + shared-memory context.** Added two Level-1 outbound context channels:
  colony situation and per-pair shared memory.

## 2026-07-08

- **Psychotype voice layer.** Added a second voice layer — each pawn's psychotype (outlook) — editable
  from the Styles settings tab, save-compatible with no migration step, and natively re-authored in
  Russian.
- **Children keep a diary.** Lowered the first-person minimum age so children write, with voices
  crystallizing at adulthood.
- **Crash and sanitizer fixes.** Fixed three capture NREs (new-game mod conflict, stale-lover thought,
  vanilla-rejected memory) and stripped leaked format placeholders from generated text.

## 2026-07-07

- **Integration API v2–v3.** Added LLM-connection sharing and event filters (`ApiVersion` now 3).
- **Opt-out error reporter shipped.** New `Source/Diagnostics/` layer sends opt-out crash telemetry,
  wired live and hardened.
- **RimTalk bridge implemented** (steps 2–8, review-hardened), plus Diary-tab height-clamping fixes
  and an animal / unbuilt-pawn capture NRE fix.

## 2026-07-06

- **Release 0.3.1.**
- **Prompt context detail levels.** Added global `Full` / `Balanced` / `Compact` presets plus
  per-API-lane overrides.
- **Per-pawn custom writing-style prompt.** Each pawn's Diary tab now opens an editor to pick or write
  its writing style.
- **Prompt diversity pass (batches 1–3)** with Russian parity, writing-style catalog fixes, and
  removal of 22 dead duplicate Advanced tuning rows; RimTalk ambient-group groundwork.

## 2026-07-05

- **Public integration API v1.** Reset `PawnDiaryApi.ApiVersion` to 1 for the first public release,
  consolidating the internal v6–v22 pre-release ladder (pawn/title snapshots, forced recording, event
  filters, and more).
- **In-game API Explorer + docs** and an Advanced-tab list to disable automatic event types per player.

## 2026-07-04

- **Integration API v2–v5 (internal)** with a consolidated `design/EXTERNAL_API_CAPABILITIES.md`.
- **Minimal RimTalk bridge scaffold** shipped as a separate mod.
- **Advanced overrides UX** (opt-in raw-XML Def editing), LLM/network review fixes, and
  `publish.ps1 -AutoBump` tooling.

## 2026-07-03

- **Release 0.2.0.**
- **Event-coverage pass (Anomaly focus).** Inventoried every Anomaly incident and added body-part
  (Hediff-domain) diary events for artificial / anomalous part installs.
- **Render-time paragraph reflow** for long entries, plus observed-condition / event-window lifecycle
  hardening.

## 2026-07-02

- **Inbound mod-integration API.** Other mods can now push moments into the diary via a public inbound
  events API (internal numbering).
- **Per-lane reasoning-tag handling** and improved Metalhorror tone.
- **Packaging.** Dropped the bundled Harmony in favor of the `brrainz.harmony` dependency, with a
  pre-release performance pass and capture / UI robustness fixes.

## 2026-07-01

- **Pawn arc reflections.** Added passion/psylink/xenotype/title progression pages and rare yearly arc
  reflections; diary arcs now start with arrival and continue from the prior ending, and resurrected
  pawns keep writing.
- **Gray-flesh suspicion → metalhorror handoff** via observed conditions.
- **Tuning + prompt settings tabs** for XML overrides, a hardened generated-text sanitizer, and
  Russian reflection localization.

## 2026-06-30

- **Observed conditions.** Added the scan-backed system for lasting colony states (`MapDanger`, toxic
  fallout, and similar).
- **Mod versioning** (`modVersion` `0.1.0`), a Dev-mode event test panel, improved starting-arrival
  context, and separate retention / archive controls.

## 2026-06-29

- **Archive compaction.** Added the compact `diaryArchiveEntries` save store and
  `DiaryArchiveRepository` so old completed/stale/failed pages compact before hot refs drop.
- **Event ingestion bus.** All 17 catalog sources now submit uniform `DiarySignal` payloads through
  one dispatch/dedup/emit path.
- **Large-history performance and thought-classification tightening**, and the always-on gray-flesh
  event-window monitor disabled.

## 2026-06-28

- **Localization and prompt polish.** Shipped full Russian localization, tightened hediff prompt/style
  handling, added XML event windows, and fixed dev/export reliability.

## 2026-06-27

- **Event windows and history performance.** Added XML-driven event windows, refined Anomaly/hediff
  prompt routing, and moved history retention to a virtualized per-pawn cap.

## 2026-06-26

- **Docs and reliability hardening.** Cleaned up docs, improved generation/retention reliability, added
  prompt fallback compatibility, and isolated runtime/UI failures.

## 2026-06-25

- **Diary tab became default; major refactor.** Made the pawn inspect tab the default entry point and
  split generation, Harmony, and settings code into focused, cached, better-tested files.

## 2026-06-24

- **New event sources and platform hardening.** Added ability, psychic ritual, and Ideology ritual
  events; dropped native Ollama for OpenAI-compatible lanes; and hardened API routing and UI reliability.

## 2026-06-23

- **Settings compacted; pawn-selection entry point.** Reorganized settings, moved the Diary entry point
  to pawn selection, added a mod icon, and fixed prompt/parsing/reflection bugs.

## 2026-06-22

- **Quest and raid events added.** Added quest and raid event sources, split prompt structure into
  `DiaryEventPromptDef`, and nudged prose toward sensory immediacy.

## 2026-06-21

- **Formatting and romance events matured.** Matured formatting/staggered speech, completed the Event
  Catalog, added Romance relation events, and tightened core hardening.

## 2026-06-19 - 2026-06-20

- **Generated speech prototyped.** Direct-speech Social-log rows can be generated, but display remains
  unreliable, so the setting stays hidden and off.

## 2026-06-17 - 2026-06-18

- **Workshop prep and pipeline extraction.** Prepared Workshop release tooling, broadened LLM
  compatibility, extracted the pipeline into pure contracts, and expanded health/combat/social capture.

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
