# integrations/ — adapter mods for Pawn Diary

Each folder here is a **separate RimWorld mod** (its own `About/About.xml`) that integrates another
mod with Pawn Diary through the public API ([Adapter Contract](../repowiki/en/content/Integration%20Framework/Public%20API%20Reference/Adapter%20Contract.md)).
Adapters compile against `1.6/Assemblies/PawnDiary.dll` and touch only the
`PawnDiary.Integration` namespace — never core internals.

RimWorld does not load mods from nested folders, so nothing in here is active in-game until it is
copied next to the core mod. Use the deploy script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\deploy-integrations.ps1
```

It creates a junction for every adapter folder in the RimWorld `Mods/` root (siblings of this repo)
and refuses to overwrite a folder that is not a Pawn Diary adapter. Edits and rebuilt DLLs therefore
take effect through the live junction without another copy step.

Build an adapter the same way as the core mod:

```
MSBuild integrations\PawnDiary.ExampleAdapter\Source\PawnDiaryExampleAdapter.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.RimTalkBridge\Source\PawnDiaryRimTalkBridge.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.PersonalitiesBridge\Source\PawnDiaryPersonalities123.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.Vsie\Source\PawnDiaryVsie.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.SpeakUp\Source\PawnDiarySpeakUp.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.RimpsycheBridge\Source\PawnDiaryRimpsyche.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.PowerfulAiBridge\Source\PawnDiaryPowerfulAiBridge.csproj /t:Build /p:Configuration=Debug
```

(`PawnDiary.Vsie/` is mostly XML; its only assembly is the small gathering hook — `PawnDiaryVsie.dll`.)

`PawnDiary.ExampleAdapter/` is both the **canonical integration example** and an **in-game API
Explorer**: a developer tool that lets you exercise every public `PawnDiaryApi` family from a
three-pane window (method tree | form | running result log), grouping related overloads into one
form, plus four `[DebugAction]` quick actions. The tree includes the psychotype
read/write/generator surface and the paid one-shot LLM completion request/poll/cancel lifecycle.
Open it in Dev mode → Debug Actions → **Pawn Diary Example Adapter** → **Open API
explorer…**. The method tree has a search box (filters by method/summary/category), per-category
collapse, visible plain-language descriptions under each endpoint signature, and hover tooltips
showing each method's full label and summary; the form pane names the subject/partner a call will
target, uses multiline editors for prose/context fields, and can reset the shared form state;
hovering method titles and field labels shows short help popovers; the result log keeps short
histories compact so the selected detail stays visible and colours rows by outcome. The window opens
after closing the Debug Actions launcher and provides a thin drag strip so it behaves as a movable,
resizeable overlay while outside clicks pass through to normal game UI/camera controls.
Its readiness badge and Readiness methods show both `IsReady` and the player-controlled
`IsExternalApiEnabled` master switch, and non-readiness invokes are skipped while the master switch is
off.
All request fields start with concrete sample values for quick submit/preview testing. The ordinary,
prompt-entry, and direct-entry forms default to their own XML-claimed event keys, so all three
shipped External settings rows are exercised. A concise walkthrough lives in
`PawnDiary.ExampleAdapter/API_EXPLORER.md`. The write forms expose the public
v1 `forceRecord` flag and default it on so repeated smoke-test clicks are not hidden by dedup or
budget guardrails. The daily-event timer that used to live here is gone. All direct API calls now
live in `PawnDiary.ExampleAdapter/Source/PawnDiaryExampleApi.cs`, with XML doc comments explaining
each method's args and safe return value. Copy that file plus the matching group XML in
`1.6/Defs/DiaryExternalGroups_Example.xml` to start a real adapter, then replace the quick
debug-action trigger with a hook into your target mod.

The example adapter also demonstrates the two process-global hooks an integration normally
registers, through `PawnDiaryExampleApi.RegisterHooksOnce()`:

- `RegisterEntryStatusListener` — fired on the main thread after a saved POV's status changes; the
  explorer records each firing in its **Hooks → Activity log** tab so you can prove it works.
- `RegisterPawnContextProvider` — fired during prompt context collection; the adapter contributes one
  `example_traits=…` line per pawn and bumps a counter visible in the same tab.

`PawnDiary.RimTalkBridge/` is the first real adapter target: a two-way bridge between Pawn Diary and
RimTalk, gated by an **integration-level** dropdown in its own mod settings (see
`design/RIMTALK_BRIDGE_PLAN.md` for the full design and `repowiki/README.md` for shipped behavior):

- **Off** — no data flows in either direction.
- **Shared context** (default, level 1) — recent diary memories are injected into RimTalk's chat
  prompts (via `ContextHookRegistry.InjectPawnSection` + a `{{pawn1.diary}}` variable), and each
  pawn's RimTalk persona is surfaced to Pawn Diary as a `chat_persona=` context-provider line. Purely
  additive context; no chat-originated diary entry is created.
- **Shared context + conversations** (level 2) — finished reply chains enter a bounded editorial
  funnel: pure local scoring → ranked 12-candidate queue → batches of at most 6 through the existing
  Pawn Diary completion API → `ignore`, `related`, or `standalone`. Only accepted results call normal
  pairwise `SubmitPromptEntry`, creating one `DiaryEvent` with two POV pages. The core
  `rimtalk_chatter` group is fallback-only and disables itself whenever this bridge is installed, so
  per-line ambient/promotion capture cannot duplicate a selected whole conversation.

Local scoring is free. The XML defaults allow at most two small assessment requests per in-game day,
independent of chat volume, and normal diary generation is spent only on accepted conversations.
Semantic assessment can use Automatic/any active Pawn Diary lane or be disabled for a stricter,
less-accurate zero-extra-request mode. No lane waits with a bounded expiring queue; failed/malformed
assessment records nothing. Each successfully submitted conversation starts a saved rolling 60,000-tick
cooldown for both participants, so neither can receive another chat event for one full game day even
across save/load; rejected submissions refund it. Settings also expose a validated comma-separated
reaction-term editor used by both selection modes and a full semantic-prompt editor. Blank overrides
track the localized XML/DefInjected defaults. Advanced toggles also cover two-way persona/psychotype
synchronization and colony/pair throttles. Both optional LLM directions keep the source's concrete
character meaning recognizable; only unsupported surface mechanics are discarded, and direction-specific
prompt revisions refresh older cached transforms once. The former RimTalk-engine writing toggle is retired: its frozen Scribe
key still reads, but accepted entries always use Pawn Diary's pairwise prompt path. The project stays
net48/x64 because RimTalk-typed hook code references the workshop assembly
(`$(RimTalkAssembly)` MSBuild property).
When LLM persona rewriting is enabled, settings disclose the exact direction-specific payload:
effective Pawn Diary psychotype rule for export or the complete RimTalk persona for import, plus the
narrow local system-prompt modifier policy and an explicit list of fields that are not added.

The bridge's pure decision logic (conversation assembly, Unicode matching/overlap, editable-term/prompt
validation, local scoring,
bounded queue/day gating, batch formatting, strict response parsing, submission planning, throttling,
and context formatting) is unit-tested by `tests/RimTalkBridgeLogicTests/`:

```
dotnet run --project tests/RimTalkBridgeLogicTests/RimTalkBridgeLogicTests.csproj
```

The pre-commit verify hook builds only the core mod; adapters (and their pure tests) are built, run,
and deployed manually.

`PawnDiary.SpeakUp/` (`aimmlegate.pawndiary.adapter.speakup`) is a reflection-only adapter for
SpeakUp (`JPT.speakup`). Five XML families separate deep talk, jokes, prisoner talk, thought reactions,
and ordinary chatter. Its default-on assembly observes completed transient Talk chains without a
SpeakUp.dll compile reference and submits `speakupbridge_conversation` after the configured 1–5 reply
threshold (default 3). If the reflected surface drifts, only whole-conversation capture disables; XML
classification remains. Core `speakup_chitchat` is the fallback when this adapter is absent and keeps
its frozen setting/save tokens. The release script builds and packages this adapter by default.

```
dotnet run --project tests/SpeakUpBridgeLogicTests/SpeakUpBridgeLogicTests.csproj
```

`PawnDiary.RimpsycheBridge/` (`aimmlegate.pawndiary.adapter.rimpsyche`) targets Rimpsyche - Personality
Core (`Maux36.Rimpsyche`, Workshop `3535112473`). It provides three independent tiers: a compact cached
psyche context line, a change-detected source-owned psychotype outlook, and signature-checked capture of
high-alignment conversations with a saved per-pair cooldown. The project has a typed compile reference
to installed `RimPsyche.dll`; target reads are isolated behind the package guard, and XML groups are
gated. Its deterministic base outlook is authored from dominant Rimpsyche nodes, not read from Pawn
Diary. The default LLM transform keeps that personality-derived outlook authoritative and uses
descriptors/interests only as supported secondary emphasis. The source-owned result replaces the
effective Diary psychotype rather than extending it; the effective-prompt hash refreshes an old
default-produced target when that policy changes. LLM mode shows the exact `psyche=` / `interests=` /
`base outlook:` request schema and current descriptor/interest caps in its scrollable settings page.
Thresholds and caps live in the adapter's tuning Def.

```
dotnet run --project tests/RimpsycheBridgeLogicTests/RimpsycheBridgeLogicTests.csproj
```

`PawnDiary.PowerfulAiBridge/` (`aimmlegate.pawndiary.adapter.powerfulai`) is a thin, one-way persona
adapter for Powerful AI Integration (`codex.dynamicrolesstoryteller`). It uses reflection to read the
enabled PAI character bound to a pawn, so it does not compile against the target DLL and is safely idle
if that public field surface is absent. It copies all five nonblank persona fields into a reversible
Pawn Diary psychotype override and never sends diary data back to PAI. Its single mode setting offers
Disabled, Direct (default), and LLM-assisted; the latter uses a selectable Pawn Diary API lane and
falls back to Direct. The settings page discloses the exact structured persona payload. Pure formatting
and change-detection helpers are covered here:

```
dotnet run --project tests/PowerfulAiBridgeLogicTests/PowerfulAiBridgeLogicTests.csproj
```

Unlike the other runtime adapters, its publish payload intentionally includes the complete `Source/`
tree. Build and stage only this bridge with `scripts\publish.ps1 -PublishPowerfulAiAdapter` or include
it with `-PublishAllAdapters`.

`PawnDiary.Vsie/` and `PawnDiary.PersonalitiesBridge/` are personality/social compatibility adapters,
each a separate mod for one target so a player installs only what matches their mod list:

- **`PawnDiary.Vsie/`** (`Pawn Diary: Vanilla Social Interactions Expanded`) — **mostly XML, plus a
  tiny assembly** (`PawnDiaryVsie.dll`). Four `enableWhenPackageIdsLoaded`-gated
  `DiaryInteractionGroupDef`s give VSIE's venting, teaching, best-friend milestone, and extra mood
  thoughts their own diary voice, plus a VSIE-gated patch routing VSIE's angry `Discord` insult into the
  core insults group. The assembly adds the **gathering bridge**: a Harmony postfix on the base-game
  `GatheringWorker.TryExecute` (matches `def.defName` as a string — **no VSIE assembly reference**)
  forwards `VSIE_BirthdayParty` / `VSIE_Funeral` to the public API as External events claimed by two
  `domain=External` groups. Because External events are excluded from Pawn Diary's Events tab, the adapter
  carries its **own mod settings** (`VsieBridgeMod`) with a per-type birthday/funeral toggle. Its pure
  defName→plan map is unit-tested by `tests/VsieBridgeLogicTests/`. Inert without VSIE (groups gated;
  `PatchAll` skipped unless VSIE is active).

```
dotnet run --project tests/VsieBridgeLogicTests/VsieBridgeLogicTests.csproj
```

- **`PawnDiary.PersonalitiesBridge/`** (`Pawn Diary: 1-2-3 Personalities`) — **XML + a small
  assembly.** Tier 1 (XML) routes Module 2's compatibility interactions and personality mood thoughts.
  The assembly (`PawnDiaryPersonalities123.dll`, net472, reads 1-2-3 Personalities' public Enneagram
  API, **no Harmony**) turns each colonist's Enneagram root into their **editable** Pawn Diary
  psychotype via one single-choice mode: off, map to the closest built-in psychotype, override from the
  built-in outlook text, or an experimental LLM transform on a selectable lane with an editable prompt
  (falling back to the built-in text). That outlook is authored from the 1-2-3 Enneagram root, not read
  from the pawn's current Diary psychotype. The default prompt keeps it authoritative and lets
  style/main-trait data alter only supported emphasis; the resulting custom rule replaces the effective
  base rule rather than extending it. Effective-prompt change detection refreshes old default-produced
  text without replacing a player-customized prompt. LLM mode shows the exact nonblank
  `personality style:` / `main trait:` / `base outlook:` / raw target-owned `details:` request schema
  in its scrollable settings page. Change-detected and saved so player edits survive reloads. Its
  pure mapping is unit-tested:

```
dotnet run --project tests/Personalities123BridgeLogicTests/Personalities123BridgeLogicTests.csproj
```

The explorer's pure text-parsing helpers (`ExplorerParsing.cs`) are unit-tested by
`tests/ExampleAdapterParsingTests/`:

```
dotnet run --project tests/ExampleAdapterParsingTests/ExampleAdapterParsingTests.csproj
```

The DTO formatting glue (`SnapshotFormatter.cs`) is intentionally not pure-tested — the DTOs live in
`PawnDiary.dll`, which references RimWorld, so pulling them into a console test project would drag
RimWorld/Unity in transitively and break the "pure tests compile without RimWorld" rule.
