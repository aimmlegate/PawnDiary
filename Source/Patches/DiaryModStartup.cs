// Mod entry point. [StaticConstructorOnStartup] makes RimWorld run this class's static
// constructor once at game load (there is no main()). We use it to apply our Harmony patches and
// inject the Diary inspector tab after the vanilla Needs tab on every humanlike pawn.
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
            // can't abort this static constructor (which would also skip the tab injection below).
            Harmony harmony = new Harmony("aimml.pawndiary");
            try
            {
                harmony.PatchAll();
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] PatchAll failed: " + e);
            }

            // Registered manually (not via PatchAll) because its target is a fragile compiler-
            // generated method name — see RelicInstallCompletionPatch for why.
            RelicInstallCompletionPatch.TryRegister(harmony);

            // Registered manually (not via PatchAll) because the target method name may change
            // between RimWorld versions — see ThoughtGainPatch.TryRegister for why.
            ThoughtGainPatch.TryRegister(harmony);

            InjectDiaryTab();
            Log.Message("[Pawn Diary] Loaded.");
        }

        /// <summary>
        /// Injects the Diary inspector tab into every humanlike pawn's inspector, placing it after Needs.
        /// </summary>
        // Add the Diary inspector tab to every humanlike pawn. If another path already inserted it,
        // remove that earlier slot first so the tab ends up consistently after the vanilla Needs tab.
        private static void InjectDiaryTab()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null || !def.race.Humanlike)
                {
                    continue;
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
}
