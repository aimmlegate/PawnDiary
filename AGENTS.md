# Pawn Diary — working notes for code agents

**Always keep `DOCUMENTATION.md` up to date.** It is the living design doc for this mod.
After any change to the mod's behavior or structure, update the affected section of
`DOCUMENTATION.md` and add a dated line to its Changelog in the same change. Treat the doc
as part of "done", not an afterthought.

## Key facts
- This runs on RimWorld's Unity **Mono** runtime. Only assemblies in
  `RimWorldWin64_Data/Managed` exist at runtime. **Do not** use `System.Web.Extensions` /
  `JavaScriptSerializer` or add external JSON libs — JSON is parsed by `MiniJson.cs`.
- C# source lives under `Source/` (grouped: `Core/`, `Models/`, `Generation/`, `Defs/`,
  `Patches/`, `UI/`, `Settings/`, `Util/`). The game ignores `Source/`; it loads only the
  compiled DLL. The `.csproj` uses a recursive glob, so new `.cs` files need no project edit.
- Build: `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug` →
  `1.6/Assemblies/PawnDiary.dll`. Always build after changes to confirm it compiles.
- See `DOCUMENTATION.md` for the full architecture, data flow, and settings.
