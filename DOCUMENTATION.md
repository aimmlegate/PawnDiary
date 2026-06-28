# Pawn Diary - Maintainer Guide

Last updated: 2026-06-28

Related files:

- `AGENTS.md`: detailed rules for code agents and deep architecture constraints.
- `EVENT_PROMPT_MAP.md`: event-to-prompt coverage map.
- `CHANGELOG.md`: milestone history.

## 1. Purpose

Pawn Diary records meaningful RimWorld colony moments and turns them into short diary pages through
configured OpenAI-compatible API lanes. RimWorld loads the compiled DLL at startup through Harmony
patches, Defs, a `GameComponent`, and an inspector tab. There is no `main()`.

Diary pages belong only to free humanlike colonists old enough for first-person writing
(`DiaryTuningDef.minimumFirstPersonAgeYears`, default 13). Animals, prisoners, slaves, enemies,
visitors, non-colonists, and underage colonists do not own pages. If only one participant is eligible,
the event becomes a solo entry. If two eligible colonists are involved, the initiator entry is
generated first and the recipient entry gets hidden continuity from it.

Arrivals and deaths are neutral boundary pages. Arrival pages introduce a pawn's diary; death pages
end it and hide later same-tick events for that pawn.

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, `Languages/`, and the compiled DLL in
`1.6/Assemblies/PawnDiary.dll`. Source and tests are kept in the repo for development.

| Path | Role |
|---|---|
| `About/` | Mod metadata, preview, icon, dependency declaration. |
| `1.6/Defs/` | XML-owned policy: event groups, tuning, prompts, styles, UI, text effects. |
| `Languages/` | Keyed and DefInjected English text plus optional translation sources. |
| `Source/Capture/` | Pure Event Catalog payloads and decisions. |
| `Source/Core/` | `DiaryGameComponent` partials: capture adapters, save/load, scans, generation queue. |
| `Source/Generation/` | Runtime context builders, prompt adapters, LLM client, DLC-safe live reads. |
| `Source/Pipeline/` | Pure prompt planning, request JSON, response cleanup, text decoration, API policy. |
| `Source/Patches/` | Harmony startup, domain hooks, inspect-tab/command patches. |
| `Source/Settings/` | Saved settings, API lane UI/controller, Prompt Studio, Writing Style Studio. |
| `Source/UI/` | Diary inspect tab, card rendering, paging, formatting. |
| `tests/` | Standalone pure-helper test projects. |
| `prompt-lab/` | Prompt fixture and variant validation harness. |
| `scripts/publish.ps1` | Local Workshop payload prep. |

## 3. Runtime Flow

1. A Harmony hook or scanner notices a candidate event.
2. The adapter snapshots live RimWorld facts, settings/XML gates, dedup state, and any random roll.
3. The pure Event Catalog decides whether to drop, create a solo/pair/neutral entry, batch, or route
   to day reflection.
4. `AddSoloEvent` or `AddPairwiseEvent` creates a saved `DiaryEvent` and indexes it on eligible
   pawn records. The active-event retention cap then drops oldest events beyond the configured
   limit and scrubs pawn references to them.
5. Generation queues immediately when possible; periodic scans retry pending or orphaned work.
6. `DiaryPipelineAdapters` copies runtime/XML/localized state into pure pipeline DTOs.
7. Pure helpers assemble prompts, serialize request JSON, parse provider responses, and clean text.
8. `LlmClient` sends requests and returns results to the main thread; successful main pages may queue
   a title follow-up.

`GameComponentTick` runs game-time scans continuously, but the expensive active-event generation
rescan is demand-driven: load catch-up, delayed raid entries, and recovered orphaned work request a
pass, then the catch-up pass runs at most every 200 ticks until it is no longer needed. Those
generation/title/orphan scans use the XML-tuned hot window (`activeScanEventWindow`, default 1000,
a global count across all pawns); older events stay saved and visible as archive history but are not
retried or title-backfilled.
Orphaned "writing..." recovery is a separate, slower hot-window pass every 600 ticks. Completed LLM
results and debug logs are drained from both `GameComponentTick` and `GameComponentUpdate`, so
requests that were already queued can finish and apply while the game is paused. Pending generation
is not saved; it resets on load and is requeued when the event is still inside the hot window.
First-person generation is skipped for pawns below the XML Consciousness floor, while neutral
arrival/death pages bypass that guard.

