// RimWorld entry point and saved settings for the dedicated SpeakUp adapter. Tier 1 is entirely XML;
// this class only installs the reflection-bound whole-conversation observer when SpeakUp is active.
// A missing or changed SpeakUp runtime therefore cannot stop the richer interaction groups from loading.
//
// New to C#/RimWorld? RimWorld constructs a Mod subclass once during play-data loading. See AGENTS.md
// and skills/pawndiary-engineering/SKILL.md for the optional-mod and localization rules used here.
using System;
using HarmonyLib;
using PawnDiarySpeakUp.Pure;
using UnityEngine;
using Verse;

namespace PawnDiarySpeakUp
{
    /// <summary>Saved adapter settings for Tier 2 whole-conversation capture.</summary>
    public class SpeakUpBridgeSettings : ModSettings
    {
        /// <summary>Whether completed SpeakUp Talk chains may become External diary events.</summary>
        public bool captureWholeConversations = true;

        /// <summary>Minimum SpeakUp latestReplyCount required before a Talk is submitted.</summary>
        public int minimumReplies = TalkSummaryFormat.DefaultMinimumReplies;

        /// <summary>Loads/saves the two stable setting keys and clamps corrupt/old values safely.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref captureWholeConversations, "captureWholeConversations", true);
            Scribe_Values.Look(ref minimumReplies, "minimumReplies", TalkSummaryFormat.DefaultMinimumReplies);
            minimumReplies = Math.Max(1, Math.Min(5, minimumReplies));
        }
    }

    /// <summary>
    /// Adapter bootstrap. Installs no patches at all when SpeakUp is absent, and installs only manually
    /// resolved patches when present—there is no optional-mod type token for the CLR to resolve eagerly.
    /// </summary>
    public class SpeakUpBridgeMod : Mod
    {
        /// <summary>Adapter package id used as the public API sourceId and Harmony owner id.</summary>
        internal const string SourceId = "aimmlegate.pawndiary.adapter.speakup";
        internal const string HarmonyId = "aimmlegate.pawndiary.adapter.speakup";
        internal const string SpeakUpPackageId = "JPT.speakup";
        internal const string LogPrefix = "[Pawn Diary: SpeakUp]";

        /// <summary>Process-global settings instance created with the mod.</summary>
        internal static SpeakUpBridgeSettings Settings;

        /// <summary>Constructs the adapter and conditionally binds the reflection bridge.</summary>
        public SpeakUpBridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SpeakUpBridgeSettings>();

            // Inert force-load path: XML has the same package gate, and we skip every Harmony lookup.
            if (!ModsConfig.IsActive(SpeakUpPackageId))
            {
                return;
            }

            try
            {
                TalkCapture.TryRegister(new Harmony(HarmonyId));
            }
            catch (Exception e)
            {
                // The reflection helper also protects itself, but the Mod constructor is the final safety
                // boundary: a conversation observer must never take down RimWorld's play-data load.
                Log.Warning(LogPrefix + " Tier 2 disabled because its Harmony registration failed: " + e);
            }
        }

        /// <summary>Localized title shown in RimWorld's mod-settings list.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiarySpeakUp.Settings.Category".Translate();
        }

        /// <summary>Draws the Tier 2 enable switch and its one-to-five delivered-reply threshold.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            if (!ModsConfig.IsActive(SpeakUpPackageId))
            {
                GUI.color = new Color(1f, 0.7f, 0.7f);
                list.Label("PawnDiarySpeakUp.Settings.SpeakUpMissing".Translate());
                GUI.color = Color.white;
                list.GapLine();
            }

            list.Label("PawnDiarySpeakUp.Settings.ConversationsSection".Translate());
            bool wasEnabled = Settings.captureWholeConversations;
            list.CheckboxLabeled(
                "PawnDiarySpeakUp.Settings.CaptureConversations".Translate(),
                ref Settings.captureWholeConversations,
                "PawnDiarySpeakUp.Settings.CaptureConversationsDesc".Translate());

            list.Label("PawnDiarySpeakUp.Settings.MinimumReplies".Translate(Settings.minimumReplies));
            Settings.minimumReplies = Mathf.Clamp(
                Mathf.RoundToInt(list.Slider(Settings.minimumReplies, 1f, 5f)), 1, 5);
            list.Label("PawnDiarySpeakUp.Settings.MinimumRepliesDesc".Translate());

            if (wasEnabled && !Settings.captureWholeConversations)
            {
                // An off switch takes effect immediately; do not retain half a talk until expiry.
                TalkCapture.ResetTransient();
            }

            list.End();
        }
    }
}
