// RimWorld mod entry point for the separate "Pawn Diary: 1-2-3 Personalities" adapter mod. It owns the
// bridge's saved settings (one mode selector plus the Tier-3 lane + prompt) and draws the settings
// window. There is no Harmony and no context provider: the bridge only READS 1-2-3 Personalities' public
// API and calls Pawn Diary's integration API from the per-game GameComponent.
//
// The single setting is a MODE with three escalating tiers of how a colonist's Enneagram shapes their
// editable Pawn Diary psychotype:
//   Off                 - the bridge does nothing.
//   InternalPsychotype  - map the root to the closest built-in Pawn Diary psychotype (base type).
//   Override            - seed the editable custom rule from the built-in outlook text.
//   LlmTransform        - seed the editable custom rule from an LLM rewrite of the pawn's 1-2-3 data,
//                         on a selectable lane with an editable prompt (falls back to Override).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using PawnDiary.Integration;
using UnityEngine;
using Verse;

namespace PawnDiaryPersonalities123
{
    /// <summary>How a colonist's 1-2-3 personality drives their Pawn Diary psychotype. Single-choice.</summary>
    public enum Personalities123Mode
    {
        /// <summary>The bridge does nothing; player-owned psychotype values are left untouched.</summary>
        Off,

        /// <summary>Tier 1: set the pawn's base psychotype to the closest built-in Pawn Diary type.</summary>
        InternalPsychotype,

        /// <summary>Tier 2: seed the editable custom rule from the built-in root→outlook text.</summary>
        Override,

        /// <summary>Tier 3: seed the editable custom rule from an LLM transform (falls back to Tier 2).</summary>
        LlmTransform
    }

    /// <summary>
    /// Adapter mod entry point. RimWorld creates this once when the mod list is loaded, before any game
    /// exists. The per-game <see cref="Personalities123GameComponent"/> does all the seeding work.
    /// </summary>
    public class PawnDiaryPersonalities123Mod : Mod
    {
        internal const string LogPrefix = "[Pawn Diary: 1-2-3 Personalities]";

        /// <summary>Saved bridge settings. Null only before the mod constructor has run.</summary>
        internal static PawnDiaryPersonalities123Settings Settings;

        /// <summary>
        /// True when 1-2-3 Personalities M1 (hahkethomemah.simplepersonalities) is in the active mod
        /// list. Cached once at startup: the mod list cannot change while the game is running, and
        /// ModsConfig.IsActive walks a list on every call. Every code path that touches SP_Module1 types
        /// must check this first (see the SP_Module1-type isolation rule in EnneagramSync).
        /// </summary>
        internal static bool SimplePersonalitiesActive;

        /// <summary>
        /// Bumped whenever the player changes a bridge setting (mode / lane / prompt). The per-game
        /// component watches this and re-seeds every colonist when it moves, so a settings edit takes
        /// effect for the whole colony at once instead of only on each pawn's next personality change.
        /// </summary>
        internal static int SettingsGeneration;

        // Signature of the settings already folded into SettingsGeneration, so an open→close with no
        // real change does not force a needless re-seed (which, in the LLM tier, would re-spend tokens).
        private static string appliedSignature = string.Empty;

        public PawnDiaryPersonalities123Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiaryPersonalities123Settings>();
            SimplePersonalitiesActive = ModsConfig.IsActive(BridgeIds.SimplePersonalitiesPackageId);

            if (!SimplePersonalitiesActive)
            {
                // About.xml declares the dependency, but RimWorld does not hard-enforce it. Warn once
                // and idle instead of erroring: every bridge feature checks SimplePersonalitiesActive.
                Log.WarningOnce(
                    LogPrefix + " 1-2-3 Personalities (" + BridgeIds.SimplePersonalitiesPackageId
                    + ") is not in the active mod list; the bridge stays idle.",
                    "PawnDiaryPersonalities123.Missing".GetHashCode());
            }

            // Record the starting signature so the first settings-window close only bumps the generation
            // if the player actually changed something.
            appliedSignature = SettingsSignature();
        }

