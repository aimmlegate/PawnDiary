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

## Pointers
- `DOCUMENTATION.md` — architecture, data flow, generation modes, settings, **§2 file map**,
  **§4 eligibility**, **§12 localization**.
- `CHANGELOG.md` — dated history (add an entry with each change).
- `1.6/Defs/*.xml` — editable interaction groups / tuning / prompts / personas (no recompile).
- `Languages/English/Keyed/PawnDiary.xml` — UI + prompt strings.
