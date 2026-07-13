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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PawnDiary.Integration;
using UnityEngine;
using Verse;

namespace PawnDiaryRimpsyche
{
    /// <summary>How Rimpsyche drives the pawn's editable Pawn Diary psychotype.</summary>
    public enum RimpsychePersonaMode
    {
        Off,
        InternalPsychotype,
        DirectText,
        LlmTransform
    }

    /// <summary>Saved player choices for the two optional Rimpsyche bridge tiers.</summary>
    public class PawnDiaryRimpsycheSettings : ModSettings
    {
        /// <summary>Tier B: let Rimpsyche own the pawn's external diary-outlook slot.</summary>
        public RimpsychePersonaMode personaMode = RimpsychePersonaMode.DirectText;

        /// <summary>LLM lane index, or -1 for the first active lane.</summary>
        public int transformLaneIndex = -1;

        /// <summary>Editable LLM rewrite instruction; blank uses the localized default.</summary>
        public string transformPrompt = string.Empty;

        /// <summary>Tier C: submit high-|alignment| conversations as first-class diary events.</summary>
        public bool recordChargedConversations = true;

        /// <summary>Saves frozen setting keys to RimWorld's per-mod config XML.</summary>
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RimpsychePersonaMode loaded = (RimpsychePersonaMode)(-1);
                Scribe_Values.Look(ref loaded, "personaMode", (RimpsychePersonaMode)(-1));
                if ((int)loaded < 0 || (int)loaded > (int)RimpsychePersonaMode.LlmTransform)
                {
                    bool oldEnabled = true;
                    Scribe_Values.Look(ref oldEnabled, "usePsychotypeOverride", true);
                    personaMode = oldEnabled ? RimpsychePersonaMode.DirectText : RimpsychePersonaMode.Off;
                }
                else
                {
                    personaMode = loaded;
                }
            }
            else
            {
                Scribe_Values.Look(ref personaMode, "personaMode", RimpsychePersonaMode.DirectText);
            }
            Scribe_Values.Look(ref transformLaneIndex, "transformLaneIndex", -1);
            Scribe_Values.Look(ref transformPrompt, "transformPrompt", string.Empty);
            Scribe_Values.Look(ref recordChargedConversations, "recordChargedConversations", true);
        }

        public string ResolveTransformPrompt()
        {
            return string.IsNullOrWhiteSpace(transformPrompt)
                ? "PawnDiaryRimpsyche.Settings.TransformPromptDefault".Translate().Resolve()
                : transformPrompt;
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
                    RegisterPsychotypeGenerator();
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
            // TryInstall guards its own body, but constructing the Harmony instance is outside it, so the
            // mod constructor is the final safety boundary: a conversation observer must never take down
            // RimWorld's play-data load if Harmony's static init throws (e.g. a version clash).
            try
            {
                ConversationCapture.TryInstall(new Harmony(HarmonyId));
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    LogPrefix + " failed to install the Rimpsyche conversation hook; Tier C is disabled: "
                    + exception,
                    "PawnDiaryRimpsyche.InteractionHook.ConstructFailed".GetHashCode());
            }

            Log.Message(LogPrefix + " initialized.");
        }

        private static void RegisterPsychotypeGenerator()
        {
            PawnDiaryApi.RegisterExternalPsychotypeGenerator(new ExternalPsychotypeGenerator
            {
                sourceId = BridgeIds.ModId,
                canReroll = pawn => RimpsycheActive && Settings != null && Settings.personaMode == RimpsychePersonaMode.LlmTransform,
                isBusy = PsycheSync.IsTransformInFlight,
                reroll = PsycheSync.RerollTransform
            });
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
            DrawMode(listing, RimpsychePersonaMode.Off, "Off");
            DrawMode(listing, RimpsychePersonaMode.InternalPsychotype, "Mapping");
            DrawMode(listing, RimpsychePersonaMode.DirectText, "Direct");
            DrawMode(listing, RimpsychePersonaMode.LlmTransform, "Llm");
            if (Settings.personaMode == RimpsychePersonaMode.LlmTransform)
            {
                DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();
                Rect laneRect = listing.GetRect(28f);
                Widgets.Label(new Rect(laneRect.x, laneRect.y, 150f, laneRect.height), "PawnDiaryRimpsyche.Settings.TransformLane".Translate());
                if (Widgets.ButtonText(new Rect(laneRect.x + 158f, laneRect.y, laneRect.width - 158f, laneRect.height), LaneLabel(setup)))
                {
                    Find.WindowStack.Add(new FloatMenu(LaneOptions(setup)));
                }
                Rect promptRect = listing.GetRect(100f);
                string shown = string.IsNullOrEmpty(Settings.transformPrompt) ? Settings.ResolveTransformPrompt() : Settings.transformPrompt;
                string edited = Widgets.TextArea(promptRect, shown);
                if (edited != shown) Settings.transformPrompt = edited;
            }
            listing.CheckboxLabeled(
                "PawnDiaryRimpsyche.Settings.ChargedConversations".Translate(),
                ref Settings.recordChargedConversations,
                "PawnDiaryRimpsyche.Settings.ChargedConversationsDesc".Translate());

            listing.End();
        }

        private static void DrawMode(Listing_Standard listing, RimpsychePersonaMode mode, string suffix)
        {
            if (listing.RadioButton(("PawnDiaryRimpsyche.Settings.Mode." + suffix).Translate(), Settings.personaMode == mode, 8f))
            {
                Settings.personaMode = mode;
            }
            listing.Label(("PawnDiaryRimpsyche.Settings.Mode." + suffix + "Desc").Translate());
        }

        private static string LaneLabel(DiaryApiSetupSnapshot setup)
        {
            if (Settings.transformLaneIndex >= 0 && setup?.lanes != null)
            {
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                    if (lane.index == Settings.transformLaneIndex) return string.IsNullOrWhiteSpace(lane.model) ? lane.url : lane.model;
            }
            return "PawnDiaryRimpsyche.Settings.TransformLaneAuto".Translate();
        }

        private static List<FloatMenuOption> LaneOptions(DiaryApiSetupSnapshot setup)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PawnDiaryRimpsyche.Settings.TransformLaneAuto".Translate(), () => Settings.transformLaneIndex = -1)
            };
            if (setup?.lanes != null)
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                {
                    int index = lane.index;
                    string label = string.IsNullOrWhiteSpace(lane.model) ? lane.url : lane.model;
                    options.Add(new FloatMenuOption(label, () => Settings.transformLaneIndex = index));
                }
            return options;
        }
    }
}