        /// <summary>Returns the settings-list label shown by RimWorld's mod options menu.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiaryPersonalities123.Settings.Category".Translate();
        }

        /// <summary>
        /// Called when the mod settings window closes. If the player actually changed a seeding-relevant
        /// setting, bump <see cref="SettingsGeneration"/> so the per-game component re-seeds every colonist
        /// with the new mode / lane / prompt.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();

            string signature = SettingsSignature();
            if (signature != appliedSignature)
            {
                appliedSignature = signature;
                SettingsGeneration++;
            }
        }

        // A cheap identity of the seeding-relevant settings, compared on window close to detect changes.
        private static string SettingsSignature()
        {
            PawnDiaryPersonalities123Settings settings = Settings;
            if (settings == null)
            {
                return string.Empty;
            }

            return (int)settings.mode + "|" + settings.transformLaneIndex + "|" + (settings.transformPrompt ?? string.Empty);
        }

        /// <summary>Draws the mode selector plus, when the LLM tier is chosen, its lane + prompt block.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            if (!SimplePersonalitiesActive)
            {
                GUI.color = Color.yellow;
                listing.Label("PawnDiaryPersonalities123.Settings.Missing".Translate());
                GUI.color = Color.white;
                listing.Gap();
            }

            listing.Label("PawnDiaryPersonalities123.Settings.Mode".Translate());
            DrawModeOption(listing, Personalities123Mode.Off, "PawnDiaryPersonalities123.Settings.Mode.Off", "PawnDiaryPersonalities123.Settings.Mode.OffDesc");
            DrawModeOption(listing, Personalities123Mode.InternalPsychotype, "PawnDiaryPersonalities123.Settings.Mode.Internal", "PawnDiaryPersonalities123.Settings.Mode.InternalDesc");
            DrawModeOption(listing, Personalities123Mode.Override, "PawnDiaryPersonalities123.Settings.Mode.Override", "PawnDiaryPersonalities123.Settings.Mode.OverrideDesc");
            DrawModeOption(listing, Personalities123Mode.LlmTransform, "PawnDiaryPersonalities123.Settings.Mode.Llm", "PawnDiaryPersonalities123.Settings.Mode.LlmDesc");

            if (Settings.mode == Personalities123Mode.LlmTransform)
            {
                listing.Gap();
                DrawTransformSection(listing);
            }

            listing.End();
        }

        private void DrawModeOption(Listing_Standard listing, Personalities123Mode mode, string labelKey, string descKey)
        {
            if (listing.RadioButton(labelKey.Translate(), Settings.mode == mode, 8f))
            {
                Settings.mode = mode;
            }

            DrawMutedDescription(listing, descKey.Translate());
        }

        // The Tier-3 LLM block, drawn in the same idiom as the main mod's connection section: a medium
        // section title with a divider, a dropdown-style selector button, and a framed editor.
        private void DrawTransformSection(Listing_Standard listing)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            listing.Label("PawnDiaryPersonalities123.Settings.LlmSectionTitle".Translate());
            Text.Font = previousFont;
            listing.GapLine(6f);

            DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();

            // Lane selector row: label + a ButtonText that opens a FloatMenu of the player's lanes.
            Rect laneRect = listing.GetRect(28f);
            float laneLabelWidth = Mathf.Min(160f, laneRect.width * 0.4f);
            Rect laneLabelRect = new Rect(laneRect.x, laneRect.y, laneLabelWidth, laneRect.height);
            Rect laneButtonRect = new Rect(laneLabelRect.xMax + 8f, laneRect.y, laneRect.width - laneLabelWidth - 8f, laneRect.height);
            Widgets.Label(laneLabelRect, "PawnDiaryPersonalities123.Settings.TransformLane".Translate());
            if (Widgets.ButtonText(laneButtonRect, CurrentLaneLabel(setup)))
            {
                Find.WindowStack.Add(new FloatMenu(BuildLaneOptions(setup)));
            }

            if (setup == null || setup.activeLaneCount == 0)
            {
                DrawMutedDescription(listing, "PawnDiaryPersonalities123.Settings.NoLanesHint".Translate());
            }

            listing.Gap(6f);

            // Prompt editor: a label + Reset button on one row, then a multi-line text area.
            Rect headerRect = listing.GetRect(24f);
            Rect resetRect = new Rect(headerRect.xMax - 120f, headerRect.y, 120f, headerRect.height);
            Rect promptLabelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 128f, headerRect.height);
            Widgets.Label(promptLabelRect, "PawnDiaryPersonalities123.Settings.TransformPrompt".Translate());
            if (Widgets.ButtonText(resetRect, "PawnDiaryPersonalities123.Settings.ResetPrompt".Translate()))
            {
                // Empty means "use the localized default" (see Settings.ResolveTransformPrompt).
                Settings.transformPrompt = string.Empty;
            }

            // When the player has not customized the prompt, show the resolved default in the box so it is
            // readable and editable; the first edit materializes it into the saved setting.
            Rect promptRect = listing.GetRect(120f);
            string shown = string.IsNullOrEmpty(Settings.transformPrompt) ? Settings.ResolveTransformPrompt() : Settings.transformPrompt;
            string edited = Widgets.TextArea(promptRect, shown);
            if (edited != shown)
            {
                Settings.transformPrompt = edited;
            }
        }

        private string CurrentLaneLabel(DiaryApiSetupSnapshot setup)
        {
            if (Settings.transformLaneIndex < 0)
            {
                return "PawnDiaryPersonalities123.Settings.TransformLaneAuto".Translate();
            }

            if (setup != null)
            {
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                {
                    if (lane.index == Settings.transformLaneIndex)
                    {
                        return LaneLabel(lane);
                    }
                }
            }

            // Saved index no longer exists (lane removed); the request falls back to the first active
            // lane, so show the Automatic label to match that effective behavior.
            return "PawnDiaryPersonalities123.Settings.TransformLaneAuto".Translate();
        }

        private List<FloatMenuOption> BuildLaneOptions(DiaryApiSetupSnapshot setup)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PawnDiaryPersonalities123.Settings.TransformLaneAuto".Translate(), delegate
                {
                    Settings.transformLaneIndex = -1;
                })
            };

            if (setup != null)
            {
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                {
                    int index = lane.index;
                    options.Add(new FloatMenuOption(LaneLabel(lane), delegate
                    {
                        Settings.transformLaneIndex = index;
                    }));
                }
            }

            return options;
        }

        private static string LaneLabel(DiaryApiLaneSnapshot lane)
        {
            string name;
            if (!string.IsNullOrWhiteSpace(lane.model))
            {
                name = lane.model;
            }
            else if (!string.IsNullOrWhiteSpace(lane.url))
            {
                name = lane.url;
            }
            else
            {
                name = "PawnDiaryPersonalities123.Settings.LaneFallback".Translate(lane.index + 1);
            }

            if (lane.active)
            {
                return name;
            }

            return "PawnDiaryPersonalities123.Settings.LaneInactive".Translate(name).Resolve();
        }

        private static void DrawMutedDescription(Listing_Standard listing, string text)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            listing.Label(text);
            GUI.color = previousColor;
            Text.Font = previousFont;
            listing.Gap(4f);
        }
    }

    /// <summary>
    /// Saved settings for the bridge adapter. Scribe keys are FROZEN once shipped: they live in the
    /// player's mod-settings XML, so renaming one silently resets that setting for every player.
    /// </summary>
    public class PawnDiaryPersonalities123Settings : ModSettings
    {
        /// <summary>Which tier drives the pawn's diary psychotype. Default matches the prior default-on
        /// outlook behavior, now written to the editable layer.</summary>
        public Personalities123Mode mode = Personalities123Mode.Override;

        /// <summary>Tier 3: lane index into Pawn Diary's configured lanes, or -1 for the first active lane.</summary>
        public int transformLaneIndex = -1;

        /// <summary>Tier 3: the editable transform instruction. Empty means "use the localized default".</summary>
        public string transformPrompt = string.Empty;

        /// <summary>The transform instruction to send: the player's text, or the localized default when blank.</summary>
        public string ResolveTransformPrompt()
        {
            if (string.IsNullOrWhiteSpace(transformPrompt))
            {
                return "PawnDiaryPersonalities123.Settings.TransformPromptDefault".Translate();
            }

            return transformPrompt;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", Personalities123Mode.Override);
            Scribe_Values.Look(ref transformLaneIndex, "transformLaneIndex", -1);
            Scribe_Values.Look(ref transformPrompt, "transformPrompt", string.Empty);
        }
    }
}
