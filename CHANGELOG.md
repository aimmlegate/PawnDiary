# Changelog

Dated history of important changes to the mod, newest first. `DOCUMENTATION.md` describes the
current design; this file records how it got there.

- **2026-06-18 (settings prompt studio simplification)**
  - Reworked Prompt Studio to expose only the shared system prompts for diary entries, day
    reflections, neutral chronicles, and title generation. Saved overrides fall back to
    `DiaryPromptDef.xml` when cleared.
  - Removed the Events/filter editor from mod settings. Event group matching, instructions, and
    default enablement now stay XML-only through `DiaryInteractionGroupDefs.xml`; old saved group
    toggles are ignored during settings normalization.
  - Kept Persona presets in settings and aligned the help text with the current
    `DiaryPersonaDef` XML shape (`label`, `rule`, and `themes`).

- **2026-06-18 (diary tab per-frame view cache)**
  - The diary tab no longer rebuilds a `DiaryEntryView` for every event on every frame. It now caches
    the built list and reuses it until the pawn's render token changes, where the token is the pawn's
    event count plus a new `DiaryStateVersion` counter. `DiaryEvent` bumps that counter from its
    status/text/title setters (`SetStatus`, `ApplyLlmResult`, `SetTitle`, `SetTitleStatus`), so every
    rendered change still invalidates the cache; event additions are caught by the count. New
    `Source/Models/DiaryRenderToken.cs` and `DiaryGameComponent.RenderTokenFor`.
  - `GeneratedEntryForPlayLogEntry` (social-log click lookup) now hoists `ComputeDiaryBounds` out of
    its loop and uses the index-based bounds check, mirroring `EntriesFor` — removing the per-event
    boundary re-derivation (previously O(E²) per click).

- **2026-06-18 (pipeline refactor cleanup)**
  - Removed the dead pre-pipeline rendering engine from `DiaryPromptBuilder.cs` (~400 lines): the
    legacy prompt-string wrappers, `ComposeSystemPrompt`/`SystemPromptForEvent`/`TitleSystemPrompt`,
    and the render/facts/direct-speech/persona helpers that `DiaryPromptPlanner` now owns. The file
    is now a thin facade over the pure pipeline.
  - Routed `ShouldResolvePromptEnchantment` through `DiaryPromptPlanner.TemplateKeyFor` so the
    enchantment gate and the shipped prompt share one template-selection source and can no longer
    drift (previously a duplicate selector).
  - Dropped vestigial `DiaryResponseRules` fields that nothing consumed (`allowDirectSpeechBlocks`,
    `recipientPlainProseOnly`, `atmosphereCue`, `distortDirectSpeech`, `staggeredIntensity`,
    `textDecorationContext`) plus their unread payload feeders; the planner now builds response rules
    through the single `DiaryResponseRules.ForRequest` path. The UI still reads display state from the
    saved `DiaryEvent`, unchanged.
  - `DiaryTextDecorationDefs.CurrentRules` now caches the code fallback and resolves the Def lookup
    once, instead of re-querying `DefDatabase` and re-allocating the fallback list on every call when
    the XML Def is absent (this getter runs per text block per frame).
  - Added `LlmResponseParser` tests for the previously-untested no-closing-tag branches of
    `StripTaggedReasoningBlocks` (final-answer marker, blank-line split, remove-to-end) and the
    multi-tag loop.

- **2026-06-18 (settings temperature slider)**
  - Added a localized Generation settings slider for the saved LLM `temperature` value, exposing the
    existing 0-2 request sampling control without editing config files.

- **2026-06-18 (prompt-lab exhaustive stability runs)**
  - Added `prompt-lab/run.js --all-variants` to build every eligible XML event group with stable
    initiator/recipient or solo cases crossed against a fixed prompt-enchantment matrix: baseline,
    pain, blood loss, consciousness, fever, intoxication, and sensory loss.
  - Added `--passes` for repeated identical prompt sets, compact markdown saving (`Prompt` +
    `Parsed result` per case), configurable prompt-enchantment variants, and support for
    `--include-groups all` / `--include-personas all`.
  - Fixed generated prompt-lab rendering so pair/solo assembler fixtures and static template
    fixtures actually render through the shared `assembler.js` mirror before requests are sent, and
    fixed absolute `--result-folder` save paths.

- **2026-06-18 (local verification hooks)**
  - Added tracked `.githooks/pre-commit`, `.githooks/pre-push`, and `.githooks/verify.ps1` so local
    commits and pushes can automatically run whitespace checks, XML parsing, pure test projects, and
    the Debug MSBuild build.
  - Documented `git config core.hooksPath .githooks`, the committed-DLL freshness check, and the
    explicit `PAWNDIARY_SKIP_VERIFY_HOOKS=1` emergency bypass.

- **2026-06-18 (agent architecture guardrails)**
  - Updated the shared `skills/pawndiary-engineering` workflow and top-level `AGENTS.md` rules so
    future changes default to explicit layer boundaries, typed plain-data contracts, pure/testable
    helpers where possible, XML-owned tunable constants, and focused pure test coverage.