## 4. Event Sources

| Source | How it is observed | Result |
|---|---|---|
| Social interactions | `PlayLog.Add` | Pair, solo, batched, or ambient note by XML group. |
| Mental states | `MentalStateHandler.TryStartMentalState` | Social fighting can be pairwise; other breaks are solo. |
| Romance | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise lover/spouse/ex relation moments. |
| Tales and combat | `TaleRecorder.RecordTale` | Solo, pair, delayed combat batches, or death description. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first page. |
| Deaths | `Pawn.Kill` plus XML death TaleDefs | Neutral final page. |
| Mood events | `GameConditionManager.RegisterCondition` | One entry per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | XML-filtered memory entries; ambient thoughts can batch. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, chemical, and similar worsening stages. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo inspiration entry. |
| Hediffs | `Pawn_HealthTracker.AddHediff` and scan | Immediate or day-reflection health entries by XML policy, including string-matched Anomaly mental afflictions. |
| Work | Periodic current-job sampling | Non-social, non-violent work, controlled by XML odds/cooldowns. |
| Raids and infestations | `IncidentWorker.TryExecute` | Fan-out to eligible colonists; ordinary raids can delay generation. |
| Quests | `Quest.Accept`, `Quest.End`, defensive UI/state scan | Accepted, completed, and failed quest entries; prompt labels reject placeholder names and humanize code-like quest defNames. |
| Event windows | `IncidentWorker.TryExecute`, `Quest` lifecycle, `Thing.SpawnSetup`, `SignalAction_Letter`, `CompProximityLetter`, `Building_VoidMonolith.Activate`, `Pawn_AgeTracker.BirthdayBiological`, `Pawn_HealthTracker.AddHediff`, `PrisonBreakUtility.StartPrisonBreak` | XML starts/ends narrative windows or one-shot events, writes phase entries, and can bias prompts while active. |
| Rituals | Ideology and psychic ritual completion hooks | Fan-out by role/perspective when DLC content is active. |
| Abilities | `Ability.Activate` overloads | Cooldown-weighted caster entry. |
| Day reflections | Sleep/rest trigger | One reflective page per pawn/day when important signals exist. |

Hooks are grouped by domain under `Source/Patches/`. Fragile reflection targets register through
`DiaryPatchRegistrar` so missing methods warn and no-op instead of breaking startup. Capture hooks,
per-tick work, save/load bookkeeping, startup registration, and vanilla UI overlays isolate failures
with one-time logging and preserve vanilla behavior.

## 5. XML Policy

Most feature tuning lives in XML so changes do not require recompiling:

- `DiaryInteractionGroupDefs.xml`: event classification, instructions, tones, color cues, batching,
  hediff modes, package-aware compatibility routing, and default enablement.
- `DiaryEventWindowDefs.xml`: generic start/end/timeout windows or one-shot events over incident,
  quest, spawned thing, letter, proximity-letter, and special story-object signals, including phase
  diary text and active prompt weighting.
- `DiaryEventPromptDefs.xml`: event prompt text, event enhancement text, and optional forced model.
- `DiaryPromptTemplateDefs.xml`: which structured fields each prompt shape renders.
- `DiaryPromptDef.xml`: shared system/final instructions.
- `DiaryPersonaDefs.xml`: built-in writing styles.
- `DiaryHediffPersonaOverrideDefs.xml`: active hediffs that temporarily force a writing style.
- `DiaryPromptEnchantmentDefs.xml` and `DiaryHumorCueDefs.xml`: optional live-context and subtle
  humor cues.
- `DiarySignalPolicyDefs.xml` and `DiaryTuningDef.xml`: scan intervals, odds, cooldowns, thresholds,
  day-reflection policy, and shared fallback tuning.
- `DiaryUiStyleDef.xml` and `DiaryTextDecorationDefs.xml`: Diary UI dimensions/colors and display
  rich-text decoration.

Interaction groups match by domain, exact `defName`, substring token, and optional source package
ID. Event prompt policy resolves from most specific to broadest key: source defName, interaction
group, classifier key, then domain. Prompt text, enhancement text, and forced-model text resolve
independently so narrow XML can override one field and inherit the others.
Mood-impact fallback lists in `DiaryTuningDef.xml` classify GameCondition defNames that are always
positive or negative when no live thought offset is measurable; Anomaly `GrayPall` and `DeathPall`
are negative string matches there. `UnnaturalDarkness` is routed through the mixed MoodEvent group
instead because its Anomaly ThoughtDef can be negative or positive depending on the pawn.

