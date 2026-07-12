# Changelog

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