- **2026-06-18 (XML text decoration rules)**
  - Added `DiaryTextDecorationDefs.xml`, `DiaryTextDecorationDef.cs`, and pure
    `DiaryTextDecorations` contracts for XML-defined display text decorations. Rules can match saved
    hediff facts, trait facts, event tags, color cues, atmosphere cues, defNames, domains, or context
    keys, then apply ordered `DirectSpeech`, `Body`, or `All` scope decorations.
  - Reworked staggered word sizes and Zalgo into deterministic pure rich-text transforms. The default
    XML now applies staggered word sizes only to direct speech for intoxication/anesthesia-like
    hediffs, and Zalgo only to direct speech for unsettling anomaly/dark cues.
  - Captured compact per-POV hediff/trait facts on new diary events and carried the decoration
    context through `DiaryEntryView` and the typed prompt/response contracts. Added
    `tests/DiaryTextDecorationTests` for matching, ordering, serialization, and transform behavior.

- **2026-06-18 (XML diary UI style)**
  - Added `DiaryUiStyleDef.xml` and `DiaryUiStyleDef.cs` for display-only Diary tab styling:
    dimensions, card spacing, linked-entry sizing, default expanded-entry count, speech marker tags,
    text colors, accent palettes, pending/fade/pulse timings, and saved `colorCue` accent mappings.
  - Routed Diary tab drawing through `DiaryUiStyles.Current` with code fallbacks, keeping runtime
    behavior toggles in saved settings while making visual defaults editable from XML.

- **2026-06-18 (typed generation pipeline contracts)**
  - Added explicit `Source/Pipeline` DTO contracts for event payloads, XML policy snapshots, prompt
    requests/plans, response rules, and response plans. `DiaryPipelineAdapters` is now the impure
    RimWorld/XML/localization bridge, while `DiaryPromptPlanner` and `DiaryResponsePostprocessor`
    are pure and testable.
  - Routed generation queueing through `DiaryPromptPlan` instead of raw prompt strings, carrying the
    selected template key, system prompt, user prompt, and prompt-time `DiaryResponseRules` into
    `LlmGenerationRequest`. `LlmClient` now applies response cleanup through the postprocessor rather
    than rereading request shape directly.
  - Added `tests/DiaryPipelineTests`, a standalone pure test harness that compiles without RimWorld
    assemblies and covers prompt selection, solo first-person plans, dual-POV initiator/recipient
    plans, neutral death/arrival plans, title plans, and response postprocessing.

- **2026-06-18 (pure LLM response parser)**
  - Moved provider response extraction, provider-status error surfacing, reasoning/thinking block
    stripping, and local max-token/sentence cleanup out of `LlmClient` into pure
    `LlmResponseParser`, leaving `LlmClient` focused on HTTP lanes, retry/failover, deadlines, and
    main-thread result/log handoff.
  - Added a standalone `tests/LlmResponseParserTests` console harness covering OpenAI Chat,
    OpenAI Responses, Ollama, reasoning scrub cases, provider errors, and title-vs-entry cleanup.

- **2026-06-18 (atmosphere-led prompts + persona in system prompt)**
  - Rewrote the first-person and reflection system prompts (`DiaryPromptDef.xml` and the matching C#
    defaults in `DiaryPromptDef.cs`) to lead with a positive "write toward atmosphere" block —
    anchor the entry in one concrete sensation/object/gesture, let mood/health/relationship/setting/
    tone color the subtext, keep the colonist's voice — then a consolidated "stay truthful" block.
    This replaces the old prohibition wall that muted output on both small RP-tuned and large
    reasoning models, while keeping the anti-hallucination guardrails. Added a flat-vs-atmospheric
    example pair to teach the intended lift, and added a fragment carve-out so persona voices built
    on fragments (imagist, fractured, black-signal, fiery) are no longer forced into tidy grammar.
  - Moved the persona voice out of the user-message `persona:` field and into the system prompt via
    new `DiaryPromptBuilder.ComposeSystemPrompt`, wrapped by the new `PawnDiary.Prompt.PersonaVoice`
    Keyed string, so the voice governs *how* the entry is written instead of competing as one field.
    Added `includePersona` to `DiaryPromptTemplateDef` (default true; false on death/arrival/title),
    removed the `persona` field from the shipped templates and the code fallback field list, and
    trimmed `singlePovInstruction` / `recipientFollowupInstruction` to a short task restatement since
    the guardrails now live only in the system prompt. The `Persona` field source still resolves for
    custom templates that want the voice back in the user message.

