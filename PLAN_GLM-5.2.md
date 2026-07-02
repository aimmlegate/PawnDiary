# Pawn Diary — External Mod Integration Plan

> Goal: support compatibility patches for popular mods (Pawn Social Overhaul, RimWorld 1-2-3
> Personality, Psychology, Rimpsyche, RimTalk, RimJobWorld, and others) through a **first-class
> public API** so adapter mods talk to a stable contract and never touch Pawn Diary internals.
>
> Direction now: **inbound** (data → Pawn Diary) is the priority; the same API also **exposes**
> Pawn Diary context outbound (e.g. for RimTalk) so it can be consumed by external mods.
>
> Decisions made:
> - Adapter mods live **in subfolders of this repo** (`adapters/<ModName>/`).
> - Reference C# adapter = **RimTalk, outbound-first** (proves `IPawnDiaryContextQuery`).

---

## 1. Pawn Diary already has the extension points — build on them

Three existing mechanisms do most of the work. This plan extends them rather than reinventing.

### a) Pure-XML classification of foreign mods' defs

`DiaryInteractionGroupDef` (`Source/Defs/InteractionGroups.cs:203`) already classifies another
mod's defs by:

- `matchDefNames` (exact), `matchPrefixes`, `matchSuffixes`, `matchSegments` (CamelCase word)
  — `Source/Defs/InteractionGroups.cs:279-299`
- `matchPackageIds` — claims any def whose `Def.modContentPack.PackageId` is listed
  — `Source/Defs/InteractionGroups.cs:304`
- `disableWhenPackageIdsLoaded` — a built-in group yields to a richer mod replacement
  — `Source/Defs/InteractionGroups.cs:225`

Any mod whose content flows through the choke points Pawn Diary already hooks
(`PlayLog.Add`, `MentalStateHandler.TryStartMentalState`, `TaleRecorder.RecordTale`,
`GameConditionManager.RegisterCondition`, hediff scanning) needs **zero C#** — just a Def XML
shipped in a patch.

### b) `DlcContext` is the template for an adapter

`Source/Generation/DlcContext.cs` is the "one home" that double-guards optional content
(`ModsConfig.<Dlc>Active` + null-check) and returns `string.Empty` when absent. DLCs are effectively
official integration mods; this is the exact shape an external-mod adapter should take.

### c) GameContext string + XML `ContextField`

- `DiaryContextFields.Value()` (`Source/Generation/DiaryContextFields.cs:27`) parses
  `key=value; key2=value2` context strings.
- Prompt templates reference these via `ContextField("quest name", "quest_label")`
  (`Source/Defs/PromptArchitectureDefs.cs:406`), producing a field with `source="GameContext"`.

So a new context line injected into the GameContext string is rendered into the prompt by a
**pure-XML template field — no code.** This is the existing inbound extension point.

---

## 2. Three integration tiers

| Tier | When | Mechanism | Adapter ships |
|---|---|---|---|
| **1. Pure XML** | Source mod's data flows through an existing choke point (interaction / tale / hediff / thought / mental state / game condition / quest) | `DiaryInteractionGroupDef` + `DiaryPromptEnchantmentDef` + `ContextField` in a prompt template | One `.xml` Def file + translations |
| **2. Inbound C# adapter** | Source mod's data is **not** reachable from a choke point (custom stats, relationship graphs, chat logs, body trackers) | Adapter mod references a published **`PawnDiary.Api.dll`**, registers an `IContextContributor`, Pawn Diary calls it during context build | Separate mod: C# + About.xml |
| **3. Outbound consumer** | A mod wants to **read** Pawn Diary context (RimTalk grounding chat on the diary) | Same Api dll, `IPawnDiaryContextQuery` returns plain snapshots | Separate mod, read-only |

### Per-mod routing (initial assessment — verify when building each)

| Mod | Likely tier | Why |
|---|---|---|
| **Pawn Social Overhaul** | 1 XML | Renames/replaces interaction defs → `matchDefNames` |
| **1-2-3 Personality** | 1 XML (if traits/thoughts) / 2 (if custom stat block) | Check whether it surfaces as thoughts |
| **Psychology** | 2 inbound | Custom 2D personality stats, relationship graph — not a choke point |
| **Rimpsyche** | 2 inbound | Custom mental model |
| **RimTalk** | 3 outbound (+ optional 2 inbound for chat transcripts) | Wants Pawn Diary context to ground chat |
| **RimJobWorld** | 1 XML for acts/hediffs + 2 for custom body state | Interactions/hediffs via XML; custom trackers via adapter |

---

## 3. The public API — `PawnDiary.Api.dll`

Ship a **second, tiny assembly** inside the main mod at `1.6/Assemblies/PawnDiary.Api.dll`. It
contains **only contracts**, no logic. Adapter mods compile against this dll only — they never
reference `PawnDiary.dll` (the "don't touch our mod directly" requirement).

