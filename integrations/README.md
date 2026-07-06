# integrations/ — adapter mods for Pawn Diary

Each folder here is a **separate RimWorld mod** (its own `About/About.xml`) that integrates another
mod with Pawn Diary through the public API (`INTEGRATIONS.md` at the repo root is the contract).
Adapters compile against `1.6/Assemblies/PawnDiary.dll` and touch only the
`PawnDiary.Integration` namespace — never core internals.

RimWorld does not load mods from nested folders, so nothing in here is active in-game until it is
copied next to the core mod. Use the deploy script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\deploy-integrations.ps1
```

It copies every adapter folder to the RimWorld `Mods/` root (siblings of this repo) and refuses to
overwrite a folder that is not a Pawn Diary adapter.

Build an adapter the same way as the core mod:

```
MSBuild integrations\PawnDiary.ExampleAdapter\Source\PawnDiaryExampleAdapter.csproj /t:Build /p:Configuration=Debug
MSBuild integrations\PawnDiary.RimTalkBridge\Source\PawnDiaryRimTalkBridge.csproj /t:Build /p:Configuration=Debug
```

`PawnDiary.ExampleAdapter/` is both the **canonical integration example** and an **in-game API
Explorer**: a developer tool that lets you exercise every public `PawnDiaryApi` method from a
three-pane window (method tree | form | running result log) plus four `[DebugAction]` quick
actions. Open it in Dev mode → Debug Actions → **Pawn Diary Example Adapter** → **Open API
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
All request fields start with quiet-moment sample values for quick submit/preview testing. A concise
walkthrough lives in `PawnDiary.ExampleAdapter/API_EXPLORER.md`. The write forms expose the public
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

`PawnDiary.RimTalkBridge/` is the first real adapter target, currently reset to the example-adapter
shape: a GameComponent registration point plus a bridge-named `PawnDiaryApi` facade. It no longer
contains the old log-only RimTalk Harmony patch or settings UI. Its project stays net48/x64 because
the planned RimTalk hook code will reference the current RimTalk workshop assembly. Follow
`design/RIMTALK_BRIDGE_PLAN.md` for the next bridge implementation steps. The pre-commit verify hook
builds only the core mod; adapters are built/deployed manually.

The explorer's pure text-parsing helpers (`ExplorerParsing.cs`) are unit-tested by
`tests/ExampleAdapterParsingTests/`:

```
dotnet run --project tests/ExampleAdapterParsingTests/ExampleAdapterParsingTests.csproj
```

The DTO formatting glue (`SnapshotFormatter.cs`) is intentionally not pure-tested — the DTOs live in
`PawnDiary.dll`, which references RimWorld, so pulling them into a console test project would drag
RimWorld/Unity in transitively and break the "pure tests compile without RimWorld" rule.
