# Pawn Diary — Architecture & Behavior

Current-state guide for the mod. Companion: [CHANGELOG.md](CHANGELOG.md) (milestone history).

_Last updated: 2026-06-25_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and rewrites them as short diary pages
through configured OpenAI-compatible LLM API lanes. RimWorld loads the compiled DLL at startup;
Harmony patches, `GameComponent`s, Defs, and inspector tabs are discovered by RimWorld lifecycle
hooks. There is no `main()`.

Diary ownership and first-person generation require a free humanlike colonist at least
`DiaryTuningDef.minimumFirstPersonAgeYears` old (13 by default). Animals, prisoners, slaves, enemies,
visitors, non-colonist participants, and underage colonists never own pages; mixed events become a
solo entry for the one eligible colonist. Pairwise colonist events generate initiator POV first, then
recipient POV with the initiator page as hidden continuity.

Neutral arrival pages are first-page entries. Neutral death pages are terminal: later same-tick
events for that pawn are hidden and not generated.

---

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, and `Languages/`. Source lives under `Source/`; build output
`1.6/Assemblies/PawnDiary.dll` is committed so a clone runs as a mod.

```text
PawnDiary/
|-- About/                       mod metadata, icon, preview
|-- 1.6/
|   |-- Assemblies/              PawnDiary.dll (committed) + bundled 0Harmony.dll fallback
|   `-- Defs/                    groups, tuning, prompts, writing styles, humor cues, UI/text policy (12 files)
|-- Languages/                   Keyed + DefInjected localization
|-- Source/
|   |-- Capture/                 Event Catalog: pure payloads/specs/registry + CaptureContext/Decision
|   |-- Core/                    DiaryGameComponent partials (one per source), batching, generation queue
|   |-- Defs/                    Def classes + XML lookup helpers
|   |-- Generation/              context builders, prompt assembler, LLM client, DLC reads
|   |-- Models/                  saved/display models (DiaryEvent, DiaryEntry, PawnDiaryRecord)
|   |-- Patches/                 Harmony startup + hooks + inspect command
|   |-- Pipeline/                pure prompt/response/decor contracts + API endpoint policy
|   |-- Settings/                settings data + UI
|   |-- UI/                      hidden Diary inspect tab + partials, text formatting
|   `-- Util/                    MiniJson (runtime-safe JSON; do not replace)
|-- prompt-lab/                  Node prompt-testing harness (golden + generated fixtures)
|-- tests/                       5 standalone pure-helper test projects
|-- skills/                      repo workflow skills
`-- *.md                         AGENTS / DOCUMENTATION / CHANGELOG / README
```

Key files:

| File | Role |
|---|---|
| `Patches/DiaryModStartup.cs`, `Patches/DiaryPatches.cs` | Startup, hidden-tab registration, Harmony hooks. |
| `Capture/*` | Event Catalog: `DiaryEventType`, `XxxEventData` (+`Decide`), `XxxEventSpec`, `DiaryEventCatalog`. |
| `Core/DiaryGameComponent*.cs` | Recording, batching, scans, save/load, lookup indexes, generation queue (one partial per source). |
| `Models/DiaryEvent.cs`, `Models/PawnDiaryRecord.cs` | Saved event model and per-pawn diary index/settings. |
| `Generation/DiaryPromptBuilder.cs`, `Pipeline/*` | Prompt facade plus pure planning, response cleanup, API lane policy/identity, domain recovery, text decoration. |
| `Generation/DiaryContextBuilder.cs`, `Generation/DlcContext.cs`, `Generation/PawnFactCapture.cs`, `Generation/MoodImpactClassifier.cs` | Pawn/surroundings/relationship/health/weapon context; all live-pawn reads centralized and guarded here (DLC reads in `DlcContext`; display-fact snapshots — staggered-handwriting intensity and text-decoration hediff/trait facts — in `PawnFactCapture`; per-pawn GameCondition mood direction in `MoodImpactClassifier`). `DiaryContextBuilder` now keeps only the impure collectors — its pure one-line text cleaner was extracted to `DiaryLineCleaner` and its localized mood/pain/opinion/age/beauty/bleed band tokens to `DiaryBuckets`. |
| `Defs/InteractionGroups.cs`, `DiarySignalPolicyDef.cs`, `DiaryTuningDef.cs` | XML classifiers, per-group prompt instruction rollout (classify Def → roll one `instructions` variant at capture), odds, cooldowns, scanner policy, shared tuning. |
| `DiaryPromptDef.cs`, `PromptArchitectureDefs.cs`, `DiaryPersonaDef.cs`, `DiaryHumorCueDef.cs`, `DiaryUiStyleDef.cs`, `DiaryTextDecorationDef.cs` | XML-owned shared prompts, event prompt policy, writing styles, humor cues, UI, and display policy. |
| `Generation/LlmClient.cs`, `LlmResponseParser.cs` | HTTP queue/failover/concurrency and pure provider response parsing. |
| `Settings/PawnDiaryMod.cs`, `PawnDiarySettings.cs` | Settings data and settings UI. |
| `UI/ITab_Pawn_Diary*.cs`, `DiaryTextFormat.cs` | Hidden Diary inspect tab, cards, paging, debug controls, safe rich-text formatting. |
| `Util/MiniJson.cs` | Runtime-safe JSON parser; do not add external JSON dependencies. |

---

## 3. Data Flow

1. A Harmony hook or scanner sees a candidate gameplay moment. Recorders no-op until RimWorld is in
   play (so startup/scenario setup doesn't read calendar ticks too early).
2. The adapter snapshots live RimWorld facts, XML/settings gates, dedup facts, and any RNG result.
3. The Event Catalog decides `Drop`, immediate event creation, a neutral prompt route, or a
   batch/ambient/day-reflection route.
4. `AddSoloEvent` / `AddPairwiseEvent` creates a saved `DiaryEvent`, semantic `colorCue`, per-POV
   decoration facts, and references from eligible pawn records.
5. Generation queues immediately when possible and is retried by periodic scans.
6. `DiaryPipelineAdapters` copies event/XML/localization/settings state into DTOs.
7. Pure pipeline helpers produce a prompt plan, parse provider output, and postprocess text.
8. `LlmClient` sends requests, handles failover/concurrency, and returns results on the main thread.
9. `ApplyLlmResult` stores success/failure; title generation (and the disabled speech injection) run
   after successful main entries.
10. `EntriesFor` reads saved events for the Diary tab, which caches built views until the pawn render
    token changes.

`GameComponentTick` drains completed results, flushes main-thread debug logs, recovers orphaned
pending entries, and queues pending work roughly every 120 ticks. Pending generation state resets on
load. Non-neutral POVs below the XML-tuned Consciousness floor
(`DiaryTuningDef.minimumConsciousnessForFirstPersonGeneration`, default 0.11) are skipped; neutral
arrival/death bypass that guard.

---

## 4. Event Sources

| Source | Capture | Output |
|---|---|---|
| Social interactions | `PlayLog.Add` → `RecordInteraction` | Pairwise, solo, pair batch, or ambient day note by XML group. |
| Mental states | `MentalStateHandler.TryStartMentalState` | `SocialFighting` pairwise when both eligible; other breaks solo. |
| Romance relations | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise for Lover/Spouse/ExLover/ExSpouse. |
| Tales / combat tales | `TaleRecorder.RecordTale` + Tale batch policy | Solo, pairwise, delayed combat batches, or neutral death description. |
| Arrivals | Starting-colonist scan + `Pawn.SetFaction` | Neutral first-page arrival (prior faction/recruiter/kind/creepjoiner/surroundings). |
| Deaths | `Pawn.Kill` prefix/postfix + XML-marked death TaleDefs | Neutral final page (cached cause/context); fallback for non-Tale deaths. |
| Mood events | `GameConditionManager.RegisterCondition` | Once per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | Temporary memories filtered by XML thresholds/tokens; ambient thoughts can batch. |
| Thought progression | Periodic scan | Hunger/rest/outdoors/chemical stages when they first appear or worsen. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo entry for the inspired pawn. |
| Hediffs | `Pawn_HealthTracker.AddHediff` + progression scan | Immediate or day-reflection health entries by XML Hediff policy. |
| Work | Periodic current-job sampling | Skips social/violent work; applies XML odds/cooldowns and `workGenerationWeight`. |
| Raids / infestations | `IncidentWorker.TryExecute` (filtered to `IncidentWorker_Raid` plus infestation workers) | Once per eligible colonist on the target map; payload = incident/faction defName, raid points, arrival mode, and strategy. Ordinary raids delay LLM generation; drop-pod raids and infestations generate immediately. |
| Quests | `Quest.Accept` + defensive `MainTabWindow_Quests` patch + `Quest.EverAccepted` scan + `Quest.End` | Only accepted quests recorded. `Success`→"completed", `Fail`→"failed"; one entry per eligible colonist per signal with description/issuer/rewards context. |
| Rituals | `LordJob_Ritual.ApplyOutcome`, `LordToil_PsychicRitual.RitualCompleted` | Ideology rituals fan out to author/target/participants/spectators with role/title/status. Anomaly psychic rituals fan out from the completion callback, deliberately omit role/title fields, and use darker/stranger instructions. |
| Abilities | `Ability.Activate` (local + world overloads) | Successful uses become solo entries for the caster, cooldown-weighted (fast abilities rare, rare ones more likely). |
| Day reflections | Sleep/rest trigger | One reflective entry per pawn/day only when an XML-configured important signal kind exists (`event`/`opinion`/`hediff` by default; `filler` alone cannot trigger unless XML allows). |

`PlayLog.Add` preflights eligibility and XML significance before rendering RimWorld's POV grammar
strings, so routine social-log rows that can't become diary entries stay cheap. Generated Social-log
speech patches the concrete 1.6 worker `ToGameStringFromPOV_Worker` with an old-name fallback so a
display-method rename can't abort `PatchAll` before later hooks register.

---

## 5. Event Catalog

`Source/Capture/` is the pure decision layer:

- `DiaryEventType`: one enum value per source.
- `XxxEventData : DiaryEventData`: primitive payload plus `static Decide(data, ctx)`.
- `CaptureContext`: precomputed eligibility, user/signal/ambient enablement, and tick facts.
- `CaptureDecision`: `Drop`, `GenerateSolo`, `GeneratePair`, `RouteBatch`, `RouteAmbient`,
  `RouteDayReflection`, `GenerateSoloDeathDescription`, `GeneratePairDeathDescription`, or
  `GenerateSoloArrivalDescription`.
- `XxxEventSpec`: wrapper registered in `DiaryEventCatalog`.

Catalog-backed sources: Thought, Inspiration, MoodEvent, MentalState, Tale, Hediff, Interaction,
Romance, Arrival, Death fallback, Work, ThoughtProgression, DayReflection, Raid, Quest, Ritual,
Ability.

DayReflection is a meta-source: the adapter counts candidate cues plus the important subset.
`DiaryTuningDef.daySummaryImportantSignalKinds` controls which kinds are important
(`event`/`opinion`/`hediff`/`filler`); its `Decide` drops days with no important candidates.

Direct `AddSoloEvent`/`AddPairwiseEvent` call sites outside `RecordXxx` are **sinks** (batch
flushers, ambient-note flushers, the generation dispatcher, the event factory) — they execute routes
chosen by catalog-backed sources, they don't decide new captures.

Adding a source:

1. Add a `DiaryEventType` value.
2. Add `Capture/Events/XxxEventData.cs` (primitive fields, `Decide`, any pure context builder).
3. Add `Capture/Specs/XxxEventSpec.cs`.
4. Register it in `DiaryEventCatalog`.
5. Update the impure adapter to snapshot facts, ask the catalog, then execute the returned route.
6. Update `DiaryCapturePolicyTests` (including catalog dispatch coverage).

Keep RimWorld/Verse/Unity objects, `.Translate()`, settings reads, `Find.TickManager`, IO, RNG,
dedup mutation, event creation, and LLM queueing in adapters. Pure code takes DTOs/primitives only.

---

## 6. XML Policy

`1.6/Defs/DiaryInteractionGroupDefs.xml` owns group matching, instructions, color cues, batching,
promotion, Hediff policy, and default enablement. Domains: Interaction, MentalState, Tale, MoodEvent,
Thought, Inspiration, Romance, Work, Hediff, Raid, Quest, Ritual, Ability. Matching is
domain-scoped by exact `defName` or substring token; XML order matters and catch-all groups go last.

**Quest domain** is unusual: its `matchDefNames` are lifecycle signals
(`accepted`/`completed`/`failed`), not defNames — one `DiaryEventType.Quest` fans out to three prompt
groups. Saved Quest entries still keep the real `QuestScriptDef`; recovery classifies them from the
saved `signal=` context field.

**Ritual domain** classifies by string markers only. Ideology/Royalty/Biotech/Odyssey rituals use
`Precept_Ritual` defName + behavior worker class and carry role facts in context
(`ritual_title`, `ritual_behavior`, `ritual_perspective`, `ritual_role`, `royal_title`,
`ideological_role`). Anomaly psychic rituals use `psychic_ritual` + a `PsychicRitual` classifier
prefix, carry only `psychic_ritual_perspective`, outcome, and plain-word quality, and deliberately
omit role/title fields. Prompt templates render role/title fields only when present.

`DiaryTuningDef.xml` owns ritual quality bands (progress/power → `terrible`/`weak`/`decent`/`strong`/
`excellent`/`unknown`). Ritual prompts must let that label shape confidence/aftermath/emotional
weight without naming or explaining it directly. Psychic-ritual invoker prompts ask only for invented
or broken ritual words inside a speech block; the visual distortion is applied later by
`DiaryTextDecorationDefs.xml` from the saved `psychic_ritual_perspective=invoker` context.

**Ability domain** classifies by `AbilityDef` defName + safe string tokens (category, `Psycast`,
hostile/utility disposition). Saved context uses `ability=`, `ability_label=`, `ability_category=`,
`ability_target=`, `ability_cooldown_ticks=`. `DiaryTuningDef.xml` controls `abilityDedupTicks` and
the cooldown-weighted sampling curve.

**Raid domain** classifies by safe incident/arrival/strategy strings, not live pawn or DLC objects.
Saved context uses `raid=`, `label=`, `faction=`, `points=`, plus optional `arrival_mode=` and
`strategy=`. `DiaryTuningDef.xml` controls `raidDedupTicks` and `raidGenerationDelayTicks`; the delay
applies only to ordinary raids so prompts can emphasize warning, positioning, and anticipation.
Drop-pod raids and infestations bypass the delay and use their own XML instructions for sudden
internal contact.

`DiarySignalPolicyDefs.xml` owns thought/work policy: thresholds, tokens, staged progression, ambient
batching, scan odds, cooldowns. `DiaryTuningDef.xml` keeps shared fallback tuning for mood/health
buckets, nearby context, minimum first-person age, the first-person Consciousness gate, the 0..4
low-consciousness/intoxication display-staggering thresholds, mood-impact condition families
(positive/negative `GameCondition` defNames), day-reflection signal kinds/weights, weather mention
chances, and scanner intervals. Hediff groups define Immediate vs DayReflection mode, visible/bad/injury
gates, severity thresholds, and weights. Tale groups can declare death victim role lists, keeping
death classification data-owned and DLC/mod friendly.

---

## 7. Prompts And Writing Styles

Prompts are compact `key: value` lines. Empty values and `none`/`n/a`/`unknown` sentinels are
dropped. Templates include pair, solo, batched, day-reflection, death-description,
arrival-description, and title shapes.

System prompts are intentionally short (global safety/format only). Event-type guidance lives in
`DiaryEventPromptDef`: `prompt` → `event prompt:`, `enhancement` → `event enhancement:`, rendered
before per-group `instruction:`/`tone:` flavor. The first-person system prompt asks for one sensory
detail, one emotional beat, and one implied consequence/tension from supplied facts — without
inventing facts.

**Variant pools.** Each group may carry `instructions` / `tones` lists so an event type doesn't
repeat verbatim. When a pool has any non-blank entry, the pure `PromptVariants.Pick` selects one
wording per entry: `instruction` rolls once at capture (by `InteractionGroups.InstructionForGroup`,
which the capture path freezes onto the event — never changes after) and is persisted; `tone`
picks deterministically by event id (stable across save/load and regeneration). The singular
`instruction`/`tone` remain as fallback/settings-preview. Weighting is XML-owned (list a wording more
than once to make it common). Don't leave blank `<li>` slots — selection skips whitespace, which
misaligns indexed DefInjected keys.

Shipped pools are written for **small-model separability**: each `instructions` pool holds ~3
*lens-distinct* variants (different sensory/narrative/temporal entry points in concrete nouns/verbs,
not emotional synonyms), and each tone-bearing group ships exactly two `tones` variants that differ
in rhythm/attention and emotional posture rather than near-synonym moods. Variants must not ask for
unsupplied facts (exact words, witnesses, named aftermath) unless gated behind supplied context.
Prompt-lab parses the same pools and `--all-variants` crosses instruction/tone/enchantment variants
with a coverage check.

**Prompt Studio** can save per-event overrides for `DiaryEventPromptDef.prompt`/`.enhancement`; empty
text or text matching the localized XML value clears the override, so XML stays the default catalog.

Layer boundaries:

- Impure: event hooks, `DiaryGameComponent`, settings, XML lookup, localization, IO, RNG, save
  mutation, transport, and live-pawn fact collection (`DlcContext`, `PawnFactCapture`,
  `DiaryContextBuilder`'s summary builders, `MoodImpactClassifier`). The collectors delegate
  formatting to the localized band tokens in `DiaryBuckets` (`.Translate()`-bound, so main-thread)
  and to the pure text cleaner in `DiaryLineCleaner`.
- Bridge: `DiaryPipelineAdapters`.
- Pure: `DiaryEvent`/`PawnDiaryRecord` (saved models — they store plain values only and never read a
  live `Pawn`), `DiaryPromptPlanner`, `PromptAssembler`, `PromptVariants`, `DiaryContextFields`,
  `DiaryLineCleaner`, `ApiEndpointPolicy`, `ApiLaneSelector`, `ApiLaneIdentity`, `LlmResponseParser`,
  `DiaryResponsePostprocessor`, `DiaryTextDecorations`.

**Writing styles** come from `DiaryPersonaDef` + settings overrides/custom rows. The Def and save
field names still say "Persona" for compatibility, but the player-facing feature is writing styles.
Weighted selection uses base weight, trait/backstory matches, creepjoiner bonuses, and duplicate
penalties. Style text is appended to first-person system prompts only (neutral arrival/death and
title prompts are style-free). Each rule describes how a pawn tends to write notes; the wrapper tells
the model to follow the concrete sentence shape/opening/punctuation/detail choice, and explicitly
**not** to roleplay a chat persona, add catchphrases, or invent dialogue.

**Prompt enchantments** are XML-weighted live context cues. Eligible first-person prompts may add one
localized `important context:` field as pressure (not the subject unless the event centers on it).
Health/capacity cues can appear on any eligible first-person prompt; important events may also put a
Royalty title or Ideology role into the same weighted pool — the two are mutually exclusive per
prompt. These are separate from `DiaryEventPromptDef.enhancement` (static event-type guidance).

**Humor cues** are a hidden, always-on per-entry writing license. Roughly one in ten eligible
first-person entries (base rate XML-tunable via `DiaryTuningDef.humorChance`, default `0.10`) gets one
subtle structural cue appended to its system prompt — never a "be funny" instruction, always a single
sentence-shape constraint (an understatement coda, a dry inventory, a clerical tally of loss). The
`DiaryHumorCueDef` catalog (`DiaryHumorCueDefs.xml`) holds Light (dry/absurdist) and Gallows
(dark/deadpan) tiers; `HumorCues.CueFor` rolls the base rate, derives the stakes tier from the event
(important, Raid domain, or `combat`/`socialFight`/`mentalBreak` color cue ⇒ Gallows), and weighted-
picks one cue. The chosen rule rides into the prompt folded inside the **persona voice block**
(`DiaryPipelineAdapters.HumorVoiceBlock` + `CombinedVoiceBlock`), so it needs no planner/contract
field and is automatically suppressed on neutral death/arrival/title templates that opt out of
persona text. There is no settings field or UI; the feature is invisible to the player. Cue `rule`
and `label` text is localized via `DefInjected`; the `tier` keyword is an internal schema token.

**Direct speech** is allowed only for initiator/single-POV interaction prompts, using one closed
`[[speech]]...[[/speech]]` block when source notes support it. Recipient follow-ups forbid speech and
get hidden initiator continuity. Before generated text is saved, response cleanup preserves complete
speech blocks but sanitizes hallucinated bracket tags: malformed closers repaired, unpaired markers
stripped to prose, unknown `[[tag]]...[[/tag]]` removed, bracketed prose flattened, and echoed schema
punctuation (`;` `=` `:` `|`) stripped.

Generated speech Social-log injection is currently disabled/hidden (the compatibility field still
loads for save safety, settings force it off, and the call site is commented out — RimWorld accepted
synthetic rows without reliably showing them in the Social tab UI). Title generation is on by
default; successful main entries queue a capped title follow-up pinned to the successful lane.

---

## 8. Settings And UI

Core settings: API lanes, lane routing mode, request tuning (timeout/concurrency/max tokens/
temperature), title generation, atmospheric formatting, prompt enchantments, work/social generation
weights, system-prompt overrides, per-event prompt/enhancement overrides, XML-backed event filters,
and writing-style presets. Dev mode reveals **prompt test mode** in mod settings: real gameplay
events still assemble their prompts, but the queue marks the POV prompt-only and never calls the LLM;
those cards show in the Diary tab while dev mode is on.

**API lanes** support OpenAI-compatible Chat Completions and OpenAI Responses (model fetch/pick,
per-row connection tests, per-row auth, per-row reasoning effort, and the shared request-tuning
block inside the expanded connection section). Auth styles: Bearer, No auth, or an editable custom
API-key header defaulting to `x-goog-api-key`, or `key=` query. Endpoint URLs and custom-header text
normalize on send/load/save boundaries, not every draw, so editing mid-typing isn't rewritten.
Query-key auth replaces any existing `key=` while preserving URL fragments; logs strip query strings.
Old `api-key`/`x-api-key` rows migrate to custom-header auth. Gemini's OpenAI-compatible endpoint
(`https://generativelanguage.googleapis.com/v1beta/openai/chat/completions`) uses Chat-compatible
mode with the Reasoning selector sending `reasoning_effort`.

`ApiLaneIdentity` owns the comparison modes used by the API lane system: concurrency gates/cooldowns,
failover duplicate removal, successful-lane pinning, model-fetch stale-result checks, and connection
test stale-result checks. Those modes intentionally do not compare the exact same fields. For example,
generation gates use normalized endpoint/model/effective auth and ignore reasoning effort, while
settings fetch/test results use exact raw row snapshots so an in-flight result is discarded after a row
edit. `ApiLaneLabels` formats sanitized English debug labels and strips query/fragment text before a
URL reaches logs.

Row order is editable with compact arrows. The global routing mode controls how strongly order
affects primary selection: **Balanced** spreads primaries equally, **Prefer top rows** favors earlier
rows, **Failover only** always starts with the first ready row. Row order is also the failover order
in every mode.

**Prompt Studio** is a collapsible section; its selector covers shared system prompts and
`DiaryEventPromptDef` event types, with the selected editor in one highlighted block. Writing-style
presets likewise use one block for summary/add/reset/selection/rule editing/tag toggles.

The Diary surface is an inspect tab internally. By default, selecting one eligible colonist (or
colonist corpse) adds a **Diary** command button (journal-and-pen icon) that opens/closes the hidden
tab. A settings toggle can instead show Diary in the normal pawn inspect-tab row and hide the bottom
command. In command mode, the command overlays a subtle underline for newly finished pages and
pulsing dots while any page or title is still being written. In tab mode, the Diary tab is left
plain, with no tab-strip status indicator. Opening the pawn's Diary acknowledges the finished-page
marker.
Social-log diary links and linked-POV navigation open the same tab in either mode.

The Diary UI shows completed pages in production. Dev mode adds generation enablement, writing-style
picker, pending/raw/failure rows, prompt/status diagnostics, in-progress indicators, transient
formatting previews, and mock-page fill. Histories page by in-game year. Cards show date/title,
accent, group chip, model id, linked POV previews, and title-pending animation.

The dev-mode **Prompt suite** button opens a data-driven dropdown of event categories (driven by
`DiaryGameComponent.AllSuiteEntries`, so new categories auto-appear). Picking one deletes any prior
test entry and captures exactly one plain prompt-only card routed through the normal queue; picking
another replaces it. A companion **Clear test prompts** button removes every prompt-test entry. Pair
categories also produce a recipient POV card and are omitted when no second colonist exists. Death
and arrival shapes are excluded (a synthetic one would become the pawn's diary boundary and hide real
pages). The suite validates prompt planning/queueing/UI display, not Harmony capture — use §13 for
real hook checks.

The tab is sized by `tabWidth`/`tabHeight` (`DiaryUiStyleDef.xml`). Newest cards start expanded
(`autoExpandedEntryCount`, default 3); older history starts collapsed; clicking a header toggles
either. In dev mode every expanded card shows a subtle bottom-left copy button (copies the prompt
for prompt-only cards, else generated text).

`DiaryUiStyleDef.xml` owns visual constants. `DiaryTextFormat` escapes raw model rich-text tags,
then converts light markdown and valid speech markers to Unity rich text. `DiaryTextDecorationDef`
owns display-only decorations: intoxication/anesthesia speech uses the strongest staggered word-size
setting; extreme-dark speech dims selected words (vs. strange-chat Zalgo); combat/social-fight/mental
cues add stronger page washes and header rules; generated text is never mutated on save. The same
`Diary_TextDecorations` `StaggeredWordSizes` rule list is also the single source of truth for which
hediffs count as intoxicating at capture time (via `DiaryTextDecorations.HediffMatchesStaggeredRules`),
so modders/DLCs extend the set by editing XML — no parallel hardcoded keyword list. Live humanlike
pawn names in prose are highlighted — colonists use their favorite color when available,
slaves/prisoners/enemies/neutral pawns use XML-backed status colors, ambiguous/uncolored matches
fall back to bold.

---

## 9. LLM Reliability

Each enabled endpoint/model/mode/auth row is an API lane. New requests choose a primary lane by the
routing mode; recipient follow-ups and title requests pin to the prior successful lane when possible
unless it's cooling and another is ready. Per-lane semaphores enforce concurrency;
`ServicePointManager.DefaultConnectionLimit` is raised for Mono.

`LlmClient` builds mode-specific URLs/payloads, retries transient errors up to three times per lane,
and surfaces timeout/permanent/empty/incomplete responses as failure text. Transient failures and
timeouts apply an automatic cooldown growing 10s→20s→40s→80s→160s→cap 300s; any success clears it.
Cooling lanes are skipped while another is ready but still tried when every lane is cooling. Each
failover pass snapshots readiness once so a cooldown expiring/added mid-loop can't skip every lane.
Lane identity ignores stale key text for `No auth` rows; saving API settings prunes cooldowns for
removed/reconfigured rows. Generation and model-list HTTP bodies are streamed with hard byte caps
before JSON parsing/logging. Successful responses are trimmed locally to `maxTokens`, preferring
complete sentences.

`LlmResponseParser` extracts typed visible output before fallback fields and strips
structured/transcript reasoning before debug/save. The save-time sanitizer preserves valid speech
blocks while removing hallucinated bracket tags and echoed schema punctuation. API keys are never
logged or saved in event metadata. New game sessions cancel stale requests. Orphaned pending entries
reset after two scans. If no enabled lane has a model, the entry fails with
`PawnDiary.Error.NoApiConfigured`.

---

## 10. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`. Event indexes and transient
day-reflection guards rebuild on load; per-pawn event-id lists prune blank/duplicate/dangling refs.

**Add/remove safety:** adding Pawn Diary to an existing save creates an empty diary component on the
next load and records future events only. Removing it is gameplay-safe — the mod persists no custom
`ThingDef`/`HediffDef`/`ThoughtDef`/job/need/pawn-or-map component or cross-reference data onto
vanity objects. The diary history lives inside Pawn Diary's own `GameComponent`; without the mod the
UI/history is unavailable and RimWorld may leave stale Pawn Diary XML in the save until it's
rewritten. Legacy/dev saves that used generated Social-log injection may keep synthetic
`PlayLogEntry_Interaction` rows whose replacement text requires Pawn Diary's component and patch.

`DiaryEvent` saves raw/generated text, statuses/errors, context, source ids, LLM metadata, semantic
`colorCue`, titles, assembled prompts, compact per-POV hediff/trait facts, legacy staggered
intensity, and capped pre-cleanup debug text. Decorated rich text is not persisted. Classification is
inferred from stable `gameContext` fields (`tale=`, `mental_state=`, `mood_event=`, `thought=`,
`inspiration=`, `romance=`, `work=`, `hediff=`, `raid=`, `quest=`). Pending requests are not
persisted (statuses reset on load; scans requeue). Death/arrival caches evict oldest stale entries at
their cap and clear when a new session starts.

TODO: `DiaryEvent` still stores repeated `initiator*`/`recipient*`/`neutral*` field families; a
future migration should introduce saved role-slot objects, hydrate them from legacy fields, move
callers to slot accessors, then retire direct legacy writes.

---

## 11. Runtime And DLC Constraints

- RimWorld runs on Unity Mono; use only assemblies in `RimWorldWin64_Data/Managed`. Do not add
  `System.Web.Extensions`/`JavaScriptSerializer` or external JSON libs (JSON is parsed by `MiniJson`).
- **Harmony is the only external dependency.** RimWorld 1.6 no longer ships `0Harmony.dll` in
  `Managed`, so `About/About.xml` declares `brrainz.harmony` under `<modDependencies>`/`<loadAfter>`;
  a copy is bundled in `1.6/Assemblies/` as a fallback, and the loader dedupes it. Referenced at
  build time from `Source/Libs/0Harmony.dll` (`Private=False`).
- No paid DLC is required; optional DLC content must cleanly no-op when absent.
- Prefer XML string `defName` matchers for DLC-aware content; absent DLC defs never appear.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` + null checks (Anomaly
  creepjoiner checks use the same helper).
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use `GetNamedSilentFail` or XML
  string matching. XML references to DLC defs need `MayRequire`; plain string matcher lists do not.

---

## 12. Localization

Player-facing UI strings and natural-language prompt text live in
`Languages/English/Keyed/PawnDiary.xml`, resolved with `.Translate()` on the main thread. Def text
(`label`, `instruction`, `tone`, writing-style `rule`, prompt defs/templates, hediff/body-part
labels) localizes through DefInjected.

Keep DefInjected English stubs in sync for `DiaryInteractionGroupDef`, `DiaryEventPromptDef`,
`DiaryPersonaDef`, and `DiaryPromptDef` when editing XML labels/instructions/tones/event
prompts/writing-style rules/shared prompts. Variant pools localize through indexed DefInjected keys
(`<group.instructions.0>`, `<group.instructions.1>`, `<group.tones.0>`, …) — one per list position;
keep blanks out so indices stay aligned.

Kept English on purpose: prompt schema labels (`event:`, `role:`, `thought=`), role/sentinel words
(`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`), internal defNames/theme tags, and
background-thread `LlmClient` debug/error strings (`.Translate()` is not thread-safe).

To add a language, copy `Languages/English` to `Languages/<Language>`, translate Keyed values, and
optionally add DefInjected translations for XML Def text.

---

## 13. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Output is `1.6/Assemblies/PawnDiary.dll`. If `MSBuild` isn't on `PATH`, locate it with `vswhere` or a
Visual Studio Developer PowerShell. Enable hooks: `git config core.hooksPath .githooks`.
`.githooks/verify.ps1` checks staged whitespace, XML/project well-formedness, pure tests, Debug
MSBuild, committed-DLL freshness, and Harmony freshness. Emergency bypass:
`PAWNDIARY_SKIP_VERIFY_HOOKS=1`.

Pure tests (compile without RimWorld/Unity):

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
```

`DiaryCapturePolicyTests` covers Event Catalog decisions/dispatch; `DiaryPipelineTests` covers prompt
planning, API lane policy/identity, and domain recovery; `PromptVariantsTests` covers instruction/tone
pool selection (fallback, determinism, negative-seed normalization, blank-skip);
`LlmResponseParserTests` and `DiaryTextDecorationTests` cover their respective pure helpers.

**Live hook validation (RimBridge/GABS).** Use a disposable save with dev mode on, RimBridge/GABS
connected, and prompt test mode enabled. Prompt test mode intercepts only after a real event reaches
`QueuePrompt`, so a success appears as:

```text
[PawnDiary debug] Captured prompt without generation event=<id> role=<role>
```

Restart RimWorld after any DLL rebuild and check the log for `[Pawn Diary] PatchAll failed` first —
a startup PatchAll error leaves later hooks unregistered, making no-capture results meaningless.

Low-impact real hook checks:

| Hook family | Debug action | Expected |
|---|---|---|
| Inspiration | `Actions\Inspiration...\Frenzy_Work` on a colonist | One prompt-only capture. |
| Mental state | `Actions\Mental state...\Crying`, then `Actions\T: Stop mental state` | One capture, then pawn clears. |
| Thought | `Actions\Show more actions\T: Give bad thought` | One capture from `TryGainMemory`. |
| Social `PlayLog.Add` | `Actions\T: Force Interaction` → choose colonist → `insult` | Pairwise captures when the hook is installed and policy allows. |
| Mood condition | `Actions\Add Game Condition...\Aurora\1 hour` | One capture per eligible affected colonist. |

Automation should keep a `lastLogSequence` cursor and count new capture lines after each step: a solo
event → one role capture; a pair event → initiator + recipient with the same event id; a neutral
arrival/death → one neutral capture; a colony-wide fan-out → one initiator per eligible colonist. For
stronger assertions than the log allows, dump recent saved `DiaryEvent` rows and report
`interactionDefName`, `gameContext`, involved pawn ids, and per-role status. `scripts/gabs/
pawndiary-live-smoke.lua` is the smallest hybrid smoke fixture (one GABS call: snapshot cursor →
`Crying` on a colonist → stop mental state → fail if no new capture log).

Full live auto-test coverage should walk every implemented source/route once (startup arrival, each
solo/pair/fan-out source, quest accepted/completed/failed, Ideology + Anomaly rituals when DLC is
present, ability use, tale/death/hediff/work/day-reflection routes). The practical target is proving
each Harmony/scanner hook is installed and that real RimWorld events reach prompt construction — not
exercising every XML matcher token (those are covered by prompt-lab and pure catalog tests). Quest
coverage needs all three lifecycle groups, so accept one quest (covered) and end two more with
`Success` and `Fail`. In base-game-only runs, mark the DLC-only sources (rituals, psychic rituals,
Biotech pregnancy) not applicable.

When a live action produces no capture, classify the miss before changing code: vanilla may have
refused the action, XML/settings policy may have dropped it, the debug action may bypass the vanilla
hook, the pawn may be ineligible/incapacitated, or Harmony may not have installed the hook.
Prompt-suite entries are not a substitute — they synthesize `DiaryEvent`s inside the mod instead of
entering through RimWorld's sources.

Prompt lab:

```powershell
cd prompt-lab
npm test
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

`npm test` runs the C#/JS assembler golden check and verifies the generated all-variants fixture set
covers every XML prompt template, every `DiaryEventPromptDef` prompt/enhancement, configured
prompt-enchantment variants, runtime context marker families, and the repeated-pass path. Release
payloads are made with `scripts/publish.ps1` (throwaway Release DLL, copies only runnable mod files
including runtime textures into `dist/<published packageId>`).

---

## 14. Changelog Policy

`CHANGELOG.md` is a milestone history, not a commit log. Add or update a dated section only for
user-visible changes, architecture changes, migrations, important fixes, release work, or known
issues. Prefer one grouped bullet over several micro-entries.
