# Pawn Diary - Architecture & Behavior

Current-state guide for the mod. Keep this file focused on how the system works now. Keep
[CHANGELOG.md](CHANGELOG.md) grouped by milestone, not by individual commit.

_Last updated: 2026-06-24 (live event auto-test scenario)_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and rewrites them as short diary pages
through configured compatible LLM API lanes. RimWorld loads the compiled DLL at startup; Harmony
patches, `GameComponent`s, Defs, and inspector tabs are discovered by RimWorld lifecycle hooks.

Diary ownership and first-person generation require a free humanlike colonist old enough for
`DiaryTuningDef.minimumFirstPersonAgeYears` (13 by default). Animals, prisoners, slaves, enemies,
visitors, non-colonist participants, and underage colonists do not own diary pages. Mixed events
become a solo entry for the eligible colonist. Pairwise colonist events generate initiator POV first,
then recipient POV with the initiator page as hidden continuity.

Neutral arrival pages are first-page entries. Neutral death pages are terminal: later same-tick
events for that pawn are hidden and not generated.

---

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, and `Languages/`. Source lives under `Source/`; build output
`1.6/Assemblies/PawnDiary.dll` is committed so a clone runs as a mod.

```text
PawnDiary/
|-- About/                         mod metadata and preview
|-- 1.6/
|   |-- Assemblies/PawnDiary.dll    committed build output
|   `-- Defs/                       groups, tuning, prompts, writing styles, UI/text policy
|-- Languages/                     Keyed and DefInjected localization
|-- Source/
|   |-- Capture/                   Event Catalog pure payloads/specs/registry
|   |-- Core/                      DiaryGameComponent partials, capture, batching, generation queue
|   |-- Defs/                      Def classes and XML lookup helpers
|   |-- Generation/                context builders, prompt facade, LLM client
|   |-- Models/                    saved/display models
|   |-- Patches/                   Harmony startup and hooks
|   |-- Pipeline/                  pure prompt/response/decor contracts
|   |-- Settings/                  settings data and settings UI
|   |-- UI/                        Hidden Diary inspector tab and command-opened UI
|   `-- Util/                      MiniJson
|-- prompt-lab/                    Node prompt-testing harness
|-- tests/                         standalone pure-helper tests
|-- skills/                        repo workflow skills
`-- *.md                           docs
```

Key files:

| File | Role |
|---|---|
| `DiaryModStartup.cs` / `DiaryPatches.cs` | Startup, hidden tab registration, Harmony hooks. |
| `Source/Capture/*` | Event Catalog: `DiaryEventType`, `XxxEventData`, `XxxEventSpec`, and `DiaryEventCatalog`. |
| `DiaryGameComponent*.cs` | Recording, batching, scans, save/load, lookup indexes, and generation queueing. |
| `DiaryEvent.cs` / `PawnDiaryRecord.cs` | Saved event model and per-pawn diary index/settings. |
| `DiaryPromptBuilder.cs` / `Source/Pipeline/*` | Prompt facade plus pure planning, response cleanup, domain recovery, and text decoration. |
| `DiaryContextBuilder.cs` / `DlcContext.cs` | Pawn/surroundings/relationship/health/weapon context; DLC reads are centralized and guarded. |
| `InteractionGroups.cs`, `DiarySignalPolicyDef.cs`, `DiaryTuningDef.cs` | XML classifiers, odds, cooldowns, scanner policy, and shared tuning. |
| `DiaryPromptDef.cs`, `PromptArchitectureDefs.cs`, `DiaryPersonaDef.cs`, `DiaryUiStyleDef.cs`, `DiaryTextDecorationDef.cs` | XML-owned shared prompts, event prompt policy, writing styles, UI, and display policy. |
| `LlmClient.cs` / `LlmResponseParser.cs` | HTTP queue/failover/concurrency and pure provider response parsing. |
| `PawnDiaryMod.cs` / `PawnDiarySettings.cs` | Settings data and settings UI. |
| `ITab_Pawn_Diary*.cs` / `DiaryTextFormat.cs` | Hidden Diary inspect tab, cards, paging, debug controls, and safe rich-text formatting. |
| `MiniJson.cs` | Runtime-safe JSON parser. Do not replace with unsupported dependencies. |

---

## 3. Data Flow

1. A Harmony hook or scanner sees a candidate gameplay moment. Recorders no-op until RimWorld is in
   play, so startup pawn generation and scenario setup do not read calendar ticks too early.
2. The adapter snapshots live RimWorld facts, XML/settings gates, dedup facts, and any RNG result
   that must stay impure.
3. The Event Catalog decides `Drop`, immediate event creation, neutral prompt route, or a batch/
   ambient/day-reflection route.
4. `AddSoloEvent` or `AddPairwiseEvent` creates a saved `DiaryEvent`, semantic `colorCue`, per-POV
   decoration facts, and references from eligible pawn records.
5. Generation queues immediately when possible and is retried by periodic scans.
6. `DiaryPipelineAdapters` copies event/XML/localization/settings state into DTOs.
7. Pure pipeline helpers produce a prompt plan, parse provider output, and postprocess text.
8. `LlmClient` sends requests, handles failover/concurrency, and returns results on the main thread.
9. `ApplyLlmResult` stores success or failure state; title generation and optional generated-speech
   Social-log injection run after successful main entries.
10. `EntriesFor` reads saved events for the Diary tab, which caches built views until the pawn render
   token changes.

`GameComponentTick` drains completed results, flushes main-thread debug logs, recovers orphaned
pending entries, and queues pending work roughly every 120 ticks. Pending generation state resets on
load. Non-neutral POVs below 11% Consciousness are skipped; neutral arrival/death bypass that guard.

---

## 4. Event Sources

| Source | Capture | Output |
|---|---|---|
| Social interactions | `PlayLog.Add` -> `RecordInteraction` | Pairwise, solo, pair batch, or ambient day note by XML group. |
| Mental states | `MentalStateHandler.TryStartMentalState` | `SocialFighting` pairwise when both pawns are eligible; other accepted breaks are solo. |
| Romance relation changes | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise entries for `Lover`, `Spouse`, `ExLover`, `ExSpouse`. |
| Tales and combat tales | `TaleRecorder.RecordTale` plus Tale batch policy | Solo, pairwise, delayed combat batches, or neutral death descriptions. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first-page arrival with prior faction/recruiter/kind/creepjoiner/surroundings context. |
| Deaths | `Pawn.Kill` prefix/postfix plus XML-marked death TaleDefs | Neutral final page with cached cause/context; fallback covers non-Tale deaths. |
| Mood events | `GameConditionManager.RegisterCondition` | Once per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | Temporary memories filtered by XML thresholds/tokens; ambient thoughts can batch. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, and chemical stages when they first appear or worsen. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo entry for the inspired pawn. |
| Hediffs | `Pawn_HealthTracker.AddHediff` plus progression scan | Immediate or day-reflection health entries by XML Hediff policy. |
| Work | Periodic current-job sampling | Skips social/violent work, applies XML odds/cooldowns and `workGenerationWeight`. |
| Raids | `IncidentWorker.TryExecute` (filtered to `IncidentWorker_Raid`) | Once per eligible colonist on the raid's target map. Minimal payload: incident defName, raider faction defName, raid points. |
| Quests | `Quest.Accept`, a defensive `MainTabWindow_Quests` accept-action fallback, a `Quest.EverAccepted` state scan, and `Quest.End` | Only accepted quests are recorded. `Success` -> "completed", `Fail` -> "failed"; one entry per eligible colonist per signal, with description, issuer faction, and rewards context. |
| Rituals | `LordJob_Ritual.ApplyOutcome`, `PsychicRitualGraph.End` | Finished Ideology rituals fan out to author, target pawn when present, participants, and spectators with role/title/status context. Successful Anomaly psychic rituals fan out similarly from the psychic ritual graph, but deliberately omit role/title prompt fields and use darker, stranger instructions. |
| Day reflections | Sleep/rest trigger | One reflective entry per pawn/day only when at least one XML-configured important signal kind exists. The default important kinds are major events, opinion shifts, and health signals; filler can be folded in as background but cannot trigger a reflection by itself unless XML allows it. |

`PlayLog.Add` preflights live pawn eligibility and XML significance before rendering RimWorld's POV
grammar strings, so routine social-log rows that cannot become diary entries stay cheap.
Generated Social-log speech rows patch the concrete RimWorld 1.6 interaction text worker
(`ToGameStringFromPOV_Worker`) with an old-name fallback, so a display-method rename cannot abort
`PatchAll` before the later mood, relation, raid, and quest hooks register.

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

Currently catalog-backed: Thought, Inspiration, MoodEvent, MentalState, Tale, Hediff, Interaction,
Romance, Arrival, Death fallback, Work, ThoughtProgression, DayReflection, Raid, Quest, and Ritual.

DayReflection is a meta-source: the adapter counts all candidate cues plus the subset that are
important enough to justify a reflection. `DiaryTuningDef.daySummaryImportantSignalKinds` controls
which candidate kinds are important (`event`, `opinion`, `hediff`, `filler`). Its pure `Decide`
drops days with no important candidates, so ambient small talk can color a reflection after
something meaningful happened but cannot create one alone by default.

Direct `AddSoloEvent`/`AddPairwiseEvent` call sites that remain outside `RecordXxx` are sinks:
interaction/tale batch flushers, ambient day-note flushers, the generation dispatcher, and the event
factory. They execute routes chosen by catalog-backed sources rather than deciding whether a new
gameplay source should be captured.

Adding a source:

1. Add a `DiaryEventType` value.
2. Add `Source/Capture/Events/XxxEventData.cs` with primitive fields, `Decide`, and any pure context
   string builder.
3. Add `Source/Capture/Specs/XxxEventSpec.cs`.
4. Register it in `DiaryEventCatalog`.
5. Update the impure adapter to snapshot facts, ask the catalog, then execute the returned route.
6. Add/update `DiaryCapturePolicyTests`, including catalog dispatch coverage.

Keep RimWorld/Verse/Unity objects, `.Translate()`, settings reads, `Find.TickManager`, IO, RNG,
dedup mutation, event creation, and LLM queueing in adapters. Pure code must accept DTOs/primitives.

---

## 6. XML Policy

`1.6/Defs/DiaryInteractionGroupDefs.xml` owns group matching, instructions, color cues, batching,
promotion, Hediff policy, and default enablement. Domains include Interaction, MentalState, Tale,
MoodEvent, Thought, Inspiration, Romance, Work, Hediff, Raid, Quest, and Ritual. Matching is domain-scoped
by exact `defName` or substring token; XML order matters and catch-all groups go last. The Quest
domain is unusual: its matchDefNames are lifecycle signals (`accepted`/`completed`/`failed`), not
defNames, because one `DiaryEventType.Quest` fans out to three prompt groups. Saved Quest entries
still keep the real `QuestScriptDef` in their source defName/context; display and prompt-policy
recovery classify them from the saved `signal=` context field.

Ritual-domain entries classify by string markers only. Ideology/Royalty/Biotech/Odyssey rituals use
`Precept_Ritual` defName plus the ritual behavior worker class when available, and carry role facts
in context: `ritual_title`, `ritual_behavior`, `ritual_perspective`, `ritual_role`, `royal_title`,
and `ideological_role`. Anomaly psychic rituals use `psychic_ritual` plus a `PsychicRitual`
classifier prefix, carry only `psychic_ritual_perspective`, outcome, and quality, and deliberately
omit role/title fields. XML groups use these string-only classifiers for DLC-specific edge cases
such as Royalty throne/anima rituals, Biotech childbirth, Odyssey gravship launch/landing, and
Anomaly psychic rituals. Prompt templates render the role/title fields only when present, so
non-ritual and psychic-ritual events do not spend tokens on empty ritual context.
Psychic ritual invoker prompts ask only for invented or broken ritual words inside a speech block;
the visual distortion is applied later by `DiaryTextDecorationDefs.xml` from the saved
`psychic_ritual_perspective=invoker` context while the card keeps the dark color cue.

`DiarySignalPolicyDefs.xml` owns tracker-specific thought/work policy: thresholds, tokens, staged
progression, ambient batching, scan odds, and cooldowns. `DiaryTuningDef.xml` keeps shared fallback
tuning for mood/health buckets, nearby context, minimum first-person age, day-reflection important
signal kinds/weights, weather mention chances, and scanner intervals.

Hediff-domain groups define Immediate vs DayReflection mode, visible/bad/injury gates, severity
thresholds, always qualifiers, add/progression recording, severity steps, dedup, and weights.

Tale-domain groups can declare death victim role lists, keeping death Tale classification data-owned
and DLC/mod friendly.

---

## 7. Prompts And Writing Styles

Prompts are compact `key: value` lines. Empty values and `none` / `n/a` / `unknown` sentinels are
dropped. Prompt templates include pair, solo, batched, day-reflection, death-description,
arrival-description, and title shapes.

The system prompts are intentionally short and only carry global safety/format rules. Event-type
prompt control lives in `DiaryEventPromptDef`: `prompt` renders as `event prompt:`, and `enhancement`
renders as `event enhancement:`. Narrower XML group policy still renders as `instruction:` and
`tone:`. This keeps quests, raids, thoughts, work, health, romance, and other source types tunable
without editing C# or bloating the shared system prompt. The first-person system prompt asks the
model to make supplied facts immediate through one sensory detail, one emotional beat, and one
implied consequence or tension, while still forbidding invented facts.

Each group may also carry **variant pools** (`instructions` / `tones` lists) so the same event type
does not repeat verbatim every time. When a pool has any non-blank entry, one wording is selected
per entry by the pure `PromptVariants.Pick` helper: the `instruction` variant rolls once at capture
(and is persisted on the entry, so it never changes), while the `tone` variant is picked
deterministically by event id so an entry keeps its tone across save/load and regeneration. The
singular `instruction` / `tone` remain as fallback and settings-preview values. Weighting is
XML-owned: list a wording more than once to make it more common. Do not leave blank `<li>` slots —
selection skips whitespace entries, which would misalign the indexed DefInjected translation keys.

Shipped pools are written for **small-model separability**. Each `instructions` pool holds ~3
*lens-distinct* variants — different sensory, narrative, or temporal entry points written in
concrete nouns/verbs (e.g. "open on sound" / "open on the body" / "open on the room") rather than
emotional synonyms. Each tone-bearing group also ships exactly two `tones` variants. Those tone
variants should not be near-synonym mood labels; make one change rhythm/attention (clipped,
ceremonial, tactile, stunned, etc.) and the other change emotional posture (guarded, exposed,
grateful, grieving, etc.) so small models produce visibly different shapes.

Variants must not ask for unsupplied facts ("who heard it", exact words, named aftermath, etc.)
unless the wording also gates that detail behind supplied context; use conditional phrasing such as
"if supplied" or steer toward internal/body/pressure cues instead. Prompt-lab's generated XML
fixtures parse the same variant pools, and `--all-variants` crosses instruction variants, tone
variants, and the prompt-enchantment matrix so lab runs can catch drift in shipped variant wording.

Prompt Studio in mod settings can save per-event overrides for an existing
`DiaryEventPromptDef.prompt` or `.enhancement`. Empty text or text matching the localized XML value
clears the override, so XML remains the default catalog and new event types still start in Defs.

Layer boundaries:

- Impure: event hooks, `DiaryGameComponent`, settings, XML lookup, localization, IO, RNG, save
  mutation, and transport.
- Bridge: `DiaryPipelineAdapters`.
- Pure: `DiaryPromptPlanner`, `PromptAssembler`, `PromptVariants`, `DiaryContextFields`,
  `LlmResponseParser`, `DiaryResponsePostprocessor`, and `DiaryTextDecorations`.

Writing styles come from `DiaryPersonaDef` plus settings overrides/custom rows. The Def and save
field names still say "Persona" for compatibility with older Pawn Diary saves, but the player-facing
feature is writing styles. Weighted selection uses base weight, trait/backstory matches, creepjoiner
bonuses, and duplicate penalties. Style text is appended to first-person system prompts only;
neutral arrival/death and title prompts are style-free. Each built-in rule is phrased as how that
pawn tends to write diary notes, and the stock catalog is intentionally high-contrast for small local
models: sentence count/order, punctuation habits, fragments, repeated words, questions, body logs,
social room-reading, and other concrete mechanics separate the presets more than mood synonyms do.
The wrapper tells the model to follow the rule's concrete sentence shape, opening move, punctuation,
and detail choice, and explicitly not to roleplay a chat persona, add catchphrases, or invent
dialogue.

Prompt enchantments are XML-weighted live pawn context cues. Eligible first-person prompts may add
one localized `important context:` field as pressure, not as the subject unless the event itself
already centers on that fact. Health/capacity cues can appear on any eligible first-person prompt;
important events may also put the pawn's Royalty title or Ideology role into the same weighted pool.
Only one candidate wins, so royal title and ideoligion role cues are mutually exclusive in a single
prompt. These are separate from `DiaryEventPromptDef.enhancement`, which is static event-type
guidance.

Direct speech is allowed only for initiator/single-POV interaction prompts, using one closed
`[[speech]]...[[/speech]]` block when source notes support it. Recipient follow-ups forbid speech
blocks and receive hidden initiator continuity. Before generated text is saved, response cleanup
preserves complete speech blocks but sanitizes hallucinated bracket tags from smaller models:
malformed speech closers are repaired, unpaired speech markers are stripped to prose, unknown
`[[tag]]...[[/tag]]` markers are removed, and bracketed prose like `[[I should speak.]]` is
flattened back into normal text.

Generated speech Social-log injection is currently disabled and hidden. The old compatibility field
is still loaded for save safety, settings force it off, and the LLM-result call site remains
commented out because RimWorld accepted the synthetic row without reliably showing it in the Social
tab UI.

Title generation defaults on. Successful main entries queue a capped title follow-up pinned to the
successful lane when possible.

---

## 8. Settings And UI

Core settings include API lanes, lane routing mode, request tuning (timeout, max concurrency, max
tokens, and temperature), title generation, atmospheric formatting, prompt enchantments,
work/social generation weights, system prompt overrides, per-event prompt/enhancement overrides,
XML-backed event filters, and writing-style presets. RimWorld dev mode also reveals prompt test mode in
mod settings: real gameplay events still assemble their system and user prompts, but the generation
queue marks the POV as prompt-only and never calls the LLM client. Those prompt-only cards are shown
in the Diary tab while dev mode is on so prompt formatting can be checked from live events without
producing generated diary text.

API lanes support OpenAI-compatible Chat Completions, OpenAI Responses, and native Ollama Chat,
including model fetch/pick, per-row connection tests, per-row auth style (Bearer, no auth,
`api-key`, `x-api-key`, or `key=` query), Responses reasoning effort, Ollama thinking output, and
the shared request-tuning block shown inside the expanded connection section. Endpoint URLs
normalize on load/save, not every settings draw, so users can edit or clear the active text field
without it being rewritten mid-typing. Query-key auth replaces any existing `key=` parameter while
preserving URL fragments, and logs strip query strings.

API row order is editable with compact arrow buttons that show their full label on hover. The global
routing mode controls how strongly that order affects primary selection: **Balanced** spreads primary requests equally across enabled rows,
**Prefer top rows** gives earlier rows more primary turns, and **Failover only** always starts with
the first ready row. Row order also remains the failover order in every mode.

Prompt Studio is a collapsible settings section. Its selector contains both shared system prompts
and `DiaryEventPromptDef` event prompt types, and the selected prompt's editor appears in the same
highlighted block as the selector. Writing-style presets likewise use one highlighted block for
summary, add/reset actions, style selection, rule editing, tag toggles, and the selected preset
action.

The Diary surface is still an inspect tab internally, but its normal inspector tab-strip button is
hidden. Selecting one eligible colonist pawn, or a colonist corpse, adds a **Diary** command button
that opens or closes that same tab. Social-log diary links and linked-POV diary navigation also
continue to open the same hidden tab.

The Diary UI shows completed pages in production. Dev mode adds generation enablement, writing-style
picker, pending/raw/failure rows, prompt/status diagnostics, in-progress indicators, transient
formatting preview rows for prose, markdown, speech, combat/social-fight/mental/dark/death
colors, linked cards, writing placeholders, title-pending animation, and atmosphere checks, plus
mock-page fill. Long histories page by in-game year. Cards show date/title, accent, group chip,
model id, linked POV previews, and title-pending animation.

The dev-mode Diary controls also include a **Prompt suite** button (next to the mock-page filler).
It turns prompt test mode on and opens a dropdown of event categories — insult, social fight,
romance, mental break, hediff, inspiration, work, thought, mood event, tale, and day reflection.
The dropdown is driven by a single data-driven registry (`DiaryGameComponent.AllSuiteEntries`), so
adding a future category means appending one entry there and it auto-appears in the menu. Picking a
category deletes any prior test entry and captures exactly **one** prompt-only card for that
category (rendered plainly, with no decoration), routed through the normal generation queue; picking
another replaces it. A companion **Clear test prompts** button deletes every prompt-test entry from
all colonists' diaries. Pair categories also produce a recipient POV card in a second colonist's
diary and are omitted from the menu when no second colonist exists. Death and arrival shapes are
intentionally excluded: a synthetic death/arrival event would become that pawn's diary boundary (see
`ComputeDiaryBounds`) and hide the pawn's real pages, so those two shapes are still tested through
real gameplay hooks. The prompt suite validates prompt planning, queueing, and Diary UI display; it
does not validate Harmony event capture. Use the live hook workflow in §13 for real hook checks.

The Diary tab itself is sized by `tabWidth`/`tabHeight` in `DiaryUiStyleDef.xml`. In dev mode every
expanded entry card also shows a subtle copy button at the bottom-left of the card: clicking it copies the
card's text to the clipboard — the captured prompt for prompt-only cards, otherwise the generated
text — so prompts and output can be pasted out for inspection. The badge rests at ~0.5 alpha,
brightens on hover, and reserves a dev-only footer so it clears the model-name line drawn above it.

`DiaryUiStyleDef.xml` owns visual constants. `DiaryTextFormat` escapes raw model rich-text tags,
then converts light markdown and valid speech markers to Unity rich text. `DiaryTextDecorationDef`
owns display-only decorations; intoxication/anesthesia speech uses the strongest staggered word-size
setting so it is visibly impaired, extreme-dark speech dims selected words instead of using the
strange-chat Zalgo effect, combat/social-fight color cues add stronger hostile/conflict page washes
and header rules, mental-break pages use stronger fractured spacing with their own wash, and generated
text itself is not mutated on save. The Diary tab also highlights live humanlike pawn names mentioned
in rendered prose: colonists use their favorite color when available, slaves/prisoners/enemies/neutral
pawns use XML-backed status colors, and ambiguous or uncolored matches fall back to bold-only text.

---

## 9. LLM Reliability

Each enabled endpoint/model/mode/auth row is an API lane. New requests choose a primary lane by the
saved routing mode. Recipient follow-ups and title requests pin to the prior successful lane when
possible unless that lane is cooling and another lane is ready. Per-lane semaphores enforce
concurrency, and `ServicePointManager.DefaultConnectionLimit` is raised for Mono.

`LlmClient` builds mode-specific URLs/payloads, retries transient errors up to three times per lane,
and surfaces timeout/permanent/empty/incomplete responses as failure text. Transient lane failures
and timeouts apply an automatic runtime cooldown that grows 10s, 20s, 40s, 80s, 160s, then caps at
300s; any success clears that lane's cooldown. Cooling lanes are skipped for primary selection and
failover while another lane is ready, but can still be tried when every lane is cooling. Each
failover pass snapshots lane readiness once so a cooldown expiring or being added mid-loop cannot
skip every lane. Lane identity ignores stale API-key text for `No auth` rows, and saving API
settings prunes cooldowns for removed or reconfigured rows. Generation and model-list HTTP bodies
are streamed with hard byte caps before JSON parsing/logging, so a bad endpoint cannot force an
unbounded response string allocation. Successful responses are trimmed locally to `maxTokens`,
preferring complete sentences for diary/note text.

`LlmResponseParser` extracts typed visible output before fallback fields, falls back from blank
Ollama content to root `response`, and strips structured or transcript-style reasoning/thinking
before debug or save persistence. API keys are never logged or saved in event metadata. New game
sessions cancel stale requests. Orphaned pending entries reset only after two scans.

If no enabled lane has a model, the entry fails with `PawnDiary.Error.NoApiConfigured`.

---

## 10. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`. Event indexes and transient
day-reflection written guards are rebuilt on load, and per-pawn event-id lists prune blank,
duplicate, or dangling references.

Add/remove safety: adding Pawn Diary to an existing save creates an empty diary component on the next
load and starts recording future events only. Removing it is gameplay-safe because the mod does not
persist custom `ThingDef`, `HediffDef`, `ThoughtDef`, job, need, pawn component, map component, or
cross-reference data onto vanilla pawns/maps. The saved diary history lives inside Pawn Diary's own
`GameComponent`; without the mod loaded, the Diary UI/history is unavailable and RimWorld may leave
stale Pawn Diary component XML in the save until the save is rewritten. Default builds do not write
outside that component. Legacy or development saves that used generated Social-log injection may keep
those synthetic vanilla `PlayLogEntry_Interaction` rows, but the generated replacement text requires
Pawn Diary's component and display patch.

`DiaryEvent` saves raw/generated text, statuses/errors, context, source ids, LLM metadata, semantic
`colorCue`, titles, assembled prompts, compact per-POV hediff/trait facts, legacy staggered
intensity, and capped pre-cleanup debug text. Decorated rich text is not persisted.

Classification is inferred from stable `gameContext` fields such as `tale=`, `mental_state=`,
`mood_event=`, `thought=`, `inspiration=`, `romance=`, `work=`, `hediff=`, `raid=`, and `quest=`.

Pending requests are not persisted. Pending statuses reset on load and scans requeue eligible work.
Death and arrival caches evict oldest stale entries at their cap and clear when a new session starts.

TODO: `DiaryEvent` still stores repeated `initiator*`, `recipient*`, and `neutral*` field families.
A future migration should introduce saved role-slot objects, hydrate them from legacy fields, move
callers to slot accessors, and only then consider retiring direct legacy writes.

---

## 11. Runtime And DLC Constraints

- RimWorld runs on Unity Mono. Use only assemblies present in `RimWorldWin64_Data/Managed`.
- Do not add `System.Web.Extensions`, `JavaScriptSerializer`, or external JSON dependencies.
- The mod declares no paid DLC dependency. Optional DLC content must cleanly no-op when absent.
- Prefer XML string `defName` matchers for DLC-aware content; absent DLC defs simply never appear.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks;
  Anomaly creepjoiner checks use the same centralized helper.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use `GetNamedSilentFail` or XML
  string matching.
- XML references to DLC defs need `MayRequire`; plain string matcher lists do not.

---

## 12. Localization

Player-facing UI strings and natural-language prompt text live in
`Languages/English/Keyed/PawnDiary.xml` and are resolved with `.Translate()` on the main thread.
Def text (`label`, `instruction`, `tone`, writing-style `rule`, prompt defs/templates, hediff/body-part
labels) localizes through DefInjected.

Keep DefInjected English stubs in sync for `DiaryInteractionGroupDef`, `DiaryEventPromptDef`,
`DiaryPersonaDef`, and `DiaryPromptDef` when editing XML labels, instructions, tones, event prompts,
writing-style rules, or shared prompts. Variant pools (`instructions` / `tones` lists) localize through
indexed DefInjected keys — `<group.instructions.0>`, `<group.instructions.1>`, `<group.tones.0>`,
etc. — one entry per list position; keep blanks out of the pool so indices stay aligned.

Kept English intentionally: prompt schema labels (`event:`, `role:`, `thought=`), role/sentinel
words (`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`), internal defNames/theme tags,
and background-thread `LlmClient` debug/error strings.

To add a language, copy `Languages/English` to `Languages/<Language>`, translate Keyed values, and
optionally add DefInjected translations for XML Def text.

---

## 13. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Output is `1.6/Assemblies/PawnDiary.dll`. If `MSBuild` is not on `PATH`, locate it with `vswhere`
or use a Visual Studio Developer PowerShell.

Enable hooks:

```powershell
git config core.hooksPath .githooks
```

`.githooks/verify.ps1` checks staged whitespace, XML/project well-formedness, pure tests, Debug
MSBuild, committed DLL freshness, and Harmony freshness. Emergency bypass:
`PAWNDIARY_SKIP_VERIFY_HOOKS=1`.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
```

Pure test harnesses compile without RimWorld/Unity assemblies. `DiaryCapturePolicyTests` covers
Event Catalog decisions and dispatch. `DiaryPipelineTests` covers prompt planning and domain
recovery. `PromptVariantsTests` covers instruction/tone pool selection (fallback, determinism,
negative-seed normalization, blank-skip).

Live RimBridge/GABS hook validation:

Use a disposable save with RimWorld dev mode, RimBridge/GABS connected, and Pawn Diary prompt test
mode enabled. Prompt test mode is still useful for hook validation because it intercepts only after a
real event has reached `QueuePrompt`; a success appears as:

```text
[PawnDiary debug] Captured prompt without generation event=<id> role=<role>
```

Before testing events, restart RimWorld after any DLL rebuild and check the current log for
`[Pawn Diary] PatchAll failed`. A startup PatchAll error can leave later Harmony hooks unregistered,
so no-capture results after such an error are not meaningful.

Suggested low-impact real hook checks:

| Hook family | Debug action path | Expected evidence |
|---|---|---|
| Inspiration | `Actions\Inspiration...\Frenzy_Work` on an eligible colonist | One prompt-only capture for that pawn. |
| Mental state | `Actions\Mental state...\Crying` on a colonist, then `Actions\T: Stop mental state` | One prompt-only capture, then the pawn returns to no mental state. |
| Thought | `Actions\Show more actions\T: Give bad thought` on a colonist | One prompt-only capture from `MemoryThoughtHandler.TryGainMemory`. |
| Social `PlayLog.Add` | `Actions\T: Force Interaction`, choose another colonist, then `insult` | Pairwise prompt-only captures when the social hook is installed and policy allows the interaction. |
| Mood condition | `Actions\Add Game Condition...\Aurora\1 hour` or `...\Eclipse\1 hour` | One prompt-only capture per eligible affected colonist. |

Full live auto-test scenario:

The practical target is one deterministic pass through every implemented source and route shape, not
every XML matcher token. The XML file names many DLC-, mod-, and content-specific defNames that only
exist in matching games; prompt-lab and pure catalog tests cover those policy permutations without a
live colony. The live scenario below proves the Harmony/scanner hooks are installed and that real
RimWorld events can reach prompt construction.

Automation should keep a `lastLogSequence` cursor and count new
`[PawnDiary debug] Captured prompt without generation ...` lines after each step. A solo event should
produce one role capture, a pair event should produce initiator and recipient captures with the same
event id, a neutral arrival/death should produce a neutral capture, and a colony-wide fan-out should
produce one initiator capture per eligible colonist. For stronger assertions than the RimWorld log
allows, add or use a dev dump that reads recent saved `DiaryEvent` rows and reports
`interactionDefName`, `gameContext`, involved pawn ids, and per-role status.

`scripts/gabs/pawndiary-live-smoke.lua` is the smallest script-agent hybrid smoke fixture. Run it
through `rimbridge/run_lua_file` with `includeStepResults=false` to spend one GABS call on a live
mental-state hook check: it snapshots the log cursor, targets one current-map colonist, executes
`Actions\Mental state...\Crying`, cleans up with `Actions\T: Stop mental state`, and fails if no new
PawnDiary prompt-capture log appears.

| Phase | Source/routes covered | Trigger | Expected evidence |
|---|---|---|---|
| 0 | Startup arrival, neutral prompt route | Start a disposable debug colony and wait until playable. | One neutral first-page capture per eligible starting colonist. |
| 1 | Inspiration solo | `Actions\Inspiration...\Frenzy_Work` on one colonist. | One initiator capture. |
| 2 | Mental-state solo | `Actions\Mental state...\Crying` on one colonist, then `Actions\T: Stop mental state`. | One initiator capture. |
| 3 | Temporary thought | `Actions\Show more actions\T: Give bad thought` on one colonist. | One initiator capture from `MemoryThoughtHandler.TryGainMemory`. |
| 4 | Mood event fan-out | `Actions\Add Game Condition...\Aurora\1 hour`. | One initiator capture per eligible affected colonist. |
| 5 | Romance relation pair | `Actions\T: Add/remove pawn relation`, choose `Lover` or `Spouse`, then another eligible colonist. | One event id with initiator and recipient captures. |
| 6 | Social `PlayLog.Add` pair batch | `Actions\T: Force Interaction`, choose another colonist, then `insult`; advance at least 8,000 ticks. | One event id with initiator and recipient captures after the 7,500-tick batch window. |
| 7 | Ambient social route | Force at least three `Chitchat`/small-talk style interactions for the same day, then advance 60,000 ticks or make the pawn rest. | One ambient day-note capture for the pawn when the XML minimum and quiet window are met. |
| 8 | Raid fan-out | Execute a successful low-risk `RaidEnemy` incident with enough points to spawn; 20 points can be refused by vanilla and is not a valid failure. | One initiator capture per eligible colonist on the target map, with a raid context in the saved event. |
| 9 | Quest accepted | Create an offered quest, then accept it through the quest UI or a helper that calls `Quest.Accept`. Do not count offered-but-unaccepted quests. | One initiator capture per eligible colonist; saved context should include `signal=accepted`. |
| 10 | Quest completed | End the accepted quest with `QuestEndOutcome.Success` through a real quest end path or helper. | One initiator capture per eligible colonist; saved context should include `signal=completed`. |
| 11 | Quest failed | Accept a second disposable quest and end it with `QuestEndOutcome.Fail`. | One initiator capture per eligible colonist; saved context should include `signal=failed`. |
| 12 | Ideology ritual completion | In an Ideology-enabled disposable save, complete a ritual such as a speech or dance party through the real ritual flow. In base-game-only runs, mark this source as not applicable. | Separate solo captures for eligible author, target pawn if present, participants, and spectators; saved context should include `ritual=`, `ritual_behavior=`, `ritual_role=`, and `ritual_title=`. |
| 13 | Anomaly psychic ritual completion | In an Anomaly-enabled disposable save, complete a psychic ritual such as void provocation through the real ritual flow. In base-game-only runs, mark this source as not applicable. | Separate solo captures for eligible invoker, target pawn if present, participants, and spectators; saved context should include `psychic_ritual=` and `psychic_ritual_perspective=`, should not include `ritual_role=` or `ritual_title=`, and invoker prompts should request one `[[speech]]...[[/speech]]` block with unsettling invented ritual speech. |
| 13 | Tale non-death | Trigger a real `TaleRecorder.RecordTale` path such as successful research completion, surgery, taming, or crafting. Avoid debug actions that only mutate state. | One capture or batch capture according to the matched Tale group. |
| 14 | Combat tale batch | Cause a controlled non-lethal wound/downing in a disposable save, then advance at least 8,000 ticks. | A delayed tale batch capture after the 7,500-tick combat-tale window. |
| 15 | Death neutral route | Kill a disposable extra colonist in the test save through a real kill path. | One neutral death-description capture, or the death fallback capture if vanilla emits no death Tale. |
| 16 | Hediff day-reflection route | Add a bad non-injury hediff that passes XML policy, such as a sufficiently severe disease; make the pawn sleep/rest. | One `DayReflection` capture for that pawn with a health signal in saved context. |
| 17 | Hediff immediate route | In a Biotech-enabled disposable save, add `PregnantHuman`/`Pregnant` or `PregnancyLabor`. In base-game-only runs, mark this source as not applicable. | One immediate initiator capture for pregnancy/labor. |
| 18 | Thought progression scanner | Drive food, rest, outdoors, or chemical desire into a configured stage, then advance at least 250 ticks. | One initiator capture when the configured stage first appears or worsens. |
| 19 | Work scanner | Put a colonist into a stable non-social, non-violent job and run long enough for work scans; for deterministic CI, use a temporary test policy with work odds at 100% and cooldowns at 0. | One initiator capture classified as passion, strain, routine, or dark-study work. |
| 20 | Day reflection from major event/opinion/filler | Accumulate at least one important day-summary signal, then make the pawn sleep/rest. | One `DayReflection` capture for the pawn/day; filler alone should not trigger unless XML policy says so. |

Quest checks are required for "all mod events." The quest source has three distinct lifecycle groups:
accepted, completed, and failed. A single accepted quest only covers the first group. The automation
needs either stable debug actions that create/select/end quests, or a small RimBridge/test helper that
creates a disposable quest, calls `Quest.Accept`, and calls `Quest.End(Success/Fail)`. If the global
RimWorld debug-action tree crashes while enumerating actions, use known stable paths or the helper
instead of treating enumeration failure as a Pawn Diary failure.

Use more disruptive hooks only in disposable saves. Generic health actions such as `Flu` may route
to day reflection or fail severity policy, so no immediate prompt does not by itself disprove the
`AddHediff` hook. Some direct research/debug actions set state without emitting a Tale, so no capture
there does not prove `TaleRecorder.RecordTale` is broken. For raids and quests, prefer controlled
low-point incidents or natural accepted quests and verify after the startup log is clean.

When a live action produces no prompt-only log, classify the miss before changing code: vanilla may
have refused the action, XML/settings policy may have dropped it, the debug action may bypass the
vanilla hook, the pawn may be ineligible/incapacitated, or Harmony may not have installed the hook.
Prompt-suite entries are not a substitute for this pass because they synthesize `DiaryEvent`s inside
the mod instead of entering through RimWorld's event sources.

Prompt lab:

```powershell
cd prompt-lab
npm test
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

`npm test` runs the C#/JS assembler golden check and verifies the generated all-variants fixture
set covers every current XML prompt template, every `DiaryEventPromptDef` prompt/enhancement,
configured prompt-enchantment variants, runtime context marker families, and the repeated-pass path.

Release payloads are made with `scripts/publish.ps1`; it builds a throwaway Release DLL and copies
only runnable mod files, including runtime textures, into `dist/<published packageId>`.

---

## 14. Changelog Policy

`CHANGELOG.md` is a milestone history, not a commit log. Add or update a dated section only for
user-visible changes, architecture changes, migrations, important fixes, release work, or known
issues. Prefer one grouped bullet over several micro-entries.
