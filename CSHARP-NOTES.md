# C# & RimWorld for JS/TS devs

A quick reference for the recurring C#/RimWorld idioms in this mod, each with the closest
JavaScript/TypeScript analogy. The inline comments in the code link back here instead of
re-explaining the same idiom in ten places. Read this once and the code should read easily.

> Big picture: this is a **library** (`.dll`) that RimWorld loads at startup. There is no
> `main()`. RimWorld discovers our classes by reflection and calls into them at the right
> moments (startup, every tick, on save/load, when a UI tab opens). So most of our code is
> "fill in this method and the game will call it," much like implementing React lifecycle
> methods or framework hooks.

---

## Language idioms

### Properties vs fields
```csharp
public string name;              // field  — plain data, like a class property in TS
public string Name { get; set; } // auto-property — same usage, but can add logic later
public string Display => name;   // read-only property — like a TS getter `get Display()`
```
`foo.Name = "x"` and `foo.name = "x"` look identical at the call site; the difference is the
property can run code in its `get`/`set`. **Convention:** PascalCase for public
methods/properties/types, camelCase for fields and locals. (RimWorld's own Def fields are
camelCase, e.g. `defName`, `label`.)

### Static classes and members = module singletons
```csharp
public static class InteractionGroups { public static List<...> All { get { ... } } }
```
A `static class` can't be instantiated — it's just a bag of functions/!state shared process-wide,
like a TS module that `export`s functions and a module-level variable. `static` fields are the
equivalent of a top-level `let` in a module: one shared copy.

### `ref` and `out` parameters (pass-by-reference)
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

### Generics
`List<string>` ≈ `string[]`/`Array<string>`; `Dictionary<string, bool>` ≈ `Map<string, boolean>`
(or a plain object). `IReadOnlyList<T>` ≈ `ReadonlyArray<T>`.

### LINQ ≈ array methods
```csharp
items.Where(x => x.ok).OrderByDescending(x => x.n).Take(3).Select(x => x.label).ToArray()
//    .filter        .sort((a,b)=>b.n-a.n)        .slice(0,3) .map
```
`FirstOrDefault()` ≈ `find()` (returns `null`/default if none). `.ToList()/.ToArray()` force the
lazy query to actually run.

### Null handling
`?.` (null-conditional) and `??` (null-coalescing) are the same as TS. `x ?? 0` = "x, or 0 if
null". `pawn?.relations?.OpinionOf(other) ?? 0` reads exactly like TS optional chaining.

### `async Task` ≈ `async`/`Promise`
`Task` ≈ `Promise<void>`, `Task<T>` ≈ `Promise<T>`, `await` is the same. `CancellationToken` ≈
`AbortSignal` (cooperative cancellation). See `LlmClient.cs` (already well-commented) for the
queue/timeout/retry logic.

---

## RimWorld idioms

### Defs & DefDatabase  (data-driven content from XML)
A **Def** is a chunk of game content defined in XML and loaded into a global registry at
startup — think JSON config files that RimWorld auto-loads into a `Map` keyed by name. Editing
XML and restarting changes behavior **without recompiling**.
- A class `class FooDef : Def` declares the shape; XML `<MyNamespace.FooDef>...</FooDef>` provides
  the data; the loader fills public fields by name (reflection).
- `Def` gives every def a `defName` (unique string key) and `label` (display name).
- Read them back with `DefDatabase<FooDef>.AllDefsListForReading` (all of them) or
  `GetNamedSilentFail("key")` (one by defName, or `null`).
- In this mod: `DiaryInteractionGroupDef` (the interaction-group catalog,
  `1.6/Defs/DiaryInteractionGroupDefs.xml`) and `DiaryTuningDef` (the tuning knobs,
  `1.6/Defs/DiaryTuningDef.xml`). To add a group or retune a number, edit the XML and restart.

### `IExposable` / `ExposeData` / `Scribe_*`  (save/load)
RimWorld saves the game by calling `ExposeData()` on every `IExposable` object. The same method
runs for **both** saving and loading — `Scribe_*` reads the current mode and either writes the
field to the save file or reads it back. It's like a single `serialize`/`hydrate` function that
works both directions:
```csharp
public void ExposeData() {
    Scribe_Values.Look(ref tick, "tick");                 // a scalar
    Scribe_Collections.Look(ref list, "list", LookMode.Deep); // a list of IExposables
    if (Scribe.mode == LoadSaveMode.PostLoadInit) { /* fix-ups after load */ }
}
```
`ref` is used so the same call can overwrite the field on load. The `"tick"` string is the XML
tag in the save file — **renaming it breaks old saves**. See `DiaryEvent.cs`, `PawnDiaryRecord.cs`,
`DiaryEntry.cs`, `PawnDiarySettings.cs`.

### `[StaticConstructorOnStartup]`  (run code at game load)
A class tagged with this attribute has its `static` constructor run once, early, at game startup.
RimWorld mods use it as their entry point. `DiaryModStartup.cs` uses it to apply Harmony patches
and inject our UI tab. (An attribute `[X]` ≈ a decorator `@X` in TS, but here it's a marker the
engine scans for, not a function wrapper.)

### Harmony patches  (hooking vanilla methods)
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

### Game lifecycle hooks we implement
- `GameComponent` (`DiaryGameComponent`): `GameComponentTick()` runs every game tick (~60/sec) —
  our event loop for applying finished LLM results; `ExposeData()` saves our data; `StartedNewGame`/
  `LoadedGame` fire on new/loaded games.
- `Mod` (`PawnDiaryMod`): `DoSettingsWindowContents(Rect)` draws the settings UI (immediate-mode
  GUI — you re-emit the whole UI every frame, like redrawing to a canvas).
- `ITab` (`ITab_Pawn_Diary`): `FillTab()` draws the inspector tab when the player opens it.

### `Pawn`, `Def`, ticks — quick glossary
- **Pawn**: a character (colonist/animal/etc.). `pawn.GetUniqueLoadID()` is its stable id (we key
  diaries by it).
- **InteractionDef / MentalStateDef**: the *kind* of a social interaction / mental break (its
  `defName` is the stable id we classify on).
- **tick**: the game's time unit; 60 ticks ≈ 1 real second at 1× speed.

---

## Where things live (after the refactor)
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
