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
2. **Respect the architecture barriers by default.** New features should follow the established
   flow: impure game listener/state collection -> plain typed payload/context -> pure selection,
   planning, parsing, formatting, or rule application -> impure transport/persistence/UI adapter.
   If the contract needs more data, extend the plain DTO/Def contract rather than passing live
   `Pawn`, `Def`, Verse/Unity GUI, settings, or transport objects into pure code. Whenever logic can
   be pure, make it pure and add/extend a standalone test under `tests/`.
3. **Put tunable constants in XML.** Prompt text, group policy, thresholds, odds, UI style values,
   text decoration rules, tags, colors, and similar feature policy belong in XML Defs with safe code
   fallbacks where needed. Hardcoded C# values are for stable schema/save tokens, defensive caps,
   parser sentinels, and fallback defaults.
4. **Stay localization-friendly.** Never hardcode English that can reach the **UI** or the **LLM
   prompt**. New player-facing strings and prompt text become keys in
   `Languages/English/Keyed/PawnDiary.xml`, used via `"My.Key".Translate()` (`{0}`/`{1}` for
   interpolation). Two carve-outs stay English on purpose: structured prompt *schema* labels
   (`event:`, `sex=`, the role and `none`/`n/a`/`unknown` sentinel words), and `LlmClient`'s
   background-thread strings — `.Translate()` is **not thread-safe**, main-thread only. Def text
   (`rule`, `label`, `instruction`, `systemPrompt`) is localized via `DefInjected`, not Keyed. Full
   carve-outs: `DOCUMENTATION.md §12`.
5. **Comment for novices.** Favor extensive, plain-English comments: every `.cs` file opens with a
   header explaining its role; public types/methods get a `///` summary; non-obvious C#/RimWorld
   idioms get a `//` note or a link to the primer below (e.g.
   `// New to C#/RimWorld? See AGENTS.md ("IExposable")`). Update comments when you touch the code.
6. **Build after every change** to confirm it compiles (see *Building*).
7. **Don't break a no-DLC game.** This mod targets **base RimWorld** and declares no DLC in
   `About/About.xml` — keep it that way. A new feature may *react* to DLC content but must never
   *require* it. Before touching anything from Royalty / Ideology / Biotech / Anomaly, read
   **DLC-safety** below.

## Skill routing
Before acting, check whether `skills/pawndiary-engineering/SKILL.md` applies. If it does, follow it
exactly (plan first, smallest safe change, validate, document) — don't skip build or doc updates.

## DLC-safety — a feature must not require a paid DLC
The mod must keep working for players who own **no** paid DLC. It does today; keep it that way.

**The trap (this surprises people coming from Node/npm):** *all* RimWorld C# — base game **and**
all four DLCs — ships in one assembly, `Assembly-CSharp.dll`. DLCs are just content packs (XML
defs, art, audio) plus an ownership flag. So a DLC type like `Gene` or `JobDriver_InstallRelic`
**compiles fine without the DLC** — unlike a missing npm package, the reference resolves and the
build passes. It tells you nothing. The breakage shows up at **runtime**, where the DLC's content
and pawn state are absent.

Rules, strongest first:

- **Prefer our reactive string-matching pattern — it references no DLC at all.** We never name DLC
  content in C#. We hook base-game choke points that *every* event flows through — `PlayLog.Add`,
  `MentalStateHandler.TryStartMentalState`, `TaleRecorder.RecordTale`,
  `GameConditionManager.RegisterCondition` — and classify each event by matching its `defName`
  **as a string** (`1.6/Defs/DiaryInteractionGroupDefs.xml`). DLC defNames simply never appear
  without the DLC, so those matchers sit harmlessly inert: no crash, no null-check, no `MayRequire`.
  Add new DLC-aware content (a gene-, ritual-, or anomaly-themed prompt group) the **same way** —
  string matchers in XML, never a C# type or def reference.
- **Null-check DLC-gated pawn data — and put it in `DlcContext`.** These trackers are **`null`**
  for a player without the DLC: `pawn.genes` (Biotech), `pawn.royalty` (Royalty), `pawn.ideo`
  (Ideology). `Source/Generation/DlcContext.cs` is the **one** home for reading them: each accessor
  double-guards (`ModsConfig.<Dlc>Active` + null-check) and returns `string.Empty` when absent, so
  callers append the result unconditionally and a no-DLC game just omits the line (see the
  `xenotype=`/`title=`/`faith=` fields in `BuildPawnSummary`). Add new DLC pawn reads there, in the
  same shape — don't scatter raw `pawn.genes`/`royalty`/`ideo` access through the codebase.
- **Gate live DLC behavior** behind `ModsConfig.RoyaltyActive` / `IdeologyActive` / `BiotechActive`
  / `AnomalyActive` before doing anything DLC-specific. The type existing ≠ the content present.
- **Never `DefDatabase<T>.GetNamed("SomeDlcDef")`** — it throws when the def is missing. Use
  `GetNamedSilentFail` + null-check, or (better) the string-matcher pattern above.
- **In XML, never reference a DLC def** (as `ParentName`, a `<li>` def reference, or a field)
  without tagging it `MayRequire="Ludeon.RimWorld.Biotech"` (etc.); an untagged reference to a
  missing def errors on load. Our matcher lists are plain strings, so they're safe.
- **Harmony-patching a DLC method is fine** (the type exists) — it just never fires without the
  DLC. If the target is a fragile compiler-generated name, register it defensively with a
  null-check + warning like `RelicInstallCompletionPatch.TryRegister`, never via bare `PatchAll`.
- **Only add a DLC `<modDependencies>` to `About.xml` if the *entire mod* needs it** — never for
  one optional feature.

Done bar for any DLC-touching change: *would this run, cleanly no-op, or crash if the DLC weren't
installed?* It must run or no-op — never crash, never spam errors.

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

## Git hooks
Tracked hooks live in `.githooks/`. Enable them in a clone with:
```
git config core.hooksPath .githooks
```
`pre-commit` and `pre-push` run `.githooks/verify.ps1`: whitespace checks, XML parsing, pure test
projects, and the Debug MSBuild build. If the build changes `1.6/Assemblies/PawnDiary.dll`, stage
the rebuilt DLL and retry. Use `PAWNDIARY_SKIP_VERIFY_HOOKS=1` only for an intentional emergency
bypass.

---

## Pointers
- `DOCUMENTATION.md` — architecture, data flow, generation modes, settings, **§2 file map**,
  **§4 eligibility**, **§12 localization**.
- `CHANGELOG.md` — dated history (add an entry with each change).
- `1.6/Defs/*.xml` — editable interaction groups / tuning / prompts / personas (no recompile).
- `Languages/English/Keyed/PawnDiary.xml` — UI + prompt strings.
