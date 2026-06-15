# Pawn Diary — guide for code agents

Onboarding for anyone (AI agent or human) changing this mod. **Read this once** and the code
should read easily. Companion files:
- `skills/pawndiary-engineering/SKILL.md` — the workflow to follow (plan → smallest change → build → document).
- `DOCUMENTATION.md` — full architecture, data flow, settings, and **§12 Localization**.
- `CHANGELOG.md` — dated history of every change.
- `CLAUDE.md` / `CODEX.md` — thin per-agent wrappers; both defer to this file.

> **Big picture:** this is a **library** (`.dll`) that RimWorld loads at startup. There is no
> `main()`. RimWorld discovers our classes by reflection and calls into them at the right moments
> (startup, every tick, on save/load, when a UI tab opens). So most of our code is "fill in this
> method and the game will call it," like implementing framework lifecycle hooks.

---

## Working rules — follow these on every change

1. **Keep the docs current.** After any change to behavior or structure, update the affected
   section of `DOCUMENTATION.md` **and** add a dated line to `CHANGELOG.md`, in the same change.
   Docs are part of "done."
2. **Stay localization-friendly.** Never hardcode English that can reach the **UI** or the **LLM
   prompt**. New player-facing strings and prompt text become keys in
   `Languages/English/Keyed/PawnDiary.xml`, used via `"My.Key".Translate()` (`{0}`/`{1}` for
   interpolation). Two carve-outs stay English on purpose: structured prompt *schema* labels
   (`event:`, `sex=`, the role and `none`/`n/a`/`unknown` sentinel words), and `LlmClient`'s
   background-thread strings — `.Translate()` is **not thread-safe**, main-thread only. Def text
   (`rule`, `label`, `instruction`, `systemPrompt`) is localized via `DefInjected`, not Keyed. Full
   carve-outs: `DOCUMENTATION.md §12`.
3. **Comment for novices.** Favor extensive, plain-English comments: every `.cs` file opens with a
   header explaining its role; public types/methods get a `///` summary; non-obvious C#/RimWorld
   idioms get a `//` note or a link to the primer below (e.g.
   `// New to C#/RimWorld? See AGENTS.md ("IExposable")`). Update comments when you touch the code.
4. **Build after every change** to confirm it compiles (see *Building*).

## Skill routing
Before acting, check whether `skills/pawndiary-engineering/SKILL.md` applies. If it does, follow it
exactly (plan first, smallest safe change, validate, document) — don't skip build or doc updates.

## Key facts
- **Runtime is RimWorld's Unity Mono.** Only assemblies shipped in `RimWorldWin64_Data/Managed`
  exist at runtime. **Do not** use `System.Web.Extensions` / `JavaScriptSerializer` or add external
  JSON libraries — JSON is parsed by `Source/Util/MiniJson.cs`.
- **Source layout.** All C# lives under `Source/` (the game ignores it and loads only the compiled
  DLL), grouped by concern. The `.csproj` globs `**\*.cs`, so new files need no project edit. Full
  tree: `DOCUMENTATION.md §2`.
- **Editable data is XML**, loaded at startup with no recompile: Defs under `1.6/Defs/` and strings
  under `Languages/`.

## Building
```
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```
→ outputs `1.6/Assemblies/PawnDiary.dll`, which is **committed on purpose** so the mod runs straight
from a clone — rebuild and stage it whenever you change C#. If `MSBuild` isn't on `PATH`, find it
with `vswhere -latest -find MSBuild\**\Bin\MSBuild.exe`, or build from a VS "Developer PowerShell."

---

## C# & RimWorld for JS/TS devs

The recurring C#/RimWorld idioms in this mod, each with its closest JS/TS analogy. Inline code
comments link here by section name (e.g. `see AGENTS.md ("Defs")`) instead of re-explaining the same
idiom in ten places.

