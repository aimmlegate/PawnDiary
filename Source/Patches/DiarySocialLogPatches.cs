// Social-log Harmony patches. These hooks capture vanilla social interactions, redirect finished
// diary-entry clicks from the Social tab, and let generated direct-speech rows display parsed text.
// New to this? See AGENTS.md ("Harmony patches").
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Captures vanilla social PlayLog additions and forwards eligible interactions to the diary.
    /// </summary>
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
            if (GeneratedSpeechPlayLog.IsAddingGeneratedSpeechEntry)
            {
                return;
            }

            PlayLogEntry_Interaction interactionEntry = entry as PlayLogEntry_Interaction;
            if (interactionEntry == null)
            {
                return;
            }

            InteractionDef interactionDef = IntDefField?.GetValue(interactionEntry) as InteractionDef;
            Pawn initiator = InitiatorField?.GetValue(interactionEntry) as Pawn;
            Pawn recipient = RecipientField?.GetValue(interactionEntry) as Pawn;
            DiaryGameComponent component = DiaryGameComponent.Current;
            if (component == null || !component.ShouldCaptureInteractionFromPlayLog(initiator, recipient, interactionDef))
            {
                return;
            }

            bool renderGameText = component.ShouldRenderInteractionTextFromPlayLog(interactionDef);
            string initiatorGameText = renderGameText ? GameTextFromPov(interactionEntry, initiator) : string.Empty;
            string recipientGameText = renderGameText ? GameTextFromPov(interactionEntry, recipient) : string.Empty;

            component.RecordInteraction(initiator, recipient, interactionDef,
                initiatorGameText, recipientGameText, interactionEntry.LogID);
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
                using (SpeakUpReplySchedulingGuardPatch.SuppressDuringCapture())
                {
                    return interactionEntry.ToGameStringFromPOV(pawn, false);
                }
            }
            catch
            {
                return interactionEntry.ToString();
            }
        }
    }

    /// <summary>
    /// Overrides generated direct-speech PlayLog row text without affecting ordinary vanilla rows.
    /// </summary>
    [HarmonyPatch]
    public static class PlayLogGeneratedSpeechTextPatch
    {
        /// <summary>
        /// Finds the concrete interaction text renderer for this RimWorld build. In 1.6 the public
        /// base method delegates to PlayLogEntry_Interaction.ToGameStringFromPOV_Worker, so patching
        /// the old public name on the concrete class can fail PatchAll before later hooks register.
        /// </summary>
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV_Worker",
                new[] { typeof(Thing), typeof(bool) })
                ?? AccessTools.Method(typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV",
                new[] { typeof(Thing), typeof(bool) });
        }

        /// <summary>
        /// Harmony Prefix for generated direct-speech rows. Normal vanilla interaction rows continue
        /// through RimWorld's grammar; injected rows display the already parsed LLM speech text.
        /// </summary>
        public static bool Prefix(PlayLogEntry_Interaction __instance, ref string __result)
        {
            // Fast path: when this game has no generated speech rows at all, skip the per-row lookup
            // entirely and let every interaction render through vanilla grammar.
            if (!GeneratedSpeechPlayLog.HasGeneratedSpeechRows)
            {
                return true;
            }

            string text;
            if (!GeneratedSpeechPlayLog.TryGetText(__instance, out text))
            {
                return true;
            }

            __result = text;
            return false;
        }
    }

    /// <summary>
    /// Opens the Diary tab from social-log clicks when a generated diary entry exists for that row.
    /// </summary>
    [HarmonyPatch(typeof(PlayLogEntry_Interaction), nameof(PlayLogEntry_Interaction.ClickedFromPOV))]
    public static class PlayLogInteractionClickPatch
    {
        /// <summary>
        /// Harmony Prefix for social interaction log clicks. When this exact PlayLog row has a
        /// finished diary entry for the clicked pawn's POV, open Diary instead of vanilla behavior.
        /// Returning true lets RimWorld continue normally when no diary entry is available yet.
        /// </summary>
        public static bool Prefix(PlayLogEntry_Interaction __instance, Thing pov)
        {
            if (__instance == null)
            {
                return true;
            }

            Pawn pawn = pov as Pawn;
            if (pawn == null)
            {
                return true;
            }

            DiaryEntryView entry = DiaryGameComponent.Current?.GeneratedEntryForPlayLogEntry(pawn, __instance.LogID);
            if (entry == null)
            {
                return true;
            }

            if (!EnsureSelected(pawn))
            {
                return true;
            }

            ITab_Pawn_Diary.RequestScrollToEntry(pawn, entry.EventId);
            InspectTabBase opened = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Diary));
            if (opened is ITab_Pawn_Diary)
            {
                return false;
            }

            ITab_Pawn_Diary.ClearPendingScrollRequest();
            return true;
        }

        /// <summary>
        /// The inspect pane opens tabs for the current selection, so make sure the POV pawn is
        /// selected. Social-tab clicks usually already satisfy this; the spawned guard avoids
        /// trying to select an off-map pawn from another play-log surface.
        /// </summary>
        private static bool EnsureSelected(Pawn pawn)
        {
            if (pawn == null || Find.Selector == null)
            {
                return false;
            }

            if (Find.Selector.IsSelected(pawn))
            {
                return true;
            }

            if (!pawn.Spawned)
            {
                return false;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn, true, false);
            return true;
        }
    }

    // Fires when a pawn gains a direct relation with another pawn (Lover, Spouse, Rival,
    // Cousin, Parent, ...). RecordRomance filters to the four vanilla romance relations and
    // emits a pairwise diary event for the relation change. Pair dedup (canonical pair key in
    // RecordRomance) collapses the mirrored call when RimWorld adds the relation symmetrically on
    // the other pawn's tracker.
    /// <summary>
    /// Captures direct relation additions so romance relation changes can become pairwise entries.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class PawnRelationAddPatch
    {
        // Reflection accessor for the private Pawn_RelationsTracker.pawn field so we can read the
        // subject pawn (the tracker's owner). Mirrors the MentalStateHandler.pawn pattern.
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");

        static PawnRelationAddPatch()
        {
            if (PawnField == null)
            {
                Log.Warning("[PawnDiary] Could not find Pawn_RelationsTracker.pawn; romance diary events will not be captured.");
            }
        }

        /// <summary>
        /// Harmony Postfix for Pawn_RelationsTracker.AddDirectRelation. Forwards the relation
        /// change to DiaryGameComponent.RecordRomance, which filters to romance relations and
        /// records a pairwise diary event when both pawns are eligible.
        /// </summary>
        public static void Postfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (__instance == null || def == null || otherPawn == null)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordRomance(pawn, otherPawn, def);
        }
    }
}