- **2026-06-18 (reasoning leak fix)**
  - Fixed reasoning/thinking text leaking into saved diary entries from R1-style models whose chat
    template emits the opening `<think>` in the prompt, so the completion begins inside the reasoning
    block and contains only a closing `</think>` before the answer. `LlmClient` now drops a lone
    closing reasoning tag (and everything before it) when there is no matching opening tag, alongside
    the existing paired/unclosed `<think>` handling.
  - Prompt templates with an empty `<fields>` list now render the code-defined fallback fields for
    that shape (via `DiaryPromptTemplates.FieldsFor`) instead of an empty body; removed a dead
    re-fetch in `DiaryPromptBuilder.RenderTemplate`.
  - `DiarySignalPolicy`/`DiaryContextReaction` fallbacks are cached per key instead of allocating a
    new object on every accessor call when a Def is missing.
  - Prompt Studio status note now states plainly that prompt customizations from older versions are
    no longer applied and wording comes only from XML.

- **2026-06-18 (XML prompt and signal architecture)**
  - Added XML `DiaryPromptTemplateDef`, `DiaryContextReactionDef`, and `DiarySignalPolicyDef`
    layers so prompt field lists, context reactions, thought/work tracker tuning, and prompt
    template instructions can be adjusted without C# changes.
  - Routed temporary thoughts, ambient thought batching, staged thought progression, sampled work,
    recent threat-letter context, and active map-condition context through XML policies.
  - Moved low Consciousness from persona-specific rules into weighted capacity prompt
    enchantments, and removed persona consciousness settings from XML/runtime/settings UI.
  - Made Prompt Studio and event group instruction views XML-only status/previews, removed saved
    prompt/group-instruction overrides, and updated prompt-lab generated fixtures to render
    `DiaryPromptTemplateDefs.xml`.

- **2026-06-18 (direct speech blocks)**
  - Changed interaction direct-speech prompting to use closed `[[speech]]...[[/speech]]` marker
    lines for initiator/single-POV speech, while recipient follow-up prompts now forbid speech
    blocks and stay plain prose.
  - Rendered closed non-recipient speech markers as separate colored diary lines, stripped unclosed
    or recipient markers back to ordinary prose, and removed markers from linked-entry previews.

- **2026-06-18 (API connection test)**
  - Added a per-API Test button that sends a tiny localized prompt through the selected endpoint,
    model, key, compatibility mode, reasoning, and Ollama thinking settings.
  - Shows row-level success/failure status and writes safe RimWorld console debug logs for
    connection successes/errors without logging API keys.

- **2026-06-18 (LLM client review fixes)**
  - Kept duplicate failover lanes distinct when they share endpoint/model/mode but use different
    API keys, and used the successful lane key only in memory for immediate recipient/title pinning.
  - Surfaced provider-level 200-response errors/incomplete statuses for Responses/Ollama, stripped
    common unclosed reasoning tags, and capped persisted raw debug responses per POV.

- **2026-06-17 (reasoning block scrub)**
  - Kept non-streaming LLM calls waiting for complete responses, then stripped structured Responses
    reasoning items and common text reasoning blocks before storing debug text or diary entries.
  - Updated raw-response comments/docs to clarify they store pre-length-cleanup final-answer text,
    not model reasoning transcripts.

- **2026-06-17 (compatible API modes)**
  - Added per-API compatibility modes for OpenAI-style chat completions, OpenAI Responses, and
    native Ollama chat so users can add common compatible APIs from the settings UI.
  - Added OpenAI Responses reasoning-effort selection and Ollama native thinking on/off per lane,
    with mode-aware generation URLs, response parsing, model fetching, failover, and lane pinning.

- **2026-06-17 (diary UI debug raw response)**
  - Added raw LLM responses to `LlmGenerationResult` and persisted them per-POV in
    `DiaryEvent` so they survive save/load.
  - Threaded raw responses into `DiaryEntryView` and displayed them in the existing diary-tab
    debug text block, giving an in-game view of untrimmed model output.

- **2026-06-17 (role-slot extraction TODO)**
  - Documented a safe migration outline for extracting repeated `DiaryEvent` initiator,
    recipient, and neutral field families into saved role slots.

- **2026-06-17 (Diary tab partial split)**
  - Split the large `ITab_Pawn_Diary` implementation into focused partial files for dev controls,
    year paging, entry-card chrome/navigation, expansion state, and roleplay text layout without
    changing tab behavior.

- **2026-06-17 (review hardening and helper split)**
  - Fixed settings model-list fetch invalidation so removed/reset/edited API rows cannot leave the
    UI stuck fetching or auto-fill a stale result into a shifted row.
  - Added shared exact `gameContext` field parsing and used it for arrival/death ownership, work
    deduplication, prompt context facts, and source markers.
  - Escaped raw generated angle-bracket tags before diary rich-text formatting, marked successful
    title follow-ups complete immediately, gated verbose API logs behind the dev LLM debug toggle,
    and cleared transient arrival/death context caches at game-session start.
  - Split endpoint URL helpers and settings-time model discovery out of `PawnDiaryMod.cs` into
    focused `EndpointUtility` and `ModelListClient` files.

