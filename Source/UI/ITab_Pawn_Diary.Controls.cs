// Dev controls and per-pawn setting helpers for the Diary tab. Split from ITab_Pawn_Diary.cs with no behavior change.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary
    {
        /// <summary>
        /// Returns the height needed for the per-pawn controls above the diary list.
        /// Dev-only rows are omitted in normal play, keeping the tab focused on entries.
        /// </summary>
        private static float PawnControlsHeight()
        {

            if (!Prefs.DevMode)
            {

                return 0f;

            }



            float lines = 4f; // generation toggle + mock-history filler + two transient formatting preview rows

            if (PawnDiaryMod.Settings != null)
            {

                lines += 3f; // dev toggles: persona controls, LLM diagnostics, show generating

                if (ShouldShowPersonaSettings())
                {

                    lines += 1f; // persona picker

                }

            }



            return lines * ControlLineHeight + (lines - 1f) * ControlGap;

        }



        /// <summary>
        /// Renders the per-pawn generation toggle plus dev-mode-only troubleshooting controls.
        /// </summary>
        private void DrawPawnControls(Pawn pawn, DiaryGameComponent component, Rect rect)
        {

            if (pawn == null || component == null)
            {

                return;

            }



            if (!Prefs.DevMode)
            {

                return;

            }



            Listing_Standard listing = new Listing_Standard();

            listing.Begin(rect);



            // Toggling this only gates future LLM requests. Recorded events remain visible as raw

            // diary entries, which lets players pause generation without losing history.

            bool enabled = component.DiaryGenerationEnabledFor(pawn);

            bool before = enabled;

            listing.CheckboxLabeled(

                "PawnDiary.Tab.GenerateForPawn".Translate(),

                ref enabled,

                "PawnDiary.Tab.GenerateForPawnTip".Translate());

            if (enabled != before)
            {

                component.SetDiaryGenerationEnabled(pawn, enabled);

            }



            PawnDiarySettings settings = PawnDiaryMod.Settings;

            bool writeGlobalSettings = false;

            if (Prefs.DevMode && settings != null)
            {

                bool showPersonaSettings = settings.showPersonaSettings;

                bool showPersonaBefore = showPersonaSettings;

                listing.CheckboxLabeled(

                    "PawnDiary.Tab.ShowPersonaSettings".Translate(),

                    ref showPersonaSettings,

                    "PawnDiary.Tab.ShowPersonaSettingsTip".Translate());

                if (showPersonaSettings != showPersonaBefore)
                {

                    settings.showPersonaSettings = showPersonaSettings;

                    writeGlobalSettings = true;

                }



                bool showLlmDebugInfo = settings.showLlmDebugInfo;

                bool showDebugBefore = showLlmDebugInfo;

                listing.CheckboxLabeled(

                    "PawnDiary.Tab.ShowLlmDebugInfo".Translate(),

                    ref showLlmDebugInfo,

                    "PawnDiary.Tab.ShowLlmDebugInfoTip".Translate());

                if (showLlmDebugInfo != showDebugBefore)
                {

                    settings.showLlmDebugInfo = showLlmDebugInfo;

                    writeGlobalSettings = true;

                }



                bool showGeneratingEntries = settings.showGeneratingEntries;

                bool showGeneratingBefore = showGeneratingEntries;

                listing.CheckboxLabeled(

                    "PawnDiary.Tab.ShowGeneratingEntries".Translate(),

                    ref showGeneratingEntries,

                    "PawnDiary.Tab.ShowGeneratingEntriesTip".Translate());

                if (showGeneratingEntries != showGeneratingBefore)
                {

                    settings.showGeneratingEntries = showGeneratingEntries;

                    writeGlobalSettings = true;

                }

            }



            // The mock-page filler and the prompt test suite share one row. Each is a self-contained

            // dev fixture: mock pages test long-history scrolling; the prompt suite captures one

            // prompt-only card per event category so every prompt shape can be inspected without an LLM.

            Rect devButtonRow = listing.GetRect(ControlLineHeight);

            float devButtonGap = 4f;

            float mockButtonWidth = (devButtonRow.width - devButtonGap) * 0.5f;

            Rect mockButtonRect = new Rect(devButtonRow.x, devButtonRow.y, mockButtonWidth, devButtonRow.height);

            Rect promptSuiteButtonRect = new Rect(mockButtonRect.xMax + devButtonGap, devButtonRow.y, devButtonRow.width - mockButtonWidth - devButtonGap, devButtonRow.height);

            if (Widgets.ButtonText(mockButtonRect, "PawnDiary.Tab.FillMockEntries".Translate(DevMockDiaryTargetCount)))
            {

                int added = component.FillMockDiaryEntriesForDev(pawn, DevMockDiaryTargetCount);

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

                "PawnDiary.Tab.FillMockEntriesTip".Translate(DevMockDiaryTargetCount));



            if (Widgets.ButtonText(promptSuiteButtonRect, "PawnDiary.Tab.GeneratePromptSuite".Translate()))
            {

                HandleGeneratePromptSuite(pawn, component);

            }



            TooltipHandler.TipRegion(

                promptSuiteButtonRect,

                "PawnDiary.Tab.GeneratePromptSuiteTip".Translate());


            DrawDevPreviewButtons(listing, pawn);



            // Persona picker. Options come from DiaryPersonaDefs.xml so new presets can be added

            // without touching UI code; the choice is saved per pawn and used for future generations.

            if (ShouldShowPersonaSettings())
            {

                DiaryPersonaDef persona = component.PersonaFor(pawn);

                if (listing.ButtonText("PawnDiary.Tab.PersonaButton".Translate(PersonaLabel(persona))))
                {

                    List<FloatMenuOption> options = DiaryPersonas.All

                        .OrderBy(PersonaLabel)

                        .Select(option =>

                        {

                            DiaryPersonaDef selected = option;

                            return new FloatMenuOption(PersonaLabel(selected), delegate

                            {

                                component.SetPersona(pawn, selected.defName);

                            });

                        })

                        .ToList();



                    Find.WindowStack.Add(new FloatMenu(options));

                }

            }



            listing.End();



            if (writeGlobalSettings)
            {

                WriteGlobalSettings();

            }

        }



        /// <summary>
        /// Returns the human-readable label for a persona, falling back to "default" if null
        /// or to defName if the label is blank.
        /// </summary>
        private static string PersonaLabel(DiaryPersonaDef persona)
        {

            if (persona == null)
            {

                return "PawnDiary.Persona.DefaultLabel".Translate();

            }



            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;

        }



        /// <summary>
        /// Draws compact animated dots while hidden pending entries are being written.
        /// </summary>
        private static void DrawWritingIndicator(Rect rect)
        {

            DrawWritingDots(

                new Rect(rect.x + rect.width * 0.5f - 10f, rect.y + rect.height * 0.5f - 2f, 24f, 8f),

                new Color(0.78f, 0.95f, 0.78f),

                0f);

        }



        /// <summary>
        /// True when an entry has actual LLM output ready for the production diary list.
        /// </summary>
        private static bool IsGenerated(DiaryEntryView entry)
        {

            return entry != null && !string.IsNullOrWhiteSpace(entry.GeneratedText);

        }



        /// <summary>
        /// Dev-mode preference gate for manual persona editing in the Diary tab.
        /// </summary>
        private static bool ShouldShowPersonaSettings()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showPersonaSettings;

        }



        /// <summary>
        /// Dev-mode preference gate for raw/pending entries and the LLM prompt/status block.
        /// </summary>
        private static bool ShouldShowLlmDebugInfo()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showLlmDebugInfo;

        }



        /// <summary>
        /// Dev-mode gate for prompt-only cards captured by the no-generation prompt test setting.
        /// </summary>
        private static bool ShouldShowPromptOnlyEntries()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.promptTestMode;

        }



        /// <summary>
        /// Dev-mode preference gate for revealing entries still in the LLM generation pipeline
        /// (in-progress or stuck), without the full prompt/status diagnostic block.
        /// </summary>
        private static bool ShouldShowGeneratingEntries()
        {

            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showGeneratingEntries;

        }



        /// <summary>
        /// Dev-only handler for the "Generate prompt test suite" button. Enables prompt test mode (so
        /// the queue captures prompts instead of calling an LLM), then asks the component to seed one
        /// synthetic event per category and reports how many prompt-only pages were captured. Pair
        /// categories are silently skipped when the pawn has no second colonist.
        /// </summary>
        private void HandleGeneratePromptSuite(Pawn pawn, DiaryGameComponent component)
        {

            if (pawn == null || component == null)
            {

                return;

            }



            PawnDiarySettings settings = PawnDiaryMod.Settings;

            if (settings != null && !settings.promptTestMode)
            {

                settings.promptTestMode = true;

                WriteGlobalSettings();

            }



            int added = component.GeneratePromptTestSuiteForDev(pawn, null);

            if (added > 0)
            {

                Messages.Message(

                    "PawnDiary.Tab.PromptSuiteAdded".Translate(added, pawn.LabelShortCap),

                    MessageTypeDefOf.PositiveEvent,

                    false);

            }

            else
            {

                Messages.Message(

                    "PawnDiary.Tab.PromptSuiteEmpty".Translate(pawn.LabelShortCap),

                    MessageTypeDefOf.NeutralEvent,

                    false);

            }

        }



        /// <summary>
        /// Persists global mod UI preferences changed from this pawn tab.
        /// </summary>
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
