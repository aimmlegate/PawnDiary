// RimWorld dev-mode actions for Pawn Diary. These use the game's built-in Debug Actions menu, so
// testing helpers stay with RimWorld's normal developer tooling instead of adding production UI.
using System;
using System.Collections.Generic;
using LudeonTK;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PawnDiary
{
    /// <summary>
    /// Entry points discovered by RimWorld's debug-action scanner while dev mode is enabled.
    /// </summary>
    public static class PawnDiaryDebugActions
    {
        /// <summary>
        /// Opens the event test panel from RimWorld's Debug Actions menu.
        /// </summary>
        [DebugAction("Pawn Diary", "Event test panel...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void OpenEventTestPanel()
        {
            if (!Prefs.DevMode || DiaryGameComponent.Instance == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_PawnDiaryEventTestPanel());
        }

        /// <summary>
        /// Opens the standalone three-pane reader before the alternative-mode setting is enabled.
        /// </summary>
        [DebugAction("Pawn Diary", "Open diary reader window", allowedGameStates = AllowedGameStates.Playing, actionType = DebugActionType.Action)]
        public static void OpenDiaryReaderWindow()
        {
            if (!Prefs.DevMode || DiaryGameComponent.Instance == null)
            {
                return;
            }

            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn
                ?? (Find.Selector?.SingleSelectedThing as Corpse)?.InnerPawn;
            Dialog_DiaryReader.Open(pawn);
        }

        /// <summary>
        /// Writes every saved hot and archived diary page to disk from RimWorld's Debug Actions menu.
        /// </summary>
        [DebugAction("Pawn Diary", "Export all diary pages...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void ExportAllDiaries()
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            HandleExportAllDiariesForDev();
        }

        /// <summary>
        /// Exercises the public integration API end-to-end (PawnDiaryApi.SubmitEvent → External
        /// signal → group XML → diary entry) with no adapter mod installed. The built-in
        /// externalDevTest group claims the "pawndiary_dev_test" key this action submits.
        /// </summary>
        [DebugAction("Pawn Diary", "Submit test external event...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void SubmitTestExternalEvent()
        {
            if (!Prefs.DevMode || DiaryGameComponent.Instance == null)
            {
                return;
            }

            List<Pawn> pawns = Dialog_PawnDiaryEventTestPanel.EligiblePawns();
            if (pawns.Count == 0)
            {
                Messages.Message("PawnDiary.Dev.EventPanel.NoPawn".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn optionPawn = pawns[i];
                if (optionPawn == null)
                {
                    continue;
                }

                Pawn capturedPawn = optionPawn;
                options.Add(new FloatMenuOption(capturedPawn.LabelShortCap, delegate
                {
                    // Built exactly like an adapter mod would build it, so this path is a live
                    // sample of the documented contract (see the Adapter Contract wiki page).
                    ExternalEventRequest request = new ExternalEventRequest
                    {
                        sourceId = "PawnDiary.DevTest",
                        eventKey = "pawndiary_dev_test",
                        subject = capturedPawn,
                        summaryText = "PawnDiary.Dev.ExternalTestSummary".Translate(capturedPawn.LabelShortCap).Resolve(),
                        extraContext = new List<string> { "origin=debug_action" }
                    };

                    bool accepted = PawnDiaryApi.SubmitEvent(request);
                    Messages.Message(
                        accepted
                            ? "PawnDiary.Dev.ExternalTestSubmitted".Translate(capturedPawn.LabelShortCap)
                            : "PawnDiary.Dev.ExternalTestRejected".Translate(),
                        accepted ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                        false);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Opens a pawn picker that removes compact archived pages for one pawn.
        /// </summary>
        [DebugAction("Pawn Diary", "Purge archived entries for pawn...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void PurgeArchivedEntriesForPawn()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (!Prefs.DevMode || component == null)
            {
                return;
            }

            List<Pawn> pawns = Dialog_PawnDiaryEventTestPanel.EligiblePawns();
            if (pawns.Count == 0)
            {
                Messages.Message("PawnDiary.Dev.EventPanel.NoPawn".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn optionPawn = pawns[i];
                if (optionPawn == null)
                {
                    continue;
                }

                Pawn capturedPawn = optionPawn;
                options.Add(new FloatMenuOption(capturedPawn.LabelShortCap, delegate
                {
                    int removed = component.PurgeArchivedEntriesForPawnForDev(capturedPawn);
                    Messages.Message(
                        "PawnDiary.Tab.ArchivedEntriesPurged".Translate(removed, capturedPawn.LabelShortCap),
                        removed > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                        false);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Opens a picker of currently active event windows and force-closes the selected one. This is
        /// the escape hatch for a window stuck open after its threat dissolved but before its timeout
        /// (or recovered from a bad save). Brute-remove: no end/timeout page is recorded.
        /// </summary>
        [DebugAction("Pawn Diary", "Force-close active event window...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void ForceCloseActiveEventWindow()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (!Prefs.DevMode || component == null)
            {
                return;
            }

            List<ActiveEventWindowState> windows = component.ActiveEventWindowsForDev;
            if (windows.Count == 0)
            {
                Messages.Message("PawnDiary.Dev.NoActiveEventWindows".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < windows.Count; i++)
            {
                ActiveEventWindowState captured = windows[i];
                if (captured == null)
                {
                    continue;
                }

                // Resolve the def for a readable label; fall back to the stored defName if the def is
                // gone/disabled (the window would be retired on the next scan anyway).
                DiaryEventWindowDef def = string.IsNullOrEmpty(captured.windowDefName)
                    ? null
                    : DefDatabase<DiaryEventWindowDef>.GetNamedSilentFail(captured.windowDefName);
                string label = def != null ? def.LabelCap.ToString() : captured.windowDefName;
                string tool = "PawnDiary.Dev.ForceCloseEventWindow.Tooltip".Translate(
                    captured.windowDefName ?? string.Empty,
                    captured.startLabel ?? string.Empty);
                options.Add(new FloatMenuOption(label, delegate
                {
                    bool removed = component.ForceCloseEventWindowForDev(captured);
                    Messages.Message(
                        "PawnDiary.Dev.EventWindowForceClosed".Translate(label),
                        removed ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                        false);
                })
                { tooltip = tool });
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Logs bounded Phase 3 state tokens for the currently selected pawn.</summary>
        [DebugAction("Pawn Diary", "Log selected pawn belief state...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void LogSelectedPawnBeliefState()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!Prefs.DevMode || component == null || pawn == null)
            {
                Messages.Message("PawnDiary.Dev.BeliefState.NoPawn".Translate(),
                    MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            Log.Message("[Pawn Diary] belief_state " + component.BeliefStateDiagnosticsForDev(pawn));
            Messages.Message("PawnDiary.Dev.BeliefState.Logged".Translate(),
                MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// Dumps the selected pawn's knowledge state (MEMORY_SYSTEM_REDESIGN_PLAN §7): culture
        /// provenance, profile status, every stored important event, and the last prompt-selection
        /// report with per-candidate reasons and annotation targets. On demand only — no log spam.
        /// </summary>
        [DebugAction("Pawn Diary", "Log selected pawn knowledge state...", allowedGameStates = AllowedGameStates.PlayingOnMap, actionType = DebugActionType.Action)]
        public static void LogSelectedPawnKnowledgeState()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (!Prefs.DevMode || component == null || pawn == null)
            {
                Messages.Message("PawnDiary.Dev.Knowledge.NoPawn".Translate(),
                    MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            Log.Message("[Pawn Diary] knowledge_state\n" + component.KnowledgeDiagnosticsForDev(pawn));
            Messages.Message("PawnDiary.Dev.Knowledge.Logged".Translate(),
                MessageTypeDefOf.NeutralEvent, false);
        }

        private static void HandleExportAllDiariesForDev()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (component == null)
            {
                Messages.Message(
                    "PawnDiary.Settings.ExportAllDiariesNoGame".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            string filePath;
            string error;
            if (component.TryExportAllDiariesForDev(out filePath, out error))
            {
                GUIUtility.systemCopyBuffer = filePath;
                Messages.Message(
                    "PawnDiary.Settings.ExportAllDiariesDone".Translate(filePath),
                    MessageTypeDefOf.PositiveEvent,
                    false);
                return;
            }

            Messages.Message(
                "PawnDiary.Settings.ExportAllDiariesFailed".Translate(error),
                MessageTypeDefOf.RejectInput,
                false);
        }
    }

    /// <summary>
    /// Dev-only window for triggering real RimWorld event paths plus prompt-only fixtures.
    /// </summary>
    internal sealed class Dialog_PawnDiaryEventTestPanel : Window
    {
        private const float HeaderHeight = 32f;
        private const float PawnRowsHeight = 68f;
        private const float ToggleRowsHeight = 78f;
        private const float Gap = 8f;
        private const float SectionRailWidth = 156f;
        private const float PanelPadding = 8f;
        private const float ActionRowHeight = 32f;
        private const float ActionGap = 6f;
        private const int DevMockDiaryTargetYears = 3;
        private const int DevMockDiaryEntriesPerYear = 2000;
        private const int DevMockDiaryTargetCount = DevMockDiaryTargetYears * DevMockDiaryEntriesPerYear;

        public Dialog_PawnDiaryEventTestPanel()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(820f, 760f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (!Prefs.DevMode || component == null)
            {
                Widgets.Label(inRect, "PawnDiary.Dev.EventPanel.NoGame".Translate());
                return;
            }

            List<Pawn> pawns = EligiblePawns();
            Pawn pawn = SelectedPawn(pawns, component);
            Pawn partner = PartnerPawn(pawns, pawn, component);

            float y = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, HeaderHeight), "PawnDiary.Dev.EventPanel.Title".Translate());
            Text.Font = GameFont.Small;
            y += HeaderHeight + Gap;

            if (pawn == null)
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 60f), "PawnDiary.Dev.EventPanel.NoPawn".Translate());
                return;
            }

            DrawPawnRows(new Rect(0f, y, inRect.width, PawnRowsHeight), pawns, pawn, partner, component);
            y += PawnRowsHeight + Gap;

            DrawSettingsToggles(new Rect(0f, y, inRect.width, ToggleRowsHeight));
            y += ToggleRowsHeight + Gap;

            DrawSectionedControls(new Rect(0f, y, inRect.width, inRect.height - y), component, pawn, partner);
        }

        private void DrawPawnRows(Rect rect, List<Pawn> pawns, Pawn selectedPawn, Pawn partner, DiaryGameComponent component)
        {
            const float labelWidth = 96f;
            const float toggleWidth = 210f;
            const float rowHeight = 32f;
            Rect pawnRow = new Rect(rect.x, rect.y, rect.width, rowHeight);
            Rect partnerRow = new Rect(rect.x, rect.y + rowHeight + 4f, rect.width, rowHeight);

            DrawPawnSelectorRow(pawnRow, "PawnDiary.Dev.EventPanel.Pawn", pawns, selectedPawn, component, false);

            Rect labelRect = new Rect(partnerRow.x, partnerRow.y + 5f, labelWidth, partnerRow.height);
            Rect partnerButtonRect = new Rect(labelRect.xMax + 6f, partnerRow.y, partnerRow.width - labelWidth - toggleWidth - 12f, partnerRow.height);
            Widgets.Label(labelRect, "PawnDiary.Dev.EventPanel.Partner".Translate());
            if (partner == null)
            {
                Widgets.Label(partnerButtonRect, "PawnDiary.Dev.EventPanel.NoPartner".Translate());
            }
            else if (ButtonTextLeft(partnerButtonRect, partner.LabelShortCap))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn optionPawn = pawns[i];
                    if (optionPawn == null || optionPawn == selectedPawn)
                    {
                        continue;
                    }

                    options.Add(new FloatMenuOption(optionPawn.LabelShortCap, delegate
                    {
                        component.SetDevPanelSelectedPartnerForDev(optionPawn.GetUniqueLoadID());
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            Rect generationRect = new Rect(rect.xMax - toggleWidth, rect.y, toggleWidth, rowHeight);
            bool enabled = component.DiaryGenerationEnabledFor(selectedPawn);
            bool before = enabled;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(generationRect);
            listing.CheckboxLabeled("PawnDiary.Tab.GenerateForPawn".Translate(), ref enabled, "PawnDiary.Tab.GenerateForPawnTip".Translate());
            listing.End();
            if (enabled != before)
            {
                component.SetDiaryGenerationEnabled(selectedPawn, enabled);
            }
        }

        private void DrawPawnSelectorRow(
            Rect rect,
            string labelKey,
            List<Pawn> pawns,
            Pawn selectedPawn,
            DiaryGameComponent component,
            bool partnerOnly)
        {
            const float labelWidth = 96f;
            const float toggleWidth = 210f;
            Rect labelRect = new Rect(rect.x, rect.y + 5f, labelWidth, rect.height);
            Rect pawnButtonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - toggleWidth - 12f, rect.height);

            Widgets.Label(labelRect, labelKey.Translate());
            if (selectedPawn == null)
            {
                Widgets.Label(pawnButtonRect, "PawnDiary.Dev.EventPanel.NoPawn".Translate());
                return;
            }

            if (ButtonTextLeft(pawnButtonRect, selectedPawn.LabelShortCap))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn optionPawn = pawns[i];
                    if (optionPawn == null)
                    {
                        continue;
                    }

                    options.Add(new FloatMenuOption(optionPawn.LabelShortCap, delegate
                    {
                        if (partnerOnly)
                        {
                            component.SetDevPanelSelectedPartnerForDev(optionPawn.GetUniqueLoadID());
                        }
                        else
                        {
                            component.SetDevPanelSelectedPawnForDev(optionPawn.GetUniqueLoadID());
                        }
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static void DrawSettingsToggles(Rect rect)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                return;
            }

            bool changed = false;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            bool promptTestMode = settings.promptTestMode;
            listing.CheckboxLabeled(
                "PawnDiary.Settings.PromptTestMode".Translate(),
                ref promptTestMode,
                "PawnDiary.Settings.PromptTestModeTip".Translate());
            if (promptTestMode != settings.promptTestMode)
            {
                settings.promptTestMode = promptTestMode;
                changed = true;
            }

            bool showDebug = settings.showLlmDebugInfo;
            listing.CheckboxLabeled(
                "PawnDiary.Tab.ShowLlmDebugInfo".Translate(),
                ref showDebug,
                "PawnDiary.Tab.ShowLlmDebugInfoTip".Translate());
            if (showDebug != settings.showLlmDebugInfo)
            {
                settings.showLlmDebugInfo = showDebug;
                changed = true;
            }

            bool showGenerating = settings.showGeneratingEntries;
            listing.CheckboxLabeled(
                "PawnDiary.Tab.ShowGeneratingEntries".Translate(),
                ref showGenerating,
                "PawnDiary.Tab.ShowGeneratingEntriesTip".Translate());
            if (showGenerating != settings.showGeneratingEntries)
            {
                settings.showGeneratingEntries = showGenerating;
                changed = true;
            }

            listing.End();
            if (changed)
            {
                WriteGlobalSettings();
            }
        }

        private void DrawSectionedControls(Rect rect, DiaryGameComponent component, Pawn pawn, Pawn partner)
        {
            Rect railRect = new Rect(rect.x, rect.y, SectionRailWidth, rect.height);
            Rect bodyRect = new Rect(railRect.xMax + Gap, rect.y, rect.width - SectionRailWidth - Gap, rect.height);

            Widgets.DrawMenuSection(railRect);
            Widgets.DrawMenuSection(bodyRect);

            DrawSectionRail(railRect.ContractedBy(PanelPadding), component);

            Rect bodyInner = bodyRect.ContractedBy(PanelPadding);
            string section = component.DevPanelSectionForDev;
            if (section == DiaryGameComponent.DevPanelSectionFixtures)
            {
                DrawScrollableSection(
                    bodyInner,
                    component,
                    section,
                    PromptFixturesViewHeight(component, pawn),
                    viewRect => DrawPromptFixturesSection(viewRect, component, pawn));
                return;
            }

            DrawScrollableSection(
                bodyInner,
                component,
                DiaryGameComponent.DevPanelSectionDiary,
                DiaryToolsViewHeight(),
                viewRect => DrawDiaryToolsSection(viewRect, component, pawn));
        }

        private void DrawSectionRail(Rect rect, DiaryGameComponent component)
        {
            float y = rect.y;
            DrawSectionButton(
                new Rect(rect.x, y, rect.width, ActionRowHeight),
                component,
                DiaryGameComponent.DevPanelSectionDiary,
                "PawnDiary.Dev.EventPanel.SectionDiary");
            y += ActionRowHeight + ActionGap;

            DrawSectionButton(
                new Rect(rect.x, y, rect.width, ActionRowHeight),
                component,
                DiaryGameComponent.DevPanelSectionFixtures,
                "PawnDiary.Dev.EventPanel.SectionFixtures");
        }

        private void DrawSectionButton(Rect rect, DiaryGameComponent component, string sectionId, string labelKey)
        {
            bool selected = component.DevPanelSectionForDev == sectionId;
            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            if (ButtonTextLeft(rect, labelKey.Translate()))
            {
                component.SetDevPanelSectionForDev(sectionId);
            }
        }

        private void DrawScrollableSection(
            Rect rect,
            DiaryGameComponent component,
            string sectionId,
            float viewHeight,
            Action<Rect> drawer)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Math.Max(rect.height, viewHeight));
            Vector2 scroll = new Vector2(0f, component.DevPanelScrollYForDev(sectionId));
            Widgets.BeginScrollView(rect, ref scroll, viewRect);
            drawer(viewRect);
            Widgets.EndScrollView();
            component.SetDevPanelScrollYForDev(sectionId, scroll.y);
        }

        // Legacy real-trigger drawer. The rail no longer exposes this section, but the helpers stay
        // here for now so a future live-hook check can reintroduce the surface deliberately.
        private void DrawRealEventsSection(Rect rect, DiaryGameComponent component, Pawn pawn, Pawn partner)
        {
            float y = DrawSectionTitle(rect, "PawnDiary.Dev.EventPanel.RealEvents");
            y = DrawDefBackedTriggerButtons(rect, component, pawn, partner, y);

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "PawnDiary.Dev.EventPanel.TriggerButtons".Translate());
            y += 28f;

            int column = 0;
            DrawDangerGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.TriggerArrival", () => TriggerArrival(pawn));
            DrawDangerGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.TriggerDeath", () => TriggerDeath(pawn));
            DrawDangerGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.TriggerWorkScan", delegate
            {
                bool recorded = component.TriggerWorkSignalForDev(pawn);
                Message(recorded, "PawnDiary.Dev.EventPanel.TriggerWorkScan".Translate().Resolve());
            });
            DrawDangerGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.TriggerThoughtProgression", () => TriggerThoughtProgression(component, pawn));
            DrawDangerGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.TriggerDayReflection", delegate
            {
                component.FlushDaySummaryForDev(pawn);
                ScannerMessage("PawnDiary.Dev.EventPanel.TriggerDayReflection".Translate().Resolve());
            });
        }

        private float DrawDefBackedTriggerButtons(Rect rect, DiaryGameComponent component, Pawn pawn, Pawn partner, float y)
        {
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "PawnDiary.Dev.EventPanel.TriggerChoices".Translate());
            y += 28f;

            int column = 0;
            DrawDefBackedTriggerButton<ThoughtDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerThought",
                component.DevPanelThoughtDefNameForDev,
                component.SetDevPanelThoughtDefNameForDev,
                () => TriggerThought(pawn, component.DevPanelThoughtDefNameForDev),
                IsMemoryThoughtDef,
                true);
            DrawDefBackedTriggerButton<InspirationDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerInspiration",
                component.DevPanelInspirationDefNameForDev,
                component.SetDevPanelInspirationDefNameForDev,
                () => TriggerInspiration(pawn, component.DevPanelInspirationDefNameForDev),
                null,
                true);
            DrawDefBackedTriggerButton<MentalStateDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerMentalState",
                component.DevPanelMentalStateDefNameForDev,
                component.SetDevPanelMentalStateDefNameForDev,
                () => TriggerMentalState(
                    pawn,
                    component.DevPanelMentalStateDefNameForDev,
                    null,
                    "PawnDiary.Dev.EventPanel.TriggerMentalState".Translate().Resolve()),
                null,
                true);
            DrawDefBackedTriggerButton<MentalStateDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerSocialFight",
                component.DevPanelPairedMentalStateDefNameForDev,
                component.SetDevPanelPairedMentalStateDefNameForDev,
                () => TriggerMentalState(
                    pawn,
                    component.DevPanelPairedMentalStateDefNameForDev,
                    partner,
                    "PawnDiary.Dev.EventPanel.TriggerSocialFight".Translate().Resolve()),
                null,
                true);
            DrawDefBackedTriggerButton<TaleDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerTale",
                component.DevPanelTaleDefNameForDev,
                component.SetDevPanelTaleDefNameForDev,
                () => TriggerTale(pawn, component.DevPanelTaleDefNameForDev),
                IsSinglePawnTaleDef,
                true);
            DrawDefBackedTriggerButton<HediffDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerHediff",
                component.DevPanelHediffDefNameForDev,
                component.SetDevPanelHediffDefNameForDev,
                () => TriggerHediff(pawn, component.DevPanelHediffDefNameForDev),
                null,
                true);
            DrawDefBackedTriggerButton<GameConditionDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerMoodEvent",
                component.DevPanelGameConditionDefNameForDev,
                component.SetDevPanelGameConditionDefNameForDev,
                () => TriggerMoodEvent(pawn, component.DevPanelGameConditionDefNameForDev),
                null,
                true);
            DrawDefBackedTriggerButton<InteractionDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerInteraction",
                component.DevPanelInteractionDefNameForDev,
                component.SetDevPanelInteractionDefNameForDev,
                () => TriggerInteraction(pawn, partner, component.DevPanelInteractionDefNameForDev),
                null,
                true);
            DrawDefBackedTriggerButton<PawnRelationDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerRomance",
                component.DevPanelRelationDefNameForDev,
                component.SetDevPanelRelationDefNameForDev,
                () => TriggerRomance(pawn, partner, component.DevPanelRelationDefNameForDev),
                null,
                true);
            DrawDefBackedTriggerButton<IncidentDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerRaid",
                component.DevPanelIncidentDefNameForDev,
                component.SetDevPanelIncidentDefNameForDev,
                () => TriggerRaid(pawn, component.DevPanelIncidentDefNameForDev),
                def => def?.Worker != null,
                true);
            DrawDefBackedTriggerButton<QuestScriptDef>(
                rect,
                ref y,
                ref column,
                "PawnDiary.Dev.EventPanel.TriggerQuest",
                component.DevPanelQuestScriptDefNameForDev,
                component.SetDevPanelQuestScriptDefNameForDev,
                () => TriggerQuest(pawn, component.DevPanelQuestScriptDefNameForDev),
                null,
                true);
            DrawAbilityTriggerButton(rect, ref y, ref column, component, pawn);
            FinishGridRow(ref y, ref column);

            DrawGridButton(rect, ref y, ref column, 1, "PawnDiary.Dev.EventPanel.ResetTriggerChoices", component.ResetDevPanelTriggerDefsForDev);
            FinishGridRow(ref y, ref column);
            y += ActionGap;
            return y;
        }

        private void DrawDiaryToolsSection(Rect rect, DiaryGameComponent component, Pawn pawn)
        {
            float y = DrawSectionTitle(rect, "PawnDiary.Dev.EventPanel.DiaryTools");
            Rect mockButtonRect = new Rect(rect.x, y, rect.width, ActionRowHeight);
            if (ButtonTextLeftDanger(mockButtonRect, "PawnDiary.Tab.FillMockEntries".Translate(DevMockDiaryTargetCount)))
            {
                int added = component.FillMockDiaryEntriesForDev(
                    pawn,
                    DevMockDiaryTargetCount,
                    DevMockDiaryTargetYears);
                if (added > 0)
                {
                    Messages.Message(
                        "PawnDiary.Tab.MockEntriesAdded".Translate(added, pawn.LabelShortCap),
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
                else
                {
                    Messages.Message(
                        "PawnDiary.Tab.MockEntriesAlreadyFilled".Translate(DevMockDiaryTargetCount, pawn.LabelShortCap),
                        MessageTypeDefOf.NeutralEvent,
                        false);
                }
            }

            TooltipHandler.TipRegion(
                mockButtonRect,
                "PawnDiary.Tab.FillMockEntriesTip".Translate(
                    DevMockDiaryTargetCount,
                    DevMockDiaryTargetYears,
                    DevMockDiaryEntriesPerYear));
            y += ActionRowHeight + ActionGap;

            Rect purgeArchiveButtonRect = new Rect(rect.x, y, rect.width, ActionRowHeight);
            if (ButtonTextLeftDanger(purgeArchiveButtonRect, "PawnDiary.Tab.PurgeArchivedEntries".Translate()))
            {
                int removed = component.PurgeArchivedEntriesForPawnForDev(pawn);
                Messages.Message(
                    "PawnDiary.Tab.ArchivedEntriesPurged".Translate(removed, pawn.LabelShortCap),
                    removed > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                    false);
            }

            TooltipHandler.TipRegion(purgeArchiveButtonRect, "PawnDiary.Tab.PurgeArchivedEntriesTip".Translate());
            y += ActionRowHeight + ActionGap;

            if (ButtonTextLeft(new Rect(rect.x, y, rect.width, ActionRowHeight), "PawnDiary.Tab.PersonaButton".Translate(PersonaLabel(component.PersonaFor(pawn)))))
            {
                List<DiaryPersonaDef> personas = new List<DiaryPersonaDef>(DiaryPersonas.All);
                personas.Sort((a, b) => string.Compare(PersonaLabel(a), PersonaLabel(b), StringComparison.OrdinalIgnoreCase));

                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = 0; i < personas.Count; i++)
                {
                    DiaryPersonaDef option = personas[i];
                    options.Add(new FloatMenuOption(PersonaLabel(option), delegate
                    {
                        component.SetPersona(pawn, option.defName);
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            y += ActionRowHeight + ActionGap * 2f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "PawnDiary.Dev.EventPanel.CardPreviews".Translate());
            y += 26f;
            Rect previewBlock = new Rect(rect.x, y, rect.width, PreviewGridHeight());
            DrawDevPreviewGrid(previewBlock, pawn);
            TooltipHandler.TipRegion(previewBlock, "PawnDiary.Tab.DevPreviewTip".Translate());
        }

        private void DrawPromptFixturesSection(Rect rect, DiaryGameComponent component, Pawn pawn)
        {
            IReadOnlyList<DiaryGameComponent.DevPromptSuiteEntry> entries = component.AvailableSuiteEntriesForDev(pawn);
            component.EnsureDevPanelFixtureSelectionForDev();

            float y = DrawSectionTitle(rect, "PawnDiary.Dev.EventPanel.PromptFixtures");
            int selectedCount = component.DevPanelSelectedFixtureCountForDev(entries);
            Widgets.Label(
                new Rect(rect.x, y, rect.width, 24f),
                "PawnDiary.Dev.EventPanel.SelectedFixtures".Translate(selectedCount, entries.Count));
            y += 28f;

            int column = 0;
            DrawGridButton(rect, ref y, ref column, 3, "PawnDiary.Dev.EventPanel.GenerateAll", delegate
            {
                EnsurePromptTestMode();
                int shown = component.ShowPromptSuiteEntriesForDev(pawn, entries);
                Messages.Message("PawnDiary.Dev.EventPanel.Generated".Translate(shown), MessageTypeDefOf.PositiveEvent, false);
            });
            DrawGridButton(rect, ref y, ref column, 3, "PawnDiary.Dev.EventPanel.GenerateSelected", delegate
            {
                EnsurePromptTestMode();
                int shown = component.ShowPromptSuiteEntriesForDev(pawn, component.DevPanelSelectedFixturesForDev(entries));
                Messages.Message("PawnDiary.Dev.EventPanel.Generated".Translate(shown), MessageTypeDefOf.PositiveEvent, false);
            });
            DrawDangerGridButton(rect, ref y, ref column, 3, "PawnDiary.Dev.EventPanel.Clear", delegate
            {
                int removed = component.ClearPromptSuiteForDev();
                Messages.Message("PawnDiary.Tab.PromptSuiteCleared".Translate(removed), MessageTypeDefOf.NeutralEvent, false);
            });
            FinishGridRow(ref y, ref column);

            DrawGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.SelectAll", component.SelectAllDevPanelFixturesForDev);
            DrawGridButton(rect, ref y, ref column, 2, "PawnDiary.Dev.EventPanel.SelectNone", component.ClearDevPanelFixturesForDev);
            FinishGridRow(ref y, ref column);

            y += ActionGap;
            DrawFixtureCheckboxGrid(new Rect(rect.x, y, rect.width, rect.height - y), component, entries);
        }

        private void DrawDevPreviewGrid(Rect rect, Pawn pawn)
        {
            float y = rect.y;
            int column = 0;
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewPlain", DiaryJournalView.DevDiaryPreviewKind.Plain, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewMarkdown", DiaryJournalView.DevDiaryPreviewKind.Markdown, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewSpeech", DiaryJournalView.DevDiaryPreviewKind.Speech, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewStaggered", DiaryJournalView.DevDiaryPreviewKind.Staggered, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewCombat", DiaryJournalView.DevDiaryPreviewKind.Combat, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewSocialFight", DiaryJournalView.DevDiaryPreviewKind.SocialFight, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewDeath", DiaryJournalView.DevDiaryPreviewKind.Death, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewMental", DiaryJournalView.DevDiaryPreviewKind.Mental, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewDark", DiaryJournalView.DevDiaryPreviewKind.Dark, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewStrange", DiaryJournalView.DevDiaryPreviewKind.Strange, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewLinked", DiaryJournalView.DevDiaryPreviewKind.Linked, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewWriting", DiaryJournalView.DevDiaryPreviewKind.Writing, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewTitle", DiaryJournalView.DevDiaryPreviewKind.TitlePending, pawn);
            DrawPreviewButton(rect, ref y, ref column, "PawnDiary.Tab.DevPreviewClear", DiaryJournalView.DevDiaryPreviewKind.None, pawn);
        }

        private void DrawPreviewButton(
            Rect gridRect,
            ref float y,
            ref int column,
            string labelKey,
            DiaryJournalView.DevDiaryPreviewKind kind,
            Pawn pawn)
        {
            DrawGridButton(gridRect, ref y, ref column, 4, labelKey, () => DiaryJournalView.RequestDevPreviewForDev(pawn, kind));
        }

        private void DrawFixtureCheckboxGrid(
            Rect rect,
            DiaryGameComponent component,
            IReadOnlyList<DiaryGameComponent.DevPromptSuiteEntry> entries)
        {
            float columnWidth = (rect.width - ActionGap) / 2f;
            for (int i = 0; i < entries.Count; i++)
            {
                DiaryGameComponent.DevPromptSuiteEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                int column = i % 2;
                int row = i / 2;
                Rect checkRect = new Rect(
                    rect.x + column * (columnWidth + ActionGap),
                    rect.y + row * ActionRowHeight,
                    columnWidth,
                    ActionRowHeight);
                bool selected = component.DevPanelFixtureSelectedForDev(entry.id);
                bool before = selected;
                Widgets.CheckboxLabeled(checkRect, EntryLabel(entry), ref selected);
                if (selected != before)
                {
                    component.SetDevPanelFixtureSelectedForDev(entry.id, selected);
                }
            }
        }

        private static float DrawSectionTitle(Rect rect, string labelKey)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 28f), labelKey.Translate());
            Text.Font = GameFont.Small;
            return rect.y + 34f;
        }

        private static bool ButtonTextLeft(Rect rect, string label)
        {
            return ButtonTextClickedButton(rect, label) == 0;
        }

        private static bool ButtonTextLeftDanger(Rect rect, string label)
        {
            return ButtonTextClickedButton(rect, label, true) == 0;
        }

        private static int ButtonTextClickedButton(Rect rect, string label)
        {
            return ButtonTextClickedButton(rect, label, false);
        }

        private static int ButtonTextClickedButton(Rect rect, string label, bool danger)
        {
            Event ev = Event.current;
            bool rightClicked = ev != null
                && ev.type == EventType.MouseDown
                && ev.button == 1
                && rect.Contains(ev.mousePosition);

            Color previousColor = GUI.color;
            if (danger)
            {
                GUI.color = DiaryUiStyles.Current.DevDangerButtonColor;
            }

            bool leftClicked = Widgets.ButtonText(rect, label);
            GUI.color = previousColor;
            if (rightClicked)
            {
                ev.Use();
                return 1;
            }

            return leftClicked ? 0 : -1;
        }

        private static void DrawGridButton(
            Rect gridRect,
            ref float y,
            ref int column,
            int columns,
            string labelKey,
            Action action)
        {
            DrawGridButtonText(gridRect, ref y, ref column, columns, labelKey.Translate().Resolve(), action);
        }

        private static void DrawDangerGridButton(
            Rect gridRect,
            ref float y,
            ref int column,
            int columns,
            string labelKey,
            Action action)
        {
            DrawGridButtonText(gridRect, ref y, ref column, columns, labelKey.Translate().Resolve(), action, true);
        }

        private static void DrawGridButtonText(
            Rect gridRect,
            ref float y,
            ref int column,
            int columns,
            string label,
            Action action)
        {
            DrawGridButtonText(gridRect, ref y, ref column, columns, label, action, null, null);
        }

        private static void DrawGridButtonText(
            Rect gridRect,
            ref float y,
            ref int column,
            int columns,
            string label,
            Action action,
            bool danger)
        {
            DrawGridButtonText(gridRect, ref y, ref column, columns, label, action, null, null, danger);
        }

        private static void DrawGridButtonText(
            Rect gridRect,
            ref float y,
            ref int column,
            int columns,
            string label,
            Action leftClickAction,
            Action rightClickAction,
            string tooltip)
        {
            DrawGridButtonText(gridRect, ref y, ref column, columns, label, leftClickAction, rightClickAction, tooltip, false);
        }

        private static void DrawGridButtonText(
            Rect gridRect,
            ref float y,
            ref int column,
            int columns,
            string label,
            Action leftClickAction,
            Action rightClickAction,
            string tooltip,
            bool danger)
        {
            float width = (gridRect.width - ActionGap * (columns - 1)) / columns;
            Rect buttonRect = new Rect(
                gridRect.x + column * (width + ActionGap),
                y,
                width,
                ActionRowHeight);
            int clickedButton = ButtonTextClickedButton(buttonRect, label, danger);
            if (clickedButton == 0)
            {
                leftClickAction();
            }
            else if (clickedButton == 1 && rightClickAction != null)
            {
                rightClickAction();
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(buttonRect, tooltip);
            }

            column++;
            if (column >= columns)
            {
                column = 0;
                y += ActionRowHeight + ActionGap;
            }
        }

        private void DrawDefBackedTriggerButton<T>(
            Rect gridRect,
            ref float y,
            ref int column,
            string labelKey,
            string defName,
            Action<string> setter,
            Action trigger,
            Predicate<T> predicate = null,
            bool danger = false) where T : Def
        {
            string label = "PawnDiary.Dev.EventPanel.DefChoice".Translate(
                labelKey.Translate(),
                SelectedDefLabel<T>(defName)).Resolve();
            DrawGridButtonText(
                gridRect,
                ref y,
                ref column,
                2,
                label,
                trigger,
                () => OpenDefMenu(setter, predicate),
                "PawnDiary.Dev.EventPanel.TriggerDefButtonTip".Translate().Resolve(),
                danger);
        }

        private void DrawAbilityTriggerButton(Rect gridRect, ref float y, ref int column, DiaryGameComponent component, Pawn pawn)
        {
            string selected = component.DevPanelAbilityDefNameForDev;
            string selectedLabel = string.IsNullOrWhiteSpace(selected)
                ? "PawnDiary.Dev.EventPanel.FirstPawnAbility".Translate().Resolve()
                : SelectedDefLabel<AbilityDef>(selected);
            string label = "PawnDiary.Dev.EventPanel.DefChoice".Translate(
                "PawnDiary.Dev.EventPanel.TriggerAbility".Translate(),
                selectedLabel).Resolve();
            DrawGridButtonText(
                gridRect,
                ref y,
                ref column,
                2,
                label,
                () => TriggerAbility(pawn, component.DevPanelAbilityDefNameForDev),
                delegate
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("PawnDiary.Dev.EventPanel.FirstPawnAbility".Translate(), delegate
                        {
                            component.SetDevPanelAbilityDefNameForDev(null);
                        })
                    };
                AddDefMenuOptions(options, component.SetDevPanelAbilityDefNameForDev, null as Predicate<AbilityDef>);
                Find.WindowStack.Add(new FloatMenu(options));
            },
                "PawnDiary.Dev.EventPanel.TriggerDefButtonTip".Translate().Resolve(),
                true);
        }

        private void OpenDefMenu<T>(Action<string> setter, Predicate<T> predicate = null) where T : Def
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddDefMenuOptions(options, setter, predicate);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddDefMenuOptions<T>(
            List<FloatMenuOption> options,
            Action<string> setter,
            Predicate<T> predicate = null) where T : Def
        {
            List<T> defs = SelectableDefs(predicate);
            for (int i = 0; i < defs.Count; i++)
            {
                T def = defs[i];
                string selectedDefName = def.defName;
                options.Add(new FloatMenuOption(DefMenuLabel(def), delegate
                {
                    setter(selectedDefName);
                }));
            }
        }

        private static List<T> SelectableDefs<T>(Predicate<T> predicate = null) where T : Def
        {
            List<T> result = new List<T>();
            List<T> defs = DefDatabase<T>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                T def = defs[i];
                if (def != null && (predicate == null || predicate(def)))
                {
                    result.Add(def);
                }
            }

            result.Sort((left, right) => string.Compare(DefMenuLabel(left), DefMenuLabel(right), StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static string SelectedDefLabel<T>(string defName) where T : Def
        {
            T def = DefNamed<T>(defName);
            return def == null
                ? "PawnDiary.Dev.EventPanel.MissingDef".Translate(defName).Resolve()
                : DefMenuLabel(def);
        }

        private static string DefMenuLabel(Def def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string label = def.LabelCap.Resolve();
            if (string.IsNullOrWhiteSpace(label) || string.Equals(label, def.defName, StringComparison.OrdinalIgnoreCase))
            {
                return def.defName;
            }

            return label + " [" + def.defName + "]";
        }

        private static bool IsMemoryThoughtDef(ThoughtDef def)
        {
            return def?.thoughtClass != null && typeof(Thought_Memory).IsAssignableFrom(def.thoughtClass);
        }

        private static bool IsSinglePawnTaleDef(TaleDef def)
        {
            return def?.taleClass != null && typeof(Tale_SinglePawn).IsAssignableFrom(def.taleClass);
        }

        private static void FinishGridRow(ref float y, ref int column)
        {
            if (column == 0)
            {
                return;
            }

            column = 0;
            y += ActionRowHeight + ActionGap;
        }

        private static float RealEventsViewHeight()
        {
            return 34f + TriggerChoicesViewHeight() + 28f + GridHeight(5, 2);
        }

        private static float DiaryToolsViewHeight()
        {
            return 34f + ActionRowHeight * 3f + ActionGap * 5f + 26f + PreviewGridHeight();
        }

        private static float PromptFixturesViewHeight(DiaryGameComponent component, Pawn pawn)
        {
            int fixtureCount = component.AvailableSuiteEntriesForDev(pawn).Count;
            return 34f + 28f + GridHeight(3, 3) + GridHeight(2, 2) + ActionGap
                + GridHeight(fixtureCount, 2);
        }

        private static float PreviewGridHeight()
        {
            return GridHeight(14, 4);
        }

        private static float TriggerChoicesViewHeight()
        {
            return 28f + GridHeight(12, 2) + GridHeight(1, 1) + ActionGap * 2f;
        }

        private static float GridHeight(int count, int columns)
        {
            if (count <= 0)
            {
                return 0f;
            }

            int rows = (count + columns - 1) / columns;
            return rows * ActionRowHeight + Math.Max(0, rows - 1) * ActionGap;
        }

        private void TriggerThought(Pawn pawn, string defName)
        {
            ThoughtDef def = DefNamed<ThoughtDef>(defName);
            if (pawn?.needs?.mood?.thoughts?.memories == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerThought".Translate().Resolve());
                return;
            }

            try
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(def, null, null);
                Message(true, "PawnDiary.Dev.EventPanel.TriggerThought".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev thought trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerThought".Translate().Resolve());
            }
        }

        private void TriggerInspiration(Pawn pawn, string defName)
        {
            InspirationDef def = DefNamed<InspirationDef>(defName);
            if (pawn?.mindState?.inspirationHandler == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerInspiration".Translate().Resolve());
                return;
            }

            try
            {
                pawn.mindState.inspirationHandler.EndInspiration(def);
                bool started = pawn.mindState.inspirationHandler.TryStartInspiration(
                    def,
                    "PawnDiary.Dev.EventPanel.DevReason".Translate().Resolve(),
                    true);
                Message(started, "PawnDiary.Dev.EventPanel.TriggerInspiration".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev inspiration trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerInspiration".Translate().Resolve());
            }
        }

        private void TriggerMentalState(Pawn pawn, string defName, Pawn otherPawn, string label)
        {
            MentalStateDef def = DefNamed<MentalStateDef>(defName);
            if (pawn?.mindState?.mentalStateHandler == null || def == null)
            {
                Message(false, label);
                return;
            }

            if (otherPawn == null && string.Equals(defName, "SocialFighting", StringComparison.OrdinalIgnoreCase))
            {
                Message(false, label);
                return;
            }

            if (pawn.mindState.mentalStateHandler.InMentalState)
            {
                pawn.mindState.mentalStateHandler.Reset();
            }

            try
            {
                bool started = pawn.mindState.mentalStateHandler.TryStartMentalState(
                    def,
                    "PawnDiary.Dev.EventPanel.DevReason".Translate().Resolve(),
                    true,
                    false,
                    false,
                    otherPawn,
                    false,
                    false,
                    false);
                Message(started, label);
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev mental-state trigger failed: " + e);
                Message(false, label);
            }
        }

        private void TriggerTale(Pawn pawn, string defName)
        {
            TaleDef def = DefNamed<TaleDef>(defName);
            if (pawn == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerTale".Translate().Resolve());
                return;
            }

            try
            {
                Tale tale = TaleRecorder.RecordTale(def, pawn);
                Message(tale != null, "PawnDiary.Dev.EventPanel.TriggerTale".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev tale trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerTale".Translate().Resolve());
            }
        }

        private void TriggerHediff(Pawn pawn, string defName)
        {
            HediffDef def = DefNamed<HediffDef>(defName);
            if (pawn?.health == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerHediff".Translate().Resolve());
                return;
            }

            try
            {
                Hediff existing = pawn.health.hediffSet?.GetFirstHediffOfDef(def);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }

                Hediff hediff = HediffMaker.MakeHediff(def, pawn, null);
                hediff.Severity = Math.Max(0.4f, hediff.Severity);
                pawn.health.AddHediff(hediff, null, null, null);
                Message(true, "PawnDiary.Dev.EventPanel.TriggerHediff".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev hediff trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerHediff".Translate().Resolve());
            }
        }

        private void TriggerMoodEvent(Pawn pawn, string defName)
        {
            Map map = MapFor(pawn);
            GameConditionDef def = DefNamed<GameConditionDef>(defName);
            if (map?.gameConditionManager == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerMoodEvent".Translate().Resolve());
                return;
            }

            try
            {
                GameCondition condition = GameConditionMaker.MakeCondition(def, GenDate.TicksPerDay);
                map.gameConditionManager.RegisterCondition(condition);
                Message(true, "PawnDiary.Dev.EventPanel.TriggerMoodEvent".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev game-condition trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerMoodEvent".Translate().Resolve());
            }
        }

        private void TriggerInteraction(Pawn pawn, Pawn partner, string defName)
        {
            InteractionDef def = DefNamed<InteractionDef>(defName);
            if (pawn == null || partner == null || def == null || Find.PlayLog == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerInteraction".Translate().Resolve());
                return;
            }

            try
            {
                Find.PlayLog.Add(new PlayLogEntry_Interaction(def, pawn, partner, new List<RulePackDef>()));
                Message(true, "PawnDiary.Dev.EventPanel.TriggerInteraction".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev interaction trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerInteraction".Translate().Resolve());
            }
        }

        private void TriggerRomance(Pawn pawn, Pawn partner, string defName)
        {
            PawnRelationDef def = DefNamed<PawnRelationDef>(defName);
            if (pawn?.relations == null || partner == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerRomance".Translate().Resolve());
                return;
            }

            try
            {
                if (pawn.relations.DirectRelationExists(def, partner))
                {
                    pawn.relations.TryRemoveDirectRelation(def, partner);
                }

                pawn.relations.AddDirectRelation(def, partner);
                Message(true, "PawnDiary.Dev.EventPanel.TriggerRomance".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev relation trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerRomance".Translate().Resolve());
            }
        }

        private void TriggerArrival(Pawn recruiter)
        {
            Pawn pawn = MakeDisposablePawn(recruiter, Faction.OfAncients);
            if (pawn == null || Faction.OfPlayer == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerArrival".Translate().Resolve());
                return;
            }

            pawn.SetFaction(Faction.OfPlayer, recruiter);
            Message(true, "PawnDiary.Dev.EventPanel.TriggerArrival".Translate().Resolve());
        }

        private void TriggerDeath(Pawn nearPawn)
        {
            Pawn pawn = MakeDisposablePawn(nearPawn, Faction.OfPlayer);
            if (pawn == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerDeath".Translate().Resolve());
                return;
            }

            pawn.Kill(null, null);
            Message(true, "PawnDiary.Dev.EventPanel.TriggerDeath".Translate().Resolve());
        }

        private void TriggerRaid(Pawn pawn, string defName)
        {
            Map map = MapFor(pawn);
            IncidentDef def = DefNamed<IncidentDef>(defName);
            if (map == null || def?.Worker == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerRaid".Translate().Resolve());
                return;
            }

            try
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                parms.forced = true;
                bool executed = def.Worker.TryExecute(parms);
                Message(executed, "PawnDiary.Dev.EventPanel.TriggerRaid".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev incident trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerRaid".Translate().Resolve());
            }
        }

        private void TriggerQuest(Pawn pawn, string defName)
        {
            QuestScriptDef def = DefNamed<QuestScriptDef>(defName);
            if (pawn == null || def == null)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerQuest".Translate().Resolve());
                return;
            }

            try
            {
                Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(def, 100f);
                if (quest == null)
                {
                    Message(false, "PawnDiary.Dev.EventPanel.TriggerQuest".Translate().Resolve());
                    return;
                }

                quest.Accept(pawn);
                quest.End(QuestEndOutcome.Success, false, false);
                Message(true, "PawnDiary.Dev.EventPanel.TriggerQuest".Translate().Resolve());
            }
            catch (Exception e)
            {
                Log.Warning("[Pawn Diary] Dev quest trigger failed: " + e);
                Message(false, "PawnDiary.Dev.EventPanel.TriggerQuest".Translate().Resolve());
            }
        }

        private void TriggerAbility(Pawn pawn, string defName)
        {
            List<Ability> abilities = pawn?.abilities?.AllAbilitiesForReading;
            if (abilities == null || abilities.Count == 0)
            {
                Message(false, "PawnDiary.Dev.EventPanel.TriggerAbility".Translate().Resolve());
                return;
            }

            for (int i = 0; i < abilities.Count; i++)
            {
                Ability ability = abilities[i];
                if (ability == null || ability.def == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(defName) || string.Equals(ability.def.defName, defName, StringComparison.Ordinal))
                {
                    try
                    {
                        bool activated = ability.Activate(new LocalTargetInfo(pawn), LocalTargetInfo.Invalid);
                        Message(activated, "PawnDiary.Dev.EventPanel.TriggerAbility".Translate().Resolve());
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[Pawn Diary] Dev ability trigger failed: " + e);
                        Message(false, "PawnDiary.Dev.EventPanel.TriggerAbility".Translate().Resolve());
                    }
                    return;
                }
            }

            Message(false, "PawnDiary.Dev.EventPanel.TriggerAbility".Translate().Resolve());
        }

        private void TriggerThoughtProgression(DiaryGameComponent component, Pawn pawn)
        {
            if (pawn?.needs?.food != null)
            {
                pawn.needs.food.CurLevel = Math.Min(pawn.needs.food.CurLevel, 0.01f);
            }

            component.ScanThoughtProgressionsForDev();
            ScannerMessage("PawnDiary.Dev.EventPanel.TriggerThoughtProgression".Translate().Resolve());
        }

        private Pawn MakeDisposablePawn(Pawn nearPawn, Faction faction)
        {
            Map map = MapFor(nearPawn);
            PawnKindDef kind = DefNamed<PawnKindDef>("Colonist");
            if (map == null || kind == null)
            {
                return null;
            }

            Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
            if (pawn == null)
            {
                return null;
            }

            IntVec3 cell = nearPawn != null && nearPawn.Spawned
                ? CellFinder.RandomSpawnCellForPawnNear(nearPawn.Position, map, 4)
                : CellFinder.RandomCell(map);
            GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);
            return pawn;
        }

        private static string EntryLabel(DiaryGameComponent.DevPromptSuiteEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.labelKey))
            {
                return string.Empty;
            }

            return entry.labelKey.Translate().Resolve();
        }

        private static string PersonaLabel(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate().Resolve();
            }

            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;
        }

        private Pawn SelectedPawn(List<Pawn> pawns, DiaryGameComponent component)
        {
            if (pawns == null || pawns.Count == 0)
            {
                component.SetDevPanelSelectedPawnForDev(null);
                return null;
            }

            string selectedPawnId = component.DevPanelSelectedPawnIdForDev;
            if (!string.IsNullOrWhiteSpace(selectedPawnId))
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (pawns[i] != null && string.Equals(pawns[i].GetUniqueLoadID(), selectedPawnId, StringComparison.Ordinal))
                    {
                        return pawns[i];
                    }
                }
            }

            component.SetDevPanelSelectedPawnForDev(pawns[0].GetUniqueLoadID());
            return pawns[0];
        }

        private Pawn PartnerPawn(List<Pawn> pawns, Pawn selectedPawn, DiaryGameComponent component)
        {
            if (pawns == null || pawns.Count < 2 || selectedPawn == null)
            {
                component.SetDevPanelSelectedPartnerForDev(null);
                return null;
            }

            string selectedPartnerId = component.DevPanelSelectedPartnerIdForDev;
            if (!string.IsNullOrWhiteSpace(selectedPartnerId))
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn != null
                        && pawn != selectedPawn
                        && string.Equals(pawn.GetUniqueLoadID(), selectedPartnerId, StringComparison.Ordinal))
                    {
                        return pawn;
                    }
                }
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && pawn != selectedPawn)
                {
                    component.SetDevPanelSelectedPartnerForDev(pawn.GetUniqueLoadID());
                    return pawn;
                }
            }

            component.SetDevPanelSelectedPartnerForDev(null);
            return null;
        }

        internal static List<Pawn> EligiblePawns()
        {
            List<Pawn> result = new List<Pawn>();
            if (Find.Maps == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>();
            for (int m = 0; m < Find.Maps.Count; m++)
            {
                Map map = Find.Maps[m];
                List<Pawn> colonists = map?.mapPawns?.FreeColonists;
                if (colonists == null)
                {
                    continue;
                }

                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];
                    string pawnId = pawn?.GetUniqueLoadID();
                    if (!string.IsNullOrWhiteSpace(pawnId)
                        && seen.Add(pawnId)
                        && DiaryGameComponent.IsDiaryEligible(pawn))
                    {
                        result.Add(pawn);
                    }
                }
            }

            return result;
        }

        private static Map MapFor(Pawn pawn)
        {
            if (pawn?.Map != null)
            {
                return pawn.Map;
            }

            if (Find.CurrentMap != null)
            {
                return Find.CurrentMap;
            }

            List<Map> maps = Find.Maps;
            return maps == null || maps.Count == 0 ? null : maps[0];
        }

        private static T DefNamed<T>(string defName) where T : Def
        {
            return string.IsNullOrWhiteSpace(defName) ? null : DefDatabase<T>.GetNamedSilentFail(defName);
        }

        private static void Message(bool success, string label)
        {
            string key = success ? "PawnDiary.Dev.EventPanel.TriggerSucceeded" : "PawnDiary.Dev.EventPanel.TriggerFailed";
            Messages.Message(key.Translate(label), success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }

        private static void ScannerMessage(string label)
        {
            Messages.Message("PawnDiary.Dev.EventPanel.ScannerRan".Translate(label), MessageTypeDefOf.NeutralEvent, false);
        }

        private static void EnsurePromptTestMode()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null || settings.promptTestMode)
            {
                return;
            }

            settings.promptTestMode = true;
            WriteGlobalSettings();
        }

        private static void WriteGlobalSettings()
        {
            PawnDiaryMod mod = LoadedModManager.GetMod<PawnDiaryMod>();
            if (mod != null)
            {
                mod.WriteSettings();
            }
        }
    }
}