- **2026-06-17 (active map context in settings)**
  - Added visible active map conditions to the first-person prompt `setting:` line, capped at three
    labels so long-running conditions can color entries without creating new diary events.
  - Added a fresh recent-threat hint from RimWorld's letter archive while a player-home map remains
    in danger, bounded by age and archive scan limits to avoid stale raid context.

- **2026-06-17 (low-creative-reach prompt guard)**
  - Added first-person and reflection system-prompt guardrails for roleplay-tuned models that
    over-expand sparse context, forbidding new names, places, backstory, symbols, and off-screen
    actions not present in the notes.
  - Added the previous event-subject prompt defaults to saved-settings migration so untouched
    Prompt Studio text upgrades to the new guard automatically.

- **2026-06-17 (always-on setting context)**
  - Changed first-person prompt policy so `setting:` is sent for every prompt when surroundings are
    available, including social, batched, internal-state, and reflection entries.
  - Confirmed weather/biome remain gated to outdoor surroundings, while indoor prompts still carry
    room role and `indoors`.
  - Synced the prompt-lab policy mirror and documentation with the new always-on setting behavior.

- **2026-06-17 (prompt event-subject guardrails)**
  - Reworded first-person and reflection prompt defaults so the actual event stays the subject and
    optional context fields only color voice, focus, and subtext.
  - Demoted `important health:` from a high-priority story target to physical/mood pressure unless
    the event itself is medical, with explicit guards against invented treatment scenes.
  - Updated single-POV and recipient follow-up instructions to keep health, setting, weapon,
    relationship, persona, and hidden continuity from becoming alternate scenes.

- **2026-06-17 (prompt lab prompt-policy sync)**
  - Updated prompt-lab generated fixtures to mirror `DiaryPromptBuilder`'s compact context policy,
    including optional `important health:` cues, last-opener cues, combat context, hidden initiator
    context, and XML/Keyed direct-speech instructions.
  - Fixed prompt-lab XML boolean parsing for `important`, `combat`, and `catchAll` group flags, and
    restored XML order as the generated group sampling order.
  - Switched prompt-lab title fixtures and title follow-ups to read
    `DiaryPromptDef.titleUserInstruction` instead of using a stale hardcoded title trailer.

- **2026-06-17 (pregnancy diary events)**
  - Added dedicated Pregnancy and Labor Hediff groups for pregnancy start, trimester-style severity
    progression, and labor start/pushing signals.
  - Added optional immediate-Hediff fallback text keys so XML groups can use natural localized event
    text instead of the generic health-condition wording.
  - Added a pregnancy-memory Thought group for pregnancy termination, stillbirth, miscarriage, and
    partner miscarriage memories.

- **2026-06-17 (concrete prompt examples)**
  - Tightened first-person diary and day-reflection prompts to require concrete supplied details,
    avoid vague filler, and include compact good/bad examples for small local models.
  - Added short good/wrong examples to interaction direct-speech cues so quoted dialogue stays tied
    to the current POV pawn.
  - Migrated untouched saved prompt defaults on load while preserving customized Prompt Studio text.

- **2026-06-17 (role-specific direct-speech prompts)**
  - Split paired interaction direct-speech guidance into separate initiator and recipient prompt
    cues, each naming the current POV pawn and the opposite pawn.
  - Updated single-POV interaction guidance to name the POV pawn and forbid quoted dialogue when
    that pawn did not speak.
  - Hardened the interaction-prompt detector so neutral, dev mock, mental-state, tale, mood,
    thought, inspiration, work, hediff, and day-reflection entries cannot receive direct-speech cues
    through a future defName collision.

- **2026-06-17 (direct-speech POV guard)**
  - Reworded the interaction direct-speech prompt cues so quotes are conditional, never forced, and
    can only represent words plausibly spoken by the current POV pawn.
  - Told paired and single-POV prompts to paraphrase the other pawn without quotes, or use no quoted
    dialogue when the current POV pawn did not speak.

- **2026-06-17 (hediff mod support fixes)**
  - Made hediff progression and immediate-event dedup keys body-part aware, so same-def conditions
    on different parts do not mask each other.
  - Removed the obsolete `daySummaryHediffMinSeverity` tuning field; Hediff group `<minSeverity>`
    is now the single severity gate.
  - Included Hediff groups in the settings height estimate so larger compatibility catalogs remain
    scrollable.

- **2026-06-17 (XML hediff mod support)**
  - Added a generic Hediff event-group domain with XML policies for day-reflection or immediate
    health-condition diary signals, so modded hediff support can be added with Def patches instead
    of per-mod C#.
  - Routed `AddHediff` and a new severity-step scanner through that XML layer while preserving the
    previous major bad non-injury affliction behavior through the `hediffMajorHealth` catch-all.
  - Exposed Hediff groups in the Events settings UI and documented XML compatibility examples for
    extension mods.

