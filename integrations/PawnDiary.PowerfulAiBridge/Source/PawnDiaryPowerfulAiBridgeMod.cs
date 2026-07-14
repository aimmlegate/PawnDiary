// RimWorld mod entry point and small settings window for the separate Powerful AI Integration bridge.
// The adapter has one responsibility: mirror PAI persona text into Pawn Diary's reversible psychotype
// override, either directly or through Pawn Diary's existing one-shot LLM completion API.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using UnityEngine;
using Verse;

namespace PawnDiaryPowerfulAiBridge
{
    /// <summary>How Powerful AI persona text reaches Pawn Diary.</summary>
    public enum PowerfulAiPersonaMode
    {
        Disabled,
        Direct,
        LlmAssisted
    }

    /// <summary>Adapter mod entry point and owner of the global bridge settings.</summary>
    public class PawnDiaryPowerfulAiBridgeMod : Mod
    {
        internal const string LogPrefix = "[Pawn Diary: Powerful AI Integration]";
        internal static PawnDiaryPowerfulAiBridgeSettings Settings;
        internal static bool PowerfulAiActive;
        private static bool generatorRegistered;

        public PawnDiaryPowerfulAiBridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiaryPowerfulAiBridgeSettings>();
            PowerfulAiActive = ModsConfig.IsActive(BridgeIds.PowerfulAiPackageId);
            if (!PowerfulAiActive)
            {
                Log.WarningOnce(
                    LogPrefix + " Powerful AI Integration is not active; the bridge stays idle.",
                    "PawnDiaryPowerfulAiBridge.Missing".GetHashCode());
            }
        }

        public override string SettingsCategory()
        {
            return "PawnDiaryPowerfulAiBridge.Settings.Category".Translate();
        }

        /// <summary>Draws the three modes and a lane selector used only by LLM-assisted mode.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(inRect);

            if (!PowerfulAiActive)
            {
                Color previous = GUI.color;
                GUI.color = Color.yellow;
                listing.Label("PawnDiaryPowerfulAiBridge.Settings.Missing".Translate());
                GUI.color = previous;
                listing.Gap();
            }

            listing.Label("PawnDiaryPowerfulAiBridge.Settings.Mode".Translate());
            DrawMode(listing, PowerfulAiPersonaMode.Disabled,
                "PawnDiaryPowerfulAiBridge.Settings.Mode.Disabled",
                "PawnDiaryPowerfulAiBridge.Settings.Mode.DisabledDesc");
            DrawMode(listing, PowerfulAiPersonaMode.Direct,
                "PawnDiaryPowerfulAiBridge.Settings.Mode.Direct",
                "PawnDiaryPowerfulAiBridge.Settings.Mode.DirectDesc");
            DrawMode(listing, PowerfulAiPersonaMode.LlmAssisted,
                "PawnDiaryPowerfulAiBridge.Settings.Mode.Llm",
                "PawnDiaryPowerfulAiBridge.Settings.Mode.LlmDesc");

            if (Settings.mode == PowerfulAiPersonaMode.LlmAssisted)
            {
                listing.GapLine();
                DrawLaneSelector(listing);
                listing.Gap(6f);
                DrawMuted(listing, "PawnDiaryPowerfulAiBridge.Settings.DataSent".Translate());
            }

