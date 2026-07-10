// RimWorld mod entry point for the separate "Pawn Diary: 1-2-3 Personalities" adapter mod.
// It owns the bridge's saved settings (two toggles), draws the settings window, and registers the
// Tier A pawn-context provider. There is no Harmony here: the bridge only reads 1-2-3 Personalities'
// public API and calls Pawn Diary's integration API.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using UnityEngine;
using Verse;

namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Adapter mod entry point. RimWorld creates this once when the mod list is loaded, before any game
    /// exists, which makes the constructor the right place for the process-global provider registration.
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
                return;
            }

            // The mod constructor runs once per process, so it owns registration; the per-game
            // GameComponent owns periodic work and cache resets. Isolated so a future Pawn Diary or
            // 1-2-3 Personalities rename degrades to "Tier A disabled" rather than taking down the whole
            // mod ctor (and with it the settings UI this mod also owns).
            try
            {
                EnneagramSync.RegisterContextProvider();
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to register the personality context provider; Tier A is disabled: " + e);
            }

            Log.Message(LogPrefix + " initialized.");
        }

        /// <summary>Returns the settings-list label shown by RimWorld's mod options menu.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiaryPersonalities123.Settings.Category".Translate();
        }

        /// <summary>Draws the two bridge toggles plus a note when 1-2-3 Personalities is absent.</summary>
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

            bool contextLine = Settings.provideContextLine;
            listing.CheckboxLabeled(
                "PawnDiaryPersonalities123.Settings.ProvideContextLine".Translate(),
                ref contextLine,
                "PawnDiaryPersonalities123.Settings.ProvideContextLineDesc".Translate());
            Settings.provideContextLine = contextLine;

            bool outlook = Settings.usePersonalityOutlook;
            listing.CheckboxLabeled(
                "PawnDiaryPersonalities123.Settings.UsePersonalityOutlook".Translate(),
                ref outlook,
                "PawnDiaryPersonalities123.Settings.UsePersonalityOutlookDesc".Translate());
            Settings.usePersonalityOutlook = outlook;

            listing.End();
        }
    }

    /// <summary>
    /// Saved settings for the bridge adapter. Scribe keys are FROZEN once shipped: they live in the
    /// player's mod-settings XML, so renaming one silently resets that setting for every player.
    /// </summary>
    public class PawnDiaryPersonalities123Settings : ModSettings
    {
        /// <summary>Tier A (on): show a "personality=&lt;variant&gt;, &lt;trait&gt;" line in the pawn summary.</summary>
        public bool provideContextLine = true;

        /// <summary>Tier B (on): use the pawn's Enneagram root as their diary outlook (psychotype override).</summary>
        public bool usePersonalityOutlook = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref provideContextLine, "provideContextLine", true);
            Scribe_Values.Look(ref usePersonalityOutlook, "usePersonalityOutlook", true);
        }
    }
}
