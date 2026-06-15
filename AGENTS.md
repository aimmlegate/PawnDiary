# Pawn Diary — guide for code agents

<<<<<<< HEAD
This file is the OpenCode-focused wrapper and shared baseline for all agents.
Also see:
- `skills/pawndiary-engineering/SKILL.md` (source-of-truth workflow)
- `CLAUDE.md` (Claude Code wrapper)
- `CODEX.md` (Codex wrapper)

**Always keep `DOCUMENTATION.md` up to date.** It is the living design doc for this mod.
After any change to the mod's behavior or structure, update the affected section of
`DOCUMENTATION.md` and add a dated line to its Changelog in the same change. Treat the doc
as part of "done", not an afterthought.
=======
Onboarding for anyone (AI agent or human) changing this mod. **Read this once** and the code
should read easily. For the full architecture, data flow, and settings see `DOCUMENTATION.md`;
for the dated history see `CHANGELOG.md`.

> **Big picture:** this is a **library** (`.dll`) that RimWorld loads at startup. There is no
> `main()`. RimWorld discovers our classes by reflection and calls into them at the right moments
> (startup, every tick, on save/load, when a UI tab opens). So most of our code is "fill in this
> method and the game will call it," much like implementing framework lifecycle hooks.

---

## Working rules — follow these on every change

1. **Keep the docs current.** After any change to the mod's behavior or structure, update the
   affected section of `DOCUMENTATION.md` **and** add a dated line to `CHANGELOG.md` — in the same
   change. Treat docs as part of "done", not an afterthought.

2. **Stay localization-friendly.** Never hardcode English that can reach the **UI** or the **LLM
   prompt**. This is what lets a translated game stay translated, and stops stray English
   scaffolding from nudging the model into writing English. Concretely:
   - Any new player-facing string or natural-language prompt text must be a key in
     `Languages/English/Keyed/PawnDiary.xml`, used via `"My.Key".Translate()`. For interpolation
     use `{0}`, `{1}` placeholders in the XML and pass args: `"My.Key".Translate(a, b)`.
   - **Keep in English on purpose** (a stable machine *schema*, not prose): the structured prompt
     field labels in `DiaryPromptBuilder` (`event:`, `pov:`, `setting:`, …), the `key=` summary
     sub-keys in `BuildPawnSummary` (`sex=`, `mood=`, …), the `initiator`/`recipient` role words,
     and the `none`/`n/a`/`unknown` skip-sentinels that `AppendField` filters on.
   - Def-borne text (persona `rule`, group `label`/`instruction`, `DiaryPromptDef`'s system prompt
     + instructions) is localized the RimWorld way — `Languages/<lang>/DefInjected/…`, not Keyed.
     Leave the English in `1.6/Defs/*.xml` as the source/fallback.
   - `.Translate()` is **not thread-safe** — call it only on the main thread. `LlmClient` runs on
     background threads, so its network-error strings stay English by design.
   - Full rationale and the exact carve-outs: `DOCUMENTATION.md §12 (Localization)`.

3. **Comment for AI agents and novice devs.** Favor extensive, plain-English comments:
   - Every `.cs` file opens with a header comment explaining its role and how it fits the flow.
   - Every public type/method gets a `///` summary; non-obvious logic gets inline `//` notes.
   - Explain any non-obvious C#/RimWorld idiom in JS/TS terms, or point to the primer below
     (e.g. `// New to C#/RimWorld? See AGENTS.md ("IExposable")`).
   - When you touch existing code, update its comments to match. Clarity over brevity.

4. **Build after every change** to confirm it compiles (see *Building* below).

---
>>>>>>> origin/main

## Skill routing (critical)
- Before acting, check whether `skills/pawndiary-engineering/SKILL.md` applies.
- If it applies, follow it exactly (plan first, smallest safe change, validate, document).
- Do not skip build validation or documentation updates.

## Key facts

- **Runtime is RimWorld's Unity Mono.** Only assemblies shipped in `RimWorldWin64_Data/Managed`
  exist at runtime. **Do not** use `System.Web.Extensions` / `JavaScriptSerializer` or add
  external JSON libraries — JSON is parsed by `Source/Util/MiniJson.cs`.
- **Source layout.** All C# lives under `Source/`, grouped by concern (`Core/`, `Models/`,
  `Generation/`, `Defs/`, `Patches/`, `UI/`, `Settings/`, `Util/`). The game ignores `Source/`; it
  loads only the compiled DLL. The `.csproj` uses a recursive `**\*.cs` glob, so new `.cs` files
  need no project edit.
- **Editable data lives in XML**, loaded at startup with no recompile: Defs under `1.6/Defs/`
  (interaction groups, tuning numbers, prompts, personas) and strings under `Languages/`.

## Building

