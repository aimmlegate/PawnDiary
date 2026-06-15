# Changelog

Dated history of every change to the mod. **Add an entry here with each change** (newest first).
This is the single history file that `DOCUMENTATION.md` and `AGENTS.md` both point to; the design
doc itself describes only "what happens now".

- **2026-06-16 (thought feature review fixes)**
  - Converted `ThoughtGainPatch` from `[HarmonyPatch]` attribute to `TryRegister` pattern (matching
    `RelicInstallCompletionPatch`), so a missing or renamed `ThoughtHandler.GetNewThought` only
    disables thought entries instead of crashing all patches.
  - Fixed `GetThoughtMoodOffset` to use `thought.MoodOffset()` (current stage's `baseMoodEffect`)
    instead of summing all stages, which gave incorrect magnitude for multi-stage thoughts.
  - Fixed bypass threshold priority: `thoughtBypassThresholdTokens` (death, banishment) now always
    bypass the magnitude check, even if the thought also matches `thoughtEatingTokens`.
  - Removed redundant entries from `thoughtIgnoreTokens` (Bedroom, Barracks, Hospital, DiningRoom,
    ObservedRottingCorpse, RottingCorpse) — already covered by `Room` and `ObservedCorpse`.
  - Added TODO comments in `DiaryInteractionGroupDefs.xml` flagging broad substring tokens
    (Good, Nice, Bad, Sad, Hot, Cold) that may misclassify modded ThoughtDefs.
  - Fixed stale comment referencing `durationTicks` instead of `durationDays`.

- **2026-06-16 (temporary thought diary entries)**
  - Added a new `Thought` domain for recording temporary thoughts (Thought_Memory with expiration)
    as solo diary entries. A Harmony postfix on `ThoughtHandler.GetNewThought` forwards newly gained
    thoughts to `DiaryGameComponent.RecordThought`.
  - Only thoughts with `durationDays > 0` are recorded (permanent traits are skipped). Filtering rules
    are fully configurable via `DiaryTuningDef.xml`:
    - `thoughtIgnoreTokens`: substring tokens for thoughts always skipped (room stats, corpse observations).
    - `thoughtBypassThresholdTokens`: substring tokens for thoughts that skip the magnitude check
      (death, banishment, abandonment — always recorded).
    - `thoughtEatingTokens`: substring tokens for eating thoughts, which use a higher threshold.
    - `thoughtMinMoodOffset` (default ±5): minimum absolute mood offset for general thoughts.
    - `thoughtEatingMinMoodOffset` (default ±15): minimum absolute mood offset for eating thoughts.
  - Three new interaction groups in `DiaryInteractionGroupDefs.xml`: `thoughtPositive`, `thoughtNegative`,
    and `thoughtOther` (catch-all). Each has a prompt instruction that tells the LLM to match
    dramatism to the mood offset magnitude.
  - Added `PawnDiary.Settings.ThoughtsHeader` label and a "Temporary thoughts" section header in the
    mod settings UI alongside the existing domain sections.
  - Added `PawnDiary.Event.Thought`, `PawnDiary.Event.ThoughtPositive`, `PawnDiary.Event.ThoughtNegative`
    keyed strings for the raw event text fed to the LLM.
  - `DiaryEvent.GroupForDisplay` now recognizes the `thought=` gameContext field to classify saved
    thought events into the `Thought` domain for UI coloring.

- **2026-06-15 (colony arrival first entries)**
  - Added neutral, persona-independent first entries that describe how a pawn joined the colony,
    using the new `DiaryPromptDef.arrivalDescriptionInstruction`.
  - Starting colonists are recorded after new-game map creation with scenario name/description plus
    pawn details, so their first diary entry is grounded in the selected scenario.
  - Later joins are captured through `Pawn.SetFaction(..., Faction.OfPlayer, recruiter)`, with
    `ArrivalContextCache` preserving prior faction, recruiter, pawn kind, creepjoiner status, and
    surroundings. This covers vanilla recruitment/wanderer/quest joins, Anomaly creepjoiners, and
    modded arrivals that converge on player faction without requiring DLC references.
  - Arrival events queue only the neutral role and are shown only in the arriving pawn's diary; the
    existing final-death terminal guard also blocks later event references after a death entry.
- **2026-06-15 (colonist death descriptions)**
  - Added a `Pawn.Kill` Harmony prefix plus `DeathContextCache` to capture killing `DamageInfo`,
    culprit hediff, hit/destroyed body parts, lethal conditions, weapon/instigator, and nearby
    surroundings at the moment a player colonist dies.
  - Death TaleDefs now queue a separate `neutral` LLM request for the deceased colonist using the
    new `DiaryPromptDef.deathDescriptionInstruction`, producing a persona-independent third-person
    death description instead of a first-person victim diary entry.
  - The deceased pawn's diary view now displays the neutral death description for that event, while
    non-death TaleRecorder behavior remains unchanged.
  - Death descriptions are terminal for that pawn: later-tick events are not added to the pawn's
    visible diary, are skipped by pending-generation scans, and are blocked by queue-time generation
    gates.
- **2026-06-15 (dev-gated diary tab controls)**
  - Hid the per-pawn persona picker from the normal Diary tab. RimWorld dev mode now shows a
    "Show persona settings" toggle that reveals the manual persona picker only when needed.
  - Added a dev-mode-only "Show LLM debug info" toggle. When enabled, the Diary tab shows
    raw/pending/failed entries plus the existing endpoint/model/status/error/prompt diagnostic
    block; normal mode keeps the production-only generated-entry view.
  - Confirmed the Diary inspector tab injection remains immediately after the vanilla Social tab,
    and corrected UI comments/docs that still referenced older tab positions.

- **2026-06-15 (trait/backstory-aware, colony-deduplicated initial persona roll)**
  - New pawns no longer get a uniform-random persona. `DiaryPersonas.WeightedStartingPersona`
    (`Source/Defs/DiaryPersonaDef.cs`) now rolls the *initial* voice with a weight per persona,
    keeping flat random only as a fallback. Existing saved personas and the manual picker are
    untouched — the roll runs only when a pawn's diary record is first created.
  - **Trait/backstory fit.** Each `DiaryPersonaDef` gained an optional `<themes>` list of coarse
    keywords (grim/warm/hostile/anxious/analytical/dramatic/social/whimsical/noble); all 22
    personas in `1.6/Defs/DiaryPersonaDefs.xml` are tagged. New `Source/Generation/PersonaAffinity.cs`
    maps RimWorld trait defNames (incl. spectrum-trait degrees like `NaturalMood:1` = optimist,
    `Nerves:-1` = nervous) and backstory `spawnCategories` to that vocabulary, adding weight per
    shared theme. Themes are internal keywords — never shown to the player, never localized.
  - **Soft colony de-duplication.** `DiaryGameComponent.BuildUsedPersonaCounts` counts personas
    in use by current living free colonists; weight is multiplied by ~0.25 per existing user
    (clamped to a small floor), so duplicate voices are rare but still possible once the catalog is
    spread thin, and assignment never starves with more colonists than personas.
  - Additive/back-compatible: untagged or removed persona defs still work (base weight only),
    `RandomStartingPersona` is retained as the fallback, and `DiaryPersonaDef.themes` defaults to
    an empty list so old/partial XML never NullReferences. `DOCUMENTATION.md` (per-pawn persona
    section) updated to match.
- **2026-06-15 (expanded persona catalog with 10 voice-driven presets)**
  - Added 10 new `DiaryPersonaDef`s to `1.6/Defs/DiaryPersonaDefs.xml`, growing the catalog from
    12 to 22. The new presets are distinguished by a distinctive *voice or worldview* (a way of
    framing events) rather than by mood, to complement the existing temperament-based set:
    ledger-keeper, hardboiled-gumshoe, doomsaying-prophet, clinical-experimenter,
    theatrical-tragedian, swaggering-braggart, rambling-old-timer, superstitious-ritualist,
    detached-philosopher, imagist-minimalist. Each follows the enriched rule format (writing
    style, emotional behavior, a sentence-opener cue, and a stylistic-device tip).
  - Mirrored the same 10 entries in the upstream `prompt-lab/personas.txt` catalog (entries
    13–22) so prompt-lab fixtures using `persona: random` can draw from them too.
  - Additive only: no existing personas were renamed or removed, so saved `personaDefName`
    values on existing pawns are unaffected. The default persona is unchanged
    (`DiaryPersona_StoicSurvivor`).
- **2026-06-15 (DLC-safety: guidance + first guarded DLC reads)**
  - Verified the mod runs on **base RimWorld with no paid DLC**: `About.xml` declares no DLC
    dependency, no `[DefOf]`/`GetNamed` of DLC defs, and DLC *events* are handled reactively via
    base-game patch choke points + `defName` string matching, so DLC content is inert (never
    crashes) when absent.
  - Added a **"DLC-safety"** section to `AGENTS.md` (+ a note in `DOCUMENTATION.md §8`) so new
    features keep this property: explains the "all DLC ships in one assembly, so it compiles but
    breaks at runtime" trap and the rules (prefer string matchers, route DLC pawn reads through
    `DlcContext`, gate with `ModsConfig.*Active`, avoid `GetNamed` of DLC defs, tag DLC XML refs
    with `MayRequire`).
  - **New `Source/Generation/DlcContext.cs`** — the single, double-guarded home for DLC-gated pawn
    reads (`ModsConfig.<Dlc>Active` + null-check; returns empty when the DLC/trait is absent). Wired
    three identity fields into `BuildPawnSummary`, each omitted without its DLC and for the trait's
    vanilla default: `xenotype=` (Biotech; skips plain Baseliner), `title=` (Royalty; highest
    title), `faith=` (Ideology; ideoligion + role). Verified by building against the game assembly.

