// RimWorld mod entry point for the "Pawn Diary: Vanilla Social Interactions Expanded" adapter. It owns
// the adapter's saved settings (which gatherings to record), draws the settings window, and installs the
// Harmony hook that captures VSIE's group gatherings (see VsieGatheringBridge.cs).
//
// The adapter is otherwise pure XML (interaction/thought/relation compat groups + the Discord routing
// patch); this small assembly exists only for the gathering bridge, because gatherings have no
// InteractionDef/TaleDef the XML groups could match.
//
// Why the gathering toggles live HERE and not in Pawn Diary's own Events tab: the gathering entries are
// External-domain events, and Pawn Diary's Events tab deliberately excludes External groups (they are
// governed by the master integration switch + adapter settings, not that per-group filter list — see
// PawnDiaryMod.IsSettingsEventFilterGroup). So, like the RimTalk and 1-2-3 Personalities adapters, this
// adapter owns its own settings entry.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo. (TS analogy: this is the module's exported
// entry object — RimWorld constructs it once at load, like a framework calling your plugin's register().)
using System;
using HarmonyLib;
using PawnDiaryVsie.Pure;
using UnityEngine;
using Verse;

namespace PawnDiaryVsie
{
    /// <summary>
    /// Saved settings for the VSIE adapter: which group gatherings become their own diary entries.
    /// Both default on. RimWorld persists a <see cref="ModSettings"/> automatically when the settings
    /// window closes, so the checkboxes need no explicit write.
    /// </summary>
    public class VsieBridgeSettings : ModSettings
    {
        /// <summary>Record a diary entry when VSIE throws a birthday party (organizer POV).</summary>
        public bool recordBirthdays = true;

        /// <summary>Record a diary entry when VSIE holds a funeral (organizer POV).</summary>
        public bool recordFunerals = true;

        /// <summary>
        /// Whether the gathering behind <paramref name="eventKey"/> is currently allowed to record.
        /// An unknown key is allowed (future gatherings are opt-out, not silently dropped).
        /// </summary>
        public bool AllowsEventKey(string eventKey)
        {
            if (string.Equals(eventKey, VsieGatheringMap.BirthdayEventKey, StringComparison.Ordinal))
            {
                return recordBirthdays;
            }

            if (string.Equals(eventKey, VsieGatheringMap.FuneralEventKey, StringComparison.Ordinal))
            {
                return recordFunerals;
            }

            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref recordBirthdays, "recordBirthdays", true);
            Scribe_Values.Look(ref recordFunerals, "recordFunerals", true);
        }
    }

    /// <summary>
    /// Adapter mod entry point. RimWorld creates this once when the mod list is loaded, before any game
    /// exists, which makes the constructor the right place for the process-global Harmony install.
    /// </summary>
    public class VsieBridgeMod : Mod
    {
        /// <summary>Package id of this adapter mod. Stored on each submitted event for attribution.</summary>
        internal const string SourceId = "aimmlegate.pawndiary.adapter.vsie";
        internal const string HarmonyId = "aimmlegate.pawndiary.adapter.vsie";
        internal const string LogPrefix = "[Pawn Diary: VSIE]";

        // VSIE's packageId. ModsConfig.IsActive compares case-insensitively against the active load order.
        private const string VsiePackageId = "VanillaExpanded.VanillaSocialInteractionsExpanded";

        /// <summary>Saved adapter settings. Null only before the mod constructor has run.</summary>
        internal static VsieBridgeSettings Settings;

        public VsieBridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<VsieBridgeSettings>();

            // Inert without VSIE: the XML groups are enableWhenPackageIdsLoaded-gated and the eventKeys
            // are never submitted anyway, but skip patching the gathering path entirely too so the
            // adapter truly does nothing when its target mod is not installed. (The settings window still
            // works so the player's choices are saved for when they add VSIE.)
            if (!ModsConfig.IsActive(VsiePackageId))
            {
                return;
            }

            // Isolate PatchAll: a failure to install must never take down mod loading. The base-game
            // target (RimWorld.GatheringWorker.TryExecute) always resolves, so this is belt-and-braces.
            try
            {
                new Harmony(HarmonyId).PatchAll();
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to install the gathering hook; birthday/funeral entries are disabled: " + e);
            }
        }

        public override string SettingsCategory()
        {
            return "PawnDiaryVsie.Settings.Category".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            if (!ModsConfig.IsActive(VsiePackageId))
            {
                // Mirror the RimTalk bridge: explain the idle state but still let the toggles save.
                GUI.color = new Color(1f, 0.7f, 0.7f);
                list.Label("PawnDiaryVsie.Settings.VsieMissing".Translate());
                GUI.color = Color.white;
                list.GapLine();
            }

            list.Label("PawnDiaryVsie.Settings.GatheringsSection".Translate());
            list.CheckboxLabeled(
                "PawnDiaryVsie.Settings.RecordBirthdays".Translate(),
                ref Settings.recordBirthdays,
                "PawnDiaryVsie.Settings.RecordBirthdaysDesc".Translate());
            list.CheckboxLabeled(
                "PawnDiaryVsie.Settings.RecordFunerals".Translate(),
                ref Settings.recordFunerals,
                "PawnDiaryVsie.Settings.RecordFuneralsDesc".Translate());

            list.End();
        }
    }
}
