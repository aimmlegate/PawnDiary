// RimWorld dev-mode actions for the example adapter. These are auto-discovered by RimWorld's
// debug-action scanner (it walks every loaded assembly for [DebugAction] methods) — no Harmony, no
// registration. They appear under Dev mode → Debug Actions → "Pawn Diary Example Adapter".
//
// Why a separate category from the core mod's "Pawn Diary" actions: this is a different mod, and
// keeping the category aligned with the packageId makes it obvious which menu entries come from the
// adapter vs the core.
//
// The four entries:
//   1. Open API explorer…       — opens the three-pane window (the main UI).
//   2. Submit example event…    — calls the documented quiet-moment wrapper in
//                                 PawnDiaryExampleApi.cs.
//   3. Preview example prompt…  — side-effect-free preview of the same event's assembled prompt.
//   4. Dump context bundle…     — writes one pawn's full context bundle to Player.log (quick
//                                 "what does Pawn Diary know about this pawn right now" probe).
//
// New to C#/RimWorld? See AGENTS.md. For the public API these call, see EXTERNAL_API.md.
using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using PawnDiary.Integration;
using RimWorld;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Dev-mode entry points discovered by RimWorld's debug-action scanner.
    /// </summary>
    public static class PawnDiaryExampleDebugActions
    {
        /// <summary>Debug Action category used to group the example adapter actions in RimWorld.</summary>
        private const string Category = "Pawn Diary Example Adapter";

        /// <summary>
        /// Opens the API Explorer window, the main UI for testing every public API wrapper.
        /// </summary>
        [DebugAction(Category, "Open API explorer…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void OpenApiExplorer()
        {
            if (!Prefs.DevMode || !PawnDiaryExampleApi.IsReady)
            {
                return;
            }

            CloseDebugLauncherWindows();
            Find.WindowStack.Add(new PawnDiaryApiExplorerWindow());
        }

        /// <summary>
        /// Submits the documented quiet-moment sample event for a randomly chosen colonist. The
        /// request shape lives in PawnDiaryExampleApi.BuildQuietMomentRequest so authors can copy the
        /// integration layer without copying this debug-action UI trigger.
        /// </summary>
        [DebugAction(Category, "Submit example event (random colonist)…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void SubmitExampleEvent()
        {
            if (!CanUseExternalApi())
            {
                return;
            }

            Pawn subject = PickRandomColonist();
            if (subject == null)
            {
                Messages.Message("PawnDiaryExampleAdapter.Quick.NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool recorded = PawnDiaryExampleApi.SubmitQuietMoment(subject, out SubmitEventOutcome outcome);

            // Resolve the outcome to a string before interpolation so we don't box the enum through
            // the obsolete Translator.Translate(string, params object[]) overload.
            string subjectLabel = subject.LabelShortCap.ToString();
            TaggedString msg = recorded
                ? "PawnDiaryExampleAdapter.Quick.Submitted".Translate(subjectLabel, outcome.ToString())
                : "PawnDiaryExampleAdapter.Quick.NotSubmitted".Translate(subjectLabel, outcome.ToString());
            Messages.Message(msg, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// Side-effect-free preview of the example event's assembled prompt. No event saved, no
        /// tokens spent; the preview prints to Player.log so it can be inspected outside the window.
        /// </summary>
        [DebugAction(Category, "Preview example event prompt…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void PreviewExampleEventPrompt()
        {
            if (!CanUseExternalApi())
            {
                return;
            }

            Pawn subject = PickRandomColonist();
            if (subject == null)
            {
                Messages.Message("PawnDiaryExampleAdapter.Quick.NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            DiaryPromptPreviewSnapshot preview = PawnDiaryExampleApi.PreviewQuietMoment(subject);

            if (preview == null)
            {
                Log.Warning("[Pawn Diary Example Adapter] PreviewPrompt returned null — the request was declined (see the in-game log for the reason).");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Pawn Diary Example Adapter] PreviewPrompt for " + subject.LabelShortCap + ":");
            sb.AppendLine("  povRole=" + preview.povRole + "  pairwise=" + preview.pairwise + "  group=" + preview.groupDefName);
            sb.AppendLine("  ----- systemPrompt -----");
            sb.AppendLine(preview.systemPrompt);
            sb.AppendLine("  ----- userPrompt -----");
            sb.AppendLine(preview.userPrompt);
            Log.Message(sb.ToString());
            Messages.Message("PawnDiaryExampleAdapter.Quick.PreviewLogged".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// Writes one pawn's full context bundle (style + summary + enchantments + recent memory) to
        /// Player.log. The fastest "what does Pawn Diary know about this pawn right now" probe.
        /// </summary>
        [DebugAction(Category, "Dump context bundle to log…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void DumpContextBundle()
        {
            if (!CanUseExternalApi())
            {
                return;
            }

            Pawn subject = PickRandomColonist();
            if (subject == null)
            {
                Messages.Message("PawnDiaryExampleAdapter.Quick.NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            DiaryContextBundleSnapshot bundle = PawnDiaryExampleApi.GetQuickContextBundle(subject);
            if (bundle == null)
            {
                Log.Warning("[Pawn Diary Example Adapter] GetContextBundle returned null for " + subject.LabelShortCap + ".");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Pawn Diary Example Adapter] Context bundle for " + subject.LabelShortCap + ":");
            sb.AppendLine(SnapshotFormatter.Format(bundle));
            Log.Message(sb.ToString());
            Messages.Message("PawnDiaryExampleAdapter.Quick.BundleLogged".Translate(subject.LabelShortCap.ToString()), MessageTypeDefOf.NeutralEvent, false);
        }

        // --------------------------------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Picks one eligible colony pawn for the quick debug actions.
        /// </summary>
        /// <returns>A random eligible pawn, or null when the current map has no usable pawn.</returns>
        private static Pawn PickRandomColonist()
        {
            List<Pawn> pool = ExplorerPawns.EligiblePawns();
            return (pool == null || pool.Count == 0) ? null : pool.RandomElement();
        }

        /// <summary>
        /// Checks whether the quick debug actions are allowed to call the example API facade.
        /// </summary>
        /// <returns>True when Dev Mode is active, Pawn Diary is ready, and external API access is enabled.</returns>
        private static bool CanUseExternalApi()
        {
            if (!Prefs.DevMode || !PawnDiaryExampleApi.IsReady)
            {
                return false;
            }

            if (PawnDiaryExampleApi.CanUseExternalApi)
            {
                return true;
            }

            Messages.Message("PawnDiaryExampleAdapter.Quick.ApiDisabled".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        /// <summary>
        /// Closes RimWorld's debug launcher dialogs before opening the explorer window.
        /// </summary>
        private static void CloseDebugLauncherWindows()
        {
            List<Window> toClose = new List<Window>();
            foreach (Window window in Find.WindowStack.Windows)
            {
                string typeName = window.GetType().FullName ?? string.Empty;
                if (typeName.StartsWith("LudeonTK.Dialog_Debug", StringComparison.Ordinal))
                {
                    toClose.Add(window);
                }
            }

            foreach (Window window in toClose)
            {
                window.Close(false);
            }
        }
    }
}