```
PawnDiary.Api.dll                          ← adapters compile against THIS only
├─ PawnDiaryApi                            ← static discovery handle (load-order-safe)
│    bool IsAvailable { get; }
│    IPawnDiaryIntegrationHost Host { get; }
│    bool IsCompatible(int minMajor);
│    void RegisterContributor(IPawnDiaryContextContributor c);   // buffers if host not up yet
├─ IPawnDiaryIntegrationHost
│    void RegisterContributor(IPawnDiaryContextContributor c);
│    IPawnDiaryContextQuery Query { get; }
├─ IPawnDiaryContextContributor             ← the adapter implements this
│    string Id { get; }
│    IEnumerable<PawnContextLine> PawnLines(Pawn pawn);        // always-on, per pawn
│    IEnumerable<ContextLine> EventContext(Pawn pawn, DiaryEventSnapshot ev); // per event
├─ IPawnDiaryContextQuery                   ← RimTalk consumes this
│    string GetPawnSummary(Pawn pawn);
│    IReadOnlyList<DiaryEntrySnapshot> GetRecentEntries(string pawnId, int count);
│    DiaryEventSnapshot GetActiveEvent(string pawnId);
└─ DTOs: PawnContextLine, ContextLine, DiaryEventSnapshot, DiaryEntrySnapshot   (plain data)
```

`PawnDiary.dll` implements these and, during `PawnDiaryMod.Initialize`, sets
`PawnDiaryApi.Host = new IntegrationHost(...)`.

### Wiring into the pipeline (one-line call sites, pure append)

- `DiaryContextBuilder.BuildPawnSummary` (`Source/Generation/DiaryContextBuilder.cs:569`) calls
  `IntegrationContext.AppendPawnLines(pawn)` after its own `faith=` line — adapters' `key=value`
  lines join `xenotype=`, `mood=`, etc.
- Event GameContext built in `InteractionEventData.BuildGameContext`
  (`Source/Capture/Events/InteractionEventData.cs:124`) gets
  `AppendEventContributions(pawn, ev)` appended.

---

## 4. Hardening rules (mirror AGENTS.md DLC-safety)

- **Double-guard in the adapter, catch in the host.** Adapter registers only when
  `LoadedModManager.RunningMods` contains its source mod; host wraps every contributor call in
  try/catch so one buggy adapter can't break generation.
- **Strings in, strings out.** Contributions become plain `key=value` lines immediately — the
  pure/impure barrier is preserved. No live `Def`/settings/transport objects cross the boundary.
- **Main-thread only.** Contributors run during `BuildPawnSummary` (main thread) so they can read
  pawn state safely. Outbound query returns **snapshots** (`DiaryEntrySnapshot`), never live
  objects — LLM-chat consumers must not hold `Pawn` refs across their own background threads (same
  rule as `LlmClient`).
- **Localization-friendly.** Adapter-supplied *values* are plain text (often the foreign mod's own
  labels); adapter-supplied *schema keys* stay English like the existing `sex=`/`xenotype=` carve-out
  (DOCUMENTATION.md §12).
- **API versioned.** `PawnDiaryApi.IsCompatible(major)` so a built-against-1.x adapter fails safe
  against a 2.x host instead of crashing.
- **Never ship the Api dll from an adapter.** The Api dll ships once from the main mod; adapters
  reference it at compile time (`Private=False`) and rely on RimWorld's mod-assembly loading. This
  avoids duplicate-assembly conflicts.

---

## 5. Confirmed repo layout

Driven by the build setup:

- Main project (`Source/PawnDiary.csproj`) is legacy non-SDK, net4.7.2 / LangVersion 7.3, outputs
  to `..\1.6\Assemblies\`, compile glob = `Source\**\*.cs` (scoped to `Source/`).
  → **The Api project must live outside `Source/`** or its files get double-compiled.
- Test projects (`tests/*/`) are SDK-style net10.0 and **link individual pure source files** via
  `<Compile Include="..\..\Source\...">`. Pure DTOs/helpers are testable the same way.

```
PawnDiary/                               repo root
├─ Source/                                existing main mod (glob picks this up)
│  └─ Integration/                        NEW — host impl, auto-globbed into PawnDiary.dll
│     ├─ IntegrationHost.cs                 IPawnDiaryIntegrationHost impl + try/catch isolation
│     ├─ IntegrationContext.cs              AppendPawnLines / AppendEventContributions (pure formatter + host call)
│     └─ ContextQuery.cs                    IPawnDiaryContextQuery impl (outbound, reads existing data)
├─ Api/                                   NEW — public contract assembly, OUTSIDE Source/
│  ├─ PawnDiary.Api.csproj                 net4.7.2 / Lang 7.3 → 1.6/Assemblies/PawnDiary.Api.dll
│  ├─ PawnDiaryApi.cs                      static discovery + load-order-safe RegisterContributor
│  ├─ IPawnDiaryIntegrationHost.cs
│  ├─ IPawnDiaryContextContributor.cs
│  ├─ IPawnDiaryContextQuery.cs
│  └─ Dtos/                                PawnContextLine, ContextLine, DiaryEventSnapshot, DiaryEntrySnapshot
├─ adapters/RimTalk/                      reference adapter (outbound bridge)
│  ├─ PawnDiary.RimTalkAdapter.csproj      references PawnDiary.Api.dll only (Private=False)
│  ├─ About.xml                            modDependencies: PawnDiary + RimTalk; loadAfter both
│  └─ RimTalkContextBridge.cs              patches/feeds RimTalk's chat request with our pawn context
├─ tests/IntegrationAdapterTests/         NEW — pure DTO + isolation + formatter tests
└─ 1.6/Assemblies/
   ├─ PawnDiary.dll                        rebuilt
   └─ PawnDiary.Api.dll                    NEW (shipped by main mod; adapters never ship their own copy)
```

### Two design details that matter

1. **Load-order-safe registration.** Adapters must work whether they load before or after PawnDiary.
   `PawnDiaryApi.RegisterContributor(c)` stashes in a static buffer if `Host` is null and flushes
   when `IntegrationHost` attaches during `PawnDiaryMod.Initialize`. No `<loadAfter>` fragility.
2. **The RimTalk blocker.** A RimTalk *bridge* adapter needs RimTalk's assembly (compile ref,
   `Private=False`) or a documented RimTalk API to feed context into. **The outbound API
   implementation is unblocked; the bridge in `adapters/RimTalk/` is gated on acquiring RimTalk's
   DLL.** Ship the API + a self-contained reference consumer first, then bolt on the RimTalk-specific
   patch when its assembly is available.

---

## 6. Phased rollout

### Phase 0 — API + host wiring (unblocked)
- `Api/` project → `1.6/Assemblies/PawnDiary.Api.dll`.
- `Source/Integration/*` (host impl + outbound query impl).
- Wire the two call sites (`BuildPawnSummary`, `BuildGameContext`).
- `tests/IntegrationAdapterTests/` (fake contributor + pure DTO flow + try/catch isolation).
- Docs: `DOCUMENTATION.md §13 External Integration`, `CHANGELOG.md`, `INTEGRATION.md` for adapter
  authors. Build green.

### Phase 1 — Prove Tier 1 with one pure-XML patch (unblocked)
- e.g. Pawn Social Overhaul: ship `1.6/Defs/Compat/` XML, validate no C# needed. Locks the XML-only story.

### Phase 2 — Outbound reference consumer (unblocked)
- Implement `ContextQuery` fully.
- Validate `IPawnDiaryContextQuery` end-to-end with a self-contained in-repo consumer.
- Real `adapters/RimTalk/` bridge lands once RimTalk's assembly is in hand.

### Phase 3 — Inbound C# adapter (later)
- Psychology reference adapter: separate mod under `adapters/Psychology/`, references
  `PawnDiary.Api.dll`, reads Psychology stats, contributes `personality=…` lines. Proves the
  inbound Tier-2 API contract end-to-end.

---

## 7. Build commands (reference)

```powershell
# Main mod (rebuilds 1.6/Assemblies/PawnDiary.dll)
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug

# Public Api assembly (outputs 1.6/Assemblies/PawnDiary.Api.dll)
MSBuild Api\PawnDiary.Api.csproj /t:Build /p:Configuration=Debug

# Reference adapter (after Api + RimTalk DLL available)
MSBuild adapters\RimTalk\PawnDiary.RimTalkAdapter.csproj /t:Build /p:Configuration=Debug

# Pure tests
MSBuild tests\IntegrationAdapterTests\IntegrationAdapterTests.csproj /t:Build
```

If `MSBuild` isn't on `PATH`, locate it with
`vswhere -latest -find MSBuild\**\Bin\MSBuild.exe`, or build from a VS "Developer PowerShell."
RimWorld/Unity assembly hint paths come from the `RimWorldManaged` MSBuild property (defaulting to
`D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed`); override with
`/p:RimWorldManaged=...` or the `RIMWORLD_MANAGED` env var.

---

## 8. Definition of done (per adapter)

- Compiles against `PawnDiary.Api.dll` only; no reference to `PawnDiary.dll`.
- `About.xml` declares `modDependencies` on Pawn Diary (+ source mod) and `loadAfter` both.
- Double-guards its source mod (`LoadedModManager` check) before registering/reading.
- Runs cleanly as a no-op when the source mod is absent; never throws through Pawn Diary's pipeline.
- Pure logic unit-tested where feasible; docs + CHANGELOG updated.