For optional DLC or mod content, prefer string matchers in XML. Do not hard-reference DLC defs in C#
or XML unless they are guarded; absent DLC content should simply never match.
The Anomaly Hediff group follows this pattern: `RevenantHypnosis`, `CubeInterest`,
`CubeWithdrawal`, `CubeRage`, `CorpseTorment`, and `Inhumanized` are exact string matches that create
immediate Hediff diary entries on add and on configured severity-step progression when that DLC
content is present. The same conditions also have prompt-enchantment cues, so any ordinary
first-person prompt they win adds an `important context:` line such as `high priority; moderate cube
withdrawal; compulsive absence, restless need; condition detail: ...`. `descriptionOverrideKey` can
replace the standard game description per Def; otherwise the localized `HediffDef.description` is
cleaned and capped before it reaches the model. If a hediff also wins a temporary writing-style
override, its matching prompt-enchantment candidate is suppressed so the localized condition text
does not repeat as both style and context; unrelated hediff/status candidates remain eligible.
`DiaryHediffPersonaOverrideDefs.xml` can also temporarily force the prompt POV pawn's writing style
from active hediffs; `Inhumanized` currently
uses this to force the `DiaryPersona_InhumanizedVoid` dark void style while that hediff is active.
Base-game `Alzheimers`, base-game `Dementia`, and Anomaly `CrumblingMind` stay regular
memory-decay prompt enchantments rather than writing-style overrides, while Anomaly `CrumbledMind`
uses the stronger `DiaryPersona_CrumbledMindCollapse` mind-crumbled style. Base-game `Joywire` forces the
`DiaryPersona_JoywireHaze` bright-fog style, and Anomaly `BlissLobotomy` uses the stronger
`DiaryPersona_BlissLobotomyHaze` blank-bliss style. Royalty `Mindscrew` forces the
`DiaryPersona_MindscrewPain` pain-needle style when that optional DLC hediff is present. Base-game
`TraumaSavant` has the high-priority `DiaryPersona_TraumaSavantSilent` style, which forbids dialogue
or `[[speech]]` blocks because the pawn cannot speak. Base-game drug highs use the same
prompt-enchantment override hook for `AlcoholHigh`, `Hangover`,
`AmbrosiaHigh`, `GoJuiceHigh`, `LuciferiumHigh`, `FlakeHigh`, `PsychiteTeaHigh`, `YayoHigh`, and
`SmokeleafHigh`, with XML cue keys grounded in the vanilla Hediff and Thought defs.

Event windows are the generic system for ongoing threats, one-shot warnings, or story beats that
are not hediffs.
`DiaryEventWindowDef` rows define `startSignals`, `endSignals`, `timeoutTicks`, phase diary text,
and active prompt policy. Current signal sources are `Incident/executed`, `Quest/accepted`,
`Quest/completed`, `Quest/failed`, `ThingSpawned/spawned`, `Letter/received`,
`ProximityLetter/received`, `VoidMonolith/activated`, `PawnAge/birthday`, `Hediff/added`, and
`PrisonBreak/started`; matchers are exact defName strings or broad tokens. `keepActive=false` makes
the start signal a one-shot diary event without saving an active window. `recordScope=SubjectPawn`
records only the pawn carried by the signal, used by vanilla letter triggers such as ancient danger,
proximity-letter triggers such as void monolith discovery, completed monolith activations, birthdays,
and target health events. An active window is saved, can write
start/end/timeout diary entries, and can add a weighted prompt candidate while multiplying ordinary
prompt enchantments down. Setting
`normalPromptWeightMultiplier` to `0` makes the window fully override ordinary health/status prompt
enchantments until it ends or times out. The built-in `MetalhorrorSuspicion` window starts from
`GrayFleshSample` or `Filth_GrayFleshNoticeable`, ends on `Metalhorror`, and times out after ten
RimWorld days if the emergence never arrives. The built-in `AncientDanger` rule matches vanilla's
`AncientShrineWarning` letter key and records a single entry for the approaching pawn only. The
built-in `VoidMonolithDiscovery` rule matches the Anomaly `VoidMonolith` proximity letter and
records a single extreme-dark discovery entry for the nearby pawn only. The built-in
`VoidMonolithActivation` rule matches completed void monolith activations, uses the reached
`MonolithLevelDef` as its signal defName for future XML splits, and records one extreme-dark entry
for the activating pawn. `Birthday` records a target-only uneasy aging entry from the direct
`BirthdayBiological` hook instead of inferring the moment from age-related hediffs. `HeartAttack`
records a target-only danger entry from `Hediff/added` with defName `HeartAttack`; vanilla pawn heart
attacks do not emit their own letter or message, while Anomaly's `MessageHeartAttack` belongs to the
fleshmass heart and is unrelated. `PrisonBreak` records one danger entry for every eligible colonist
on the affected map from the shared prison-break utility overload used by both natural and sparked
breakouts.

