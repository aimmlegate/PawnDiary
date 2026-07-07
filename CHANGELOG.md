# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state. The public integration
contract starts at `PawnDiaryApi.ApiVersion == 1`; older entries below preserve the internal
pre-release version ladder for project history.

## 2026-07-08

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
