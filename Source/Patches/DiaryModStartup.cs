// Mod entry point. [StaticConstructorOnStartup] makes RimWorld run this class's static
// constructor once at game load (there is no main()). We use it to apply our Harmony patches and
// register the hidden Diary inspector tab after the vanilla Needs tab on every humanlike pawn
// and on those pawns' corpse defs.
// See AGENTS.md ("[StaticConstructorOnStartup]").
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    [StaticConstructorOnStartup]
    public static class DiaryModStartup
    {
        static DiaryModStartup()
        {
            // Apply all attribute-tagged ([HarmonyPatch]) patches. Wrapped so one failing patch
            // can't abort this static constructor (which would also skip the tab registration below).
            Harmony harmony = new Harmony("aimml.pawndiary");
            try
            {
                harmony.PatchAll();
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] PatchAll failed: " + e);
            }

            // Registered manually (not via PatchAll) because these reflection/generated-name targets
            // may change between RimWorld versions. The registrar keeps that fragility in one place.
            // Each stage is isolated so one failing stage can't abort the others or this whole static
            // constructor (which would leave the mod half-initialized and spam at load).
            try
            {
                DiaryPatchRegistrar.RegisterFragilePatches(harmony);
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] Fragile patch registration failed: " + e);
            }

            try
            {
                InjectDiaryTab();
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] Diary tab injection failed: " + e);
            }

            Log.Message("[Pawn Diary] Loaded.");
        }

        /// <summary>
        /// Registers the hidden Diary inspector tab for every humanlike pawn and matching corpse def,
        /// placing it after Needs where that tab exists.
        /// </summary>
        // Add the Diary inspector tab to every humanlike pawn and its corpse ThingDef. It is hidden
        // from the tab strip by ITab_Pawn_Diary.Hidden, but must stay registered so command buttons,
        // diary links, and selected corpses can open it through RimWorld's inspect pane.
        private static void InjectDiaryTab()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null || !def.race.Humanlike)
                {
                    continue;
                }

                // Guard per-def so one malformed (often modded) race can't stop the Diary tab from
                // registering on every other pawn.
                try
                {
                    RegisterDiaryTabOn(def);
                    RegisterDiaryTabOn(def.race.corpseDef);
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[Pawn Diary] Failed to register Diary tab on " + def.defName + ": " + e,
                        ("DiaryTabInject:" + def.defName).GetHashCode());
                }
            }
        }

        /// <summary>
        /// Adds the Diary inspector tab to one ThingDef, removing any previous slot first so its
        /// position is deterministic even when several pawn races share a corpse def.
        /// </summary>
        private static void RegisterDiaryTabOn(ThingDef def)
        {
            if (def == null)
            {
                return;
            }

            if (def.inspectorTabs == null)
            {
                def.inspectorTabs = new List<Type>();
            }

            if (def.inspectorTabsResolved == null)
            {
                def.inspectorTabsResolved = new List<InspectTabBase>();
            }

            def.inspectorTabs.RemoveAll(tab => tab == typeof(ITab_Pawn_Diary));
            int needsIndex = def.inspectorTabs.IndexOf(typeof(ITab_Pawn_Needs));
            if (needsIndex >= 0)
            {
                def.inspectorTabs.Insert(needsIndex + 1, typeof(ITab_Pawn_Diary));
            }
            else
            {
                def.inspectorTabs.Add(typeof(ITab_Pawn_Diary));
            }

            InspectTabBase instance = InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Diary));
            def.inspectorTabsResolved.RemoveAll(tab => tab is ITab_Pawn_Diary);
            int resolvedNeedsIndex = def.inspectorTabsResolved.FindIndex(tab => tab is ITab_Pawn_Needs);
            if (resolvedNeedsIndex >= 0)
            {
                def.inspectorTabsResolved.Insert(resolvedNeedsIndex + 1, instance);
            }
            else
            {
                def.inspectorTabsResolved.Add(instance);
            }
        }
    }
}
