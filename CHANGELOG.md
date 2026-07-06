# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state. The public integration
contract starts at `PawnDiaryApi.ApiVersion == 1`; older entries below preserve the internal
pre-release version ladder for project history.

## 2026-07-06

- **Settings tab cleanup.** Moved automatic event filters out of Advanced into a dedicated Events
  tab, moved the global `Full` / `Balanced` / `Compact` context-detail selector into its own Main
  tab section at the bottom of the page, and replaced the old Prompts drawer with a Main-tab cut/add
  comparison showing what each context preset sends and trims. The context-detail preset rows are
  now selectable directly, `Full` no longer shows a meaningless cut row, and the section includes a
  "never cut" line for core prompt facts.

- **Per-pawn custom writing-style prompt.** Players can now experiment with a pawn's voice directly
  from that pawn's Diary tab. A new always-visible "Writing style" row opens an editor dialog where
  the player can pick the base style, read its prompt, write a pawn-specific custom prompt, preview
  the effective prompt, and see an explanation when a temporary override (hediff or external
  integration) shadows their choice. The custom prompt is saved per pawn
  (`PawnDiaryRecord.customWritingStyleRule`, additive save key) and never touches base style Defs or
  the global `PersonaPresetStore`. Effective priority is now External API override > Hediff override
  > Pawn custom prompt > Base style, resolved by a new pure `WritingStyleResolutionPolicy` with full
  test coverage; generation still consumes only the final rule string. The integration API
  `GetWritingStyle` is unchanged (base saved style only). The dev-only base-style picker that used to
  live behind `showPersonaSettings` is superseded by the new player dialog.

- **Prompt context detail levels.** Added global `Full` / `Balanced` / `Compact` context presets
  plus per-API-lane overrides. `Full` keeps the old prompt shape; lower presets run a pure,
  deterministic field selector that always keeps core event/instruction facts, then spends a
  smaller context budget on the most relevant optional fields for the event domain. The Main tab now
  includes a context-detail section showing what each preset sends and cuts. Live failover lanes
  receive their own pre-rendered prompt variant, so a
  compact fallback model is not handed the richer primary lane prompt.

- **Example adapter preview art.** Added a developer-themed `About/Preview.png` for the example
  adapter/template Workshop payload, derived from the main Pawn Diary preview with API/template
  visual cues and an `Example API Adapter` subtitle.

- **Integration API validation pass.** Closed five issues found in a focused review of the public
  integration surface and the automatic event filters:
  - **Budget reservation leak on dispatch throw.** `SubmitEvent`, `SubmitEventWithHandle`,
    `SubmitPromptEntry`, and `SubmitDirectEntry` now release their external-API budget reservation
    from the `catch` block if `Dispatch` throws, so a failing dispatch path can no longer falsely
    consume per-source/global budget until the rolling window expires.
  - **Reflection toggle honored.** `DayReflectionSignal` and `ArcReflectionSignal` now respect the
    Advanced-tab "Reflection" filter row (`IsGroupEnabled`) like other native signals, instead of
    hardcoding `userEnabled=true`. The single `reflection` group governs day, quadrum, and arc
    reflection pages. The filter row tooltip now says so.
  - **External package gates enforced.** The External-domain classifier used by `SubmitEvent`
    validation now treats a group whose `disableWhenPackageIdsLoaded`/`enableWhenPackageIdsLoaded`
    gate says it should be inert as unclaimed, matching how `IsGroupEnabled` and the filter UI
    already treat such groups. A compatibility External group without its target mod no longer
    accepts its key.
  - **Disabled-by-default groups visible in the filter UI.** `EventFilterGroupsForSettings` no
    longer hides `defaultEnabled=false` groups, so players can opt INTO rows like `questAccepted`.
    A group with no override still inherits its XML default.
  - **Stale external-API docs corrected.** `SubmitEventOutcome.DroppedByPipeline`, the
    `INTEGRATIONS.md` submit table, and the example adapter `forceRecord` tooltip no longer claim
    external events can be dropped by "disabled group"/"user toggles" — external submissions
    intentionally bypass the player event-filter toggles.
  - Also documented the `NormalizeGroupEnabledOverrides` invariant (keeps non-default overrides;
    purges only unknown keys and redundant-equals-default entries). No behavior change there.

- **Example adapter publish payload.** `scripts/publish.ps1` now builds and packages
  `integrations/PawnDiary.ExampleAdapter/` alongside the main and Russian Workshop payloads. The
  example payload ships its source code plus `API_EXPLORER.md`, `INTEGRATIONS.md`, and
  `EXTERNAL_API.md`, rewrites its dependency/load-after metadata to the published core packageId,
  adds the core Workshop URL as its required-mod link, supports its own Workshop id file/override,
  and installs a Mods-folder junction by default.

## 2026-07-05

- **Advanced automatic event filters.** Added an Advanced-tab filter list for
  `DiaryInteractionGroupDef` automatic capture groups. Visible groups are enabled by default and can
  be disabled per player to stop Pawn Diary's own game listeners/scanners from auto-recording that
  event type. External mod API submissions intentionally ignore these filters, so adapter-owned
  events remain triggerable through `PawnDiaryApi` while still respecting the integration master
  switch, validation, budget, dedup, and pawn gates.

- **Public surface tightened around `PawnDiary.Integration`.** Internalized non-contract helper
  types and entry points across capture, ingestion, generation, pipeline, UI, settings helpers, and
  `DiaryGameComponent` so adapter authors compile against `PawnDiaryApi` and DTOs, not core
  internals. Public non-Integration types now remain only for RimWorld XML Defs, Scribe/settings
  data, debug-action discovery, and lifecycle reflection.

- **Public integration API v1 — first release numbering.** `PawnDiaryApi.ApiVersion` is now `1`
  for the first public release. Public v1 includes the full integration surface built during the
  pre-release ladder, including read snapshots, context providers, status listeners, prompt-entry
  previews, direct entries, submit outcomes, and forced external recording. Future additions will
  increment to v2+ instead of continuing the internal pre-release numbering.

- **External API master-switch status.** The main-mod *Allow external mod integrations* setting is
  the player-facing global enable/disable switch for the public API and remains enabled by default.
  `PawnDiaryApi.IsExternalApiEnabled` now exposes that switch separately from `IsReady`, and the
  example adapter checks it in the API Explorer badge, Readiness tree, and quick debug actions before
  running non-readiness calls.

- **Copyable example adapter API layer.** Refactored the example adapter so
  `Source/PawnDiaryExampleApi.cs` is the only source file that calls `PawnDiaryApi` directly. It
  wraps every explorer/quick-action interaction with XML doc comments describing each method's
  purpose, required args, and safe return value, while the explorer UI and debug actions now call
  through that facade. Updated the public integration docs to point adapter authors at that file.

- **External API request-context ceiling enforced.** `ExternalEventRequestText.JoinRequestContext`
  now applies the 64-field absolute ceiling to the whole saved request context, not just the
  protected prompt-field block. When prompt instructions/fragments/enchantment candidates fill the
  ceiling, ordinary `extraContext` overflow is dropped instead of growing saved `gameContext`.

- **Forced external recording.**
  Added `forceRecord` to `ExternalEventRequest` (and therefore `ExternalPromptEntryRequest`) plus
  `ExternalDirectEntryRequest`. A valid forced write skips the external prompt budget, group/user
  toggles, source dedup, and generic event-type dedup so adapter-owned triggers can guarantee a
  diary event is created. It still respects required fields, main-thread/game readiness, the master
  integration toggle, required External group XML for ordinary `SubmitEvent`, and base diary-owner
  eligibility. Direct entries also bypass the per-pawn generation-enabled/incapacitation checks when
  storing caller-authored prose. The example API Explorer exposes the flag on all write forms and
  defaults it on for quick smoke testing.