On hot signal paths (every spawned thing, every added hediff) the recorder first runs a cheap,
allocation-free pre-filter — `EventWindowPolicy.CouldMatchByDefName` against each def's cached
trigger rules — and only resolves the signal label when some window could actually match. Optional
event-window recording is also isolated from the established raid/hediff/quest capture, so a window
failure can never suppress the diary entry those hooks already write.

## 6. Prompts And Writing Styles

Prompts are compact `key: value` lines. Empty values and `none`/`n/a`/`unknown` sentinels are dropped.
Prompt templates cover pair, solo, batched, day-reflection, neutral death, neutral arrival, and title
requests.
Quest prompts keep the raw `quest=` defName only in saved context for UI/domain classification; the
model-facing fields use `quest_label`, `quest_signal`, `quest_faction`, and `quest_rewards`. The
label path rejects placeholder `QuestName`, humanizes PascalCase/underscore fallbacks, removes the
standalone word `Quest`, and the Quest event enhancement tells the model not to copy the quest name
verbatim into the diary line.

System prompts stay short and general. Event-specific guidance comes from `DiaryEventPromptDef` and
per-group instructions/tones. Groups can define instruction/tone variant pools; instructions roll once
at capture and are saved, while tones are deterministic by event id.

Prompt Studio edits shared system prompts and event prompt/enhancement/forced-model overrides.
Writing-style presets are saved settings backed by `DiaryPersonaDef`; the code still uses "persona"
in some field names for save compatibility, but the player-facing feature is writing style.
Active hediffs can temporarily override the saved style through `DiaryHediffPersonaOverrideDef`
rules. These overrides are prompt-time only: they do not change the saved style picker value, and a
missing/off-map pawn falls back to its saved style. Memory-decay hediffs such as `Alzheimers`,
`Dementia`, and `CrumblingMind` deliberately remain prompt enchantments instead of automatic style
overrides.

Prompt enchantments add one weighted live-context pressure cue to eligible first-person prompts.
When a hediff-driven writing-style override is active, the hediff sources that selected that override
are removed from the normal prompt-enchantment pool to avoid duplicating the same condition in the
system style block and the `important context:` line.
Imported game/mod prompt text such as live hediff descriptions, hediff/capacity labels, DLC title or
role labels, scenario descriptions, and quest descriptions is flattened to one prompt line and capped
to its first two sentences before being sent to the model. Pawn Diary's own XML/Keyed prompt
instructions, writing styles, humor cues, and field labels are not sentence-capped by this guard.
Active event windows feed the same prompt-enchantment planner as extra XML-weighted candidates, so
an unresolved colony threat can shape unrelated diary pages until the configured end signal or
timeout closes it. Dev prompt-suite fixtures opt out of live prompt enchantments, including active
event-window candidates, so captured test prompts stay isolated from earlier manual event-window
tests. Humor cues are hidden, XML-weighted, and folded into the writing-style block.
Direct speech is allowed only in selected first-person interaction prompts with a closed
`[[speech]]...[[/speech]]` block.

Generated Social-log speech injection remains disabled/hidden. The saved setting exists for
compatibility, but the call site is off. Title generation is enabled by default. Successful main
entries queue their own title follow-up immediately; the full missing-title sweep runs only after
load or when settings are saved, not on every generation rescan. Title responses are validated
before they are saved: markup/control/schema characters such as leaked tag tokens are rejected, and
the stored title falls back to the first few words of the finished diary entry with `...` appended.

## 7. Settings And UI