### Language idioms
- **Properties vs fields.** `public string Name { get; set; }` is an auto-property; `public string
  Display => name;` is a read-only getter (like a TS `get`). At the call site `foo.Name = "x"` looks
  like a plain field but can run code. Convention: PascalCase for public members/types, camelCase for
  fields and locals (RimWorld's own Def fields are camelCase, e.g. `defName`, `label`).
- **`static class` = module singleton.** Can't be instantiated — just a process-wide bag of
  functions/state, like a TS module that exports functions plus a module-level variable.
  (`InteractionGroups` is one.)
- **`ref` / `out` (pass-by-reference).** TS has no equivalent (objects are by reference-value). C#
  passes the *variable*: `Parse(json, ref i)` lets the callee write back into the caller's `i`;
  `dict.TryGetValue(key, out value)` returns found? **and** fills `value` (like
  `const [ok, value] = …`). `ref` walks a parse position through `MiniJson.cs`; `out` is the try-get
  pattern everywhere.
- **Mostly like TS:** `List<T>`≈`T[]`, `Dictionary<K,V>`≈`Map`, `IReadOnlyList<T>`≈`ReadonlyArray<T>`;
  LINQ ≈ array methods (`Where`=`filter`, `Select`=`map`, `FirstOrDefault`=`find`, and `.ToList()`/
  `.ToArray()` force the lazy query to run); `?.` and `??` are identical to TS.
- **`async Task`** ≈ `async`/`Promise` (`Task<T>`≈`Promise<T>`, `await` is the same,
  `CancellationToken`≈`AbortSignal`). See `LlmClient.cs` for the queue/timeout/retry logic. **Note:**
  background `Task` continuations run off the main thread, so they must not call RimWorld APIs
  (including `.Translate()`) — see the localization rule.

### RimWorld idioms
- **Defs & DefDatabase** (data-driven content from XML). A **Def** is game content defined in XML and
  loaded into a global registry at startup — like JSON config auto-loaded into a `Map` keyed by name;
  edit the XML and restart to change behavior **without recompiling**. `class FooDef : Def` declares
  the shape and the loader fills its public fields by name. Read them back via
  `DefDatabase<FooDef>.AllDefsListForReading` (all) or `GetNamedSilentFail("key")` (one, or `null`).
  Ours: `DiaryInteractionGroupDef`, `DiaryTuningDef`, `DiaryPromptDef`, `DiaryPersonaDef` (in
  `1.6/Defs/`). Def string fields are localized via `DefInjected`, not the Keyed file.
- **`IExposable` / `ExposeData` / `Scribe_*`** (save/load). RimWorld saves the game by calling
  `ExposeData()` on every `IExposable`. The *same* method runs for both save and load — `Scribe_*`
  checks the mode and either writes the field or reads it back (one `serialize`/`hydrate` function,
  both directions):
  ```csharp
  Scribe_Values.Look(ref tick, "tick");                     // a scalar
  Scribe_Collections.Look(ref list, "list", LookMode.Deep); // a list of IExposables
  ```
  `ref` lets the call overwrite the field on load. The `"tick"` string is the save-file XML tag —
  **renaming it breaks old saves.** See `DiaryEvent.cs`, `PawnDiaryRecord.cs`, `DiaryEntry.cs`,
  `PawnDiarySettings.cs`.
- **`[StaticConstructorOnStartup]`** (run code at load). A class with this attribute runs its static
  constructor once, early, at startup — a mod's entry point. `DiaryModStartup.cs` uses it to apply
  Harmony patches and inject our UI tab. (An attribute `[X]` ≈ a decorator `@X`, but here it's a
  marker the engine scans for, not a function wrapper.)
- **Harmony patches** (hooking vanilla methods). We don't own RimWorld's code, so we **patch**
  existing methods with Harmony: a `Postfix` runs *after* the original, a `Prefix` *before*.
  ```csharp
  [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]   // which method to hook
  public static class PlayLogAddPatch {
      public static void Postfix(LogEntry entry) { /* runs after PlayLog.Add */ }
  }
  ```
  `__instance` = the object the method was called on; `__result` = its return value.
  `AccessTools.Field(type, "name")` reads a **private** vanilla field by reflection. See
  `DiaryPatches.cs`.
- **Game lifecycle hooks we implement.**
  - `GameComponent` (`DiaryGameComponent`): `GameComponentTick()` runs every tick (~60/sec) — our
    loop for applying finished LLM results; `ExposeData()` saves our data; `StartedNewGame`/
    `LoadedGame` fire on new/loaded games.
  - `Mod` (`PawnDiaryMod`): `DoSettingsWindowContents(Rect)` draws the settings UI (immediate-mode
    GUI — you re-emit the whole UI every frame).
  - `ITab` (`ITab_Pawn_Diary`): `FillTab()` draws the inspector tab when the player opens it.
- **Glossary.** **Pawn** = a character; `pawn.GetUniqueLoadID()` is its stable id (we key diaries by
  it). **InteractionDef / MentalStateDef** = the *kind* of a social interaction / mental break (its
  `defName` is the id we classify on). **tick** = the game's time unit, 60 ticks ≈ 1 real second at 1×.

---

## Pointers
- `DOCUMENTATION.md` — architecture, data flow, generation modes, settings, **§2 file map**,
  **§4 eligibility**, **§12 localization**.
- `CHANGELOG.md` — dated history (add an entry with each change).
- `1.6/Defs/*.xml` — editable interaction groups / tuning / prompts / personas (no recompile).
- `Languages/English/Keyed/PawnDiary.xml` — UI + prompt strings.