- **2026-06-15 (code-review fixes: patch robustness & duplicate entries)**
  - **Relic patch no longer risks the whole mod.** `RelicInstallCompletionPatch` targets a
    compiler-generated method name (`<MakeNewToils>b__5_5`) that can change between RimWorld
    versions. It was registered via `PatchAll`, where a failed lookup throws and aborts *every*
    other patch. It is now registered manually from `DiaryModStartup` via `TryRegister`, which
    null-checks the method and logs a warning — a future rename disables only relic entries.
    `PatchAll` is also wrapped in try/catch as a backstop.
  - **No more double diary entries for masterwork/legendary art.** `RecordCraftedQuality` now
    skips art (`CompArt`), since vanilla's `CraftedArt` tale already records it. Removed the dead
    duplicate `CraftedArt` from the `talework` group (it always classified to `talequality`).
  - **No more double entries for condition-backed incidents.** Eclipse, Aurora, ToxicFallout,
    VolcanicWinter and Flashstorm are both TaleDefs *and* GameConditionDefs, so they were logged
    once as a Tale and once as a MoodEvent. `RecordTale` now skips any tale whose defName matches
    a `GameConditionDef`; the MoodEvent domain (with positive/negative handling) owns them.
  - **Efficiency:** `DetermineMoodImpact` no longer rescans the whole `ThoughtDef` database once
    per colonist — the pawn-independent condition offset is computed once in `RecordMoodEvent`
    and passed in (`DiaryContextBuilder` stays stateless).
  - Fixed stray de-indentation introduced across ~10 files in the previous two commits, and
    corrected the `moodEventDedupTicks` comment (dedup is per-condition, not per-colonist).

