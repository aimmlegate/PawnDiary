// Mod entry point. [StaticConstructorOnStartup] makes RimWorld run this class's static
// constructor once at game load (there is no main()). We use it to apply our Harmony patches and
// inject the Diary inspector tab after the vanilla Log tab on every humanlike pawn.
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
        /// Injects the Diary inspector tab into every humanlike pawn's inspector, placing it
        /// immediately after the vanilla Log tab so it appears as a sibling tab.
        /// </summary>
        // Add the Diary inspector tab to every humanlike pawn, placed right after the
        // vanilla Log tab so it reads as a sibling of it.
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

                if (def.inspectorTabs.Contains(typeof(ITab_Pawn_Diary)))
                {
                    continue;
                }

                int logIndex = def.inspectorTabs.IndexOf(typeof(ITab_Pawn_Log));
                if (logIndex >= 0)
                {
                    def.inspectorTabs.Insert(logIndex + 1, typeof(ITab_Pawn_Diary));
                }
                else
                {
                    def.inspectorTabs.Add(typeof(ITab_Pawn_Diary));
                }

                InspectTabBase instance = InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Diary));
                int resolvedLogIndex = def.inspectorTabsResolved.FindIndex(tab => tab is ITab_Pawn_Log);
                if (resolvedLogIndex >= 0)
                {
                    def.inspectorTabsResolved.Insert(resolvedLogIndex + 1, instance);
                }
                else
                {
                    def.inspectorTabsResolved.Add(instance);
                }
            }
        }
    }
}