- **In-game API Explorer for the integration surface.** Rewrote
  `integrations/PawnDiary.ExampleAdapter/` from a one-event-per-day timer into a developer tool that
  drives **every** public `PawnDiaryApi` method from a three-pane Dev-mode window (method tree |
  request form | append-only result log with copy/clear). The method tree has a live search box
  (filters by method/summary/category and force-expands matches), per-category collapse instead of a
  single all-or-nothing toggle, and left-aligned rows with a full-label+summary hover tooltip; the
  form pane wraps a long summary instead of clipping it and shows which subject/partner a call will
  target, uses width-aware labeled fields, switches prose/context inputs to multiline editors, and
  adds a reset button for the shared form state; a second polish pass replaced rough form-height
  guesses with width-aware row measurement, moved method/field help into hover popovers on the title
  and label text, and added `integrations/PawnDiary.ExampleAdapter/API_EXPLORER.md` as a short
  operator guide; every request field now starts with a concrete quiet-moment sample value so submit,
  preview, direct-entry, style, read, and query forms need minimal typing; the explorer window is now
  a draggable, resizeable debug overlay with an explicit drag strip, closes the Debug Actions
  launcher after opening, and remains open while outside clicks pass through to normal game UI/camera
  controls; endpoint rows now show plain-language descriptions directly in the method tree; the result log de-duplicates
  the echoed method name, keeps short histories compact so details stay visible, and colours each line
  by outcome (green success / orange failure / grey neutral). It also ships four `[DebugAction]` quick
  actions under a new *Pawn Diary Example Adapter* category (open explorer, submit example event,
  preview example prompt, dump context bundle to log), and keeps its role as the canonical
  integration example: `ExampleAdapterGameComponent` still registers the two process-global hooks
  (`RegisterEntryStatusListener`, `RegisterPawnContextProvider`), and the explorer's Hooks tab shows
  their live activity. Two new External-domain groups (`exampleAdapterPromptIdea`,
  `exampleAdapterDirectNote`) join the existing `exampleAdapterQuietMoment` so all three submit paths
  (`SubmitEvent`, `SubmitPromptEntry`, `SubmitDirectEntry`) have canonical group XML. The pure
  text-parsing helpers (`ExplorerParsing.cs`) are unit-tested by a new
  `tests/ExampleAdapterParsingTests/` console project (41 assertions, no RimWorld refs). DTO
  formatting glue (`SnapshotFormatter.cs`) is intentionally impure — DTOs live in `PawnDiary.dll`,
  which references RimWorld, so a pure test project would drag the game in transitively. Updated
  `integrations/README.md`, `About/About.xml`, `EXTERNAL_API.md`, `INTEGRATIONS.md`, and
  `DOCUMENTATION.md §3.7` to point at the explorer. No core-mod code or contract change.

- **External API short guide.** Added `EXTERNAL_API.md` as a one-page overview of the
  `PawnDiary.Integration` surface for mod authors: 30-second quickstart, a capability table mapping
  common goals to the right public v1 call, the three submission paths side by side, and the hard
  rules (main-thread, never-throws, master toggle, `eventKey` is save-data, additive-only,
  prompt-ownership, token budget). It links out to the full `INTEGRATIONS.md` contract, the
  `integrations/PawnDiary.ExampleAdapter/` template, and `DOCUMENTATION.md §3.7`. The README now has
  a *For Other Mods* section pointing at all three. No code or contract change — documentation only.

- **Pre-public integration follow-up — read-path, encapsulation, determinism, and outcome surface.**
  A batch of review follow-ups landed across the integration API before the public v1 reset:
  - **SubmitEventOutcome.** Added
    `SubmitEvent(ExternalEventRequest, out SubmitEventOutcome)` and the `SubmitEventOutcome` enum
    (`Recorded`, `InvalidRequest`, `OffThread`, `Ineligible`, `DroppedBudget`, `DroppedByPipeline`)
    so adapters can distinguish the reasons a submission did not record instead of collapsing them
    into one boolean. The existing `bool SubmitEvent(request)` is unchanged (delegates to the new
    overload) — additive only.
  - **Pipeline surface encapsulation.** The pure `Source/Pipeline` external helpers
    (`ExternalEventRequestText`, `ExternalDirectEntryText`, `ExternalWritingStyleOverrideText`,
    `ExternalEntryAttribution`, `ExternalApiBudgetPolicy`, and the budget DTOs) are now `internal`,
    with `[InternalsVisibleTo("DiaryPipelineTests")]` in `AssemblyInfo.cs`. The public adapter
    contract is still only the `PawnDiary.Integration` namespace; the pure helpers were `public`
    only so the standalone test project could reach them, and the test project compiles the `.cs`
    files directly so it does not need the attribute. Shrinks the reflection-reachable surface.
  - **Absolute context-line ceiling.** `ExternalEventRequestText.MaxRequestContextLines` (64) caps
    the total `key=value` fields one external request can write into saved gameContext, so raising
    the XML-tuned enchantment-candidate cap can no longer grow saved state without bound. Mirrors the
    `MaxListeners`/`MaxProviders` parser-limit pattern.
  - **`GetEntryStats` archive scan cap.** Added `integrationStatsMaxArchiveScan` (default 500) to
    `DiaryTuningDef`. `GetEntryStats` now stops scanning a pawn's archive after that many rows
    (newest-first) instead of walking the full archive, so a stats read on a long-lived colonist
    stays main-thread cheap. Counts are approximate beyond the cap.
  - **`FindByEventAndRole` index.** `DiaryArchiveRepository` now keeps an `(eventId, povRole)` index
    alongside the existing pawn-id and archive-key indexes, turning the v17 entry-snapshot read's
    O(archive) newest-first scan into O(1). The original scan stays as a defensive fallback; the
    index is rebuilt in lockstep on load, add, and remove.
  - **`ContextProviderRegistry` iteration snapshot.** Provider invocation now snapshots the id order
    before the loop, so a provider callback that re-enters `Register` mid-iteration cannot mutate the
    live order list. Mirrors `ListenerRegistry.Notify`.
  - **Determinism: provider invocation RNG isolation.** `CollectPawnSummaryFacts` now snapshots and
    restores `UnityEngine.Random.state` around `PawnContextProviders.BuildContextLineList`, so a
    third-party provider that calls Verse `Rand` during a public pawn-summary read cannot perturb the
    game's deterministic RNG stream. Mirrors the prompt-preview path.
  - **Correctness: true min/max tick in context snapshot.** `DiaryContextSnapshot`'s
    newest/oldest tick+date are now computed by an explicit min/max scan rather than positional reads
    of `entries[0]`/`entries[Count-1]`. The entries list is built hot-first then archive (newest-first
    within each store, not globally tick-sorted), so a backdated archive row appended late was
    previously misreported as the oldest.
  - **Defense-in-depth: preview helper integration gate.** `DiaryGameComponent.PreviewExternalEventPrompt`
    now enforces `PawnDiaryMod.Settings.allowExternalIntegrations` directly, not only via the public
    `PreviewPrompt` wrapper, so a future internal caller (e.g. a debug action) cannot bypass the
    player's master integration switch.
  - **Note: budget `windowTicks` clamp.** The review backlog item "clamp negative `windowTicks`" was
    already fixed before this batch — `NonNegativeOrDefault` is applied at the
    `DiaryTuning.IntegrationPromptBudgetTuning` accessor, so 0/negative falls back to the default.
    Verified and explicitly closed.

- **Integration API — game-context injection & budget hardening (review follow-up).** Adapter input
  can no longer forge internal `gameContext` fields. `ExternalEventData.BuildGameContext` now flattens
  the `;` field separator (and line breaks) out of the adapter-controlled `eventKey`/`sourceId` and
  length-caps them, and a shared `ExternalEventRequestText.JoinAdapterExtraContext` rejects any
  `extraContext` line whose key is a reserved internal key (event-domain markers, death/arrival/
  reflection markers, classifier value keys, prompt `ContextField` keys, `external_prompt_*`) on both
  the event and direct-entry paths — closing a first-match bypass that could otherwise smuggle an
  uncapped `external_prompt_instruction`, spoof `source=`, or force a death/neutral page. The external
  API budget reservation is now refunded when the dispatcher drops the event (dedup/policy), and
  `SubmitEvent` dispatches directly so a deduped burst no longer burns an adapter's window. Added
  capture-policy and pipeline regression tests for both vectors.
