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
//   2. Submit example event…    — the canonical minimal SubmitEvent example (replaces the old
//                                 daily timer; copy this for a real adapter trigger).
//   3. Preview example prompt…  — side-effect-free preview of the same event's assembled prompt.
//   4. Dump context bundle…     — writes one pawn's full context bundle to Player.log (quick
//                                 "what does Pawn Diary know about this pawn right now" probe).
//
// New to C#/RimWorld? See AGENTS.md. For the public API these call, see EXTERNAL_API.md.
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
        // The category string is what groups these in the Debug Actions menu.
        private const string Category = "Pawn Diary Example Adapter";
        private const string SourceId = "aimmlegate.pawndiary.adapter.example";
        private const string ExampleEventKey = "exampleadapter_quiet_moment";

        /// <summary>
        /// Opens the API Explorer window — the main UI for testing every PawnDiaryApi method.
        /// </summary>
        [DebugAction(Category, "Open API explorer…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void OpenApiExplorer()
        {
            if (!Prefs.DevMode || !PawnDiaryApi.IsReady)
            {
                return;
            }

            Find.WindowStack.Add(new PawnDiaryApiExplorerWindow());
        }

        /// <summary>
        /// Canonical minimal example: submits one external event for a randomly chosen colonist.
        /// Copy this method's request shape (and the matching group XML in
        /// 1.6/Defs/DiaryExternalGroups_Example.xml) to start a real adapter — swap this dev-action
        /// trigger for a hook into your target mod.
        /// </summary>
        [DebugAction(Category, "Submit example event (random colonist)…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void SubmitExampleEvent()
        {
            if (!Prefs.DevMode || !PawnDiaryApi.IsReady)
            {
                return;
            }

            Pawn subject = PickRandomColonist();
            if (subject == null)
            {
                Messages.Message("PawnDiaryExampleAdapter.Quick.NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool recorded = PawnDiaryApi.SubmitEvent(new ExternalEventRequest
            {
                sourceId = SourceId,
                eventKey = ExampleEventKey,
                subject = subject,
                // Adapter-owned Keyed strings — see Languages/English/Keyed/ExampleAdapter.xml.
                summaryText = "PawnDiaryExampleAdapter.QuietMomentSummary".Translate(subject.LabelShortCap).Resolve(),
                eventLabel = "PawnDiaryExampleAdapter.QuietMomentLabel".Translate().Resolve(),
                extraContext = new List<string> { "origin=example_dev_action" }
            }, out SubmitEventOutcome outcome);

            // Resolve the outcome to a string before interpolation so we don't box the enum through
            // the obsolete Translator.Translate(string, params object[]) overload.
            TaggedString msg = recorded
                ? "PawnDiaryExampleAdapter.Quick.Submitted".Translate(subject.LabelShortCap, outcome.ToString())
                : "PawnDiaryExampleAdapter.Quick.NotSubmitted".Translate(subject.LabelShortCap, outcome.ToString());
            Messages.Message(msg, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// Side-effect-free preview of the example event's assembled prompt. No event saved, no
        /// tokens spent; the preview prints to Player.log so it can be inspected outside the window.
        /// </summary>
        [DebugAction(Category, "Preview example event prompt…", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void PreviewExampleEventPrompt()
        {
            if (!Prefs.DevMode || !PawnDiaryApi.IsReady)
            {
                return;
            }

            Pawn subject = PickRandomColonist();
            if (subject == null)
            {
                Messages.Message("PawnDiaryExampleAdapter.Quick.NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            DiaryPromptPreviewSnapshot preview = PawnDiaryApi.PreviewPrompt(new ExternalEventRequest
            {
                sourceId = SourceId,
                eventKey = ExampleEventKey,
                subject = subject,
                summaryText = "PawnDiaryExampleAdapter.QuietMomentSummary".Translate(subject.LabelShortCap).Resolve()
            });

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
            if (!Prefs.DevMode || !PawnDiaryApi.IsReady)
            {
                return;
            }

            Pawn subject = PickRandomColonist();
            if (subject == null)
            {
                Messages.Message("PawnDiaryExampleAdapter.Quick.NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            DiaryContextBundleSnapshot bundle = PawnDiaryApi.GetContextBundle(subject, 5);
            if (bundle == null)
            {
                Log.Warning("[Pawn Diary Example Adapter] GetContextBundle returned null for " + subject.LabelShortCap + ".");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Pawn Diary Example Adapter] Context bundle for " + subject.LabelShortCap + ":");
            sb.AppendLine(SnapshotFormatter.Format(bundle));
            Log.Message(sb.ToString());
            Messages.Message("PawnDiaryExampleAdapter.Quick.BundleLogged".Translate(subject.LabelShortCap), MessageTypeDefOf.NeutralEvent, false);
        }

        // --------------------------------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------------------------------

        private static Pawn PickRandomColonist()
        {
            List<Pawn> pool = ExplorerPawns.EligiblePawns();
            return (pool == null || pool.Count == 0) ? null : pool.RandomElement();
        }
    }
}
