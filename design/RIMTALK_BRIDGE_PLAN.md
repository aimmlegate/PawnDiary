# RimTalk Bridge — detailed implementation plan (post-API-v1)

> Status: **shipped** — steps 0–8 are implemented and merged (Level 0/1/2, persona sync Tiers A/B,
> conversation capture, engine mode). Reset baseline completed 2026-07-06: the old log-only bridge
> source was removed and replaced with an example-adapter-derived `PawnDiaryApi` facade plus
> `GameComponent` hook owner. This is a working plan, not a contract — shipped behavior is documented
> in `../DOCUMENTATION.md`, the public API contract in `../INTEGRATIONS.md`.
>
> **Follow-up work** — the two remaining features from the 2026-07-09 research handoff (global
> `{{colony_events}}` context and `{{diary_shared}}` pair shared memory) are **not** covered here;
> they are planned separately in [RIMTALK_BRIDGE_CONTEXT_EXTENSION_PLAN.md](RIMTALK_BRIDGE_CONTEXT_EXTENSION_PLAN.md).

Turn the reset scaffold in `integrations/PawnDiary.RimTalkBridge/` into the real Pawn Diary ⇄
RimTalk bridge, plus one core XML compat group. Written to be executable by a less-capable coding
agent: follow steps in order, don't improvise beyond marked decision points.

## Context (why)

- Prior roadmap Phase 6 (RimTalk adapter) predates the finalized external API. Public API v1
  (internal v22) **already ships everything needed** — submissions, wrapped prompt entries, direct
  text, context bundles/snapshots, status listeners, style reads/overrides, context providers,
  budget guardrails, dedup keys. **No new core C# / API members are required.**
- Bug being fixed on the way: the Interaction-domain catch-all group `other`
  ([DiaryInteractionGroupDefs.xml](../1.6/Defs/DiaryInteractionGroupDefs.xml), order 150, no
  batch policy) claims RimTalk's `RimTalkInteraction` PlayLog entries today, so **every RimTalk
  chat line becomes an individual diary event candidate** (spam + duplicates).

**User decisions:**
- Both directions: RimTalk chats → diary entries AND an optional "RimTalk AI provider" engine toggle.
- Ordinary chatter handled by **native PlayLog capture** (core XML compat group); the bridge
  explicitly records only **important** conversations (importance defined in code, thresholds tunable).
- "Affect RimTalk chat" = **passive context only** (no triggered chats).
- Persona/writing sync: **code-only**; LLM reconciliation deferred (designed, not built).
- Target **base RimTalk only**: `cj.rimtalk`, workshop 3551203752, v1.0.13, net48.

## Verified facts (do NOT re-research; ground truth = installed DLL v1.0.13)

- RimTalk public API (`RimTalk.API`): `ContextHookRegistry` and façade `RimTalkPromptAPI`.
  - `RegisterPawnVariable(variableName, modId, Func<Pawn,string>, description, priority)` —
    variable is only rendered **when a Scriban template references it** (`{{pawn1.<name>}}`).
  - `RegisterPawnHook(ContextCategory, HookOperation{Append,Prepend,Override}, modId,
    Func<Pawn,string,string>, priority)` — applied by RimTalk's `ContextBuilder` for rendered
    categories (verified in source for Location/Terrain/Beauty/Cleanliness/Surroundings).
  - `InjectPawnSection(sectionName, modId, ContextCategory anchor, InjectPosition{Before,After},
    Func<Pawn,string>, priority)` — anchored new sections (rendering path: **verify in-game**, ⚠️ U1).
  - `UnregisterMod(modId)`, `RemovePromptEntriesByModId(modId)` exist for cleanup.
  - `ContextCategories.Pawn.*` includes `Thoughts`, `Personality`, `Surroundings`, etc.
- `RimTalk.Service.TalkService.CreateInteraction(Pawn, TalkResponse)` — **private static**, fires
  once per displayed chat line, creates `PlayLogEntry_RimTalkInteraction` (InteractionDef defName
  `RimTalkInteraction`; the rendered log string is the real AI chat text). The deleted diagnostic
  bridge previously targeted it with a null-guarded `TargetMethod` — re-add that pattern when Step 5
  restores the listener.
- `RimTalk.Data.TalkResponse`: `Guid Id`, `Guid ParentTalkId` (reply chains; no explicit
  "conversation ended" event), `TalkType` enum {Urgent, Hediff, LevelUp, Chitchat, Event,
  QuestOffer, QuestEnd, Thought, User, Other}, `GetInteractionType()` → {None, Insult, Slight,
  Chat, Kind}, `Name`, `TargetName`, `Text`, `GetText()`, `GetTarget()` (may throw — wrap).
