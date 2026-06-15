// Harmony patches — our hooks into vanilla RimWorld. We don't own the game's code, so we "patch"
// two methods with Postfixes that run AFTER the originals to catch the events we care about:
// PlayLog.Add (every social interaction) and MentalStateHandler.TryStartMentalState (social
// fights + mental breaks). Each just forwards to DiaryGameComponent. AccessTools.Field reads
// private vanilla fields via reflection. New to this? See AGENTS.md ("Harmony patches").
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PawnDiary
{
    // Fires whenever a pawn enters a mental state: social fights (pairwise) and mental
    // breaks (solo). This is the single choke point for all mental states, so it catches
    // them regardless of how they were triggered.
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class MentalStateStartPatch
    {
        // Reflection accessor for the private MentalStateHandler.pawn field so we can read the subject pawn.
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(MentalStateHandler), "pawn");

        /// <summary>
        /// Harmony Postfix for MentalStateHandler.TryStartMentalState. Forwards successful
        /// mental state transitions to DiaryGameComponent for diary recording.
        /// </summary>
        public static void Postfix(bool __result, MentalStateHandler __instance, MentalStateDef stateDef, string reason, Pawn otherPawn)
        {
            if (!__result || stateDef == null || __instance == null)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMentalState(pawn, stateDef, otherPawn, reason);
        }
    }

    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
    public static class PlayLogAddPatch
    {
        // Reflection accessors for private fields on PlayLogEntry_Interaction — RimWorld doesn't
        // expose these publicly, so we read them via Harmony's AccessTools.
        private static readonly FieldInfo IntDefField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "intDef");
        private static readonly FieldInfo InitiatorField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "initiator");
        private static readonly FieldInfo RecipientField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "recipient");

        /// <summary>
        /// Harmony Postfix for PlayLog.Add. When the added entry is a social interaction,
        /// extracts the interaction type and participants and forwards them to DiaryGameComponent.
        /// </summary>
        public static void Postfix(LogEntry entry)
        {
            PlayLogEntry_Interaction interactionEntry = entry as PlayLogEntry_Interaction;
            if (interactionEntry == null)
            {
                return;
            }

            InteractionDef interactionDef = IntDefField?.GetValue(interactionEntry) as InteractionDef;
            Pawn initiator = InitiatorField?.GetValue(interactionEntry) as Pawn;
            Pawn recipient = RecipientField?.GetValue(interactionEntry) as Pawn;
            string initiatorGameText = GameTextFromPov(interactionEntry, initiator);
            string recipientGameText = GameTextFromPov(interactionEntry, recipient);

            DiaryGameComponent.Current?.RecordInteraction(initiator, recipient, interactionDef, initiatorGameText, recipientGameText);
        }

        /// <summary>
        /// Safely renders the interaction log entry from a pawn's point of view,
        /// falling back to ToString() if the game's POV method throws.
        /// </summary>
        private static string GameTextFromPov(PlayLogEntry_Interaction interactionEntry, Pawn pawn)
        {
            if (interactionEntry == null || pawn == null)
            {
                return string.Empty;
            }

            try
            {
                return interactionEntry.ToGameStringFromPOV(pawn, false);
            }
            catch
            {
                return interactionEntry.ToString();
            }
        }
    }
}
