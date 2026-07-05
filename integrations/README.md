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
explorer…**. The daily-event timer that used to live here is gone; the *Submit example event (random
colonist)…* quick action replaces it as the canonical minimal `SubmitEvent` example — copy that
request shape (and the matching group XML in `1.6/Defs/DiaryExternalGroups_Example.xml`) to start a
real adapter, and swap the dev-action trigger for a hook into your target mod.

The example adapter also demonstrates the two process-global hooks an integration normally
registers, in `ExampleAdapterGameComponent`:

- `RegisterEntryStatusListener` — fired on the main thread after a saved POV's status changes; the
  explorer records each firing in its **Hooks → Activity log** tab so you can prove it works.
- `RegisterPawnContextProvider` — fired during prompt context collection; the adapter contributes one
  `example_traits=…` line per pawn and bumps a counter visible in the same tab.

`PawnDiary.RimTalkBridge/` is the first real adapter scaffold: a separate mod that listens to RimTalk
chat and, when enabled, logs chat facts plus recent Pawn Diary context summaries for the
speaker/target. Its project targets net48/x64 because the current RimTalk workshop assembly does.
New adapters start from the example unless they need a target-mod Harmony patch like the RimTalk
bridge. The pre-commit verify hook builds only the core mod; adapters are built/deployed manually.

The explorer's pure text-parsing helpers (`ExplorerParsing.cs`) are unit-tested by
`tests/ExampleAdapterParsingTests/`:

```
dotnet run --project tests/ExampleAdapterParsingTests/ExampleAdapterParsingTests.csproj
```

The DTO formatting glue (`SnapshotFormatter.cs`) is intentionally not pure-tested — the DTOs live in
`PawnDiary.dll`, which references RimWorld, so pulling them into a console test project would drag
RimWorld/Unity in transitively and break the "pure tests compile without RimWorld" rule.