            listing.End();
        }

        internal static void EnsureGeneratorRegistered()
        {
            if (generatorRegistered || !PowerfulAiActive)
            {
                return;
            }

            try
            {
                PawnDiaryApi.RegisterExternalPsychotypeGenerator(new ExternalPsychotypeGenerator
                {
                    sourceId = BridgeIds.ModId,
                    canReroll = CanReroll,
                    isBusy = IsBusy,
                    reroll = Reroll
                });
                generatorRegistered = true;
            }
            catch (Exception e)
            {
                Log.ErrorOnce(LogPrefix + " could not register the LLM Regenerate action: " + e,
                    "PawnDiaryPowerfulAiBridge.GeneratorRegistration".GetHashCode());
            }
        }

        private static bool CanReroll(Pawn pawn)
        {
            return PowerfulAiActive && Settings != null
                && Settings.mode == PowerfulAiPersonaMode.LlmAssisted;
        }

        private static bool IsBusy(Pawn pawn)
        {
            PowerfulAiBridgeGameComponent component = ActiveComponent();
            return component != null && component.IsTransformInFlight(pawn);
        }

        private static void Reroll(Pawn pawn)
        {
            ActiveComponent()?.RerollTransform(pawn);
        }

        private static PowerfulAiBridgeGameComponent ActiveComponent()
        {
            return Current.Game?.GetComponent<PowerfulAiBridgeGameComponent>();
        }

        private static void DrawMode(Listing_Standard listing, PowerfulAiPersonaMode mode,
            string labelKey, string descriptionKey)
        {
            if (listing.RadioButton(labelKey.Translate(), Settings.mode == mode, 8f))
            {
                Settings.mode = mode;
            }

            DrawMuted(listing, descriptionKey.Translate());
        }

        private static void DrawLaneSelector(Listing_Standard listing)
        {
            DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();
            Rect row = listing.GetRect(28f);
            float labelWidth = Math.Min(150f, row.width * 0.4f);
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect buttonRect = new Rect(labelRect.xMax + 8f, row.y, row.width - labelWidth - 8f, row.height);
            Widgets.LabelFit(labelRect, "PawnDiaryPowerfulAiBridge.Settings.Lane".Translate());
            if (Widgets.ButtonText(buttonRect, CurrentLaneLabel(setup)))
            {
                Find.WindowStack.Add(new FloatMenu(BuildLaneOptions(setup)));
            }

            if (setup == null || setup.activeLaneCount == 0)
            {
                DrawMuted(listing, "PawnDiaryPowerfulAiBridge.Settings.NoLane".Translate());
            }
        }

        private static List<FloatMenuOption> BuildLaneOptions(DiaryApiSetupSnapshot setup)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PawnDiaryPowerfulAiBridge.Settings.Lane.Auto".Translate(),
                    delegate { Settings.transformLaneIndex = -1; })
            };

            if (setup != null && setup.lanes != null)
            {
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                {
                    int index = lane.index;
                    string label = LaneLabel(lane);
                    options.Add(new FloatMenuOption(label, delegate { Settings.transformLaneIndex = index; }));
                }
            }

            return options;
        }

        private static string CurrentLaneLabel(DiaryApiSetupSnapshot setup)
        {
            if (Settings.transformLaneIndex >= 0 && setup?.lanes != null)
            {
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                {
                    if (lane.index == Settings.transformLaneIndex)
                    {
                        return LaneLabel(lane);
                    }
                }
            }

            return "PawnDiaryPowerfulAiBridge.Settings.Lane.Auto".Translate();
        }

        private static string LaneLabel(DiaryApiLaneSnapshot lane)
        {
            string label;
            if (!string.IsNullOrWhiteSpace(lane.model))
            {
                label = lane.model;
            }
            else
            {
                label = "PawnDiaryPowerfulAiBridge.Settings.Lane.Number".Translate(lane.index + 1).Resolve();
            }
            if (lane.active)
            {
                return label;
            }

            return "PawnDiaryPowerfulAiBridge.Settings.Lane.Inactive".Translate(label).Resolve();
        }

        private static void DrawMuted(Listing_Standard listing, string text)
        {
            GameFont font = Text.Font;
            Color color = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.62f);
            listing.Label(text);
            GUI.color = color;
            Text.Font = font;
            listing.Gap(4f);
        }
    }

    /// <summary>Saved adapter settings. Scribe keys are frozen after publication.</summary>
    public class PawnDiaryPowerfulAiBridgeSettings : ModSettings
    {
        public PowerfulAiPersonaMode mode = PowerfulAiPersonaMode.Direct;
        public int transformLaneIndex = -1;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", PowerfulAiPersonaMode.Direct);
            Scribe_Values.Look(ref transformLaneIndex, "transformLaneIndex", -1);
        }
    }
}
