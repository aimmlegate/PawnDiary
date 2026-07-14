// RimWorld-free tests for complete persona formatting, caps, and stable change detection.
using System;
using PawnDiaryPowerfulAiBridge.Pure;

namespace PowerfulAiBridgeLogicTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            FormatsEverySemanticField();
            OmitsBlankFieldsAndNormalizesWhitespace();
            BlankPersonaReturnsNull();
            CapsWithoutSplittingSurrogatePair();
            FingerprintIsStableAndSensitive();
            Console.WriteLine("PowerfulAiBridgeLogicTests: " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void FormatsEverySemanticField()
        {
            PowerfulAiPersonaSnapshot snapshot = CompleteSnapshot();
            string text = PersonaTransferText.BuildDirectRule(snapshot, 4000);
            Check("persona included", text.Contains("persona: guarded but loyal"));
            Check("speech habits included", text.Contains("speech habits: short answers"));
            Check("story role included", text.Contains("story role: reluctant medic"));
            Check("character prompt included", text.Contains("character prompt: protects younger colonists"));
            Check("preset included", text.Contains("persona preset: Watchful"));
            Check("LLM input equals direct source", text == PersonaTransferText.BuildTransformInput(snapshot, 4000));
        }

        private static void OmitsBlankFieldsAndNormalizesWhitespace()
        {
            PowerfulAiPersonaSnapshot snapshot = new PowerfulAiPersonaSnapshot
            {
                dialoguePersona = "  patient\n\tand   precise  ",
                speechHabits = " ",
                storyRole = null
            };
            string text = PersonaTransferText.BuildDirectRule(snapshot, 4000);
            Check("whitespace normalized", text == "persona: patient and precise");
            Check("blank labels omitted", !text.Contains("speech habits") && !text.Contains("story role"));
        }

        private static void BlankPersonaReturnsNull()
        {
            Check("null snapshot returns null", PersonaTransferText.BuildDirectRule(null, 100) == null);
            Check("blank snapshot returns null",
                PersonaTransferText.BuildDirectRule(new PowerfulAiPersonaSnapshot(), 100) == null);
            Check("nonpositive cap returns null",
                PersonaTransferText.BuildDirectRule(CompleteSnapshot(), 0) == null);
        }

        private static void CapsWithoutSplittingSurrogatePair()
        {
            PowerfulAiPersonaSnapshot snapshot = new PowerfulAiPersonaSnapshot
            {
                dialoguePersona = "abc😀def"
            };
            string full = PersonaTransferText.BuildDirectRule(snapshot, 100);
            int highSurrogateIndex = full.IndexOf('\ud83d');
            string capped = PersonaTransferText.BuildDirectRule(snapshot, highSurrogateIndex + 1);
            Check("cap avoids dangling high surrogate",
                capped.Length == 0 || !char.IsHighSurrogate(capped[capped.Length - 1]));
        }

        private static void FingerprintIsStableAndSensitive()
        {
            string a = PersonaTransferText.StableFingerprint("same text");
            Check("same input is stable", a == PersonaTransferText.StableFingerprint("same text"));
            Check("different input changes fingerprint", a != PersonaTransferText.StableFingerprint("same text!"));
            Check("null equals empty", PersonaTransferText.StableFingerprint(null)
                == PersonaTransferText.StableFingerprint(string.Empty));
        }

        private static PowerfulAiPersonaSnapshot CompleteSnapshot()
        {
            return new PowerfulAiPersonaSnapshot
            {
                dialoguePersona = "guarded but loyal",
                speechHabits = "short answers",
                storyRole = "reluctant medic",
                characterPrompt = "protects younger colonists",
                personaPreset = "Watchful"
            };
        }

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