- **Integration API v22 — wrapped prompt entries.** `PawnDiaryApi.ApiVersion` is now `22`. Added
  `ExternalPromptEntryRequest`, `SubmitPromptEntry(...)`, and
  `PreviewPrompt(ExternalPromptEntryRequest, string povRole = null)`, letting adapters supply a
  required `promptInstruction` while Pawn Diary keeps the normal persona/style, safety, context,
  response, budget, lifecycle, title, and persistence wrapper. The instruction is stored as protected
  `external_prompt_instruction` context, External group XML is optional for this path, and matching
  groups still contribute label/toggle/styling/prompt metadata.
- **Integration API v21 — context bundle snapshot.** `PawnDiaryApi.ApiVersion` is now `21`. Added
  `GetContextBundle(...)` overloads and `DiaryContextBundleSnapshot`, composing the existing base
  writing-style read, structured pawn summary, prompt-enchantment candidates, and recent
  generated-entry context into one prompt-free DTO. Query overloads filter only the recent context
  slice, and `includeImportantEventContext` controls only prompt-enchantment candidates.
- **Integration API v20 — cheap entry stats.** `PawnDiaryApi.ApiVersion` is now `20`. Added
  `GetEntryStats(Pawn)` and `GetEntryStats(Pawn, DiaryEntryTitleQuery)`, plus
  `DiaryEntryStatsSnapshot`, so adapters can count matching active/archived diary rows without
  materializing title/prose snapshots. Stats include normalized lifecycle buckets, title/prose
  presence, and newest/oldest tick/date metadata, using the same query filters and entry-key
  deduplication as the title/context reads.
- **Integration API v19 — richer title/context filters.** `PawnDiaryApi.ApiVersion` is now `19`.
  `DiaryEntryTitleQuery` now filters existing title and context snapshot reads by cleaned external
  `sourceId`, saved `eventKey` / source defName, paired `partnerPawnId`, importance, title presence,
  and generated-prose presence. Boolean-style filters use tri-state integers (`-1` any, `0` false,
  positive true), preserving older callers' default behavior.
- **Integration API v18 — external source attribution.** `PawnDiaryApi.ApiVersion` is now `18`.
  Externally submitted or direct-injected entries derive `externallyAuthored` and
  `externalSourceId` from the saved `external=...; source=...` context without new save fields.
  `DiaryEntryTitleSnapshot`, `DiaryEntryProseSnapshot`, `DiaryEntryStatusSnapshot`, and
  `DiaryEntrySnapshot` now expose that metadata, and the Diary tab shows the cleaned source id in
  the entry footer beside model/status provenance.
- **Integration API v17 — one-entry snapshot read.** `PawnDiaryApi.ApiVersion` is now `17`. Added
  `GetEntrySnapshot(DiaryEntryHandle)` and `GetEntrySnapshot(string eventId, string povRole)`, plus
  `DiaryEntrySnapshot`, so adapters can fetch one handled entry after callbacks/status polling. The
  snapshot returns prompt-free lifecycle/title metadata, domain/atmosphere labels, completed
  player-visible prose, and a capped summary; it never exposes prompts, raw provider responses,
  errors, fallback facts, or live game objects. The handle overload can resolve compact archive rows
  using the handle's pawn id.
- **Integration API v16 — external prompt fragments and prompt-enchantment inputs.**
  `PawnDiaryApi.ApiVersion` is now `16`. `ExternalEventRequest` can carry a sanitized
  `promptFragment`, capped `promptEnchantmentCandidates`, and `replacePromptEnchantments` mode for
  ordinary `SubmitEvent` / `SubmitEventWithHandle` calls. The fragment is stored as protected
  event context and exposed through first-person prompt templates while Pawn Diary keeps its normal
  persona, event, localization, safety, and budget framing. External prompt-enchantment candidates
  feed the existing planner with XML-tuned caps/weight and can either supplement or replace live
  candidates for that event.
- **Integration API MT-7 — token-spend guardrails.** Existing token-spending external API calls now
  reserve against a transient rolling budget before dispatch: ordinary `SubmitEvent` /
  `SubmitEventWithHandle` calls estimate main generation plus optional titles, while
  `SubmitDirectEntry` is guarded only when it can ask for missing title generation. Caps are
  XML-tunable in `DiaryTuningDef` (`integrationPromptBudget*`) and cover per-source/global request
  counts plus estimated max-token spend. This adds no public member, so `ApiVersion` stays `15`.
- **Integration API v15 — prompt preview.** `PawnDiaryApi.ApiVersion` is now `15`. Added
  `PreviewPrompt(ExternalEventRequest, string povRole = null)` and
  `DiaryPromptPreviewSnapshot`, letting adapters inspect the assembled External-event system/user
  prompt without saving a diary event, consuming dedup windows, queueing generation, spending tokens,
  or leaving RNG changed. The preview uses the same transient event facts and prompt planner as live
  generation, with a pairwise-recipient flag when the real later prompt may include initiator prose
  that no preview can know yet.
- **Integration API v14 — per-pawn generation controls.** `PawnDiaryApi.ApiVersion` is now `14`.
  Added `IsDiaryGenerationEnabled(Pawn)` and `SetDiaryGenerationEnabled(Pawn, bool enabled)` for the
  same saved per-pawn generation flag exposed by the Diary tab and dev event panel. The read is
  side-effect free; the setter uses base owner eligibility so disabled pawns can be re-enabled and
  requeue pending generation work.
- **Integration API v13 — external writing-style overrides.** `PawnDiaryApi.ApiVersion` is now `13`.
  Added `SetWritingStyleOverride(Pawn, string sourceId, string rule)` and
  `ResetWritingStyleOverride(Pawn, string sourceId)`. Overrides are saved per pawn as sanitized,
  source-owned free-form prompt rules, win over both the base writing style and temporary hediff
  style, and reset only for the owning source. `GetWritingStyle` remains the base-style read. While an
  external override is active, hediff prompt-enchantment suppression no longer acts as though the
  hediff style were speaking for those conditions.
- **Integration API v12 — writing-style catalog read.** `PawnDiaryApi.ApiVersion` is now `12`.
  Added `GetAvailableWritingStyles()`, returning the effective style catalog as
  `DiaryWritingStyleSnapshot` rows so adapters can build pickers without hardcoding style defNames.
  The read includes XML styles after settings-backed edits plus custom saved styles, is main-thread
  only, respects the master integration toggle, and never creates pawn diary state.
- **Integration API v11 — public eligibility preflight.** `PawnDiaryApi.ApiVersion` is now `11`.
  Added `IsDiaryEligible(Pawn)`, a side-effect-free main-thread read for adapters that want to
  preflight a pawn before submitting events or direct text. It returns false when integrations are
  disabled, no game is loaded, the pawn fails normal diary-owner eligibility, or that pawn's saved
  diary generation toggle is off in the Diary tab / dev event panel.
- **External API documentation reconciliation.** Collapsed the API docs to two live documents:
  `INTEGRATIONS.md` for the shipped v1-v22 adapter contract and
  `design/EXTERNAL_API_CAPABILITIES.md` for planned future public API capabilities. Removed the
  stale v4 provider brief and mod-compat API plan to avoid duplicate version ledgers.
- **Integration API v10 — entry lifecycle listeners.** `PawnDiaryApi.ApiVersion` is now `10`. Added
  `RegisterEntryStatusListener(string id, Action<DiaryEntryStatusSnapshot>)` and
  `UnregisterEntryStatusListener(string id)`. Listeners receive the same compact status DTO as
  `GetEntryStatus` on the main thread after a pawn-owned entry POV changes main or title lifecycle
  state: queued, completed, failed, skipped, prompt-only, title queued/completed/failed, or direct
  injected. Listener registration is main-thread-only, replacements keep order, listener exceptions
  are logged once and disable that listener for the session, and the master integration toggle
  suppresses delivery without clearing registrations.
- **Integration API v9 — direct text injection.** `PawnDiaryApi.ApiVersion` is now `9`. Added
  `SubmitDirectEntry(ExternalDirectEntryRequest)`, which saves caller-authored subject prose directly
  as completed generated text, optionally records a partner POV when `partnerText` is supplied,
  accepts caller titles or title-only generation via `generateTitleIfMissing`, and returns the same
  `DiaryEventSubmissionResult` handle shape as v8. Direct entries use the external dedup/context
  path, respect player integration and per-pawn generation gates, and can record without an External
  group while still using one for label/toggle/styling when present.
