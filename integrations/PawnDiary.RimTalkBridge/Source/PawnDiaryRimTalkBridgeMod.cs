// RimWorld mod entry point for the separate PawnDiary: RimTalk bridge adapter.
// It owns the single saved setting and installs the Harmony patch that listens to RimTalk chat.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
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

            // PatchAll reflects over this assembly's [HarmonyPatch] classes and resolves each target.
            // Our listener's TargetMethod returns null when RimTalk is absent or has renamed the method
            // it hooks, and a null target makes PatchAll throw — which would take down the whole mod
            // ctor and, with it, the settings this mod also owns. Isolate it so a missing/changed RimTalk
            // degrades to "chat logging disabled" (TargetMethod already warns) instead of a hard error.
            try
            {
                new Harmony(HarmonyId).PatchAll();
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to install Harmony patches; chat logging is disabled: " + e);
            }

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
