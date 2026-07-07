// Assembly metadata for the Pawn Diary RimWorld DLL.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Pure pipeline helpers under Source/Pipeline (external API sanitation, budget policy, listeners,
// providers) are internal implementation detail — the public integration contract lives in the
// PawnDiary.Integration namespace. The standalone DiaryPipelineTests project compiles against the
// real assembly and needs to call those internal pure helpers, so it is explicitly trusted here.
[assembly: InternalsVisibleTo("DiaryPipelineTests")]

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
