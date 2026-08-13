// Pure unit tests for the example adapter's parsing and optional-version compatibility helpers.
// Mirrors the shape of the other tests/* console projects: a static Main that runs focused
// assertions and returns non-zero when any assertion fails.
//
// These run without RimWorld/Verse/Unity — the helpers under test are deliberately pure so the
// explorer's parsing edge cases (multiline paste, comment lines, tri-state round-trips, tick
// bounds) are covered without booting the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PawnDiaryExampleAdapter;

namespace ExampleAdapterParsingTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TestLinesFromMultiline_BasicSplit();
            TestLinesFromMultiline_DropsBlanksAndComments();
            TestLinesFromMultiline_CapsAtMaxLines();
            TestLinesFromMultiline_NullAndEmpty();
            TestLinesFromMultiline_MixedLineEndings();
            TestMultilineFromLines_RoundTrip();
            TestMultilineFromLines_NullAndEmpty();
            TestLooksLikeEventKey_ValidAndInvalid();
            TestNormalizePovRole();
            TestParseTick();
            TestParsePositiveInt();
            TestTriStateRoundTrip();
            TestTriStateOutOfRange();
            TestLoadedApiVersionProbe();
            TestFrequencyV9Shim_DynamicV8Surface();
            TestFrequencyV9Shim_DetachesV9Snapshot();
            TestFrequencyV9Shim_InvokesV9Writes();
            TestBuiltAdapterHasNoHardV9References();

            Console.WriteLine("==================================================");
            Console.WriteLine("ExplorerParsing tests: " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        // ---- LinesFromMultiline --------------------------------------------------

        private static void TestLinesFromMultiline_BasicSplit()
        {
            List<string> r = ExplorerParsing.LinesFromMultiline("a=1\nb=2\nc=3");
            Assert(r.Count == 3, "3 non-blank lines → count 3; got " + r.Count);
            Assert(r[0] == "a=1", "first line preserved; got '" + r[0] + "'");
            Assert(r[2] == "c=3", "last line preserved; got '" + r[2] + "'");
        }

        private static void TestLinesFromMultiline_DropsBlanksAndComments()
        {
            List<string> r = ExplorerParsing.LinesFromMultiline("a=1\n\n  \n# a note\nb=2");
            Assert(r.Count == 2, "blank + comment lines dropped; got " + r.Count);
            Assert(r[0] == "a=1" && r[1] == "b=2", "only the two real lines remain");
        }

        private static void TestLinesFromMultiline_CapsAtMaxLines()
        {
            // Build a blob with more lines than the cap.
            string blob = "key0=0";
            for (int i = 1; i < ExplorerParsing.MaxMultilineLines + 20; i++)
            {
                blob += "\nkey" + i + "=" + i;
            }

            List<string> r = ExplorerParsing.LinesFromMultiline(blob);
            Assert(r.Count == ExplorerParsing.MaxMultilineLines,
                "capped at MaxMultilineLines (" + ExplorerParsing.MaxMultilineLines + "); got " + r.Count);
        }

        private static void TestLinesFromMultiline_NullAndEmpty()
        {
            Assert(ExplorerParsing.LinesFromMultiline(null).Count == 0, "null → empty list");
            Assert(ExplorerParsing.LinesFromMultiline("").Count == 0, "empty → empty list");
            Assert(ExplorerParsing.LinesFromMultiline("   \n  \t \n").Count == 0, "only blanks → empty list");
        }

        private static void TestLinesFromMultiline_MixedLineEndings()
        {
            List<string> r = ExplorerParsing.LinesFromMultiline("a=1\r\nb=2\rc=3");
            Assert(r.Count == 3, "CR, LF, CRLF all split; got " + r.Count);
        }

        // ---- MultilineFromLines -------------------------------------------------

        private static void TestMultilineFromLines_RoundTrip()
        {
            List<string> input = new List<string> { "a=1", "b=2", "c=3" };
            string blob = ExplorerParsing.MultilineFromLines(input);
            // Round-trip through LinesFromMultiline and expect the same list back.
            List<string> back = ExplorerParsing.LinesFromMultiline(blob);
            Assert(back.Count == 3, "round-trip preserves count");
            Assert(back[0] == "a=1" && back[2] == "c=3", "round-trip preserves order");
        }

        private static void TestMultilineFromLines_NullAndEmpty()
        {
            Assert(ExplorerParsing.MultilineFromLines(null) == string.Empty, "null → empty string");
            Assert(ExplorerParsing.MultilineFromLines(new List<string>()) == string.Empty, "empty list → empty string");
        }

        // ---- LooksLikeEventKey --------------------------------------------------

        private static void TestLooksLikeEventKey_ValidAndInvalid()
        {
            Assert(ExplorerParsing.LooksLikeEventKey("exampleadapter_quiet_moment"), "snake_case with prefix is valid");
            Assert(!ExplorerParsing.LooksLikeEventKey(""), "empty invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("   "), "whitespace invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("nounderscore"), "no underscore invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("has space_inside"), "space invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("a".PadRight(200, 'a') + "_x"), "too long invalid");
            Assert(ExplorerParsing.LooksLikeEventKey("a_b"), "minimal two-segment valid");
        }

        // ---- NormalizePovRole ---------------------------------------------------

        private static void TestNormalizePovRole()
        {
            Assert(ExplorerParsing.NormalizePovRole("") == null, "blank → null");
            Assert(ExplorerParsing.NormalizePovRole("   ") == null, "whitespace → null");
            Assert(ExplorerParsing.NormalizePovRole(null) == null, "null → null");
            Assert(ExplorerParsing.NormalizePovRole("initiator") == "initiator", "value preserved");
            Assert(ExplorerParsing.NormalizePovRole("  recipient  ") == "recipient", "value trimmed");
        }

        // ---- ParseTick / ParsePositiveInt --------------------------------------

        private static void TestParseTick()
        {
            Assert(ExplorerParsing.ParseTick("", -1) == -1, "blank → negative default");
            Assert(ExplorerParsing.ParseTick("  ", -1) == -1, "whitespace → negative default");
            Assert(ExplorerParsing.ParseTick("12345", -1) == 12345, "valid positive → value");
            Assert(ExplorerParsing.ParseTick("-5", -1) == -1, "negative → negative default");
            Assert(ExplorerParsing.ParseTick("abc", -1) == -1, "non-numeric → negative default");
        }

        private static void TestParsePositiveInt()
        {
            Assert(ExplorerParsing.ParsePositiveInt("", 5) == 5, "blank → fallback");
            Assert(ExplorerParsing.ParsePositiveInt("0", 5) == 5, "zero → fallback");
            Assert(ExplorerParsing.ParsePositiveInt("-3", 5) == 5, "negative → fallback");
            Assert(ExplorerParsing.ParsePositiveInt("12", 5) == 12, "valid positive → value");
        }

        // ---- TriState round-trip -----------------------------------------------

        private static void TestTriStateRoundTrip()
        {
            for (int ui = 0; ui < 3; ui++)
            {
                int api = ExplorerParsing.TriStateFromIndex(ui);
                int back = ExplorerParsing.IndexFromTriState(api);
                Assert(back == ui, "UI index " + ui + " round-trips through API value " + api);
            }
        }

        private static void TestTriStateOutOfRange()
        {
            Assert(ExplorerParsing.TriStateFromIndex(99) == -1, "out-of-range UI index → any (-1)");
            Assert(ExplorerParsing.TriStateFromIndex(-7) == -1, "negative UI index → any (-1)");
            Assert(ExplorerParsing.IndexFromTriState(99) == 0, "out-of-range API value → UI any (0)");
        }

        // ---- Loaded API version -------------------------------------------------

        private static void TestLoadedApiVersionProbe()
        {
            Assert(LoadedApiVersionProbe.Read(typeof(LiteralVersionShape)) == 17,
                "literal const version is read from loaded metadata");
            Assert(LoadedApiVersionProbe.Read(typeof(StaticVersionShape)) == 23,
                "ordinary static version field is read from the loaded type");
            Assert(LoadedApiVersionProbe.Read(typeof(MissingVersionShape)) == 0,
                "missing version field fails safely to zero");
            Assert(LoadedApiVersionProbe.Read(typeof(WrongTypeVersionShape)) == 0,
                "non-integer version field fails safely to zero");
            Assert(LoadedApiVersionProbe.Read(null) == 0,
                "null facade type fails safely to zero");
        }

        private static class LiteralVersionShape
        {
            public const int ApiVersion = 17;
        }

        private static class StaticVersionShape
        {
            public static readonly int ApiVersion = 23;
        }

        private static class MissingVersionShape
        {
        }

        private static class WrongTypeVersionShape
        {
            public const string ApiVersion = "twenty-three";
        }

        // ---- Optional API-v9 frequency compatibility ----------------------------

        private static void TestFrequencyV9Shim_DynamicV8Surface()
        {
            Type v8ApiType = BuildDynamicV8ApiType();

            Assert(LoadedApiVersionProbe.Read(v8ApiType) == 8,
                "dynamic separate facade advertises API v8 through loaded metadata");
            Assert(!FrequencyApiV9Shim.IsSupported(v8ApiType),
                "API-v8 facade does not advertise optional frequency support");
            Assert(FrequencyApiV9Shim.GetEventFrequencySettings(v8ApiType) == null,
                "API-v8 facade with no v9 getter degrades to null without type-load failure");
            Assert(!FrequencyApiV9Shim.SetEventFrequencyPreset(v8ApiType, "PawnDiary_Frequency_Standard"),
                "API-v8 facade with no v9 preset setter degrades to false");
            Assert(!FrequencyApiV9Shim.SetEventFrequencyMultiplier(v8ApiType, "smalltalk", 1f),
                "API-v8 facade with no v9 multiplier setter degrades to false");
            Assert(!FrequencyApiV9Shim.ResetEventFrequencyMultiplier(v8ApiType, "smalltalk"),
                "API-v8 facade with no v9 reset method degrades to false");

            Assert(FrequencyApiV9Shim.GetEventFrequencySettings(typeof(V9MissingMembersShape)) == null,
                "advertised v9 with a missing getter still fails safely");
            Assert(!FrequencyApiV9Shim.SetEventFrequencyPreset(
                    typeof(V9MissingMembersShape),
                    "PawnDiary_Frequency_Standard"),
                "advertised v9 with a missing setter still fails safely");
        }

        private static Type BuildDynamicV8ApiType()
        {
            // This is a separate runtime assembly with the real facade's full type name but only the
            // API-v8 version field. Compiling the shim into this test project without PawnDiary.dll,
            // then exercising this shape, proves the compatibility path has no v9/core type link.
            AssemblyName assemblyName = new AssemblyName(
                "PawnDiaryV8SurfaceFixture_" + Guid.NewGuid().ToString("N"));
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name);
            TypeBuilder type = module.DefineType(
                "PawnDiary.Integration.PawnDiaryApi",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            FieldBuilder version = type.DefineField(
                "ApiVersion",
                typeof(int),
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
            version.SetConstant(8);
            return type.CreateType();
        }

        private static void TestFrequencyV9Shim_DetachesV9Snapshot()
        {
            V9ApiShape.snapshot = new V9SnapshotShape
            {
                selectedPresetDefName = "PawnDiary_Frequency_Frequent",
                selectedPresetLabel = "Frequent",
                hasCustomOverrides = true,
                filters = new List<V9FilterShape>
                {
                    new V9FilterShape
                    {
                        key = "smalltalk",
                        label = "Small talk",
                        domain = "Interaction",
                        enabled = true,
                        defaultEnabled = false,
                        hasOverride = true,
                        frequencyTier = "normal",
                        presetFrequencyMultiplier = 1.5f,
                        effectiveFrequencyMultiplier = 2f,
                        hasFrequencyOverride = true
                    }
                }
            };

            AdapterEventFrequencySettingsSnapshot copy =
                FrequencyApiV9Shim.GetEventFrequencySettings(typeof(V9ApiShape));
            Assert(copy != null, "API-v9 snapshot is discovered through reflection");
            Assert(copy != null && copy.selectedPresetDefName == "PawnDiary_Frequency_Frequent",
                "API-v9 preset defName is copied");
            Assert(copy != null && copy.selectedPresetLabel == "Frequent" && copy.hasCustomOverrides,
                "API-v9 preset label and custom flag are copied");
            Assert(copy != null && copy.filters.Count == 1,
                "API-v9 event rows are copied in order");

            AdapterEventFrequencyFilterSnapshot row = copy == null || copy.filters.Count == 0
                ? null
                : copy.filters[0];
            Assert(row != null && row.key == "smalltalk" && row.domain == "Interaction",
                "API-v9 event row identity fields are copied");
            Assert(row != null && row.enabled && !row.defaultEnabled && row.hasOverride,
                "API-v8-compatible enable fields retain their semantics");
            Assert(row != null
                && row.frequencyTier == "normal"
                && row.presetFrequencyMultiplier == 1.5f
                && row.effectiveFrequencyMultiplier == 2f
                && row.hasFrequencyOverride,
                "API-v9 frequency fields are copied");

            V9ApiShape.snapshot.selectedPresetLabel = "mutated";
            V9ApiShape.snapshot.filters[0].key = "mutated";
            V9ApiShape.snapshot.filters.Add(new V9FilterShape());
            Assert(copy != null && copy.selectedPresetLabel == "Frequent",
                "adapter preset snapshot is detached from the core object");
            Assert(copy != null && copy.filters.Count == 1 && copy.filters[0].key == "smalltalk",
                "adapter event rows/list are detached from the core object graph");
        }

        private static void TestFrequencyV9Shim_InvokesV9Writes()
        {
            V9ApiShape.lastPreset = null;
            V9ApiShape.lastGroup = null;
            V9ApiShape.lastMultiplier = -1f;
            V9ApiShape.lastResetGroup = null;

            Assert(FrequencyApiV9Shim.SetEventFrequencyPreset(
                    typeof(V9ApiShape),
                    "PawnDiary_Frequency_Lite"),
                "v9 preset setter result is returned");
            Assert(V9ApiShape.lastPreset == "PawnDiary_Frequency_Lite",
                "v9 preset setter receives the exact token");

            Assert(FrequencyApiV9Shim.SetEventFrequencyMultiplier(
                    typeof(V9ApiShape),
                    "smalltalk",
                    0.5f),
                "v9 multiplier setter result is returned");
            Assert(V9ApiShape.lastGroup == "smalltalk" && V9ApiShape.lastMultiplier == 0.5f,
                "v9 multiplier setter receives the exact group/value");

            Assert(FrequencyApiV9Shim.ResetEventFrequencyMultiplier(typeof(V9ApiShape), "smalltalk"),
                "v9 reset result is returned");
            Assert(V9ApiShape.lastResetGroup == "smalltalk",
                "v9 reset receives the exact group");
        }

        private static void TestBuiltAdapterHasNoHardV9References()
        {
            try
            {
                string repoRoot = FindRepoRoot();
                string adapterPath = Path.Combine(
                    repoRoot,
                    "integrations",
                    "PawnDiary.ExampleAdapter",
                    "1.6",
                    "Assemblies",
                    "PawnDiaryExampleAdapter.dll");

                using (FileStream stream = File.OpenRead(adapterPath))
                using (PEReader pe = new PEReader(stream))
                {
                    MetadataReader metadata = pe.GetMetadataReader();
                    bool hasV9DtoTypeReference = false;
                    foreach (TypeReferenceHandle handle in metadata.TypeReferences)
                    {
                        TypeReference type = metadata.GetTypeReference(handle);
                        string typeNamespace = metadata.GetString(type.Namespace);
                        string typeName = metadata.GetString(type.Name);
                        if (typeNamespace == "PawnDiary.Integration"
                            && typeName == "DiaryEventFrequencySettingsSnapshot")
                        {
                            hasV9DtoTypeReference = true;
                            break;
                        }
                    }

                    bool hasDirectV9MethodReference = false;
                    foreach (MemberReferenceHandle handle in metadata.MemberReferences)
                    {
                        string memberName = metadata.GetString(metadata.GetMemberReference(handle).Name);
                        if (memberName == "GetEventFrequencySettings"
                            || memberName == "SetEventFrequencyPreset"
                            || memberName == "SetEventFrequencyMultiplier"
                            || memberName == "ResetEventFrequencyMultiplier")
                        {
                            hasDirectV9MethodReference = true;
                            break;
                        }
                    }

                    Assert(!hasV9DtoTypeReference,
                        "built adapter metadata has no hard reference to the core v9 frequency DTO");
                    Assert(!hasDirectV9MethodReference,
                        "built adapter metadata has no direct call reference to core v9 frequency methods");
                }
            }
            catch (Exception e)
            {
                Assert(false, "built-adapter v8 compatibility metadata audit ran: " + e.Message);
            }
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo cursor = new DirectoryInfo(AppContext.BaseDirectory);
            while (cursor != null)
            {
                string marker = Path.Combine(
                    cursor.FullName,
                    "integrations",
                    "PawnDiary.ExampleAdapter",
                    "Source",
                    "PawnDiaryExampleAdapter.csproj");
                if (File.Exists(marker))
                {
                    return cursor.FullName;
                }

                cursor = cursor.Parent;
            }

            throw new DirectoryNotFoundException("Pawn Diary repository root was not found.");
        }

        private static class V9MissingMembersShape
        {
            public const int ApiVersion = 9;
        }

        private sealed class V9SnapshotShape
        {
            public string selectedPresetDefName;
            public string selectedPresetLabel;
            public bool hasCustomOverrides;
            public List<V9FilterShape> filters;
        }

        private sealed class V9FilterShape
        {
            public string key;
            public string label;
            public string domain;
            public bool enabled;
            public bool defaultEnabled;
            public bool hasOverride;
            public string frequencyTier;
            public float presetFrequencyMultiplier;
            public float effectiveFrequencyMultiplier;
            public bool hasFrequencyOverride;
        }

        private static class V9ApiShape
        {
            public const int ApiVersion = 9;
            public static V9SnapshotShape snapshot;
            public static string lastPreset;
            public static string lastGroup;
            public static float lastMultiplier;
            public static string lastResetGroup;

            public static V9SnapshotShape GetEventFrequencySettings()
            {
                return snapshot;
            }

            public static bool SetEventFrequencyPreset(string presetDefName)
            {
                lastPreset = presetDefName;
                return true;
            }

            public static bool SetEventFrequencyMultiplier(string key, float multiplier)
            {
                lastGroup = key;
                lastMultiplier = multiplier;
                return true;
            }

            public static bool ResetEventFrequencyMultiplier(string key)
            {
                lastResetGroup = key;
                return true;
            }
        }

        // ---- helpers ------------------------------------------------------------

        private static void Assert(bool condition, string message)
        {
            if (condition)
            {
                passed++;
                return;
            }

            failed++;
            Console.WriteLine("FAIL: " + message);
        }
    }
}