- **2026-06-17 (inspiration diary events)**
  - Added solo diary entries for successful pawn inspirations through
    `InspirationHandler.TryStartInspiration`, with a new Inspiration event group and settings
    header.
  - Removed Royalty `WordOfInspiration` from the ritual/social interaction matcher so the target's
    resulting inspiration is recorded as the diary event.

- **2026-06-17 (POV-only quoted speech)**
  - Tightened interaction prompt dialogue rules so quoted direct speech can only come from the
    current POV pawn; other pawns' speech should be paraphrased without quotes.

- **2026-06-17 (verse persona presets)**
  - Added restrained `plainspoken-poet` and `lowkey-rapper` diary persona presets, with
    low-Consciousness variants and rules that avoid overdone lyricism or forced rhyme.

- **2026-06-17 (fragmented persona presets)**
  - Added `fractured-pattern-seer` and `word-salad-oracle` diary persona presets for readable
    fragmented-association and controlled word-salad voices, including low-Consciousness variants.

- **2026-06-17 (rare atmosphere formatting)**
  - Added a default-on `enableAtmosphericFormatting` setting for display-only diary typography.
  - Limited unusual layout to extreme entries: fractured mental-break prose, unsettled anomaly/dark
    prose, centered memorial death descriptions, and staggered variable-size words for first-person
    pages recorded while the pawn was severely intoxicated or low-Consciousness.
  - Mapped Anomaly's in-game "strange chat" to `DisturbingChat` and gave only the initiator POV an
    anomaly-green accent plus dramatic distorted direct-speech formatting.
  - Increased strange-chat direct-speech distortion so quoted words look more visibly uncanny while
    still leaving the saved generated text untouched.
  - Split `DisturbingChat` into its own strange-chat interaction group and gave it chitchat's
    ambient batching and promotion odds, so ordinary strange chats stay rare while loaded moments
    can still become full pairwise entries.
  - Added a localized paired-interaction prompt rule requiring at least one short direct-speech
    sentence in double quotes, so two-pawn diary entries read less like pure description.
  - Added a softer direct-speech cue for single-POV interaction entries, used only when quoted
    speech fits naturally.
  - Stored per-POV staggered intensity on new diary events so worse impairment affects more words
    without changing prompts or saved generated text.

- **2026-06-17 (diary color cue consistency)**
  - Added a saved `colorCue` key for diary events so card accents no longer depend on localized
    group labels or generated titles.
  - Retuned Diary tab accents toward RimWorld-like colors: hostile red for combat/crisis, orange for
    social fights, green for mental breaks, light blue for daze/wander breaks, deep red for dark or
    anomaly entries, white for deep talk and day reflections, and light white-gray for non-important
    entries.

- **2026-06-17 (persona consciousness modifier settings)**
  - Extended Persona presets settings so built-in and custom personas can edit their clouded,
    fading, and barely-conscious voice modifiers.
  - Preserved XML inheritance for old built-in persona overrides until a player changes those
    modifier fields, avoiding silent loss of existing low-Consciousness tuning.

- **2026-06-17 (route cleanup)**
  - Added an explicit `arrival` interaction group for `PawnDiary_Arrival`, so arrival pages use the
    Arrival chip, important styling, and Events toggle instead of the `other` catch-all.
  - Removed inert legacy `enableLlm` and `keepRawEntryOnFailure` settings fields.
  - Removed stale Keyed strings from old status, restore-prompt, small-talk, and atmosphere paths.
  - Dropped the no-op prompt-lab `--all` switch from CLI help, package scripts, and README examples.

- **2026-06-17 (discovery event removal)**
  - Removed the non-functioning discovery event generation path: disabled Harmony stubs, recorder
    partial, GameEvent classifier/settings helpers, XML group, and unused Keyed strings.
  - Updated documentation so the active event-source list no longer advertises quarantined
    discovery generation.

- **2026-06-17 (low-consciousness persona rules)**
  - Kept the below-11% Consciousness generation guard intact while routing still-conscious impaired
    pawns through clouded, fading, and barely-conscious prompt states.
  - Replaced the generic barely-conscious persona override with persona-specific XML rules on every
    built-in `DiaryPersonaDef`, so translators and persona authors can tune each impaired voice.
  - Added localized Keyed words for the compact Consciousness `important health:` cue.

- **2026-06-17 (compact important-health prompt cues)**
  - Changed hediff prompt enchantments from a generic `prompt_enchantment:` metadata line to a
    compact `important health:` cue with urgency, condition/body part, and capped impact reasons.
  - Added first-person system-prompt guidance that important health cues are high-priority body and
    mood pressure, not checklist text to recite.
  - Moved the generated important-health cue words into Keyed translations, leaving only stable XML
    tuning tokens hardcoded in the resolver.
  - Localized the ambient interaction `with {pawn}` prompt-context connector.

- **2026-06-17 (persona settings selector)**
  - Replaced the Persona Presets button grid with a compact selector menu and expanded the persona
    editor layout so long labels and all theme tags fit without clipping.

