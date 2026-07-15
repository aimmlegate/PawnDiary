// Assembly metadata for the Pawn Diary RimWorld DLL.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Pure pipeline helpers and the saved event-store surface are internal implementation detail — the
// public integration contract lives in the PawnDiary.Integration namespace. The standalone pipeline
// tests and the separate in-game RimTest assembly compile against the real assembly and need to call
// those internals, so only those two named test assemblies are explicitly trusted here.
[assembly: InternalsVisibleTo("DiaryPipelineTests")]
[assembly: InternalsVisibleTo("PawnDiary.RimTest")]

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("PawnDiary")]
[assembly: AssemblyDescription("Adds pawn diary entries for social interactions.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("PawnDiary")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("4d267dd4-d1f4-4662-86cd-50b68311a711")]

// The assembly version attributes (AssemblyVersion / AssemblyFileVersion /
// AssemblyInformationalVersion) are generated at BUILD TIME from About/About.xml <modVersion> by the
// StampVersionFromAbout target in PawnDiary.csproj, so About.xml stays the single source of truth for
// the mod version. Do NOT hardcode them here — a copy would duplicate-conflict with the generated one.
