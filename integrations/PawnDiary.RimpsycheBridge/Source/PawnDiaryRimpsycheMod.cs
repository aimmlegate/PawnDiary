// RimWorld mod entry point for the separate Pawn Diary: Rimpsyche adapter.
//
// The constructor owns process-global setup: load saved adapter settings, cache whether Rimpsyche is
// active, register Tier A's Pawn Diary context provider, and install Tier C's signature-checked
// Harmony Postfix. Per-save reset/cadence lives in RimpsycheGameComponent instead.
//
// Both optional behavior switches default ON as specified by design:
//   * Tier B: source-owned diary psychotype outlook from the dominant psyche families.
//   * Tier C: charged conversation outcomes forwarded as External events.
// Tier A and the XML compatibility groups are always on while both mods/the master API are active.
//
// New to C#/RimWorld? See AGENTS.md and docs/lore/build.md (Mod constructors run during long-event load).
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PawnDiary.Integration;
using UnityEngine;
using Verse;

namespace PawnDiaryRimpsyche
{
    /// <summary>Saved player choices for the two optional Rimpsyche bridge tiers.</summary>
    public class PawnDiaryRimpsycheSettings : ModSettings
    {
        /// <summary>Tier B: let Rimpsyche own the pawn's external diary-outlook slot.</summary>
        public bool usePsychotypeOverride = true;

        /// <summary>Tier C: submit high-|alignment| conversations as first-class diary events.</summary>
        public bool recordChargedConversations = true;

        /// <summary>Saves frozen setting keys to RimWorld's per-mod config XML.</summary>
        public override void ExposeData()
        {
            Scribe_Values.Look(ref usePsychotypeOverride, "usePsychotypeOverride", true);
            Scribe_Values.Look(ref recordChargedConversations, "recordChargedConversations", true);
        }
    }

    /// <summary>Process-global adapter startup and settings UI.</summary>
    public class PawnDiaryRimpsycheMod : Mod
    {
        internal const string HarmonyId = "aimmlegate.pawndiary.adapter.rimpsyche";
        internal const string LogPrefix = "[Pawn Diary: Rimpsyche]";

        /// <summary>Saved settings; null only before RimWorld constructs this Mod.</summary>
        internal static PawnDiaryRimpsycheSettings Settings;

        /// <summary>
        /// Cached active-mod guard. ModsConfig walks the load list, which cannot change mid-process;
        /// every path that can reach RimPsyche.dll-typed method bodies checks this first.
        /// </summary>
        internal static bool RimpsycheActive;

        public PawnDiaryRimpsycheMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiaryRimpsycheSettings>();
            RimpsycheActive = ModsConfig.IsActive(BridgeIds.RimpsychePackageId);

            if (!RimpsycheActive)
            {
                // About.xml declares a hard dependency, but RimWorld lets users ignore it. Stay inert:
                // XML groups are package-gated and no RimPsyche.dll method body is JIT-compiled.
                Log.WarningOnce(
                    LogPrefix + " Rimpsyche (" + BridgeIds.RimpsychePackageId
                    + ") is not active; the bridge stays inert and Tier C is disabled gracefully.",
                    "PawnDiaryRimpsyche.MissingTarget".GetHashCode());
                return;
            }

            if (SupportsApiVersion(1))
            {
                try
                {
                    PsycheSync.RegisterContextProvider();
                }
                catch (Exception exception)
                {
                    Log.ErrorOnce(
                        LogPrefix + " failed to register the psyche context provider; Tier A is disabled: "
                        + exception,
                        "PawnDiaryRimpsyche.ContextProvider.Failed".GetHashCode());
                }
            }
            else
            {
                Log.WarningOnce(
                    LogPrefix + " requires Pawn Diary external API v1 or newer; code-driven tiers stay idle.",
                    "PawnDiaryRimpsyche.ApiTooOld".GetHashCode());
            }

            // The hook is installed even if its setting is currently off so the checkbox can be enabled
            // without restarting. The Postfix's very first operation is the settings-bool early return.
            ConversationCapture.TryInstall(new Harmony(HarmonyId));
            Log.Message(LogPrefix + " initialized.");
        }

        /// <summary>
        /// Runtime feature gate for additive Pawn Diary API members. Kept behind a method so the C#
        /// compiler does not fold today's const ApiVersion and erase the older-version fallback branch.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool SupportsApiVersion(int requiredVersion)
        {
            return PawnDiaryApi.ApiVersion >= requiredVersion;
        }

        /// <summary>Label shown in RimWorld's Mod Settings list.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiaryRimpsyche.Settings.Category".Translate();
        }

        /// <summary>Draws the two adapter-owned default-on toggles and their localized descriptions.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            if (!RimpsycheActive)
            {
                Color previous = GUI.color;
                GUI.color = Color.yellow;
                listing.Label("PawnDiaryRimpsyche.Settings.Missing".Translate());
                GUI.color = previous;
                listing.GapLine();
            }

            listing.Label("PawnDiaryRimpsyche.Settings.Section".Translate());
            listing.CheckboxLabeled(
                "PawnDiaryRimpsyche.Settings.PsychotypeOverride".Translate(),
                ref Settings.usePsychotypeOverride,
                "PawnDiaryRimpsyche.Settings.PsychotypeOverrideDesc".Translate());
            listing.CheckboxLabeled(
                "PawnDiaryRimpsyche.Settings.ChargedConversations".Translate(),
                ref Settings.recordChargedConversations,
                "PawnDiaryRimpsyche.Settings.ChargedConversationsDesc".Translate());

            listing.End();
        }
    }
}
