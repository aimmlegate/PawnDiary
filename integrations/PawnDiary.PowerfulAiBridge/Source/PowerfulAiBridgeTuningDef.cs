// XML-owned policy for the Powerful AI Integration bridge. Prompt text and request/cadence caps live
// in Def XML so they can be tuned and localized without recompiling this adapter.
using Verse;

namespace PawnDiaryPowerfulAiBridge
{
    /// <summary>Adapter-local persona transfer and LLM transform policy.</summary>
    public class PowerfulAiBridgeTuningDef : Def
    {
        public int passIntervalTicks = 250;
        public int personaMaxCharacters = 3500;
        public int transformMaxTokens = 240;
        public string systemPrompt = string.Empty;

        private static PowerfulAiBridgeTuningDef fallback;

        /// <summary>Returns the shipped Def or a safe direct-mode fallback if XML is unavailable.</summary>
        public static PowerfulAiBridgeTuningDef Current
        {
            get
            {
                PowerfulAiBridgeTuningDef configured =
                    DefDatabase<PowerfulAiBridgeTuningDef>.GetNamedSilentFail(BridgeIds.TuningDefName);
                if (configured != null)
                {
                    return configured;
                }

                return fallback ?? (fallback = new PowerfulAiBridgeTuningDef());
            }
        }
    }
}