```
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

→ outputs `1.6/Assemblies/PawnDiary.dll`, which is **committed on purpose** so the mod runs
straight from a clone — rebuild and stage it whenever you change C#. Always build after changes to
confirm it compiles. If `MSBuild` isn't on `PATH`, locate it with
`vswhere -latest -find MSBuild\**\Bin\MSBuild.exe`, or build from a Visual Studio
"Developer PowerShell".

---

## C# & RimWorld for JS/TS devs

A quick reference for the recurring C#/RimWorld idioms in this mod, each with the closest
JavaScript/TypeScript analogy. Inline code comments link here (e.g. `see AGENTS.md ("Defs")`)
instead of re-explaining the same idiom in ten places.

### Language idioms

#### Properties vs fields
```csharp
public string name;              // field  — plain data, like a class property in TS
public string Name { get; set; } // auto-property — same usage, but can add logic later
public string Display => name;   // read-only property — like a TS getter `get Display()`
```
`foo.Name = "x"` and `foo.name = "x"` look identical at the call site; the difference is the
property can run code in its `get`/`set`. **Convention:** PascalCase for public
methods/properties/types, camelCase for fields and locals. (RimWorld's own Def fields are
camelCase, e.g. `defName`, `label`.)

#### Static classes and members = module singletons
```csharp
public static class InteractionGroups { public static List<...> All { get { ... } } }
```
A `static class` can't be instantiated — it's just a bag of functions/state shared process-wide,
like a TS module that `export`s functions and a module-level variable. `static` fields are the
equivalent of a top-level `let` in a module: one shared copy.

#### `ref` and `out` parameters (pass-by-reference)
TS has no equivalent — arguments are always by value (objects by reference-value). C# can pass a
*variable* so the callee writes back into the caller's variable:
```csharp
int i = 0;
Parse(json, ref i);   // Parse can advance i; caller sees the new value
bool ok = dict.TryGetValue(key, out int value); // returns ok, AND fills `value`
```
`TryGetValue(key, out value)` is the C# pattern for "look up; return found?/value together"
(like destructuring `const [ok, value] = ...` in JS). `ref` is used in `MiniJson.cs` to walk a
parse position through recursive calls; `out` is everywhere for try-get patterns.

#### Generics
`List<string>` ≈ `string[]`/`Array<string>`; `Dictionary<string, bool>` ≈ `Map<string, boolean>`
(or a plain object). `IReadOnlyList<T>` ≈ `ReadonlyArray<T>`.

#### LINQ ≈ array methods
```csharp
items.Where(x => x.ok).OrderByDescending(x => x.n).Take(3).Select(x => x.label).ToArray()
//    .filter        .sort((a,b)=>b.n-a.n)        .slice(0,3) .map
```
`FirstOrDefault()` ≈ `find()` (returns `null`/default if none). `.ToList()/.ToArray()` force the
lazy query to actually run.

#### Null handling
`?.` (null-conditional) and `??` (null-coalescing) are the same as TS. `x ?? 0` = "x, or 0 if
null". `pawn?.relations?.OpinionOf(other) ?? 0` reads exactly like TS optional chaining.

#### `async Task` ≈ `async`/`Promise`
`Task` ≈ `Promise<void>`, `Task<T>` ≈ `Promise<T>`, `await` is the same. `CancellationToken` ≈
`AbortSignal` (cooperative cancellation). See `LlmClient.cs` (already well-commented) for the
queue/timeout/retry logic. **Note:** background `Task` continuations run off the main thread, so
they must not call RimWorld APIs (including `.Translate()`) — see the localization rule above.

### RimWorld idioms

#### Defs & DefDatabase  (data-driven content from XML)
A **Def** is a chunk of game content defined in XML and loaded into a global registry at
startup — think JSON config files that RimWorld auto-loads into a `Map` keyed by name. Editing
XML and restarting changes behavior **without recompiling**.
- A class `class FooDef : Def` declares the shape; XML `<MyNamespace.FooDef>...</FooDef>` provides
  the data; the loader fills public fields by name (reflection).
- `Def` gives every def a `defName` (unique string key) and `label` (display name).
- Read them back with `DefDatabase<FooDef>.AllDefsListForReading` (all of them) or
  `GetNamedSilentFail("key")` (one by defName, or `null`).
- In this mod: `DiaryInteractionGroupDef`, `DiaryTuningDef`, `DiaryPromptDef`, `DiaryPersonaDef`
  (catalogs in `1.6/Defs/`). To add a group, retune a number, or reword a prompt, edit the XML
  and restart. **Def string fields (`instruction`, `rule`, `systemPrompt`, `label`) are localized
  via `DefInjected`, not the Keyed file** — see the localization rule.

#### `IExposable` / `ExposeData` / `Scribe_*`  (save/load)
RimWorld saves the game by calling `ExposeData()` on every `IExposable` object. The same method
runs for **both** saving and loading — `Scribe_*` reads the current mode and either writes the
field to the save file or reads it back. It's like a single `serialize`/`hydrate` function that
works both directions:
```csharp
public void ExposeData() {
    Scribe_Values.Look(ref tick, "tick");                     // a scalar
    Scribe_Collections.Look(ref list, "list", LookMode.Deep); // a list of IExposables
    if (Scribe.mode == LoadSaveMode.PostLoadInit) { /* fix-ups after load */ }
}
```
`ref` is used so the same call can overwrite the field on load. The `"tick"` string is the XML
tag in the save file — **renaming it breaks old saves**. See `DiaryEvent.cs`, `PawnDiaryRecord.cs`,
`DiaryEntry.cs`, `PawnDiarySettings.cs`.

#### `[StaticConstructorOnStartup]`  (run code at game load)
A class tagged with this attribute has its `static` constructor run once, early, at game startup.
RimWorld mods use it as their entry point. `DiaryModStartup.cs` uses it to apply Harmony patches
and inject our UI tab. (An attribute `[X]` ≈ a decorator `@X` in TS, but here it's a marker the
engine scans for, not a function wrapper.)

#### Harmony patches  (hooking vanilla methods)
We don't own RimWorld's code, so to react to game events we **patch** existing methods with the
Harmony library. A `Postfix` runs *after* the original method; a `Prefix` runs *before*.
```csharp
[HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]   // which method to hook
public static class PlayLogAddPatch {
    public static void Postfix(LogEntry entry) { /* runs after PlayLog.Add */ }
}
```
Special parameter names: `__instance` = the object the method was called on; `__result` = its
return value. `AccessTools.Field(type, "name")` uses reflection to read a **private** field of a
vanilla class we otherwise couldn't touch. See `DiaryPatches.cs`.

#### Game lifecycle hooks we implement
- `GameComponent` (`DiaryGameComponent`): `GameComponentTick()` runs every game tick (~60/sec) —
  our event loop for applying finished LLM results; `ExposeData()` saves our data; `StartedNewGame`/
  `LoadedGame` fire on new/loaded games.
- `Mod` (`PawnDiaryMod`): `DoSettingsWindowContents(Rect)` draws the settings UI (immediate-mode
  GUI — you re-emit the whole UI every frame, like redrawing to a canvas).
- `ITab` (`ITab_Pawn_Diary`): `FillTab()` draws the inspector tab when the player opens it.

#### `Pawn`, `Def`, ticks — quick glossary
- **Pawn**: a character (colonist/animal/etc.). `pawn.GetUniqueLoadID()` is its stable id (we key
  diaries by it).
- **InteractionDef / MentalStateDef**: the *kind* of a social interaction / mental break (its
  `defName` is the stable id we classify on).
- **tick**: the game's time unit; 60 ticks ≈ 1 real second at 1× speed.

### Where things live

All C# is under `Source/` (the game ignores it); the compiled DLL goes to `1.6/Assemblies/`. See
`DOCUMENTATION.md §2` for the full tree. Quick orientation:
- `Source/Core/DiaryGameComponent.cs` — orchestrator: records events, queues generation, applies results, saves.
- `Source/Models/` (`DiaryEvent.cs`, `PawnDiaryRecord.cs`, `DiaryEntry.cs`) — the saved data + view models.
- `Source/Generation/DiaryContextBuilder.cs` — turns game state into the compact text the model sees.
- `Source/Generation/DiaryPromptBuilder.cs` — assembles the final prompt strings.
- `Source/Generation/LlmClient.cs` — the async HTTP client (queue, retries, timeouts).
- `Source/Defs/InteractionGroups.cs` + `1.6/Defs/DiaryInteractionGroupDefs.xml` — the event catalog (XML-editable).
- `Source/Defs/DiaryTuningDef.cs` + `1.6/Defs/DiaryTuningDef.xml` — tuning numbers (XML-editable).
- `Source/Util/MiniJson.cs` — dependency-free JSON parser (Mono lacks the usual ones).
- `Languages/English/Keyed/PawnDiary.xml` — every UI string + the natural-language prompt text.

### Eligibility

Diary entries are only generated for **free colonists**. The helper `IsDiaryEligible(pawn)`
combines a humanlike race check and `pawn.IsColonist` (faction check). It is used at every entry
point — `RecordInteraction`, `RecordMentalState`, `FlushSmallTalkBatch`, `AddEventRef` — and in
the UI (`ITab_Pawn_Diary.IsVisible`). Mixed interactions (one eligible colonist + one ineligible
pawn) produce a solo event for the colonist; interactions between two ineligible pawns are skipped.

---

## Pointers
- `DOCUMENTATION.md` — architecture, data flow, generation modes, settings, and **§12 Localization**.
- `CHANGELOG.md` — dated history of every change (add an entry with each change).
- `1.6/Defs/*.xml` — editable interaction groups / tuning / prompts / personas (no recompile).
- `Languages/English/Keyed/PawnDiary.xml` — UI + prompt strings.
