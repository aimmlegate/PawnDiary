// Pure unit tests for the 1-2-3 Personalities bridge's decision logic. Mirrors the other tests/*
// console projects: a static Main runs focused assertions and returns non-zero when any fail.
//
// These run without RimWorld/SP_Module1/Verse/Unity — the logic under test (Enneagram root -> outlook
// rule, and the Tier A context line) is deliberately pure so its edge cases are covered without booting
// the game.
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

            TestContextLine_VariantAndTrait();
            TestContextLine_VariantOnly();
            TestContextLine_TraitOnly();
            TestContextLine_BothBlankReturnsNull();
            TestContextLine_TrimsAndKeepsSchemaKey();

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
            // A mapped root always yields both a rule and a key, or neither — they never disagree.
            for (int i = 1; i <= 9; i++)
            {
                bool hasRule = EnneagramLensMapping.RuleForRoot("SP_Root" + i) != null;
                bool hasKey = EnneagramLensMapping.KeyForRoot("SP_Root" + i) != null;
                Check("Root" + i + " rule and key agree", hasRule && hasKey);
            }
        }

        // ---- ContextLine ----

        private static void TestContextLine_VariantAndTrait()
        {
            Check("variant + trait joins with comma",
                EnneagramLensMapping.ContextLine("reformer", "principled") == "personality=reformer, principled");
        }

        private static void TestContextLine_VariantOnly()
        {
            Check("variant only",
                EnneagramLensMapping.ContextLine("helper", null) == "personality=helper");
            Check("variant with blank trait",
                EnneagramLensMapping.ContextLine("helper", "   ") == "personality=helper");
        }

        private static void TestContextLine_TraitOnly()
        {
            Check("trait only falls back to trait",
                EnneagramLensMapping.ContextLine(null, "calm") == "personality=calm");
        }

        private static void TestContextLine_BothBlankReturnsNull()
        {
            Check("both null -> null", EnneagramLensMapping.ContextLine(null, null) == null);
            Check("both blank -> null", EnneagramLensMapping.ContextLine(" ", "\t") == null);
        }

        private static void TestContextLine_TrimsAndKeepsSchemaKey()
        {
            Check("labels are trimmed",
                EnneagramLensMapping.ContextLine("  investigator ", " secretive ") == "personality=investigator, secretive");
            Check("schema key prefix is present",
                EnneagramLensMapping.ContextLine("peacemaker", null).StartsWith(EnneagramLensMapping.ContextSchemaKey));
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