- **Integration API v8 — tracked entry handles and status snapshots.** `PawnDiaryApi.ApiVersion` is
  now `8`. Added `SubmitEventWithHandle(ExternalEventRequest)`, returning a
  `DiaryEventSubmissionResult` with subject/partner `DiaryEntryHandle` values when an external
  event actually creates diary entries, plus `GetEntryStatus(DiaryEntryHandle)` and
  `GetEntryStatus(string eventId, string povRole)`. Status snapshots expose compact generation
  state, title state, archived flags, display metadata, and a capped generated-prose summary without
  exposing prompts, raw provider responses, or live game objects. The older `SubmitEvent` remains
  unchanged for boolean fire-and-forget adapters.
- **Integration API v7 — recent generated-entry context snapshots.** `PawnDiaryApi.ApiVersion` is
  now `7`, with `GetContextSnapshot(Pawn, int)` and a filtered overload using
  `DiaryEntryTitleQuery`. The new `DiaryContextSnapshot` / `DiaryEntryProseSnapshot` DTOs expose
  newest-first completed generated pages as title/fallback metadata plus a one-sentence summary,
  capped by `DiaryTuningDef` (`integrationContextMaxEntries`, `integrationContextSummaryMaxChars`).
  The read walks the same hot/archive views as the Diary tab and title API, applies the same
  filters/dedup, and never exposes prompts, raw responses, fallback facts, or in-flight pages. The
  RimTalk bridge now logs these context summaries when enabled.
- **Agent-guidance maintenance hardening.** Aligned the codebase with the newly documented gotchas:
  `DiaryGameComponent.Instance` is now the live component accessor (`Current` remains only as a
  compile-time-blocked binary alias), `PawnDiaryApi` off-thread diagnostics tell adapters to queue
  and drain from a real main-thread hook, Ideology precept reads for body-mod policy route through
  `DlcContext`, hot Harmony hooks do their cheap type/null/game-state exits before entering
  `DiaryPatchSafety`, missing source-file headers were added, and the build-time Harmony reference
  now matches the active brrainz Harmony `2.4.1.0` runtime.
- **Integration API v6 — machinery-as-a-service reads (C-CTX-2 / C-CTX-3).** `PawnDiaryApi.ApiVersion`
  is now `6`, with two structured, side-effect-free reads that expose the prompt context Pawn Diary
  builds internally, so a chat/context mod can mirror our understanding of a pawn without us driving
  another model:
  - `GetPawnSummary(Pawn)` returns a `DiaryPawnSummarySnapshot` — sex, life stage, DLC identity
    (xenotype / royal title / faith), mood, health (broken out into a `DiaryHealthSummarySnapshot`
    sub-DTO: downed, pain shock, pain, bleeding, conditions), low capacities, top thoughts, and the
    API v4 external provider lines — as named DTO fields rather than the internal `key=value` blob,
    so the assembly can keep evolving prompt text without breaking the additive-only contract. Null
    for an ineligible pawn / no game / off-thread call / master toggle off; never creates a diary
    record.
  - `GetPromptEnchantments(Pawn, bool includeImportantEventContext = false)` returns the
    prompt-enchantment candidate **set** the planner would choose among right now
    (`List<DiaryPromptEnchantmentCandidateSnapshot>`), after hediff-style suppression, live
    event-window / observed-condition candidates, and normal-context weight multipliers, but before
    the final winner roll. Each snapshot mirrors the internal candidate (weight, source hediff
    defName, priority/condition text, impact cues, configured cues) with independent list copies.
    Empty for ineligible pawns, when prompt enchantments are disabled in settings, or no candidates
    match. The read preserves RimWorld `Rand` state, so polling it does not perturb later rolls.
  - To keep the prompt string bit-identical while sharing the same gathered facts, the composite
    `BuildHealthSummary` was factored into `CollectHealthFacts` (gather) + `FormatHealthSummary`
    (prompt string) + DTO fields; comma-bearing labels now stay as single structured list entries
    until the prompt string is formatted, so the prompt path and exported snapshot cannot drift
    through split/rejoin parsing. A new pure `TestPromptEnchantmentCandidateSnapshot` covers the
    internal `From` mapping, and `DiaryPipelineTests` cover the shared list formatter and planner
    candidate-preparation helper.
- **Integration API + adapter hardening (adversarial-review follow-ups).** No API surface changes;
  robustness fixes across the external-API work shipped on 2026-07-04:
  - `PawnDiaryApi` entry points no longer call RimWorld's main-thread-only `Log.*` when rejecting an
    off-main-thread call — that logging itself raced the in-game log window. Off-thread diagnostics
    now route through a thread-safe `UnityEngine.Debug` path with a once-per-key guard.
  - Pawn-context provider registration is capped at 32 distinct ids so a misbehaving adapter cannot
    grow the registry (or the per-summary walk over it) without bound; provider invocation early-outs
    off the main thread, and the final context join no longer double-cleans its lines.
  - Filtered `GetRecentEntryTitles` skips a store (hot/archived) entirely when the query excludes it
    instead of building and discarding every view.
  - Capability-refresh cancellation (`ApiConnectionController.CancelUiState`) now also clears the
    queued-rerun set, so a refresh cannot re-fire against a stale row index after a cancel.
  - The RimTalk bridge installs its Harmony patch inside a try/catch, so an absent/renamed RimTalk
    target degrades to "chat logging disabled" instead of throwing out of the mod constructor.
  - Docs/localization: `INTEGRATIONS.md` marks `sourceId` as recommended, not required (blank
    defaults to `unknown-source`); added the Russian Keyed strings for the `allowExternalIntegrations`
    toggle.

## 2026-07-04

- **Integration API v5 — filtered title reads.** `PawnDiaryApi.ApiVersion` is now `5`, with
  `GetRecentEntryTitles(Pawn, int, DiaryEntryTitleQuery)` so adapters can narrow recent title
  snapshots by semantic domain, atmosphere cue, POV role, date fragment, tick range, and
  active/archived state. The returned `DiaryEntryTitleSnapshot` stays unchanged and still excludes
  prose, prompts, raw responses, and live game objects. A new pure `DiaryEntryTitleFilter` and
  focused `DiaryPipelineTests` cases cover the matching behavior.
- **Integration API v4 — pawn-context providers.** `PawnDiaryApi.ApiVersion` is now `4`, with
  `RegisterPawnContextProvider(id, Func<Pawn,string>)` so adapter/personality mods can add one
  compact `key=value` line to the pawn summary used in prompts. Provider output shares the new pure
  `PromptContextLines` sanitation path with external `extraContext` (`OneLine`, `;`→`,`, line/count
  caps), providers are replacement-by-id and disabled/logged once on throw, and a new default-on
  `allowExternalIntegrations` setting gates external submissions, reads, and provider invocation.
  The ExampleAdapter now registers a vanilla-traits provider, and the public integration docs/roadmap
  mark v4 shipped.
- **Publish tooling: auto-bump + Russian Workshop id.** `scripts/publish.ps1` gained an `-AutoBump`
  switch that increments the patch component of the source `About.xml` `<modVersion>` (0.2.2 → 0.2.3)
  and writes it back to the repo, for a one-command "cut a new patch release" flow; it rejects
  non-`major.minor.patch` shapes (pre-release suffixes, `v` prefixes, missing version) and is
  mutually exclusive with `-Version`. Also added `About/PublishedFileId-Russian.txt` so the split
  Russian localization payload carries its Workshop id (`3753779334`) instead of shipping as a new
  item on every upload. Bumped `About.xml` to `0.2.2`.