Main settings cover API lanes, routing mode, request tuning, title generation, atmospheric
formatting, prompt enchantments, work/social weights, the active diary-event cap, event filters,
prompt overrides, and writing style presets. Dev mode exposes prompt-test mode, a full diary export
button, and extra diagnostics. The export writes every saved pawn diary page plus the backing event
records to `PawnDiaryExports/` under RimWorld's save-data folder, and copies the generated file path
to the clipboard.

The Diary UI is an inspect tab registered for humanlike pawns and their corpse defs. By default it
appears in the pawn inspect-tab row for eligible colonists and selected colonist corpses. A setting can
instead hide the tab and add a bottom command button that opens the same UI. Programmatic opens from
that command, Social-log links, and linked-entry cards temporarily expose the hidden tab long enough
for RimWorld's inspect-pane opener to resolve it, then clear that state when the tab closes. The
inspect-tab draw path and programmatic open helper also re-apply tab registration once after startup,
covering load orders where RimWorld finalizes resolved tab lists after static constructors. The
command helper is marked with RimWorld's `StaticConstructorOnStartup` because it owns the static Unity
texture cache for the button icon; the icon itself still loads lazily from the main-thread gizmo path
and falls back to the vanilla book icon if the mod texture is missing.

Production UI shows completed pages. Dev mode also shows pending/failure rows, raw prompt/status
data, formatting previews, prompt-suite tools, copy buttons, regeneration controls, and a completed
mock-page filler for stress testing long histories. That filler seeds 6,000 saved pages over 3
in-game years (about 2,000 pages per year) without calling the LLM, and dev-mode retention skips
mock stress histories so autosaves do not immediately shrink the fixture. Histories page by in-game
year; newest cards start expanded. Long histories are kept cheap by the active-event cap,
visible-entry caching, sliced main-thread year indexing, cached virtual row offsets/heights, and
viewport drawing that only emits cards inside the scroll slice plus the XML-tuned overscan buffer
(`virtualizedEntryOverscanHeight`, default 800 pixels above and below the viewport). The sliced indexer
uses `uiHistoryScanMaxEventsPerFrame` and `uiHistoryScanFrameBudgetSeconds` for year indexing,
selected-year card materialization, and selected-year row layout, so opening a pawn with thousands of
pages shows a loading panel instead of freezing the game. The loading panel reports the active load
phase only when no usable cached list exists yet: first open, uncached pawn switch, or opening a year
with no cached cards. The tab keeps a small LRU of loaded pawn views so returning to a recent pawn
restores its visible list instead of rebuilding from zero. Same-pawn index refreshes and
selected-year card refreshes build quietly behind the currently visible list. Once a year is visible,
same-year visual/layout refreshes, including scroll,
highlight refreshes, and collapse/expand, keep the list visible instead of returning to the loading
panel; loaded large years seed the clicked card's current blend so the clicked card can still animate
open or closed. Selected-year rebuilds invalidate row layout defensively so virtualized row offset
arrays cannot be reused against a changed list. The tab indexer does not perform the older
cross-colony arrival-page fallback scan while opening; it scans the selected pawn's saved diary
references once, resumes selected-year loading across frames, skips any bad/stale entry with a
one-time log, then slices the selected year's card and layout work. Inspect-tab and command badges
do not touch saved diary records during pawn
selection; they read a transient per-pawn status cache. The new-page badge is backed by a saved
per-pawn unread flag that is set when main LLM text finishes and cleared when that pawn's Diary tab
opens, while writing dots reuse cached pending counts after the Diary tab finishes its sliced load.
Archived pages use the same cards and controls as hot pages.

`DiaryTextFormat` escapes raw model rich text before applying safe formatting. Display-only text
decorations and pawn-name highlights happen at render time; generated text is not mutated on save.

## 8. API And Reliability

Each enabled endpoint/model/mode/auth row is an API lane. Supported request modes are OpenAI-compatible
Chat Completions and OpenAI Responses. Auth can be bearer, no auth, custom API-key header, or `key=`
query parameter. Logs strip secrets and query strings.

Routing modes are Balanced, Prefer top rows, and Failover only. A `DiaryEventPromptDef.forcedModel`
can try a matching active model first; blank, unknown, disabled, or failed forced lanes fall back to
normal routing. Recipient follow-ups and title requests try to pin to the previous successful lane.