- `RimTalk.Service.AIService.Query<T>(TalkRequest)` where `T : class, IJsonData`: sends
  `request.Context` as system + `request.Prompt` as user via the player's configured provider,
  JSON-deserializes into `T`, null on failure, runs on background Tasks. `AIService.IsBusy()` exists.
- `RimTalk.Data.PersonaService.GetPersonality(Pawn)` / `SetPersonality` (we only ever read).
- RimTalk source: https://github.com/jlibrary/RimTalk (`Source/API/ContextHookRegistry.cs`,
  `Source/Service/*.cs`) — use raw.githubusercontent.com fetches to check signatures/semantics.
- Core template to clone for the compat group: `speakup_chitchat`
  ([DiaryInteractionGroupDefs.xml](../1.6/Defs/DiaryInteractionGroupDefs.xml)) — matchPackageIds +
  `AmbientDayNote` batch + promotion policy.
- Bridge packageId: `aimmlegate.pawndiary.rimtalkbridge`. Core packageId currently
  `aimmlegate.pawndiary.development` (⚠️ U7).

## ⚠️ Open items — decided vs. verify vs. user-input

Marked inline as U1…U9. Summary:

| # | Item | Status |
|---|---|---|
| U1 | Does RimTalk render `InjectPawnSection` sections into the default prompt? Which anchor category? | **Verify in-game** (Step 3 spike). Decision rule given; fallback = `RegisterPawnHook(Append)` on `Pawn.Thoughts`. |
| U2 | Numeric defaults: quiet window 2500, minReplies 4, caps 2/pawn/day + 6/colony/day, pair gap 30000, transcript 4×160, context 3 entries, cache TTL 2500 | **Provisional** — all settings-backed; tune in playtest, don't bikeshed in code. |
| U3 | Creative wording of instruction/tone pools for both new groups | **Draft included; user will likely rewrite** (they own prompt voice). Flag in PR/summary. |
| U4 | Russian Keyed/DefInjected text | Agent drafts natively-styled RU, but **final RU needs the user's native pass** — never calque EN. |
| U5 | Engine mode: exact members of `RimTalk.Data.IJsonData`; whether `TalkRequest` props have public setters; JSON-suffix wording | **Verify from source before coding Step 7** (fetch URLs given). |
| U6 | Does RimTalk also rewrite *vanilla* interaction texts (`InteractionTextPatch`) causing overlap with existing groups? | **Out of scope v1** — compat group claims only `RimTalkInteraction` + `cj.rimtalk` defs; revisit if playtest shows duplicates. |
| U7 | Core packageId is `.development`; bridge About.xml depends on it | Known repo-wide issue; update when release id is chosen. No action now. |
| U8 | Tier-B override rule template wording; persona-change detection interval | Provisional template given; interval = context cache TTL. |
| U9 | Throttle counters are **not saved** (reset on game load) | **Decided simplification** for v1 — worst case a reload re-allows a few entries that day. |

## Design summary

**Bridge settings — integration levels** (dropdown, cumulative) + advanced overrides:
- **Off** — patch callback returns immediately; nothing registered payload-wise (registrations
  stay installed but return empty).
- **Level 1 "Shared context"** (default): diary memories (+ optional diary-voice line) injected
  into RimTalk prompts; RimTalk persona injected into diary pawn summaries (`chat_persona=`).
- **Level 2 "Conversations"**: + important RimTalk conversations become diary entries.
- Advanced toggles: diary-voice line (on), persona-led diary voice (Tier B, **off**, experimental),
  RimTalk-as-engine (**off**), dev chat logging (**off**; migrated from old `enabled`), numeric
  tunables (U2 list).

**Persona/writing sync tiers** (code-only; recorded rule: shared identity/memory, separate voices):
- **Tier A (ships, L1)**: mutual awareness only. Diary sees `chat_persona=<first sentence of
  RimTalk persona>` via `RegisterPawnContextProvider`; RimTalk sees `diary voice: <style rule>` +
  memories as *knowledge*. No state mutation anywhere.
- **Tier B (ships, advanced, off)**: persona-led diary voice — apply
  `SetWritingStyleOverride(pawn, modId, "voice shaped by their character: <persona first
  sentence>")`; reapply when persona text hash changes; `ResetWritingStyleOverride` for all
  touched pawns on toggle-off (core enforces source ownership, so reset is safe/idempotent).
  Rejected alternatives (do not implement): keyword-mapping persona→style catalog (brittle,
  breaks on RU), writing into RimTalk's persona via `SetPersonality` (mutates another mod's save).
- **Tier C (deferred, do NOT build)**: LLM reconciliation — one rate-limited
  `AIService.Query<ReconciledIdentity>` (JSON: `sharedIdentityLine`, `diaryVoiceRule`) reusing the
  Tier-B override slot + both injectors. Costs recorded: RimTalk tokens/pawn, bridge save data for
  caching, non-determinism. Reuses seams built here; nothing blocks adding it later.