- **2026-06-17 (insult-spree batching)**
  - Added a three-in-game-hour pairwise batch policy for insult interactions, preserving RimWorld's
    own play-log insult text as evidence while producing one diary generation for repeated jabs.
  - Added a disabled-by-default `InsultingSpree` mental-state group so generic insult-spree break
    pages do not duplicate the richer batched insult entries unless a player enables them.

- **2026-06-17 (number-light prompt context)**
  - Replaced generated prompt-context numbers with word buckets where practical: life stage instead
    of age, mood/pain/bleeding/opinion/thought impact buckets, and hediff intensity words.
  - Removed numeric list markers from batched/ambient prompt evidence and changed the "more quiet
    moments" line to avoid exact counts.
  - Reworded default prompt instructions and thought-event instructions to use prose length/effect
    guidance instead of digit ranges or mood-offset language.

- **2026-06-17 (hediff-weight prompt enchantments)**
  - Replaced XML-authored prompt-enchantment prose with live hediff context built from the game's
    condition label, body part, severity, and description.
  - Kept `DiaryPromptEnchantmentDefs.xml` as a weighting/eligibility table for visible hediffs and
    removed gene/title/stat/capacity enchantment matchers from the runtime path.
  - Broadened prompt-enchantment resolution so every first-person prompt with `persona:` may include
    one weighted live health-condition hint; neutral chronicle/title prompts still omit it.
  - Kept `my last opener (not repeat)` in first-person prompts as a compact anti-repetition cue.

- **2026-06-17 (prompt context policy streamlining)**
  - Added event-type prompt context policies so routine/internal entries send minimal first-person
    prompts, social entries keep relationship context, and combat/crisis entries keep pawn state,
    setting, current POV weapon, hidden initiator context, and optional prompt enchantments.
  - Curated neutral arrival/death fact lines instead of dumping full `gameContext` metadata into the
    prompt.
  - Removed unused opinion summary, atmosphere, and burning-passion prompt plumbing from event
    creation and `DiaryEvent` persistence.

- **2026-06-17 (consciousness prompt enchantments)**
  - Added XML `capacityBelow` matchers for pawn capacities, with optional `minValue` for exclusive
    ranges.
  - Added starter Consciousness-band enchantments while leaving Royalty titles as regular
    pawn-summary `title=` context instead of title-specific enchantment Defs.

- **2026-06-17 (starter prompt enchantment catalog)**
  - Expanded `DiaryPromptEnchantmentDefs.xml` with starter hediff enchantments for alcohol,
    hangover, luciferium, chemical addiction/withdrawal cravings, flake/yayo, smokeleaf, psychic
    hangover, blindness, dementia/Alzheimer's, trauma savant, resurrection psychosis, joywire,
    abasia, mindscrew, pregnancy, hemogen craving, psychic bond torn, and Anomaly conditions.
  - Left missing body parts out of the starter catalog after review.

- **2026-06-17 (hediff severity prompt tiers)**
  - Added four fixed hediff severity levels for prompt enchantments: XML `hediffSeverityTiers` can
    override prompt text, chance/frequency, selection weight, and urgency weighting per level.
  - Updated the starter sickness and blood-loss enchantments so higher severity can appear more
    often and use stronger instructions without asking the model to choose the severity.

- **2026-06-17 (consciousness generation guard)**
  - Blocked new non-neutral main/title LLM generation while the POV pawn is below 11%
    Consciousness by marking that POV `skipped`, while still allowing neutral arrival/death
    descriptions and healthy paired POVs.

- **2026-06-17 (prompt enchantments)**
  - Added XML-driven prompt enchantments: first-person prompts may append one weighted-random,
    chance-gated `prompt_enchantment:` rule based on visible hediffs, active genes, royal titles, or
    pawn stat thresholds.
  - Added an enabled-by-default settings toggle, starter enchantment Defs, DLC-safe gene/title
    matchers in `DlcContext`, and rebuilt the committed assembly.

- **2026-06-17 (documentation and changelog compaction)**
  - Compacted `DOCUMENTATION.md` and this changelog while preserving current architecture, runtime
    constraints, localization rules, event coverage, settings behavior, and historical milestones.