`LlmClient` handles concurrency, per-lane cooldowns, transient retries, timeout/permanent failures,
session cancellation on new game/load, and result handoff to the main thread. `LlmResponseParser`
supports Chat and Responses output shapes, strips reasoning/transcript leaks, cleans malformed speech
markers, and trims saved text locally.

## 9. Save Data And Compatibility

`DiaryGameComponent.ExposeData` saves per-pawn records (`diaries`), the event list (`diaryEvents`),
and active XML event windows (`activeEventWindows`). `DiaryEventRepository` owns the event list and
rebuilds its id lookup index after load. Per-pawn event lists prune blank, duplicate, or dangling
refs. Active event windows normalize after load so renamed/missing defs fail closed instead of
blocking future prompt context.

The diary-history cap is **per pawn** (default 3000, editable from settings, range 1–10000). Each
pawn keeps only its newest configured number of pages: `ApplyActiveEventLimit` caps every pawn's
event-id list to that many refs (oldest dropped first), then sweeps the master list down to the union
of all surviving refs — so a page shared by two pawns survives until both drop it. It runs after load,
before save, after new event creation, and when settings are saved. The common (nothing-over-cap)
path costs one `Count` check per pawn. Because the cap is per pawn, background scan cost no longer
scales with it: maintenance walks the global hot window below, not each pawn's full history. (The
`maxActiveDiaryEvents` field/Scribe key keeps its historical name for save compatibility even though
its meaning is now per pawn.)

Separately, `DiaryTuningDef.activeScanEventWindow` (default 1000, XML only, a global count across all
pawns) defines the newest saved events considered hot for retry, title catch-up, orphan recovery,
day-summary event evidence, work cooldowns, and prompt continuity/opener history. Events older than that window are archive pages:
they remain in save data and render in the Diary UI, but maintenance scans do not revisit them. If an
archived page has an attempted generation with no generated text (pending in-session, or
load-normalized back to `not_generated` with a saved prompt), the UI stops treating it as active
writing: it shows a localized "You see that: ..." fallback from the saved prompt facts/raw event
text, derives a short display-only title from the first few words, and uses the footer note to say the
page failed to generate instead of showing a model name.

`DiaryEvent` saves raw/generated text, statuses/errors, context, source ids, prompts, titles, LLM
metadata, semantic color cue, and compact display facts. Per-role state is stored in initiator,
recipient, and neutral slots but serialized under the historical flat Scribe keys.

Adding Pawn Diary to an existing save is safe; it starts recording future events. Removing it is
gameplay-safe because the mod does not persist custom pawn/map components or gameplay defs onto
vanilla objects. The diary UI/history disappears without the mod, and old generated Social-log rows
from dev builds may remain in the vanilla play log.

## 10. Runtime And DLC Constraints

- Runtime is RimWorld's Unity Mono. Use only assemblies available in `RimWorldWin64_Data/Managed`
  plus the bundled/declared Harmony dependency.
- JSON uses `Source/Util/MiniJson.cs`. Do not add `System.Web.Extensions` or external JSON libraries.
- Harmony is declared in `About/About.xml`; runtime and build copies of `0Harmony.dll` are kept in
  `1.6/Assemblies/` and `Source/Libs/`.
- No paid DLC is required. Optional DLC data must no-op cleanly when absent.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use string matching or
  `GetNamedSilentFail`.

## 11. Localization

Player-facing UI strings and natural-language prompt text use
`Languages/English/Keyed/PawnDiary.xml` through `.Translate()` on the main thread. Def text uses
DefInjected translations.

Keep DefInjected English stubs in sync when editing XML labels, instructions, tones, event prompts,
writing-style rules, or shared prompts. Variant pools use indexed DefInjected keys such as
`<group.instructions.0>`; avoid blank list entries so indices stay aligned. Custom Pawn Diary Def
translation folders use fully qualified C# type names, for example
`Languages/English/DefInjected/PawnDiary.DiaryInteractionGroupDef/`; simple folder names do not
resolve for namespaced custom Defs in RimWorld's language loader. English and Russian currently
mirror eleven localization XML files: `Keyed/PawnDiary.xml` plus ten custom-Def `DefInjected`
folders, including prompt-template field labels and ritual-quality labels that can reach prompts.