- **2026-06-15 (prompt improvements for small local models 6B–31B)**
  - Simplified the default system prompt from 5 rules (~250 words) to 3 rules (~100 words).
    Smaller models absorb fewer rules more effectively. Added explicit "One to three sentences."
  - Enriched all 12 persona rules with emotional triggers, sentence openers, and stylistic tips.
    Each persona now provides concrete guidance (e.g., "Opens with blunt observations about the
    situation. Uses short declarative sentences.") rather than just a one-line description.
  - Added `atmosphere:` prompt field—a short emotional anchor (e.g., "tense hostility",
    "bright warmth") combining mood bucket + opinion bucket. Helps small models establish tone
    without complex inference.
  - Reframed all interaction group `instruction` fields to be more cinematic/evocative "scene"
    descriptions (e.g., "a spark between two people" instead of "romantic moment between the
    pawns"). Primes the model for emotional texture rather than mechanical categorization.
  - Reduced default `maxTokens` from 160 to 100 for faster generation on small local models.
  - Added localization keys for atmosphere words (PawnDiary.Atmosphere.Mood.*, 
    PawnDiary.Atmosphere.Opinion.*).
  - Updated all prompt-lab fixtures to match the new format: reduced max_tokens, added atmosphere
    field, updated persona field to use enriched rules, and updated instruction field to use new
    scene-like phrasing.
  - Updated hardcoded persona fallback in `DiaryPersonaDef.cs` to match XML.

- **2026-06-15 (skip ProblemCauser game conditions)**
  - Commented out ProblemCauser handling: `GameConditionStartPatch` now skips conditions
    whose def is `ProblemCauser` or whose type is `GameCondition_ProblemCauser`.
    ProblemCauser conditions are too complex to handle correctly right now.
  - Commented out the `ProblemCauser` matchDefName in the mixed mood-events group in
    `DiaryInteractionGroupDefs.xml`. The `moodeventMixed` group still matches
    `PsychicSuppressorMale` and `PsychicSuppressorFemale`.

- **2026-06-15 (mood-event game conditions)**
  - Added a `GameConditionManager.RegisterCondition` Harmony postfix so mood-affecting
    game conditions (aurora, party, psychic soothe, eclipse, psychic drone, gray pall,
    toxic fallout) create solo diary events for each eligible colonist.
  - Added `GroupDomain.MoodEvent` to the `InteractionGroups` enum and
    `ClassifyMoodEvent()` classifier. Mood-event groups are data-driven from XML like
    the other domains, with a "Positive mood events" group (aurora, party, psychic soothe),
    a "Negative mood events" group (eclipse, psychic drone, gray pall, toxic fallout),
    a "Situationally mixed mood events" group (psychic suppressor by sex), and a catch-all.
  - Added `DiaryGameComponent.RecordMoodEvent()` with dedup via a transient
    `recentMoodEvents` dictionary, `moodEventDedupTicks` tuning (default 2500), and
    per-map colonist iteration.
  - Added `IsMoodEventEnabled` / `InstructionForMoodEvent` settings helpers and a
    "Mood events (colony conditions)" header in the settings UI.
  - Added `PawnDiary.Event.MoodEvent` localization key and
    `PawnDiary.Settings.MoodEventsHeader` settings header key.
  - Added `DiaryEvent.IsMoodEventEvent()` so mood events are classified into the
    MoodEvent domain for display purposes.
  - Updated `DOCUMENTATION.md` and data-flow diagram.

- **2026-06-15 (quality crafts, relics, and Anomaly tales)**
  - Added a `QualityUtility.SendCraftNotification` patch so Masterwork and Legendary items
    create solo diary events for the crafter. These use synthetic Tale-domain defNames
    (`CraftedMasterwork`, `CraftedLegendary`) and a new enabled "Masterworks & legendary crafts"
    group.
  - Added a `JobDriver_InstallRelic` completion patch so installing an ideology relic creates a
    solo diary event for the installer via the new enabled "Relics" group.
  - Split scary Anomaly TaleDefs into a dedicated enabled "Anomaly horror" group, including
    entity study, arm mutation, psychic rituals, void contact, death pall, unnatural darkness,
    and noxious haze.
  - Confirmed successful pawn operations are already covered through vanilla TaleDefs
    (`DidSurgery`, `HealedMe`) and avoided adding a duplicate surgery hook.

- **2026-06-15 (notable events beyond social logs)**
  - Added a `TaleRecorder.RecordTale` Harmony postfix so RimWorld's broader notable-history
    events can generate diary entries, not just social-log interactions and mental states.
  - Added a `Tale` group domain with data-driven TaleDef categories: combat/injuries/death,
    health/medicine, life milestones, work/achievements, raids/disasters/colony events,
    quiet personal moments, and a catch-all. High-signal categories default on; quieter ones
    default off.
  - Tale entries reuse existing solo/pairwise diary generation, colonist eligibility, per-pawn
    generation toggles, group prompt overrides, importance/combat flags, and localization.
  - Added `taleDedupTicks` tuning and skip duplicate TaleDefs already covered by the mental-state
    hook (`SocialFight`, `MentalStateBerserk`, `MentalStateGaveUp`).

- **2026-06-15 (compact hideable model setup)**
  - Made the API/model settings block compact and collapsible. Each lane now uses tighter rows for
    endpoint, key, model, Fetch, and Pick controls, and the expanded/collapsed state is saved.

- **2026-06-15 (toggle API lanes without deleting)**
  - Added an enabled checkbox to each configured API/model row. Disabled rows keep their endpoint,
    key, and model settings but are skipped by generation, round-robin selection, and failover.

- **2026-06-15 (multi-model debug logs)**
  - Added `[PawnDiary debug]` RimWorld log lines around API lane selection and LLM failover:
    configured vs active lane counts, selected primary model, failover order, skipped blank/duplicate
    lanes, each attempted model, success lane, and all-lanes-failed summaries.

- **2026-06-15 (starting persona randomization)**
  - New pawn diary records now start with a random `DiaryPersonaDef` instead of always using the
    configured default persona. Existing saves and existing pawn records keep their saved persona.

- **2026-06-15 (subtle diary model tag)**
  - Diary cards now show the generating model id as a tiny, low-contrast note at the bottom-right
    of each generated entry, making multi-model provenance visible without drawing attention.
  - Increased the model tag's line box so the tiny text is not clipped by RimWorld's UI font metrics.

- **2026-06-15 (automatic failover to the next model)**
  - On an error, a request now **falls over to the next configured API/model** instead of failing.
    `SendWithRetries` was restructured to try the chosen lane first, then each other lane in order;
    a lane that hits a permanent error, exhausts its transient retries, or times out hands off to
    the next. A failure is only reported when **every** lane fails. Per-lane logic was extracted into
    `TryLane`; candidate order is built by `BuildAttemptTargets` (LlmClient) / `BuildFailoverTargets`
    (DiaryGameComponent).
  - The deadline (`timeoutSeconds`) is now **per lane attempt** (worst-case total ≈ lanes × timeout).
  - `LlmGenerationResult` now reports the lane that actually produced the text (`endpointUrl`,
    `modelName`); `ApplyLlmResult` updates the recorded LLM meta so the debug block and **paired
    recipient pinning** follow the lane that really worked, not just the originally chosen one.
  - Single-API users are unaffected (no other lanes ⇒ no failover; behavior identical to before).

- **2026-06-15 (parallel multi-API generation)**
  - Settings now hold a **list of API lanes** (`apiEndpoints`, `List<ApiEndpointConfig>`) instead of
    a single endpoint/key/model. Each lane is one endpoint + key + **one** model; add more rows for
    more models. Old single-endpoint saves migrate into one row automatically (legacy
    `endpointUrl`/`apiKey`/`modelName` fields are kept only to seed it).
  - Diary requests are **distributed across lanes round-robin** and run in parallel. `LlmClient`'s
    single global concurrency gate became **one gate per lane** (keyed by endpoint+model), each
    sized to `maxConcurrentRequests`; lanes run independently. Added `LlmClient.NextRoundRobinIndex`.
  - **Sequential pinning:** in paired dual-POV mode the recipient reuses the **same lane the
    initiator used** (matched by the endpoint+model recorded on the event), so a sequential pair
    stays on one model. Target selection lives in `DiaryGameComponent.SelectApiTarget`.
  - Rebuilt the settings UI as an **array editor** with **+ Add API** / **− Remove** / **Reset**
    buttons and per-row "Fetch models" + "Pick"; the per-concurrency slider is now labelled per-API.
  - Added keys `PawnDiary.Settings.ApisHeader/ApiLabel/AddApi/RemoveApi` and
    `PawnDiary.Error.NoApiConfigured`; updated the max-concurrent help text.
- **2026-06-15 (diary review fixes)**
  - Fixed diary entry text clipping: `EntryHeight` measured wrapped text at `width - 16f` but the
    tab drew it at `width - 20f`, so long entries overflowed their card. Both now use `width - 20f`.
  - Restored the per-pawn persona picker to the Diary tab (it had been hidden during the UI
    rework, leaving `PersonaFor`/`SetPersona` unreachable). `ControlsHeight` grew to fit it.
  - Capacity keywords now honor the tunable `lowCapacityThreshold` again instead of a hardcoded
    `0.80f`; the def default (and `DiaryTuningDef.xml`) is set to `0.80` so behavior is unchanged.
  - Replaced `IsCombatRelated`'s `defName` substring sniffing with a data-driven `combat` bool on
    `DiaryInteractionGroupDef` (set `true` on the `insults` group in XML); mirrors the `important` flag.
  - Cached the roleplay `GUIStyle`s instead of allocating one per line every frame, and measure
    each entry's height once per frame (reused by the height sum, scroll jump, and draw loop).
  - Fixed malformed `LinkedEntryView` XML doc comments (missing `<summary>` open tags).

- **2026-06-15 (revised diary context)**
  - Removed `traits=` from pawn summary to reduce token usage and focus on more impactful context.
  - Changed `top_thoughts=` to `thoughts=`: now sends exactly one positive and one negative
    thought, selected via weighted random (stronger effects are more likely to be chosen).
  - Changed `low_capacities=` to only show Moving, Talking, Sight, and Hearing when below 80%,
    using localized keyword labels (e.g. "impaired movement") instead of percentages.
  - Added `my last opener (not repeat):` field — the first sentence of the pawn's most recent
    diary entry, included as a hint to avoid repetitive openings.
  - Added `burning passion:` field — a randomly selected passion (major 3x weight, minor 1x
    weight) from the pawn's skills. Only included for important events (not small talk, chit
    chat, etc.). Major passions show as `"Mining (burning)"`, minor as `"Plants"`.
  - Added `IsImportant()` method to `DiaryEvent` to check if the event's group is important.
  - Added `RandomBurningPassion()` and `LatestDiaryOpener()` methods to `DiaryContextBuilder`.
  - Added `initiatorLastOpener`, `recipientLastOpener`, `initiatorBurningPassion`,
    `recipientBurningPassion` fields to `DiaryEvent` with save/load support.
  - Added capacity keyword localization keys: `PawnDiary.Capacity.Moving`,
    `PawnDiary.Capacity.Talking`, `PawnDiary.Capacity.Sight`, `PawnDiary.Capacity.Hearing`.

- **2026-06-15 (linked diary entries between pawns)**
  - Added `LinkedEntryView` model: a compact, truncated preview of the other pawn's diary
    entry for the same interaction event, attached to each `DiaryEntryView`.
  - In the initiator's diary entry, the recipient's preview card appears **after** the main text.
  - In the recipient's diary entry, the initiator's preview card appears **before** the main text.
  - Clicking a linked-entry card selects the other pawn, opens their Diary tab, and scrolls to
    the same event — mirroring the existing Social-tab click-through pattern.
  - `DiaryEvent.RoleEquals` is now public so the UI tab can compare roles.
  - Added `DiaryGameComponent.FindEventById` as a public accessor for the private `FindEvent`.
  - Added localization keys: `LinkedInitiator`, `LinkedRecipient`, `LinkedNotGenerated`,
    `LinkedNoText`, `LinkedTooltip`.

- **2026-06-15 (social-adjacent diary tab and click-through)**
  - Moved the Diary inspector tab to immediately after the vanilla Social tab.
  - Added PlayLog id tracking on diary events, including batched small-talk rows.
  - Clicking a Social-tab interaction row now opens the Diary tab and scrolls to the matching
    generated entry when one exists; otherwise vanilla click behavior continues.

- **2026-06-15 (diary tab roleplay UI pass)**
  - Moved the Diary inspector tab from the far-right position to immediately after the vanilla
    Health tab.
  - Added roleplay-log rendering for generated entries: narration is muted/italic, and
    dialogue-looking lines are bold/italic with the pawn's favorite color.
  - Added XML-backed `DiaryInteractionGroupDef.important` display metadata and quiet/important
    entry markers in the Diary tab.

- **2026-06-15 (safe social-memory summaries)**
  - Changed `BuildSocialThoughtsSummary` to aggregate only stored social memories instead of
    calling RimWorld's situational social thought recalculation. This keeps animal interactions
    such as Nuzzle from logging vanilla `ThoughtWorker_Joyous` null-reference errors while diary
    prompt context is being built.

- **2026-06-15 (diary tab production UI pass)**
  - Moved the Diary inspector tab to the far-right/final pawn tab position.
  - Changed the Diary tab to show only finished generated entries; pending/raw/debug rows are hidden.
  - Temporarily hid the pawn-tab persona selector/rule text, leaving only the per-pawn generation
    checkbox visible.
  - Added a compact generating badge and switched entry cards to built-in RimWorld `Widgets`
    chrome (`DrawMenuSection`, `DrawTitleBG`, `DrawHighlightIfMouseover`, `LabelFit`).

- **2026-06-15 (docs restructuring)**
  - Extracted this changelog out of `DOCUMENTATION.md` into this file. The design doc now points
    here; keep updating it the same way — a dated entry per change, newest first.
  - Folded `CSHARP-NOTES.md` (the C#/RimWorld→JS/TS primer) into `AGENTS.md` and removed the
    standalone file. The inline `// see CSHARP-NOTES.md` comments now point to `AGENTS.md`.
  - Added two standing rules to `AGENTS.md`: keep new UI/prompt text localization-friendly
    (Keyed `.Translate()`; see `DOCUMENTATION.md §12`), and comment new code extensively for AI
    agents and novice devs.

- **2026-06-15 (localization-friendly strings)**
  - Moved all UI strings (settings window, diary tab, entry status) and all natural-language
    prompt text (event fallback sentences, surroundings/relationship/health words, and the
    mood/beauty/pain/opinion buckets) into `Languages/English/Keyed/PawnDiary.xml`, resolved via
    `.Translate()`. See `DOCUMENTATION.md §12`.
  - Structured prompt field keys (`pov:`, `setting:`, `sex=`, …), the `initiator`/`recipient`
    role words, and the `none`/`n/a`/`unknown` skip-sentinels stay English as a stable schema;
    the `LLM debug` block and background-thread `LlmClient` errors stay English too.
  - No behavior change for English players; a translator can now localize **both** the UI and the
    LLM prompt by translating one Keyed file (plus DefInjected for the persona/group/prompt Defs).

- **2026-06-15 (agent guidance doc rename)**
  - Replaced `CLAUDE.md` with `AGENTS.md` so shared repository agent rules are tool-agnostic.
  - Updated the file-map docs to reference `AGENTS.md`.

- **2026-06-14 (documentation parity fixes)**
  - Corrected the `dualPovGeneration` settings description to match implementation:
    disabling paired mode queues both POV requests independently (not lazily).
  - Corrected persistence docs: pending entries regenerate through the background
    generation scan, not on diary-tab view.

- **2026-06-14 (background-only generation)**
  - All LLM diary generation is now driven by a background tick scan, never by UI actions.
    `EntriesFor` (the diary tab) is a pure read with no side effects.
  - `GameComponentTick` runs `QueueAllPendingGenerations` every ~2 seconds (120 ticks),
    scanning for `not_generated` events and queueing generation where enabled.
  - Generation is also queued immediately on game load and when a pawn's generation is
    re-enabled via the settings checkbox.
  - Single-POV mode now queues both initiator and recipient at record time instead of
    lazily queueing the recipient on tab-open.
  - Small-talk batches no longer flush on diary tab open; they flush purely on timer,
    max-events cap, and save. The doc previously claimed a tab-open flush that was never
    implemented.
  - Removed `FlushSmallTalkBatchesForPawn` (only caller was `EntriesFor`).

- **2026-06-14 (colonist-only diary eligibility)**
  - Diary entries are now generated **only for free colonists** (`pawn.IsColonist`).
    Animals, prisoners, slaves, enemies, and visitors never receive diary entries.
  - Mixed interactions (one colonist + one ineligible pawn) produce a **solo** event for
    the colonist only, preventing the blocking issue where an ineligible initiator (e.g.
    animal Nuzzle) would queue a POV that the colonist recipient's entry depends on in
    sequential mode.
  - Interactions between two ineligible pawns are silently skipped.
  - `RecordMentalState` now requires the breaking pawn to be an eligible colonist; social
    fights between a colonist and a non-colonist produce a solo entry for the colonist.
  - `IsDiaryEligible(pawn)` (humanlike + colonist) replaces the old `IsHumanlike` checks
    throughout recording and event-ref creation.
  - `ITab_Pawn_Diary.IsVisible` now also requires `pawn.IsColonist`, hiding the tab for
    non-colonist humanlikes.
  - Public API guards: `DiaryGenerationEnabledFor`, `SetDiaryGenerationEnabled`,
    `PersonaFor`, `SetPersona` all return early for non-colonist pawns.

- **2026-06-14 (in-game persona layer + per-pawn generation toggle)**
  - Added `DiaryPersonaDef` + `1.6/Defs/DiaryPersonaDefs.xml`, copying the 12 prompt-lab writing
    personas into RimWorld-loaded XML Defs.
  - Added `DiaryPromptDef.defaultPersonaDefName` so the default pawn persona can be changed in XML.
  - Added per-pawn saved fields on `PawnDiaryRecord`: `personaDefName` and
    `diaryGenerationEnabled` (default enabled).
  - Added controls at the top of the pawn Diary inspector tab: persona selector and a checkbox to
    pause LLM generation for that pawn without losing raw recorded events.
  - Prompt builders now include the selected persona as a `persona:` line. Paired sequential
    generation respects disabled pawns and lets an enabled recipient generate without hidden
    initiator context if the initiator is disabled.

- **2026-06-14 (small-talk batching)**
  - Added per-pawn-pair batching for the `smalltalk` interaction group (`Chitchat`,
    `Conversation`, `HangOut`, `OfferFood`, etc.). A batch flushes after a quiet window, at a
    max event count, when either pawn's diary opens, or before save.
  - Added XML-backed tuning knobs: `smallTalkBatchWindowTicks` and
    `smallTalkBatchMaxEvents`.

- **2026-06-14 (paired sequential POV generation)**
  - Replaced live dual-POV generation with a paired sequential flow: initiator request first,
    recipient request second after the initiator result is applied.
  - Rewrote pairwise prompts to request one diary entry at a time. Recipient prompts now include
    the generated initiator entry as hidden continuity context.
  - Updated the settings label/help and prompt Def defaults to match the new behavior. Legacy
    dual-marker parsing remains dormant for compatibility.

- **2026-06-14 (DiaryPromptDef: single XML source of truth for markers/prompts)**
  - Added `DiaryPromptDef` (`Source/Defs/DiaryPromptDef.cs`) + `1.6/Defs/DiaryPromptDef.xml`
    holding `dualInstruction`, `initiatorMarker`, `recipientMarker`, and the `systemPrompt`
    default. Read via `DiaryPrompts.Current` with safe-code-default fallback (same pattern as
    `DiaryTuning`).
  - The old dual prompt/parser path read its markers/instruction from `DiaryPrompts.Current`
    instead of three hardcoded copies.
  - `PawnDiarySettings.DefaultSystemPrompt` is now a property reading from
    `DiaryPrompts.Current.systemPrompt`. Existing saves keep their stored system prompt; edit
    the XML and click "Restore default" in-game to adopt the new default.

- **2026-06-14 (maintainability refactor: rename, split, XML Defs, docs)**
  - **Renamed the project files** `ClassLibrary1.csproj`/`.slnx` → `PawnDiary.*` (the code,
    namespace, assembly, and output DLL were already `PawnDiary`). The mod folder is unchanged
    (RimWorld identifies the mod by `packageId`, not folder name).
  - **Split the 1968-line `DiaryGameComponent.cs`** into focused files with no behavior change:
    `DiaryEvent.cs` + `PawnDiaryRecord.cs` (data models), `DiaryContextBuilder.cs` (context
    summaries + formatting/bucket helpers, now `static`), and `DiaryPromptBuilder.cs` (prompt
    assembly). The component is now a ~570-line orchestrator. Removed the dead `BuildXmlSummary`.
  - **Moved the interaction-group catalog to XML Defs.** `InteractionGroup` is now
    `DiaryInteractionGroupDef : Def`; the 16 groups live in `1.6/Defs/DiaryInteractionGroupDefs.xml`
    and load via `DefDatabase`. Group `defName`s equal the old keys, so saved per-group settings
    still apply. Add/retune groups by editing XML — no recompile.
  - **Moved tuning magic-numbers to a `DiaryTuningDef`** (`1.6/Defs/DiaryTuningDef.xml`, read via
    `DiaryTuning.Current` with a safe-defaults fallback): dedup windows, scan radius/temperature,
    capacity/health thresholds, diary-line length, and the beauty/mood/pain/opinion buckets.
  - **Documentation pass for non-C# maintainers:** added `CSHARP-NOTES.md` (a JS/TS-oriented primer
    on Defs, `IExposable`, Harmony, `ref`/`out`, `async`, LINQ, …), a plain-English header comment on
    every `.cs` file, and XML `///` docs on the main public types/methods.
  - **Organized the repo to RimWorld convention:** moved all C# + `PawnDiary.csproj`/`.slnx` +
    `Properties/` + `Libs/` into a `Source/` folder (the game ignores it), grouped by concern
    (`Core/`, `Models/`, `Generation/`, `Defs/`, `Patches/`, `UI/`, `Settings/`, `Util/`). The root
    now holds only `About/`, `1.6/`, `Languages/`, `Source/`, `prompt-lab/`, and docs. The `.csproj`
    now uses a recursive `**\*.cs` glob (new files auto-included); build is
    `MSBuild Source\PawnDiary.csproj` (output unchanged: `1.6/Assemblies/PawnDiary.dll`).

- **2026-06-13 (persona writing styles)**
  - Added `prompt-lab/personas.txt` — a catalog of 12 writing-style personas (stoic-survivor,
    fiery-hothead, melancholy-dreamer, earnest-optimist, cynical-realist, anxious-worrier,
    gentle-caretaker, grim-veteran, whimsical-eccentric, noble-idealist, bitter-loner,
    eager-socialite) with short voice descriptions.
  - Persona is now a **separate line** in fixtures (`initiator persona: random`,
    `recipient persona: random`, `you persona: random`) — the catalog stays out of the prompt.
    `run.js` resolves `random` to a randomly picked persona description at runtime.
  - Replaced `traits:` field with the persona system in all 6 prompt-lab fixtures.
  - Updated `_system.txt`: the model is now told to match the pawn's writing persona — their
    voice, sentence rhythm, and emotional register — rather than raw trait names.

- **2026-06-13 (prompt-lab)**
  - Added `prompt-lab/` — a dependency-free Node harness to iterate on prompts outside the
    game. Hand-authored fixtures (one per event type: dual interaction, social fight, solo
    mental break, single POV) mirror the mod's compact format using realistic def labels;
    `run.js` fires them at the configured endpoint and prints the response (+ parsed dual
    split). Shared system prompt in `prompts/_system.txt`.

- **2026-06-13 (lean prompt context)**
  - Reworked prompts to send **signal only**. New `AppendField` skips empty/placeholder
    fields; all three prompt builders (single, dual, solo) were rewritten compactly and now
    omit raw logs, `shared_event`, `sequence`, `game`/worker details, and `opinions`.
  - **Conditional context:** weather/biome only outdoors; temperature only when cold/hot;
    beauty only when notable; health omitted when healthy.
  - **Extended relationship (memory layer):** relationship now reads as relation kind +
    `opinion ±N (bucket)` + the social memories driving it (`BuildSocialThoughtsSummary`
    via `ThoughtHandler.GetSocialThoughts`) + the pawn's last diary line about the other.
  - Removed now-dead `BuildSharedEventSummary` and `FormatBeauty`.

- **2026-06-13 (social fights & mental breaks)**
  - Added capture of **mental states** via a `MentalStateHandler.TryStartMentalState`
    postfix (`MentalStateStartPatch`). `SocialFighting` → pairwise diary event (both POVs,
    mirrored-call de-dup); all other mental breaks → **solo** events from the breaking pawn,
    naming any target.
  - Added `DiaryEvent.solo` (single-POV, `BuildSoloPrompt`); `CanQueueDual` returns false for
    solo. Extracted `AddPairwiseEvent` (shared by interactions + social fights) and
    `AddSoloEvent`.
  - Interaction groups gained a **domain** (Interaction / MentalState) with domain-scoped
    classification; added **Social fights** and **Mental breaks** groups (both default on),
    shown under a "Mental states & breaks" header in settings.
  - Resolved the prior caveat: social fights ARE now captured (via the mental-state path, not
    `PlayLogEntry_Interaction`).

- **2026-06-13 (interaction groups)**
  - Consolidated the significance filter and the per-interaction prompt instructions into
    **interaction groups** (`InteractionGroups.cs`). Each group has one enable toggle + one
    diary prompt, edited together in settings. Recording now = "is this interaction's group
    enabled"; the prompt = the group's instruction.
  - Squashed the long per-def list into ~14 themed groups (Romance, Recruitment, Conversion,
    Rituals, Animal, NSFW, Teaching, Small talk, …). Defaults: most on; Teaching, Small talk,
    and Other off.
  - Removed `InteractionPromptTemplates.cs`, `Defs/InteractionPromptTemplates.xml`, and the
    old `SignificantInteractionNames`/`Tokens` sets. Settings now store per-group
    enable/instruction maps (old `interactionInstructions` is dropped; instruction overrides
    from older saves are not migrated).

- **2026-06-13**
  - Fixed runtime crash: replaced `JavaScriptSerializer` with `MiniJson` (Mono lacks
    `System.Web.Extensions`). Generation works again.
  - Added **dual-POV generation** (both entries in one request) behind `dualPovGeneration`
    (default on); kept the single-POV path as a disabled fallback.
  - Added a **system prompt** (configurable, sent as a `system` message).
  - Reworked `LlmClient` into a **queue + concurrency gate** (`maxConcurrentRequests`,
    default 4, range 1–16); overflow waits instead of failing. Added a **per-request hard
    deadline** that purges stuck requests, plus stale-session purge.
  - **Removed event batching** — each significant interaction is now its own diary entry
    (no buffering/merging of beats).
  - **UI rework:** moved the diary from a gizmo + window to an **inspector tab next to the
    Log tab** (`ITab_Pawn_Diary`); **removed the colony/neutral events view**.
  - Added this documentation file.