- **Writing-style prompt contract pinned by tests.** Added two regression tests in
  `tests/DiaryPipelineTests` that guard the (already-correct) flow that injects a pawn's writing
  style into the LLM **system** prompt (it lives in the system prompt by design, never as a user
  prompt field): a pure unit test on `PromptAssembler.ComposeSystem` (the single load-bearing join)
  covering present/blank/null/opt-out/empty-base cases, and a shipped-XML contract test reading
  `1.6/Defs/DiaryPromptTemplateDefs.xml` that asserts every first-person template keeps
  `includePersona=true` and every neutral chronicle / title template opts out — so a new first-person
  shape with the wrong flag, or a future refactor of the system-prompt composition, fails in tests
  instead of silently dropping the style.
- **LLM/network adversarial-review fixes.** Addressed the findings from a multi-agent review of the
  LLM response-handling and network layer:
  - **Response cleanup no longer drops or corrupts valid entries.** The reasoning-scrub pipeline
    could silently empty an entry (which `SendOnce` then treats as a permanent "no content" failure)
    or truncate ordinary prose. Fixed: a reasoning block opened with one tag name and closed with a
    different known name (`<thinking>…</think>`) now recovers the answer via the mismatched closer
    instead of deleting everything after the opener; a trailing stray `</think>` after a finished
    entry drops only the tag, keeping the text; a reflection-looking line that *opens* the entry is
    treated as prose rather than emptying it; the over-broad "I should focus/avoid…" self-audit
    prefixes were removed. Over-stripping was narrowed: only `final:`/`final answer:`/`final response:`
    are stripped from the very start (so "Result:", "Entry:", "Diary:", "Answer:" openings survive),
    a code fence is only unwrapped when the whole response is one block (no interior fences), and an
    inline "Analysis: …" first sentence is no longer mistaken for a reasoning heading.
  - **`Retry-After` is honored.** A 429/503 with a `Retry-After` header now skips the fast local
    retries and cools the lane for the longer of the server's wait and the local exponential backoff
    (capped at one hour), instead of re-hitting a rate-limited endpoint.
  - **Settings capability refresh picks up the final edit.** The per-row `/models` refresh re-runs
    once when a row's URL/key changed while a previous refresh was in flight, so the capability cache
    reflects the player's last edit instead of a stale intermediate value.
  - **Secret redaction + request-body hardening.** The settings-window status/error paths now run
    through the same redaction as the logs (a `key=` echoed in an error body can no longer surface a
    live key in the UI); `RedactSecrets` masks a whole `Bearer` token (base64/base64url `+ / = ~`
    included) instead of an allow-listed run; and `LlmRequestJsonBuilder` coerces a non-finite
    temperature to a valid value so a corrupt setting can't emit invalid request JSON.
  New pure tests cover the parser data-loss/over-strip cases, the `Retry-After` cooldown policy, the
  broadened redaction, and the temperature guard. Deferred by design: a structured (localized)
  error-envelope refactor, and the OpenAI-Responses `reasoning.effort:"none"`/temperature compatibility
  questions (left as-is to avoid regressing the currently tested provider behavior).
- **External-API context-export surface added ("machinery as a service").** Extended the capability
  catalog with the outbound half of the context machinery: C-CTX-3 (export the prepared prompt
  enchantment candidate set before the single `RuleFor` winner pick), C-CTX-4 (full assembled-prompt
  preview via `BuildPromptPlan`, no submit/persist), C-CTX-5 (a context bundle — summary + surroundings + continuity + enchantments +
  style + recent entries — the core-side export a RimTalk bridge feeds via
  `ContextHookRegistry.RegisterPawnVariable`), and C-CTX-6 (supply/override enchantment candidates
  inbound). Reframed §2.4 as two directions (feed/override vs. read-as-a-service) and added design
  note §3.8: export structured DTOs not raw strings, prepared inputs before rolled winners,
  side-effect-free main-thread reads, and RimTalk driven from an `integrations/` bridge so core stays
  RimTalk-free. Slotted into the §4 build queue near the reads. No runtime change; `ApiVersion` stays 3.
- **External-API consent + gating + delivery decided.** Closed the last cross-cutting forks in the
  capability catalog: consent is a **single master `allowExternalIntegrations` toggle** (default on —
  installing an integration mod is the consent; the trust ladder is intentionally flat, and this also
  settles the API v4 provider toggle); injection gating uses an **optional claiming group** (group's
  policy/toggle apply when present, else the master switch + eligibility, while IN-1 keeps its
  required-group rule); and capabilities ship **one at a time as base-mod features**, each with its
  own `ApiVersion` bump in dependency order rather than bundled version waves. §4 reframed as a build
  queue, MT-6/§3.3/§3.5 and the now-retired v4 provider brief updated, open questions 1 and 5 closed.
- **External-API capability catalog added; API-surface planning restructured.** New
  `design/EXTERNAL_API_CAPABILITIES.md` is now the authority on the *shape* of the public integration
  surface: it folds in a scoping pass' worth of requested capabilities (inbound entry-creation modes —
  full prompt, partial prompt, direct text ± title; last-N reads with type/atmosphere/tone/date
  filters; writing-style get/set/reset) plus proposed complements (async completion signal, entry
  handles, by-id/count reads, temporary style override stack, per-capability consent, eligibility
  probe, regenerate/retract, UI attribution). Each capability carries a status (shipped v1–v3 /
  requested / proposed / designed), the internal hook it maps to, and its open decision; the doc also
  consolidates the six cross-cutting decisions (async delivery, prompt-bypass vs. consistency,
  injection gating, style override-stack vs. base mutation, consent granularity, versioning) and a
  strawman v4→v8 sequencing. The then-current mod-compat plan was retitled to own the *target-mod
  survey* only and pointed its ledger at the catalog; the v4 providers brief was reframed as the
  deep-dive for catalog capability C-CTX-1; `design/README.md` reindexed. No runtime change;
  `ApiVersion` stays 3.
- **API v4 design brief drafted (design-doc-before-code).** Added
  a v4 provider design brief for the planned
  `RegisterPawnContextProvider(id, Func<Pawn,string>)` member: public
  surface + feature-detection, `extraContext`-identical sanitation and once-disable failure
  isolation, the impure-snapshot purity boundary, pawn-summary placement next to the DLC-identity
  lines, and the RimPsyche consumer snippet. It reconciles the plan's stale "settings toggle mirrors
  per-group toggles" note with today's XML-only group model (`defaultEnabled` + package gates). One
  design decision is left open on purpose — the player-toggle model, which now carries an expanded
  A–G option set (master bool / per-provider dict / provider Def / no-toggle / fold-in /
  consumer-owned / hybrid) to be reconsidered before the v4 code PR; the choice stays additive so it
  blocks only the toggle slice, not the rest of the design. No runtime change; `ApiVersion` stays 3.
  Indexed in `design/README.md` and the then-current PR-4 checklist.
- **LLM response compatibility tightened.** Hardened the pure response parser for common
  OpenAI-compatible/local-model quirks: whole-response Markdown fences are unwrapped, leading
  final-answer labels are stripped after reasoning cleanup, typed content parts can read an
  `output_text` field, and focused parser tests cover the new behavior.
- **Docs reorg + integration ideas reconciled.** Moved the scattered design/planning markdown
  (mod-compat, event-coverage, and body-part plans) into a new `design/`
  folder with a `design/README.md` index, leaving only living top-level docs at the root
  (README, DOCUMENTATION, EVENT_PROMPT_MAP, INTEGRATIONS, CHANGELOG, and the agent files). All
  external-mod integration ideas were reconciled into a single design document: it gained an API
  version ledger, folded in the former
  `INTEGRATIONS.md` roadmap (now a pointer), recorded the shipped v3 writing-style publish, and
  resolved the version-numbering conflict the writing-style publish created (pawn-context providers
  move to v4, the outbound entry-prose snapshot to a later read slot). Fixed two dead links in `DOCUMENTATION.md`
  (`ARCHIVE_COMPACTION_DESIGN.md`, `PAWN_ARC_REFLECTION_IMPLEMENTATION.md` no longer exist).