Raw English is intentional for internal prompt/context schema tokens (`thought=`, saved context
keys, role ids), sentinel tokens (`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`),
defNames, API model ids, and background-thread `LlmClient` errors. XML-owned prompt-template labels
are localized through `DiaryPromptTemplateDef` DefInjected entries so Russian prompts do not mix
localized guidance with English field names.

The source tree carries a Russian translation under `Languages/Russian (Русский)/` (folder name
matches RimWorld's Core language def), mirroring the English `Keyed` + `DefInjected` layout
key-for-key. Game terms use the official RimWorld Russian glossary; the `DiaryPersonaDef` writing
styles are deliberately *not* literal translations but reconstructions on Russian literary
traditions with synthetic, author-less examples. Russian UI strings should stay compact enough for
RimWorld's narrow settings and tab surfaces, and should avoid unexplained English calques in visible
labels: prefer plain Russian words such as "address", "connection", "distribution", "editor", and
"instruction" over "endpoint", "routing", "studio", or "system prompt" translations. Keep raw
`API`, `OpenAI`, `URL`, `Bearer`, `XML`, and `UTF-8` only where they name a protocol, product,
scheme, format, or file encoding. Russian prompt prose should be idiomatic instructions for the
model rather than literal English calques. Russian strings that contain dynamic pawn, target, work,
or event placeholders should avoid making the placeholder the subject of a gendered or numbered
past-tense verb/adjective unless the code guarantees that agreement. Prefer neutral forms such as
`У {0}: ...`, `{0}: ...`, `Событие у {0}: ...`, `Применение способности {1}: {0}`, or passive and
impersonal constructions. Russian humor cues should likewise use local dry, bureaucratic, and
household understatement patterns instead of translating English deadpan/punchline mechanics. When
adding or renaming an English key, add the matching Russian key in the same file, or the entry
silently falls back to English in a Russian game. Canonical game-term translations are recorded in
`Languages/Russian (Русский)/GLOSSARY.md` (mined from RimWorld's official RU language packs); reuse
it so terminology stays consistent with the base game.

## 12. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

If `MSBuild` is not on `PATH`, use Visual Studio Developer PowerShell or locate it with `vswhere`.
The Debug build writes `1.6/Assemblies/PawnDiary.dll`, which is committed on purpose.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
```

Prompt lab:

```powershell
cd prompt-lab
npm test
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

Live hook checks use a disposable save, dev mode, prompt-test mode, and RimBridge/GABS. Prompt-test
mode intercepts only after a real event reaches `QueuePrompt`; a successful capture logs:

```text
[PawnDiary debug] Captured prompt without generation event=<id> role=<role>
```

Release payloads are prepared with:

```powershell
scripts\publish.ps1
```

The script builds a throwaway Release DLL, copies runnable mod files, runtime textures, `Source/`
without `bin`/`obj`, and reference docs into `dist/<published packageId>`, and installs the payloads
into the detected RimWorld `Mods` folder through junctions by default. Pass `-InstallToMods:$false`
to prepare `dist/` only.

Russian is packaged as a separate Workshop localization mod by default. The script produces the
normal main payload plus `dist/<published packageId>.russian`; the main payload excludes
`Languages/Russian (Русский)/`. The localization payload contains only its own translated Russian
`About/` metadata, `About/Preview-Russian.png` copied as the Workshop `Preview.png`, and the Russian
language folder. It declares a dependency/load-after on the main published packageId, uses packageId
`<published packageId>.russian` unless overridden with `-RussianLocalizationPackageId`, and installs
its own junction next to the main mod junction. Before updating an existing localization Workshop
item, either pass `-RussianLocalizationPublishedFileId <id>` or store that id in
`About/PublishedFileId-Russian.txt`; the script copies it into the localization payload as
`About/PublishedFileId.txt`. Use `-SplitRussianLocalization:$false` or
`-IncludeRussianInMainPayload` only for a legacy bundled-language payload.

## 13. When Changing The Mod

- Follow `AGENTS.md` for detailed architecture, DLC, localization, and validation rules.
- Keep tunable policy in XML when possible.
- Keep live RimWorld objects at adapter/UI/transport edges; pure helpers should use DTOs/primitives.
- Add or update focused pure tests when changing pure logic.
- Update `DOCUMENTATION.md` and `CHANGELOG.md` for behavior, structure, release, or workflow changes.