- **2026-06-17 (caches, personas, UI paging, and discovery quarantine)**
  - Bounded transient caches: recent-event dedup dictionaries now prune expired keys after reaching
    512 entries, and Diary-tab first-seen/expansion animation caches are capped.
  - Cached the merged persona catalog and invalidated it after settings-backed persona edits or
    load-time normalization; model-facing numeric context now uses invariant dot-decimal formatting.
  - Added Persona presets settings: edit built-ins, add/delete custom personas, normalize allowed
    tags, and expose custom presets in runtime picker/generation flows.
  - Added year paging for long diary histories, a year selector with page counts, newest-15 expanded
    default, older-entry collapse/expand animation, corrected collapsed-row geometry, and bounded
    hover/click hitboxes.
  - Added a dev-mode mock-page filler that creates up to 360 completed test pages for year-paging
    and collapse testing without calling an LLM.
  - Moved the Diary inspector tab insertion point from after Social to after vanilla Needs.
  - Added a `Pawn.Kill` postfix fallback so natural condition deaths such as starvation still get a
    neutral final entry when vanilla emits no death Tale.
  - Added staged situational thought scanning for malnourishment/starvation, tired/exhausted,
    trapped/entombed underground, and chemical hunger/starvation, with XML progression rules and
    no recovery-page spam.
  - Tried and then quarantined map-discovery hooks: fog reveal, ancient-danger, monolith, and debug
    fallback work was isolated in unregistered `DiscoveryPatches.cs`; `DiscoveryEventsEnabled=false`
    makes discovery recording no-op until repaired.
  - Polished title behavior and presentation: pending-title animation aligns with future title text,
    mod preview image was refreshed with a RimWorld-style `Pawn Diary` treatment, and compact rows
    match expanded card headers more closely.

- **2026-06-16 (prompts, generation weights, UI readability, and robustness)**
  - Tightened diary, reflection, neutral, and title prompt contracts with explicit sentence/word
    ranges, "prefer shorter complete output" guidance, return-only-text guidance, and stronger
    instructions to treat structured context as private evidence rather than prose to echo.
  - Added cleanup for under-limit LLM responses that still end with a dangling sentence fragment,
    and changed local response trimming to prefer the last complete sentence before `maxTokens`.
  - Removed the duplicated pawn-state `atmosphere:` prompt line while preserving mood, health, low
    capacities, top thoughts, and group `tone:`.
  - Added saved sliders for `workGenerationWeight` and `socialGenerationWeight`; work sampling and
    batched-social promotion honor 0x-5x multipliers.
  - Added settings overrides for every Def-backed user-message prompt text that reaches generation,
    plus Prompt Studio reset flows and a cleaner event-group editor with per-group instruction edits.
  - Reworked diary body rendering through `DiaryTextFormat`: light markdown becomes Unity rich text,
    quoted speech is colored inline from the pawn's favorite color, and card typography/chrome were
    warmed and cleaned up.
  - Changed surroundings context to weighted-random 1-2 nearby objects so important context objects
    such as fire, corpses, and buildings appear more often without becoming a fixed list.
  - Added one-tick snapshots for scheduled colonist scans, preventing collection-modified errors if
    pawn membership changes during sleep, work, or persona-count scans.
  - Added O(1) `eventId -> DiaryEvent` lookup rebuilds, consolidated event registration, and changed
    long-history Diary tab boundary calculation from per-event work to once per draw.
  - Raised `ServicePointManager.DefaultConnectionLimit`, moved model-fetch application back to the
    main thread, hardened LLM sends against unexpected exceptions, bounded death/arrival caches, and
    replaced magic LLM status strings with constants.
  - Refreshed the README, expanded `About/About.xml`, and added then refined the mod preview image.
  - Added temporary GameEvent discovery recording for map discoveries and monolith beats; this work
    was later disabled/quarantined on 2026-06-17.

- **2026-06-16 (work entries, prompt lab, titles, UI polish, and cleanup)**
  - Added sampled pawn-work diary entries from current Work-tab jobs, with Work-domain XML groups
    for passionate, straining, routine, and dark-study work plus XML odds/cooldowns.
  - Restored `prompt-lab/` as an XML-driven Node harness with generated/manual fixtures, model
    overrides, and git-ignored markdown result snapshots.
  - Made title generation default on, removed first-line/first-sentence fallback titles, moved title
    instructions into `DiaryPromptDef.titleUserInstruction`, and cleared stale pending title states
    on load so interrupted titles retry.
  - Polished diary cards with group accent colors/chips, date-title headers, title and entry
    animations, dot-only writing indicators, and player-facing wording about diary pages.
  - Enforced arrival pages as first visible/generated entries and death pages as terminal entries,
    including same-tick boundary handling and startup generation ordering.
  - Removed legacy paths: broken prompt-lab code before restoration, combined dual-response parsing,
    paired-mode toggle, obsolete save/migration fields, fallback response parsing, old small-talk
    tuning, and unsupported pre-event-model display paths.
  - Re-reviewed docs against source, extracted and compacted changelog history, and kept
    `DOCUMENTATION.md` aligned with current architecture.

