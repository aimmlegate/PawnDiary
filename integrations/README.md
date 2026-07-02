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
```

`PawnDiary.ExampleAdapter/` is the copy-me template: a minimal GameComponent that submits one
external event per in-game day, plus the External-domain group XML that claims its eventKey and
the adapter-owned Keyed strings. New adapters (RJW, RimTalk, ...) start as a copy of it. The
pre-commit verify hook builds only the core mod; adapters are built/deployed manually.
