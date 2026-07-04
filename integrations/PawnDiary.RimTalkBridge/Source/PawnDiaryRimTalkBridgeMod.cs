// RimWorld mod entry point for the separate PawnDiary: RimTalk bridge adapter.
// It owns the single saved setting and installs the Harmony patch that listens to RimTalk chat.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Adapter mod entry point. RimWorld creates this once when the mod is loaded.
    /// </summary>
    public class PawnDiaryRimTalkBridgeMod : Mod
    {
        internal const string HarmonyId = "aimmlegate.pawndiary.rimtalkbridge";
        internal const string LogPrefix = "[PawnDiary: RimTalk bridge]";

        internal static PawnDiaryRimTalkBridgeSettings Settings;

        private bool lastLoggedEnabled;

        public PawnDiaryRimTalkBridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiaryRimTalkBridgeSettings>();
            lastLoggedEnabled = Settings.enabled;

            new Harmony(HarmonyId).PatchAll();
            Log.Message(LogPrefix + " initialized.");
        }

        /// <summary>Returns the settings-list label shown by RimWorld.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiaryRimTalkBridge.Settings.Category".Translate();
        }

        /// <summary>Draws the bridge settings. For now there is only one knob.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            bool enabled = Settings.enabled;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.Enabled".Translate(),
                ref enabled,
                "PawnDiaryRimTalkBridge.Settings.EnabledDesc".Translate());
            Settings.enabled = enabled;

            listing.End();
        }

        /// <summary>Persists settings and emits a small status log when the bridge is toggled.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Settings.enabled != lastLoggedEnabled)
            {
                lastLoggedEnabled = Settings.enabled;
                Log.Message(LogPrefix + " logging " + (Settings.enabled ? "enabled." : "disabled."));
            }
        }
    }

    /// <summary>Saved settings for the bridge adapter.</summary>
    public class PawnDiaryRimTalkBridgeSettings : ModSettings
    {
        /// <summary>When true, RimTalk chat is logged with related Pawn Diary title snapshots.</summary>
        public bool enabled;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", false);
        }
    }
}