- **2026-06-16 (event architecture, batching, reflections, and reliability)**
  - Split `DiaryGameComponent` into focused partials for event sources, public API, event factory,
    generation, lookup, batching, ambient thoughts, and day summaries.
  - Extracted `MoodImpact` as shared positive/negative/neutral mood classification.
  - Added configurable interaction batching with `PairEvent` and `AmbientDayNote`, plus weighted
    promotion of salient batched interactions into immediate pairwise events.
  - Added ambient interaction/thought day notes and richer end-of-day `DayReflection` selection from
    major events, opinion shifts, new afflictions, and filler.
  - Added temporary thought entries through `MemoryThoughtHandler.TryGainMemory`, fixed the patch
    target, and tightened filtering, deduping, and localization behavior.
  - Added dev-mode "show generating entries", thread-safe debug-log flushing, stale/orphan pending
    recovery, and a fix for founding-colonist startup thoughts stranded by session resets.
  - Improved settings layout with measured scroll height, section styling, two-column event toggles,
    prompt editing, compact API rows, model fetch/pick controls, and scroll-click fixes.
  - Split system prompts by narrative mode: diary, day reflection, neutral chronicle, and title.
    Added per-event `tone` metadata and kept production Diary views generated-only unless dev mode
    is enabled.

- **2026-06-15 (arrivals, deaths, personas, DLC safety, event coverage, and routing)**
  - Added neutral first-entry arrival pages for starting colonists and later joins via
    `Pawn.SetFaction`, including prior faction, recruiter, pawn kind, creepjoiner, scenario, and
    surroundings context.
  - Added neutral colonist death descriptions using `Pawn.Kill` cause caches plus death TaleDefs,
    including damage, culprit hediff, missing parts, lethal conditions, and nearby context.
  - Dev-gated Diary-tab controls so normal play hides persona/debug/generation controls while dev
    mode can show writing toggles, persona picker, pending rows, and diagnostics.
  - Added trait/backstory-aware, colony-deduplicated initial persona rolls with XML themes,
    `PersonaAffinity`, base weights, theme bonuses, duplicate penalties, fallback random selection,
    and a creepjoiner bonus for `void` personas.
  - Expanded the persona catalog with more voice-driven presets, richer rules for smaller local
    models, and four dark/void personas.
  - Added DLC-safety guidance and centralized guarded optional DLC pawn reads in `DlcContext` for
    Biotech xenotype, Royalty title, and Ideology faith.
  - Added MoodEvent game conditions, skipped `ProblemCauser` conditions, and ensured conditions with
    TaleDefs record once through MoodEvent.
  - Added notable TaleRecorder coverage for raids, disasters, milestones, medicine, combat, research,
    history/art events, quality crafts, relics, and Anomaly tales while avoiding duplicate art logs.
  - Added multi-API lanes with per-lane concurrency, round-robin assignment, automatic failover,
    model-list fetch/pick UI, lane enable/disable, compact setup hiding, and key-safe debug logs.
  - Fixed patch robustness, duplicate entries, social-memory null references, combat classification,
    capacity thresholds, UI clipping, persona picker reachability, and linked-entry XML comments.

- **2026-06-15 (diary UI, context, localization, and agent docs)**
  - Reworked prompt context for leaner signal: no traits line, one positive and one negative thought,
    low-capacity keywords, latest opener, important-event passion, and safer social-thought summaries.
  - Added linked-entry previews between pawns with click-through to the other POV, plus Social-tab
    log-row click-through to matching diary entries.
  - Refined Diary-tab roleplay rendering, production-only cards, hidden debug/raw rows, compact model
    provenance tags, and generating badges; the tab later moved from Social-adjacent to Needs-adjacent.
  - Extracted changelog history out of `DOCUMENTATION.md`, folded C# notes into `AGENTS.md`, renamed
    agent guidance to `AGENTS.md`, and added standing rules for docs, localization, comments, build,
    and DLC safety.
  - Localized UI and natural-language prompt text through Keyed translations while intentionally
    keeping prompt schema labels, sentinels, internal ids, debug text, and background-thread errors
    in English.

- **2026-06-14 (background generation, eligibility, personas, XML refactor)**
  - Moved generation to background scans instead of Diary-tab side effects, corrected paired
    generation/pending-regeneration docs, and made interrupted pending entries retry.
  - Enforced colonist-only eligibility across recording, UI visibility, and public APIs; mixed events
    now create solo colonist entries.
  - Added in-game persona XML defs, default persona setting, per-pawn saved persona and generation
    toggle, and prompt persona lines.
  - Added small-talk batching, then replaced live dual-POV generation with sequential paired POV
    generation.
  - Added `DiaryPromptDef` as the XML source for prompt instructions/system prompts and migrated old
    prompt/parser constants to XML-backed lookups.
  - Renamed project files to `PawnDiary.*`, moved source into `Source/`, split the original large
    component into models/context/prompts/orchestrator, moved interaction groups and tuning to XML
    Defs, and added novice-friendly comments/docs.

- **2026-06-13 (initial diary system)**
  - Replaced `JavaScriptSerializer` with `MiniJson` for RimWorld Mono compatibility.
  - Added the first LLM diary generation path with queueing, concurrency, timeout handling, raw
    fallback behavior, and a configurable system prompt.
  - Added the initial inspector-tab UI, replacing the earlier gizmo/window and removing the colony
    neutral-events view.
  - Added prompt-lab, persona experiments, lean prompt context, social-fight and mental-break capture,
    XML-backed interaction groups with per-group enablement/instructions, and the original docs.