- **Integration API v3 — writing style publish.** `PawnDiaryApi.GetWritingStyle(Pawn)` now exposes a
  pawn's base saved writing style as a small read-only `DiaryWritingStyleSnapshot`
  (`styleDefName`, `label`, `rule`), so a chat/context mod can align its own voice with how the pawn
  writes. Publish-only: Pawn Diary never reads or drives another mod's persona. The read is
  side-effect free (never creates a diary record), excludes temporary hediff style overrides, and
  carries no internal theme tags; it is main-thread only and returns `null` instead of throwing.
  `ApiVersion` bumped 2 → 3. The RimTalk bridge now logs the resolved style for the speaker and
  target as its proof step, without feeding it back into RimTalk.
- **RimTalk persona-alignment note saved.** The then-current mod-compat plan recorded the creative rule that
  Pawn Diary writing styles and RimTalk personas should share pawn identity and memory, while staying
  separate private-writing and spoken-behavior surfaces.
- **Dev event panel buttons fixed.** `Pawn Diary > Event test panel...` no longer pauses the game,
  removes the unused real-event trigger button surface, and fixes the remaining custom text buttons
  by using RimWorld's normal left-click handling.
- **Humor cue default raised.** Hidden humor cues now default to a 20% chance for eligible
  first-person entries, with XML and defensive fallback defaults aligned.
- **Advanced reset/copy UX.** Advanced XML override areas now reset changed values by current tab
  or drawer instead of only offering a global reset, and can copy a plain text changed-settings
  summary for review without introducing a stable import/export format. Raw prompt-template and
  prompt-policy overrides moved out of the normal prompt settings path into a clearly marked
  experimental drawer.
- **Advanced settings discovery tightened.** The Advanced tab now stays visible, while raw XML
  override editing is gated behind an experimental opt-in, with All/Changed/Raw text filters and
  collapsed drawer rows that show the
  effective source plus a short active-value preview before opening the raw editor.
- **Advanced raw settings editor feedback.** Compact raw override boxes now validate while typing,
  show a colored parse preview, and leave malformed edits unapplied until the same checked parser
  accepts them. Covered schemas are weather mention chances, ritual quality bands, prompt template
  fields, prompt-enchantment severity tiers, thought progression rules, and progression skill
  milestones.
- **Advanced override drawers.** Empty inherited multi-line text/list overrides now collapse to a
  compact "using XML/default" row with an explicit edit button, keeping optional override escape
  hatches out of the main path. Clearing a string override back to blank now restores the XML snapshot
  in the live Def instead of leaving an unsaved blank value for the current session.
- **Advanced override warning.** The Advanced page now marks XML Def overrides as experimental so the
  surface reads as a testing escape hatch rather than a normal tuning workflow.

- **Minimal RimTalk bridge scaffold.** Added `integrations/PawnDiary.RimTalkBridge/` as a separate
  mod named `PawnDiary: RimTalk bridge`, with its own metadata, build project, English settings
  strings, and one Enable/Disable setting. When enabled, it Harmony-patches RimTalk's accepted-chat
  boundary and logs displayed chat facts plus recent Pawn Diary title snapshots for the speaker and
  target. The bridge is diagnostic only for now: it does not submit diary entries or inject prompt
  memory yet.
- **Integration API v2 title snapshots.** `PawnDiaryApi.ApiVersion` is now `2`, with
  `GetRecentEntryTitles(Pawn, int)` returning a capped, read-only list of recent
  `DiaryEntryTitleSnapshot` rows. The DTO exposes only title metadata (`tick`, `date`, `eventId`,
  `povRole`, `title`, `groupLabel`, `archived`) and omits prompts, raw responses, generated prose,
  and live objects.

## 2026-07-03

- **Release 0.2.0 prepared.** Source metadata now declares `modVersion` `0.2.0`; publish prep uses
  the split Russian localization payload and force-refreshes local RimWorld Mods junctions.
- **Release localization audit fixes.** EN/RU Keyed and DefInjected coverage now align, and
  arrival/dev mock prompt labels use localized strings instead of hardcoded C#.
- **Release hardening for reasoning, external events, reflow, and startup.** Explicit reasoning
  `none` now stays off even when a provider advertises efforts without listing `none`; external API
  pages keep the External display/prompt domain even if adapter `extraContext` includes native-looking
  keys; paragraph reflow clamps invalid target/max tuning and safely returns whole lines for invalid
  options; startup Harmony patch discovery now catches partial assembly type-load failures and patches
  available types instead of aborting the static constructor.
- **More diary event color cues.** Body-part diary entries now get explicit accents:
  `bodyPartAnomalous`, `bodyPartArtificial`, and `bodyPartLost`; anomalous body changes keep the
  unsettled page atmosphere while no longer sharing the generic `extremeDark` cue. XML-policy tests
  now pin the body-part, psylink/psycast, and royal title/ritual cue wiring.
- **Anomaly/body-part prompt atmosphere pass.** EN/RU prompt prose for Anomaly interactions, tales,
  hediffs, entity raids, psychic rituals, anomalous body changes, artificial parts, lost parts, and
  the unnatural corpse context now leans harder on sensory dread and body horror while still refusing
  hidden-mechanic explanations.
- **Translation report cleanup.** Removed stale EN/RU DefInjected labels and prompt-map references
  for the removed `HediffPersonaOverride_MemoryDecay` def; memory decay remains covered by the
  `DiaryEnchant_MemoryDecay` prompt enchantment only.
- **Event windows can early-cancel when their threat dissolves.** Added an XML-declarable still-present
  probe (`stillPresentThingDefNames` / `stillPresentFactionDefNames`) to `DiaryEventWindowDef`; the
  timeout scan now closes a persistent window early when neither matcher is satisfied on its map, so a
  `keepActive` dread window no longer colors prompts for its full `timeoutTicks` after the threat is
  gone. `MechClusterLanded` is the first consumer: a `Mechanoid`-faction probe ends the up-to-3-day
  window as soon as no mechanoids remain. Closes are silent (no end page); ConfigErrors requires
  `keepActive=true` for a probe.
- **Defense-in-depth caps for three Anomaly observed conditions.** `HarbingerTreePresence`,
  `PitGatePresence`, and `FleshmassHeartPresence` now carry `maxActiveTicks`/`restartCooldownTicks`
  (mirroring `AmbrosiaSprouted`/`AnomalyGrayFleshEvidence`) so their prompt bias cannot run unbounded
  if a resolved threat ever leaves a same-defName remnant — the corpse-bug shape.
- **Dev "Force-close active event window" action.** New Pawn Diary debug action lists currently active
  event windows in a picker and force-removes the selected one (brute remove, no end page) — the escape
  hatch for a window stuck open before its timeout. EN/RU strings added.
