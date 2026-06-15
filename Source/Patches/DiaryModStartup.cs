// Mod entry point. [StaticConstructorOnStartup] makes RimWorld run this class's static
// constructor once at game load (there is no main()). We use it to apply our Harmony patches and
// inject the Diary inspector tab as the final tab on every humanlike pawn.
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
            new Harmony("aimml.pawndiary").PatchAll();
            InjectDiaryTab();
            Log.Message("[Pawn Diary] Loaded.");
        }

        /// <summary>
        /// Injects the Diary inspector tab into every humanlike pawn's inspector, placing it last.
        /// </summary>
        // Add the Diary inspector tab to every humanlike pawn. If another path already inserted it,
        // remove that earlier slot first so the tab ends up consistently at the far right.
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
                def.inspectorTabs.Add(typeof(ITab_Pawn_Diary));

                InspectTabBase instance = InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Diary));
                def.inspectorTabsResolved.RemoveAll(tab => tab is ITab_Pawn_Diary);
                def.inspectorTabsResolved.Add(instance);
            }
        }
    }
}
