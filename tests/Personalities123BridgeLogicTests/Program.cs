// Pure unit tests for the 1-2-3 Personalities bridge's decision logic. Mirrors the other tests/*
// console projects: a static Main runs focused assertions and returns non-zero when any fail.
//
// These run without RimWorld/SP_Module1/Verse/Unity — the logic under test (Enneagram root -> outlook
// rule, root -> internal psychotype defName, and the LLM transform-input block) is deliberately pure so
// its edge cases are covered without booting the game.
using System;
using System.Collections.Generic;
using PawnDiaryPersonalities123.Pure;

namespace Personalities123BridgeLogicTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TestRuleForRoot_AllNineMapped();
            TestRuleForRoot_RulesAreDistinct();
            TestRuleForRoot_RegisterAndNoTypeNaming();
            TestRuleForRoot_CaseInsensitive();
            TestRuleForRoot_UnknownAndBlankReturnNull();

            TestKeyForRoot_CanonicalAndDistinct();
            TestKeyForRoot_UnknownReturnsNull();

            TestInternalPsychotype_AllNineMapped();
            TestInternalPsychotype_CaseInsensitiveAndUnknownNull();
            TestInternalPsychotype_AgreesWithRuleAndKey();

            TestTransformInput_AllFieldsAndTypeNumber();
            TestTransformInput_OmitsBlankFields();
            TestTransformInput_AllBlankReturnsNull();

            Console.WriteLine();
            Console.WriteLine("Personalities123BridgeLogicTests: " + passed + " passed, " + failed + " failed.");
            return failed > 0 ? 1 : 0;
        }

        // ---- RuleForRoot ----

        private static void TestRuleForRoot_AllNineMapped()
        {
            for (int i = 1; i <= 9; i++)
            {
                string rule = EnneagramLensMapping.RuleForRoot("SP_Root" + i);
                Check("Root" + i + " maps to a non-blank rule", !string.IsNullOrWhiteSpace(rule));
            }
        }

        private static void TestRuleForRoot_RulesAreDistinct()
        {
            HashSet<string> seen = new HashSet<string>();
            bool allDistinct = true;
            for (int i = 1; i <= 9; i++)
            {
                allDistinct &= seen.Add(EnneagramLensMapping.RuleForRoot("SP_Root" + i));
            }

            Check("all 9 root rules are distinct", allDistinct && seen.Count == 9);
        }

        private static void TestRuleForRoot_RegisterAndNoTypeNaming()
        {
            for (int i = 1; i <= 9; i++)
            {
                string rule = EnneagramLensMapping.RuleForRoot("SP_Root" + i);
                // Register: Pawn Diary's psychotype rules are written in the "This pawn ..." voice.
                Check("Root" + i + " rule uses the 'This pawn' register", rule.StartsWith("This pawn"));
                // The personality TYPE must never be named in the outlook text.
                string lower = rule.ToLowerInvariant();
                bool namesType = lower.Contains("enneagram") || lower.Contains("sp_root") || lower.Contains("personality type");
                Check("Root" + i + " rule does not name the type", !namesType);
            }
        }

        private static void TestRuleForRoot_CaseInsensitive()
        {
            Check("lower-case defName resolves",
                EnneagramLensMapping.RuleForRoot("sp_root1") == EnneagramLensMapping.RuleForRoot("SP_Root1"));
            Check("padded defName resolves",
                EnneagramLensMapping.RuleForRoot("  SP_ROOT9  ") == EnneagramLensMapping.RuleForRoot("SP_Root9"));
        }

        private static void TestRuleForRoot_UnknownAndBlankReturnNull()
        {
            Check("unknown root -> null", EnneagramLensMapping.RuleForRoot("SP_Root10") == null);
            Check("animal/other root -> null", EnneagramLensMapping.RuleForRoot("SP_Animal_Chonk") == null);
            Check("null -> null", EnneagramLensMapping.RuleForRoot(null) == null);
            Check("blank -> null", EnneagramLensMapping.RuleForRoot("   ") == null);
        }

        // ---- KeyForRoot ----

        private static void TestKeyForRoot_CanonicalAndDistinct()
        {
            HashSet<string> keys = new HashSet<string>();
            bool allDistinct = true;
            for (int i = 1; i <= 9; i++)
            {
                string key = EnneagramLensMapping.KeyForRoot("SP_Root" + i);
                Check("Root" + i + " key has the outlook prefix", key != null && key.StartsWith(EnneagramLensMapping.OutlookKeyPrefix));
                Check("Root" + i + " key is canonical", key == EnneagramLensMapping.OutlookKeyPrefix + "SP_Root" + i);
                allDistinct &= keys.Add(key);
            }

            Check("all 9 outlook keys are distinct", allDistinct && keys.Count == 9);
            // Case/padding normalize to the same canonical key so the Keyed lookup always matches.
            Check("key is casing/padding independent",
                EnneagramLensMapping.KeyForRoot("  sp_root5 ") == EnneagramLensMapping.KeyForRoot("SP_Root5"));
        }

        private static void TestKeyForRoot_UnknownReturnsNull()
        {
            Check("unknown root -> null key", EnneagramLensMapping.KeyForRoot("SP_Root10") == null);
            Check("animal root -> null key", EnneagramLensMapping.KeyForRoot("SP_Animal_Chonk") == null);
            Check("null -> null key", EnneagramLensMapping.KeyForRoot(null) == null);
        }

        // ---- InternalPsychotypeForRoot (Tier 1) ----

        private static void TestInternalPsychotype_AllNineMapped()
        {
            HashSet<string> seen = new HashSet<string>();
            for (int i = 1; i <= 9; i++)
            {
                string defName = EnneagramLensMapping.InternalPsychotypeForRoot("SP_Root" + i);
                Check("Root" + i + " maps to a DiaryPsychotype_ defName",
                    !string.IsNullOrWhiteSpace(defName) && defName.StartsWith("DiaryPsychotype_"));
                seen.Add(defName);
            }

            // The pairing is subjective, but each root should point at a distinct built-in type so the
            // colony does not collapse to one psychotype.
            Check("all 9 internal psychotypes are distinct", seen.Count == 9);
        }

        private static void TestInternalPsychotype_CaseInsensitiveAndUnknownNull()
        {
            Check("case/padding independent",
                EnneagramLensMapping.InternalPsychotypeForRoot("  sp_root8 ") == EnneagramLensMapping.InternalPsychotypeForRoot("SP_Root8"));
            Check("unknown root -> null", EnneagramLensMapping.InternalPsychotypeForRoot("SP_Root10") == null);
            Check("animal root -> null", EnneagramLensMapping.InternalPsychotypeForRoot("SP_Animal_Chonk") == null);
            Check("null -> null", EnneagramLensMapping.InternalPsychotypeForRoot(null) == null);
        }

        private static void TestInternalPsychotype_AgreesWithRuleAndKey()
        {
            // A mapped root always yields a rule, a key, AND an internal type — or none of them. The three
            // tables share CanonicalRoot, so they can never disagree about which roots are mapped.
            for (int i = 1; i <= 9; i++)
            {
                bool hasRule = EnneagramLensMapping.RuleForRoot("SP_Root" + i) != null;
                bool hasKey = EnneagramLensMapping.KeyForRoot("SP_Root" + i) != null;
                bool hasType = EnneagramLensMapping.InternalPsychotypeForRoot("SP_Root" + i) != null;
                Check("Root" + i + " rule/key/type agree", hasRule && hasKey && hasType);
            }

            Check("unmapped root: rule/key/type all null",
                EnneagramLensMapping.RuleForRoot("SP_Root10") == null
                && EnneagramLensMapping.KeyForRoot("SP_Root10") == null
                && EnneagramLensMapping.InternalPsychotypeForRoot("SP_Root10") == null);
        }

        // ---- BuildTransformInput (Tier 3) ----

        private static void TestTransformInput_AllFieldsAndTypeNumber()
        {
            string input = EnneagramLensMapping.BuildTransformInput("The Achiever", "image-driven", "SP_Root3", "raw=blob");
            Check("input includes the variant", input != null && input.Contains("personality style: The Achiever"));
            Check("input includes the main trait", input.Contains("main trait: image-driven"));
            // The root defName is reduced to the bare Enneagram type number, never the SP_Root token.
            Check("input includes the type number", input.Contains("enneagram type: 3"));
            Check("input does not leak the root defName", !input.Contains("SP_Root"));
            Check("input includes the serialization", input.Contains("details: raw=blob"));
            Check("input is newline-joined", input.Contains("\n"));
        }

        private static void TestTransformInput_OmitsBlankFields()
        {
            // Only a variant present: the other labels must not appear.
            string input = EnneagramLensMapping.BuildTransformInput("The Helper", "  ", null, null);
            Check("variant-only line present", input == "personality style: The Helper");
        }

        private static void TestTransformInput_AllBlankReturnsNull()
        {
            Check("all null -> null", EnneagramLensMapping.BuildTransformInput(null, null, null, null) == null);
            Check("all blank / unknown root -> null",
                EnneagramLensMapping.BuildTransformInput(" ", "\t", "SP_Animal_Chonk", "   ") == null);
        }

        // ---- tiny assert harness ----

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("  FAIL  " + name);
            }
        }
    }
}