- **Mod-compatibility target survey & patch plan (docs only).** A new root mod-compat planning
  document continued the API-v1 integration milestone by picking concrete target
  mods. Surveys popular social-interaction mods (2026-07) and tiers them by mechanism — Tier A
  XML-only compat groups shipped in core behind `enableWhenPackageIdsLoaded` (Vanilla Social
  Interactions Expanded, Hospitality (Continued), SpeakUp, Way Better Romance, Positive
  Connections, Romance On The Rim), Tier B personality mods motivating API v2 context providers
  (RimPsyche, Psychology), Tier C LLM chat mods motivating API v3 outbound context (RimTalk) —
  with per-PR todos, volume/absence audit guardrails, and a verification checklist requiring
  in-game packageId/defName confirmation. Follow-up research pass read each open-source target's
  repo directly and recorded verified packageIds and defNames per mod (VSIE `VSIE_` prefix,
  Positive Connections `DIL_` prefix, Hospitality/Way Better Romance unprefixed exact names,
  RimTalk's vanilla `RimTalkInteraction` log entry) plus concrete per-tier requirements: §4.1
  per-mod Tier A group specs, §4.2 the v2 provider contract designed against RimPsyche's
  documented `PsycheDataUtil`/`InteractionHook` surface, §4.3 the v3 snapshot DTO and a RimTalk
  bridge via its `ContextHookRegistry` (no upstream changes needed). `INTEGRATIONS.md` roadmap
  now points at the plan. No behavior changed.
- **Body-part diary events implemented.** Added immediate Hediff-domain entries for artificial part
  installs, anomalous organic body changes, and living-pawn natural body-part losses. Body changes now
  classify through synthetic `addedpart`/`organicpart`/`missingpart` keys, persist tier/attitude/cause
  prompt tokens, use XML-owned tier/stance tuning, and include EN/RU fallback/cue/localized group
  strings. Pure policy and saved-event classifier tests cover key format, tier resolution, attitude
  precedence, cue keys, and `gameContext` ordering.
- **Generic short-window event-type dedup.** Added an XML-tuned `genericEventTypeDedupTicks` safety
  key for sources without detailed dedup keys, plus a shared death-description key so Tale deaths and
  the Pawn.Kill fallback cannot emit duplicate final death pages in the same moment.
- **Unnatural corpse now haunts only the imitated pawn.** `UnnaturalCorpsePresence` was map-scoped
  and colored every colonist's diary when any unnatural corpse was present. It is now a Pawn-scoped
  `PawnUnnaturalCorpse` observer that asks the Anomaly DLC's own tracker
  (`GameComponent_Anomaly.PawnHasUnnaturalCorpse`, via the guarded `DlcContext` accessor) which
  colonist the corpse imitates, so the dread lands only on that pawn. End-on-disappearance now works
  like `MetalhorrorEmergence`: when the corpse is destroyed or dissolves, vanilla clears the tracker
  link, the observation stops, and the pure policy ends the state after the def's `endDebounceTicks`.
  New DLC-gated observer type `PawnUnnaturalCorpse` (no matchers; DLC-safe no-op without Anomaly);
  the corpse's keyed prompt strings (EN + RU) reworded to the haunted pawn's personal point of view.
- **Settings connection row alignment and localization.** Main-tab API rows now share the same label
  column for reasoning controls and the same action-button columns for model/API-key rows, removing
  the clipped "Reasoning" label and staggered right-side buttons. Russian settings localization now
  includes the missing reasoning capability/tag strings and shorter compact labels.
- **New color cues for psychic and royal events.** `psychic` (bright violet) for psylink gains and
  psycasts, `royalty` (gold) for title gains and royal rituals — previously psylink shared the
  Anomaly dread red (`extremeDark`) and titles shared the generic warm-white cue. Anomaly dread
  groups stay on `extremeDark`. Palette in `DiaryUiStyleDef.xml` with C# fallbacks; `colorCue` is
  saved per-event, so existing entries keep their old color.
- **Dev event panel: hid the non-functional Events section.** The event trigger buttons in the
  debug `Event test panel` (Def-backed trigger rows plus the arrival/death/work-scan/thought-
  progression/day-reflection buttons) do not currently work, so the Events section is hidden:
  its rail button is no longer drawn, saved `events` section selections normalize to Diary, and
  Diary is now the panel's default section. `DrawRealEventsSection` and the `Trigger*` helpers
  remain in `Dialog_PawnDiaryEventTestPanel` unchanged so the section can be re-enabled once the
  triggers are fixed. Diary and Fixtures sections are unaffected.
- **Event-coverage review fixes (XML + docs only).** Corrected two silently dead `ThingPresent`
  observers whose guessed defNames matched nothing (the observer resolves exact `matchDefNames`
  only): `ObeliskPresence` now matches the real Anomaly ThingDefs `WarpedObelisk_Abductor` /
  `WarpedObelisk_Duplicator` / `WarpedObelisk_Mutator` (the in-game obelisk labels do not match
  their defNames), and `UnnaturalCorpsePresence` now matches the generated `UnnaturalCorpse_Human`;
  `HarbingerTreePresence` dropped the dead `HarbingerTree` spelling (verified: `Plant_TreeHarbinger`).
  Added companion Interaction-domain display groups for the three new page-recording defs
  (`eventWindowMechCluster`, `observedPitGate`, `observedFleshmassHeart`, orders 142–144, EN+RU
  DefInjected) so their saved pages classify to a proper label/importance in the Diary tab instead
  of the "A quiet day" catch-all, matching the existing `eventWindow*` precedent. Fixed the
  `HediffPersonaOverride_Drunk` comment's `AlcoholHigh` stage thresholds (drunk starts at 0.4).
  Documented the event-coverage defs in `DOCUMENTATION.md` §5/§5.1, which the original pass missed.
  Still to verify in dev mode: whether Anomaly entity assaults route through the raid hook
  (`raidAnomalyEntities` depends on it) and the Odyssey `Flooding`/`VolcanicAsh`/vacuum defNames.
- **Event-coverage pass: XML-only groups, enchantments, personas, observed conditions, and tone
  windows (no C# changes).** Implements Tiers 1–2 of `EVENT_COVERAGE_PLAN.md` with Anomaly as the
  main focus. Retone groups (page volume unchanged): `raidAnomalyEntities` gives Anomaly entity
  assaults a horror register instead of the human-raid tone; `moodeventWeatherHardship` /
  `moodeventStormDanger` replace the generic catch-all wording for ColdSnap/HeatWave/
  VolcanicWinter/Flashstorm (+ Odyssey VolcanicAsh/Flooding, Anomaly BloodRain);
  `mentalbreakViolent`/`Escape`/`Indulgent` split the mental-break catch-all into three registers.
  New prompt enchantments: Malnutrition, Heatstroke/Hypothermia, Anesthetic, PsychicShock,
  Carcinoma, mechanites, WakeUpHigh, CryptosleepSickness, low-chance AgingBody, Biotech
  Deathrest/LungRot, Anomaly BloodRage, Odyssey VacuumExposure, plus `SleepingSickness` added to
  the FeverishBody matchers. New writing-style overrides: drunk rambling (`AlcoholHigh` at
  severity ≥ 0.4) and fading memory (Dementia/Alzheimers), backed by two new personas. New
  observed conditions (weighted prompt tone, no pages unless noted): ColdSnap/HeatWave/
  VolcanicWinter; Anomaly BloodRain/DeathPall/UnnaturalDarkness, obelisks, harbinger trees,
  nociosphere, unnatural corpse, and pit gate + fleshmass heart (these two record a start page per
  map colonist); weighted-random light flavor for thrumbo visits, alphabeavers, crop blight, and
  ambrosia groves with active-time caps and restart cooldowns. New event windows: `MechClusterLanded`
  (start page + three-day decaying dread), `ShortCircuitAftermath` and `SelfTameJoined` (tone-only,
  never pages). All matchers are plain strings (DLC-safe); every new group/def is settings-toggleable.
  English Def text plus natively written (not literally translated) Russian Keyed/DefInjected
  strings; `EVENT_PROMPT_MAP.md` tables refreshed (including correcting the stale
  MetalhorrorEmergence row to the shipped ThingPresent observer).
- **Event-coverage gap analysis & XML-only extension plan (docs only).** New root document
  `EVENT_COVERAGE_PLAN.md`: inventories every RimWorld moment the mod reacts to today, maps the
  base-game and DLC (Royalty/Ideology/Biotech/Anomaly/Odyssey) events we skip or only catch via
  generic catch-alls, and proposes a tiered, XML-only set of additions (retoned interaction
  groups, missing prompt enchantments, two persona overrides, observed conditions, and a few
  one-shot event windows) with volume guardrails. No behavior changed.
- **Render-time paragraph reflow for diary prose.** Long single-line entries are now split into
  readable paragraphs at render time. Because prompts only ever ask for sentence counts and never
  for explicit paragraph breaks, a multi-sentence entry previously wrapped as one dense block. The
  default (non-Fractured / -Unsettled / -Memorial) atmosphere now runs each prose line through a
  pure reflow helper (`DiaryParagraphReflow.ReflowLine`) and breaks it, in priority order, at
  sentence ends, RimWorld year mentions (`55xx`), semicolons, and em-dashes, with a hard length
  cap that falls back to a space boundary (words are never split mid-token). Saved `GeneratedText`
  is never mutated; both the measure and draw passes use the same helper so wrapped heights stay in
  sync. `[[speech]]` blocks and the three special atmospheres are unchanged. Tuning lives in
  `DiaryUiStyleDef.xml`: `paragraphReflowEnabled` (master toggle), `paragraphReflowTargetChars`/
  `MaxChars`, the four `…SplitOn…` cue toggles, and `paragraphReflowMinBreakSpacing`. New pure test
  project `tests/DiaryParagraphReflowTests` covers short-line pass-through, each cue, the hard
  break, stub merging, the disable toggle, and non-year-number safety. (DOCUMENTATION §7.)

- **Review fixes for API settings, arc cadence, and localization.** Background reasoning-capability
  refreshes now lock their per-row in-flight guard so UI cancellation cannot race async continuations.
  Arc reflections now honor the Advanced `arcReflectionMaxEntriesPerYear` cap across the full 1-10 UI
  range (default 2 keeps the shipped annual-plus-major cadence), with pure regression tests for one-entry
  and higher caps. Dynamic Advanced prompt-policy group prefixes moved to Keyed EN/RU strings, and
  `LlmClient.TestConnection` no longer contains an English prompt fallback; the settings UI passes the
  already-localized prompt from the main thread. (DOCUMENTATION §4/§8/§11.)

- **Title fallback guard tightened.** Title follow-up responses now reject one-line answer labels,
  instruction echoes, reasoning-style lines, terminal periods, and generated titles outside the
  3-8 word contract. Invalid title output uses the existing fallback title built from the finished
  diary entry instead of being saved as a page header. (DOCUMENTATION §6.)

- **Reasoning capability auto-refreshes across the settings window.** A row's reasoning capability
  (and model list) now fetches itself on four triggers, so a player almost never has to click
  **Fetch models** manually: when the settings window opens (one-shot, for any row whose capability
  is not yet cached), when a row's URL/key/auth changes (background refresh, once per change), when
  **Test connection** runs (in parallel with the test), and on the manual **Fetch** click (existing).
  Auto-pick-first-if-blank still applies to the full Fetch path. To keep the picker UX stable under
  multiple triggers, a new lightweight **capability-only refresh** (`ApiConnectionController.RefreshCapability`)
  updates just the thread-safe `ModelCapabilityCache` without touching the single-flight picker
  state, so several rows can refresh concurrently. The previous "auto-fetch at Pick" code was
  removed — it was redundant for OpenRouter (Fetch already cached every model) and a wasteful no-op
  loop for providers that return no capability (OpenAI-direct, GGUF, LM Studio). Providers that do
  not advertise capability still degrade gracefully (unknown → effort passes through unclamped).
  (DOCUMENTATION §8.)

## 2026-07-02

- **Per-lane reasoning-tag picker and capability-aware reasoning effort.** A "Reasoning tag"
  dropdown (default Auto) pins the exact wrapper tag a model emits, stripped alongside the built-in
  broad guess-list. Endpoints advertising `reasoning.supported_efforts`/`default_enabled` now
  constrain the effort dropdown and clamp outgoing `reasoning_effort` (fixes 400s on non-reasoning
  models like Gemma); providers without capability degrade gracefully. New `ModelReasoningCapability`,
  `ModelCapabilityCache`, `ApiEndpointPolicy.NormalizeReasoningTag`, and a tag-parameterized stripper;
  pure tests added.
- **Reasoning config now mostly auto.** The built-in Auto reasoning-tag stripper now covers
  `think`/`thinking`/`reasoning`/`analysis`/`thought`/`reflection`/`scratchpad` (was four tags);
  reasoning capability auto-fetches at model pick when not yet cached.
- **Public mod-integration API v1 (inbound events).** Other mods push moments via the stable
  `PawnDiary.Integration.PawnDiaryApi.SubmitEvent(ExternalEventRequest)` facade — validated,
  crash-isolated, main-thread-guarded, routed through the normal bus as a new `External` source.
  Prompt policy stays in XML (an External-domain group claims the `eventKey`); new
  `externalEventDedupTicks` knob and `enableWhenPackageIdsLoaded` group flag. Debug Action test entry,
  EN/RU strings, `INTEGRATIONS.md` contract, and a buildable adapter template in
  `integrations/PawnDiary.ExampleAdapter/` with a deploy script.
- **Hidden-infection "insurance" tone for post-kill metalhorror.** New `MetalhorrorInfection`
  observed condition (backed by a new `MapHiddenHediff` observer) keeps a softer dread-tone alive
  while any home-map colonist is infected — tone-only, never names the hediff or host. DLC-safe
  (plain defName string).
- **Revealed-metalhorror override lasts the whole threat.** `MetalhorrorEmergence` now ends on the
  live `Metalhorror` ThingDef leaving `ListerThings` (death → `Corpse_Entity`), removing the blunt
  2-day `maxActiveTicks` cap that cut the override off mid-rampage.
- **Per-row Test connection.** Clicking Test on row B while row A runs starts B immediately, each
  with its own status/generation counter and a thread-safe result queue. Fetch-models stays
  single-flight global.
- **Non-reasoning models stopped failing Chat Completions on Reasoning → None.** The request body
  omits `reasoning_effort` when saved effort is `none` (matching `default`), fixing 400 "Thinking
  budget not supported" errors on models like Gemma. Responses mode unchanged.
- **Pre-release performance pass.** Diary tab name-highlight refresh no longer re-measures cards
  unless highlights really changed (new pure `DiaryNameHighlighter.SameHighlights`); the hottest
  Harmony hooks (`Thing.SpawnSetup`, `AddHediff`, `TryGainMemory`) use a closure-free state-passing
  `DiaryPatchSafety.Run` overload; batch scanners allocate keys lazily.
- **No longer bundles Harmony.** Ships only `PawnDiary.dll`; runtime Harmony comes from the
  `brrainz.harmony` dependency. Removed shipped `0Harmony.dll`, publish script no longer copies it,
  and the verify hook fails on a bundled copy. `Source/Libs/0Harmony.dll` stays as a compile-time
  reference only.
- **Project builds from any checkout location.** RimWorld/Unity assembly hint paths resolve via a
  configurable `RimWorldManaged` MSBuild property (`/p:RimWorldManaged=...` or `RIMWORLD_MANAGED`
  env var), replacing the hard-coded relative path.
- **Diary export moved to Debug Actions.** `Pawn Diary > Export all diary pages...` writes hot
  pages, compact archived pages, archive rows without a live record, and backing event records; the
  old settings export button was removed.
- **Single-item interaction batches become standalone entries.** A `PairEvent` batch that collects
  only one moment now emits a normal standalone entry (original defName/label, no batch marker);
  multi-item batches unchanged. Pure test pins `events=1` template selection.
- **Lasting prompt overrides gained a stale-state guard.** Observed-condition prompt bias stops once
  a condition is missing past its end debounce, even if its row is retained for retry; event-window
  rows validate they have a positive timeout or usable end signal. Pure tests cover the boundary.
- **Inspect-tab Diary button no longer shows unread markers.** The bottom command overlay still
  signals unread/writing status; removed obsolete marker style knobs.
- **Prompt-enchantment editor exposes `chance`, not the `frequency` alias.** Legacy `frequency` XML
  is accepted but pruned from saved overrides.
- **Russian localization review.** Filled missing prompt-tuning/Prompt Studio Keyed strings, synced
  DefInjected, fixed a persona speech marker, and replaced code-flavored UI phrases with natural
  RimWorld terminology.
- **Prompt-policy override boxes no longer mirror XML translation defaults.** Key-backed boxes
  (`*Text`, `conditionLabel`, cue/batch/hediff text) stay blank when XML owns the Keyed default.
- **Arrival capture no longer risks an early-worldgen "Could not find player faction" log.** Checks
  `GamePlaying` first and uses `Faction.OfPlayerSilentFail`.
- **Malformed quests filtered from prompts.** New pure `QuestEventData.IsMalformedResolvedQuestDescription`
  rejects quests whose description resolves to `ERR:` placeholder or blank text.
- **Harmony patching is now per-class.** Startup replaced `harmony.PatchAll()` with a per-class
  `PatchAllSafely` sweep so one fragile target can't block later patches.
- **Quest UI accept fallback silent on clean boot.** `QuestUiAcceptPatch` generated-closure fallback
  no longer warns (canonical `Quest.Accept` is the real hook); dev-mode only.
- **UI text-clipping hardening.** Group-label chip is 20px tall; Advanced group title measures
  Medium-font line height instead of a hard-coded 24px.
- **Destructive dev buttons tinted red** with the XML-owned `devDangerButtonColor`.

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