**Inbound split:** core XML compat group gives ordinary chatter the ambient-day-note treatment
(one solo background note per pawn/day, existing machinery); the bridge separately submits only
important conversations as real entries. Overlap between an ambient sample line and an explicit
entry is accepted by design (background vs standout).

---

# Implementation guide

## Step 0 — ground rules for the implementing agent

- Read `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md` first; they are binding.
- Current source baseline: `integrations/PawnDiary.RimTalkBridge/Source/` now contains only
  `RimTalkBridgeGameComponent.cs`, `PawnDiaryRimTalkBridgeApi.cs`, and the project file. References
  below to replacing the old logger/settings mean "add the new implementation from this scaffold";
  `RimTalkCreateInteractionPatch`, `RimTalkChatLogger`, and the single-bool Mod settings class no
  longer exist.
- **C# constraints:** bridge csproj is `TargetFrameworkVersion v4.8`, **`LangVersion 7.3`** — no
  switch expressions, no `??=`, no using-declarations, no ranges. Core is net472 (untouched here).
- **Every `.cs` file**: header comment explaining its role, `///` summaries on public members,
  novice-oriented `//` notes (match existing bridge files' density).
- **Localization:** any string reaching UI or LLM prompt = Keyed (`"Key".Translate()`) or
  DefInjected (Def text). Carve-outs that stay English: structured schema labels in prompt
  context (`talk_type=`, `exchanges=`, `said_1=` keys — values still come from game text), log
  messages. RU mirrors required (⚠️ U4).
- **Save-data tokens are frozen once shipped:** eventKey `rimtalkbridge_conversation`, group
  defNames, `syntheticDefName`, settings Scribe keys. Choose once, never rename.
- **XML instruction/tone pools:** no blank `<li>` entries (RU DefInjected indexes by position);
  never delete a translated list row later — disable the group instead.
- Builds (run after every change; PS 5.1 — no `&&`):
  - Core (XML-only here, but verify hook parses XML + builds):
    `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`
  - Bridge: `MSBuild integrations\PawnDiary.RimTalkBridge\Source\PawnDiaryRimTalkBridge.csproj /t:Build /p:Configuration=Debug`
    (find MSBuild via `vswhere -latest -find MSBuild\**\Bin\MSBuild.exe` if not on PATH).
  - Stage the rebuilt `integrations/PawnDiary.RimTalkBridge/1.6/Assemblies/*.dll` like the core DLL.
- Deploy for in-game testing: `powershell -ExecutionPolicy Bypass -File scripts\deploy-integrations.ps1`.
- Docs are part of done: `DOCUMENTATION.md` section + dated `CHANGELOG.md` line **in the same change**.
- **RimTalk-type isolation:** any class that mentions `RimTalk.*` types in signatures/bodies must
  only be entered through a guard (`ModsConfig.IsActive("cj.rimtalk")` check +
  `[MethodImpl(MethodImplOptions.NoInlining)]` on the entry method) so a missing RimTalk never
  throws `TypeLoadException`. When the listener is reintroduced, keep the old null-guarded
  `TargetMethod` + try/catch around patch installation pattern. About.xml declares the dependency,
  but RimWorld does not hard-enforce it.

## Step 1 — core compat group (XML only; standalone value)

**Status: complete in code/docs as of 2026-07-06.** Added
`1.6/Defs/Compat/DiaryCompat_RimTalk.xml` with the `rimtalk_chatter` group gated on packageId
`cj.rimtalk`, matching `RimTalkInteraction`, capturing rendered text, and using SpeakUp's
`AmbientDayNote` batch + promotion policy. Added EN/RU `DiaryInteractionGroupDef` DefInjected rows,
updated `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, and `CHANGELOG.md`. XML parse and core build are
the required local verification; in-game RimTalk/without-RimTalk matrix remains a manual smoke test.

**Files:** NEW `1.6/Defs/Compat/DiaryCompat_RimTalk.xml`; RU DefInjected rows;
`DOCUMENTATION.md`, `CHANGELOG.md`.

1. Create `1.6/Defs/Compat/DiaryCompat_RimTalk.xml` (Defs load recursively — no registration
   needed). Content — clone of `speakup_chitchat` with these values:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<!-- Compatibility group for RimTalk (cj.rimtalk). RimTalk logs every AI chat line as a
     RimTalkInteraction PlayLog entry; without this group the Interaction catch-all turns each
     line into its own diary event. This group routes them into one ambient day-note per pawn.
     Inert and invisible unless RimTalk is in the mod list (enableWhenPackageIdsLoaded). -->
<Defs>
  <PawnDiary.DiaryInteractionGroupDef>
    <defName>rimtalk_chatter</defName>
    <label>RimTalk conversations</label>
    <order>17</order>
    <domain>Interaction</domain>
    <defaultEnabled>true</defaultEnabled>
    <important>false</important>
    <captureRenderedGameText>true</captureRenderedGameText>
    <enableWhenPackageIdsLoaded>
      <li>cj.rimtalk</li>
    </enableWhenPackageIdsLoaded>
    <batch>
      <mode>AmbientDayNote</mode>
      <windowTicks>60000</windowTicks>
      <maxEvents>999</maxEvents>
      <syntheticDefName>RimTalkAmbientDay</syntheticDefName>
      <labelKey>PawnDiary.Event.AmbientSocialLabel</labelKey>
      <headerKey>PawnDiary.Event.AmbientSocialHeader</headerKey>
      <fallbackKey>PawnDiary.Event.AmbientSocialFallback</fallbackKey>
      <instructionKey>PawnDiary.Event.AmbientSocialInstruction</instructionKey>
      <minEventsToWrite>8</minEventsToWrite>
      <maxSampleLines>3</maxSampleLines>
    </batch>
    <promotion><!-- copy the numeric block verbatim from speakup_chitchat --></promotion>
    <instruction>ordinary conversation during the day - treat it as passing talk unless mood, need, or the pair's history makes a line land harder</instruction>
    <instructions>
      <li>day-to-day talk - pick the one supplied line or moment that left a trace, and why it did</li>
      <li>day-to-day talk - write the feeling under the words: ease, friction, or distance</li>
      <li>day-to-day talk - keep it small and true; no invented quotes beyond the supplied lines</li>
    </instructions>
    <tone>with light, everyday social texture</tone>
    <tones>
      <li>loose and conversational, like talk drifting through the room</li>
      <li>plain and observant, small words carrying small weights</li>
    </tones>
    <matchDefNames>
      <li>RimTalkInteraction</li>
    </matchDefNames>
    <matchPackageIds>
      <li>cj.rimtalk</li>
    </matchPackageIds>
  </PawnDiary.DiaryInteractionGroupDef>
</Defs>
```
   ⚠️ U3: instruction/tone wording above is a draft — flag for the user's rewrite pass. Pawn
   perspective must never mention "RimTalk"/"AI".
2. RU: add DefInjected rows for `rimtalk_chatter` (label/instruction/instructions.N/tone/tones.N)
   — find the existing core RU DefInjected file for `DiaryInteractionGroupDef` under
   `Languages/Russian/DefInjected/` and mirror its structure exactly. ⚠️ U4.
3. Docs: `DOCUMENTATION.md` (compat/integrations section — add the group + why), `CHANGELOG.md`
   dated entry. If `EVENT_PROMPT_MAP.md` lists Interaction groups, add a row there too.

**Verify:** core build passes; `.githooks/verify.ps1` XML checks pass. In-game with RimTalk
loaded: the settings row "RimTalk conversations" appears; chats no longer create per-line
"A quiet day" entries; an ambient note appears after ≥8 chats. Without RimTalk: no settings row,
no errors.

## Step 2 — bridge settings model + level gating

**Files:** NEW `integrations/PawnDiary.RimTalkBridge/Source/PawnDiaryRimTalkBridgeMod.cs`,
`Languages/English/Keyed/PawnDiaryRimTalkBridge.xml`.

1. Create the Mod/settings owner with (Scribe keys frozen — comment that in code). The deleted
   diagnostic bridge used a single `enabled` key; keep that key as the dev-logging migration path:

```csharp
public class PawnDiaryRimTalkBridgeSettings : ModSettings
{
    // 0 = Off, 1 = Shared context, 2 = + Conversations. Stored as int for save stability.
    public int integrationLevel = 1;
    public bool devChatLogging;              // migrated: old Scribe key "enabled"
    public bool includeDiaryVoiceLine = true;
    public bool personaLedDiaryVoice;        // Tier B, experimental
    public bool useRimTalkEngine;            // engine mode
    public int contextEntryCount = 3;
    public int conversationQuietTicks = 2500;
    public int minRepliesForImportant = 4;
    public int perPawnDailyCap = 2;
    public int colonyDailyCap = 6;
    public int pairMinGapTicks = 30000;
    public int transcriptLineCap = 4;

    public override void ExposeData()
    {
        // "enabled" is the pre-rework key: old installs had a single log-chats checkbox.
        Scribe_Values.Look(ref devChatLogging, "enabled", false);
        Scribe_Values.Look(ref integrationLevel, "integrationLevel", 1);
        // ... one Look per field, defaults as above ...
    }
}
```
2. Settings UI (`DoSettingsWindowContents`): level as three radio rows (Off / Shared context /
   + Conversations) using `Listing_Standard.RadioButton`, then a "Advanced" gap + checkboxes and
   `TextFieldNumericLabeled` for the ints. Every label/desc = Keyed string. Add all new keys to the
   English Keyed file now (settings labels, level names, descriptions); RU mirror in Step 8 (⚠️ U4).
3. Convenience accessors on the mod class: `internal static bool LevelAtLeast(int lvl)` null-safe.
4. No logger exists after the reset. When a dev chat logger is reintroduced, gate it on
   `Settings.devChatLogging` rather than the old `enabled` Scribe key.

**Verify:** bridge builds; in-game settings window shows levels + advanced; toggling persists;
an old config (with `<enabled>True</enabled>`) loads with dev logging on and level defaulting to 1.

## Step 3 — Level 1 outbound: diary context into RimTalk prompts

**Files:** NEW `Source/Pure/ContextFormat.cs`, NEW `Source/DiaryContextInjector.cs`,
NEW `Source/BridgeGameComponent.cs`; edit mod ctor.

1. `Source/Pure/ContextFormat.cs` — **no RimWorld/RimTalk usings** (pure, testable):

```csharp
public sealed class DiaryMemoryLine { public string Title; public string Summary; public string Date; }

public static class ContextFormat
{
    // Builds the section RimTalk sees. Returns "" when there is nothing to say.
    // maxChars is a hard cap; truncate whole lines, never mid-line.
    public static string BuildDiarySection(
        List<DiaryMemoryLine> entries, string styleRule, bool includeStyle, int maxChars);
}
```
   Output shape (one string, newline-separated; label text passed in already-translated by the
   caller — pure code never calls `.Translate()`):
   `recent diary memories:` then `- <title> (<date>): <summary>` per entry, then optionally
   `diary voice: <styleRule>`. Caller passes the translated label strings as parameters.
2. `Source/DiaryContextInjector.cs` (impure; RimTalk-typed → NoInlining + IsActive guard):
   - Per-pawn cache `Dictionary<string, CachedSection>` (`pawn.GetUniqueLoadID()` → text + tick).
   - `RefreshFor(Pawn pawn)` (main thread only): reads
     `PawnDiaryApi.GetContextSnapshot(pawn, Settings.contextEntryCount)` +
     `PawnDiaryApi.GetWritingStyle(pawn)` and stores `ContextFormat.BuildDiarySection(...)`.
     Empty/ineligible → cache "".
   - `SectionFor(Pawn pawn)` — **cache read only, no API calls** (RimTalk may call this during
     prompt assembly; PawnDiaryApi reads are main-thread-only and must never run inside RimTalk's
     pipeline). Return "" when level < 1 or `!PawnDiaryApi.IsExternalApiEnabled`.
   - `RegisterAll()` called once from the mod ctor inside try/catch:
     - `ContextHookRegistry.InjectPawnSection("pawn_diary_memories", ModId,
       ContextCategories.Pawn.Thoughts, InjectPosition.After, SectionFor, 100)` ⚠️ U1
     - `ContextHookRegistry.RegisterPawnVariable("diary", ModId, SectionFor,
       "<Keyed description>", 100)` (for template-editing players, `{{pawn1.diary}}`).
     ⚠️ Before coding, confirm the exact parameter order against
     `https://raw.githubusercontent.com/jlibrary/RimTalk/main/Source/API/ContextHookRegistry.cs`
     (verified meaning: sectionName/variableName, modId, anchor/provider, position, provider,
     priority — but trust the source file, not this line).
3. `Source/BridgeGameComponent.cs` — `public class BridgeGameComponent : GameComponent` with the
   `(Game game)` ctor. In `GameComponentTick`, every 250 ticks: if level ≥ 1, refresh the context
   cache for pawns whose cache is older than `conversationQuietTicks` (reuse as TTL), iterating
   `PawnsFinder.AllMaps_FreeColonistsSpawned`. Also register (once, in `FinalizeInit`)
   `PawnDiaryApi.RegisterEntryStatusListener("aimmlegate.pawndiary.rimtalkbridge.status", s => ...)`
   that marks the affected pawn's cache stale so fresh entries appear in chat context quickly.
4. **⚠️ U1 spike (do before polishing):** deploy, enable RimTalk debug window (RimTalk settings /
   its Overlay/DebugWindow), trigger a chat for a pawn with diary entries, and inspect the built
   prompt. If the injected section does NOT appear: switch primary mechanism to
   `ContextHookRegistry.RegisterPawnHook(ContextCategories.Pawn.Thoughts, HookOperation.Append,
   ModId, (pawn, current) => current + "\n" + SectionFor(pawn), 100)` and keep `InjectPawnSection`
   removed. Record the outcome in DOCUMENTATION.md.

**Verify:** with L1 + a pawn that has ≥1 completed entry, RimTalk's prompt shows the section; L0
shows nothing; a pawn with no entries contributes "" (no empty header).

## Step 4 — persona sync (Tier A + Tier B)

**Files:** NEW `Source/PersonaSync.cs`; edit `BridgeGameComponent`, mod ctor.

1. Tier A: in mod ctor (guarded), register
   `PawnDiaryApi.RegisterPawnContextProvider("aimmlegate.pawndiary.rimtalkbridge.persona", ProvideLine)`.
   `ProvideLine(Pawn)`: null when level < 1 or persona empty; else
   `"chat_persona=" + FirstSentenceCap(PersonaService.GetPersonality(pawn), 200)`.
   `FirstSentenceCap` goes in `Source/Pure/ContextFormat.cs` (pure, testable). Core sanitizes/caps
   provider lines again — that's fine, defense in depth.
2. Tier B (all inside `PersonaSync`, driven from the component's 250-tick pass, main thread):
   - In-memory `Dictionary<string, int> appliedPersonaHash` (pawnId → hash). Not saved (⚠️ U9-like:
     after reload, overrides are re-applied on the first pass — harmless because Set is idempotent).
   - When `personaLedDiaryVoice` is on and level ≥ 1: for each free colonist, hash the persona
     text; if changed/missing →
     `PawnDiaryApi.SetWritingStyleOverride(pawn, ModId, BuildVoiceRule(personaFirstSentence))`
     where `BuildVoiceRule` uses a Keyed format string
     (EN draft: `"voice shaped by their character: {0}"`) ⚠️ U8.
   - When the toggle turns off (track previous value): for each pawn in `appliedPersonaHash`
     (and once over all free colonists for safety) call
     `PawnDiaryApi.ResetWritingStyleOverride(pawn, ModId)`; clear the dictionary. Core refuses to
     clear other sources' overrides, so this is safe.
3. Settings tooltip for Tier B must say "experimental; a character description is a cruder voice
   rule than the curated styles" (Keyed).

**Verify:** API-explorer `GetPawnSummary` shows `chat_persona=` in providerLines at L1; Tier B on
→ `PreviewPrompt` (explorer) shows the override rule in the prompt; Tier B off → rule gone;
player-picked styles untouched (`GetWritingStyle` still returns base style by contract).

## Step 5 — Level 2 inbound: important conversations → diary entries

**Files:** NEW `Source/Pure/ConversationAssembly.cs`, `Source/Pure/ImportancePolicy.cs`,
`Source/Pure/ThrottlePolicy.cs`; NEW `Source/ConversationTracker.cs`; NEW
`1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml`; NEW `RimTalkCreateInteractionPatch.cs`; edit
`BridgeGameComponent.cs`, Keyed EN.

1. Pure DTOs + assembler (no RimWorld/RimTalk types — bridge-owned enums):

```csharp
public enum BridgeTalkKind { Urgent, Hediff, LevelUp, Chitchat, Event, QuestOffer, QuestEnd, Thought, User, Other }
public enum BridgeSocialKind { None, Insult, Slight, Chat, Kind }

public sealed class ConversationLine
{
    public string TalkId; public string ParentTalkId;   // Guid.ToString(), "" when empty Guid
    public string SpeakerId; public string SpeakerName;
    public string TargetId;                              // "" for monologue
    public string Text; public BridgeTalkKind Kind; public BridgeSocialKind Social; public int Tick;
}

public sealed class Conversation
{
    public string RootTalkId; public List<ConversationLine> Lines = new List<ConversationLine>();
    public int FirstTick; public int LastTick;
    public List<string> ParticipantIds();               // distinct speaker+target ids, ordered
}

public sealed class ConversationAssembler
{
    // Adds a line: joins the conversation containing ParentTalkId, else starts a new root.
    public void Record(ConversationLine line);
    // Removes and returns conversations quiet for >= quietTicks (or all, for FlushAll on save).
    public List<Conversation> FlushQuiet(int nowTick, int quietTicks);
    public List<Conversation> FlushAll();
}
```
   Internals: `Dictionary<string,string> talkIdToRoot` + `Dictionary<string,Conversation> byRoot`.
   Defensive cap: if a conversation exceeds 64 lines, force-flush it (comment why: runaway chains).
2. `ImportancePolicy` (pure): `public static bool IsImportant(Conversation c, int minReplies)` —
   true when ANY line has `Kind ∈ {Urgent, Event, QuestOffer, QuestEnd}`, OR any line has
   `Social ∈ {Insult, Slight, Kind}`, OR `c.Lines.Count >= minReplies`; AND the conversation has
   ≥ 2 distinct participants (monologues never important). Add
   `public static string Explain(...)` returning the matched reason for dev logging. ⚠️ U2.
3. `ThrottlePolicy` (pure): holds `perPawnToday`, `colonyToday`, `dayIndex`,
   `lastTickByPairKey` (pair key = ordered `idA|idB`); method
   `TryReserve(string idA, string idB, int nowTick, int dayIndex, Limits limits)` — resets
   counters when dayIndex changes; enforces per-pawn cap (both pawns), colony cap, pair min gap.
   Counters are in-memory only (⚠️ U9, decided).
4. `Source/ConversationTracker.cs` (impure shell): static entry
   `RecordDisplayedChat(Pawn speaker, TalkResponse talk)` — maps to `ConversationLine` using safe
   wrappers for `GetTarget()`/`GetText()`/log cleanup (the deleted logger had this shape; recreate it
   in a shared internal helper rather than duplicating); maps RimTalk enums to bridge enums with a
   `switch` defaulting to `Other`/`None`; also keeps `Dictionary<string, Pawn> pawnById` (updated
   per line) so flush can resolve live pawns; skips everything when level < 2.
5. Patch change: postfix calls `ConversationTracker.RecordDisplayedChat(pawn, talk)` first, then
   the dev logger when `devChatLogging`.
6. Component: in the 250-tick pass (level ≥ 2), `FlushQuiet(now, Settings.conversationQuietTicks)`;
   for each conversation: `ImportancePolicy.IsImportant` → resolve subject/partner pawns
   (subject = participant with most lines; partner = the other; both must resolve to live,
   spawned pawns or drop) → `ThrottlePolicy.TryReserve` → submit:

```csharp
var result = PawnDiaryApi.SubmitPromptEntry(new ExternalPromptEntryRequest
{
    sourceId = "aimmlegate.pawndiary.rimtalkbridge",
    eventKey = "rimtalkbridge_conversation",          // FROZEN save token
    subject = subjectPawn,
    partner = partnerPawn,
    summaryText = "PawnDiaryRimTalkBridge.Event.ConversationSummary".Translate(partnerName, lineCount),
    promptInstruction = "PawnDiaryRimTalkBridge.Event.ConversationInstruction".Translate(),
    extraContext = BuildExtraContext(conversation),    // see below
    dedupKey = "rimtalkbridge|" + conversation.RootTalkId,
    // forceRecord stays false: respect external budget + pipeline gates.
});
```
   `BuildExtraContext` (pure-ish, string list): `talk_type=<dominant kind, lowercase>`,
   `exchanges=<count>`, then up to `transcriptLineCap` lines `said_N=<SpeakerName>: <text>` each
   capped at 160 chars. Schema keys stay English (carve-out); values are game text.
   EN Keyed drafts: summary `"Talked with {0} — {1} exchanges."`, instruction
   `"Write what this conversation stirred or changed for the writer — the feeling and aftermath,
   not a transcript."` ⚠️ U3.
   Dev-log the outcome (`SubmitEventOutcome` overload not needed; log `result.recorded` + reason
   via `Explain`) when `devChatLogging`.
7. Also call `FlushAll()` + submit from `GameComponent.ExposeData()` on `Scribe.mode ==
   LoadSaveMode.Saving`? **No** — simpler decided behavior: on save, pending conversations are
   simply dropped (they're seconds of chatter; core's own batches flush on save because they'd
   lose data — ours is disposable). Comment this decision in code.
8. Bridge External group `1.6/Defs/DiaryExternalGroups_RimTalkBridge.xml` (required or the key
   silently records nothing):

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>rimtalkbridgeConversation</defName>
  <label>notable conversations</label>
  <domain>External</domain>
  <order>1010</order>
  <important>false</important>
  <instruction>a conversation that mattered today; write what it stirred or settled, not a transcript</instruction>
  <tones><li>attentive to what was said and what was held back</li><li>honest about how the exchange landed</li></tones>
  <matchDefNames><li>rimtalkbridge_conversation</li></matchDefNames>
</PawnDiary.DiaryInteractionGroupDef>
```
   (⚠️ U3 wording; DefInjected-localizable in the bridge's own Languages tree.)

**Verify:** build + Step 6 tests; in-game L2: dev-mode force an insult chat between two colonists
→ exactly one pairwise entry after the quiet window (check Diary tab footer shows
`external source: aimmlegate.pawndiary.rimtalkbridge`); re-flush of the same conversation is
deduped; plain 2-line chitchat produces no explicit entry (ambient note only); caps hold (3rd
important conversation same pawn same day → dropped, dev log says budget/throttle).

## Step 6 — pure tests

**Files:** NEW `tests/RimTalkBridgeLogicTests/` (console project + `Program.cs`), mirroring
`tests/ExampleAdapterParsingTests/` exactly (same csproj shape; `<Compile Include>` file-links to
`..\..\integrations\PawnDiary.RimTalkBridge\Source\Pure\*.cs`).

Cases (assert-based main, like existing test projects):
- Assembly: chain via ParentTalkId lands in one conversation; unknown parent starts new root;
  interleaved conversations stay separate; quiet-window flush returns only stale ones; 64-line cap.
- Importance: each trigger independently (talk kind / social kind / reply count); monologue never;
  2-line chitchat never; threshold boundary (`minReplies` exactly).
- Throttle: per-pawn cap, colony cap, pair gap, day rollover reset.
- ContextFormat: entry list rendering, style line on/off, maxChars truncation at line boundary,
  FirstSentenceCap.

Run: `dotnet run --project tests/RimTalkBridgeLogicTests/RimTalkBridgeLogicTests.csproj`.
Add the project to wherever existing pure tests are listed (`.githooks/verify.ps1` — check whether
it enumerates test projects explicitly; if yes, add it).

## Step 7 — engine mode (advanced, default off)

**Files:** NEW `Source/RimTalkEngineClient.cs`; edit component submit path;
`design/EXTERNAL_API_CAPABILITIES.md`.

0. **⚠️ U5 pre-check (mandatory before coding):** fetch
   `https://raw.githubusercontent.com/jlibrary/RimTalk/main/Source/Data/IJsonData.cs` (and
   `TalkRequest.cs` from `Source/Data/`) — confirm `IJsonData`'s members and that
   `TalkRequest.Context`/`Prompt` have public setters and a usable ctor. If `TalkRequest` cannot
   be constructed publicly, engine mode falls back to reflection over a public factory if one
   exists — if neither works, **stop and report**; do not hack internals.
1. `DiaryTextPayload : IJsonData` with `title`, `text` (match whatever member shape IJsonData
   requires).
2. Flow, replacing the submit call when `useRimTalkEngine` && level ≥ 2:
   - `preview = PawnDiaryApi.PreviewPrompt(theSameExternalPromptEntryRequest)`; null → normal path.
   - Build TalkRequest: `Context = preview.systemPrompt`,
     `Prompt = preview.userPrompt + "\n" + JsonSuffix` where JsonSuffix (Keyed? **No** — model
     instruction, but it must stay English-JSON-schema: carve-out, comment why) instructs:
     respond ONLY with `{"title": "...", "text": "..."}`. ⚠️ U5 wording.
   - If `AIService.IsBusy()` → normal path. Else `AIService.Query<DiaryTextPayload>(req)` and
     `ContinueWith` enqueue `(conversationKey, payload-or-null, originalRequest)` onto a
     `ConcurrentQueue`; **no PawnDiaryApi calls on that thread.**
   - Component tick drains the queue (main thread): payload valid →
     `PawnDiaryApi.SubmitDirectEntry(new ExternalDirectEntryRequest { ..., text = payload.text,
     title = payload.title, dedupKey = same, generateTitleIfMissing = false })`; payload null →
     fallback `SubmitPromptEntry(originalRequest)` (never lose the conversation).
3. Add to `design/EXTERNAL_API_CAPABILITIES.md` planned table: "GEN-1 | Pluggable external
   generation backend (route native diary generation through an adapter-provided client) |
   consumer: RimTalk bridge engine mode | large; would let one API key serve both mods fully."
   (Queue only — do not implement.)

**Verify:** toggle on → dev log shows preview→query→direct-entry path and the Diary tab entry
appears with a title; kill the API key in RimTalk → fallback path fires; toggle off → normal path.

## Step 8 — localization, docs, deploy, final matrix

1. RU Keyed mirror for every new bridge key (`Languages/Russian/Keyed/PawnDiaryRimTalkBridge.xml`)
   + RU DefInjected for the bridge group + core compat group. Author natively-styled Russian
   (⚠️ U4 — flag for the user's native pass in the summary; never calque).
2. `About/About.xml` description rewrite (it still says "minimal developer bridge").
3. `integrations/README.md` — replace the log-only description with levels overview.
4. `DOCUMENTATION.md` — integrations section: bridge architecture (levels, tiers, data flow,
   U1 outcome), compat group; §2 file map additions. `CHANGELOG.md` dated entries per step (or one
   consolidated). `INTEGRATIONS.md`: no contract change; optionally point at the bridge as the
   reference adapter in the roadmap paragraph.
5. Deploy + full in-game matrix:
   - RimTalk absent: compat group invisible; bridge warns once, idles; zero errors.
   - Bridge absent: compat group still active (ambient layer works alone).
   - L0 / L1 / L2 behaviors as per step verifies; master `Allow external mod integrations` off →
     injector returns "", submissions return safe false, provider not invoked.
   - Performance sanity: no per-frame API reads (hooks serve cache), 250-tick cadence confirmed.

## Explicitly out of scope (do not build)

- Tier C LLM persona/style reconciliation (designed above, deferred).
- Triggered chats / diary reactions in chat (`TalkService.GenerateTalk`, `CustomDialogueService`).
- Full engine takeover for native diary generation (queued as GEN-1 capability instead).
- RimTalk addon mods (Expand Memory, Persona Director, …) — base RimTalk only.
- Handling RimTalk's rewriting of vanilla interaction texts (⚠️ U6 — revisit only if playtest
  shows duplicates).
