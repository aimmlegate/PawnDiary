// Mod entry point. [StaticConstructorOnStartup] makes RimWorld run this class's static
// constructor once at game load (there is no main()). We use it to apply our Harmony patches,
// apply any saved XML-Def settings overrides after DefDatabase is populated, and register the hidden
// Diary inspector tab after the vanilla Needs tab on every humanlike pawn and on those pawns' corpse defs.
// See AGENTS.md ("[StaticConstructorOnStartup]").
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    [StaticConstructorOnStartup]
    internal static class DiaryModStartup
    {
        private static bool postStartupInjectionDone;

        static DiaryModStartup()
        {
            // Every registration below reports its outcome to DiaryPatchManifest so the last line of
            // startup can log one hook-health summary (the update-day breakage overview).
            DiaryPatchManifest.Reset();

            // Apply all attribute-tagged ([HarmonyPatch]) patches. Each class is patched independently
            // so one bad target (often a fragile RimWorld-version-specific method) cannot abort the
            // whole sweep and leave the rest of the mod's hooks unregistered.
            Harmony harmony = new Harmony("aimml.pawndiary");
            PatchAllSafely(harmony);

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
                DiaryPatchManifest.Report(
                    "startup",
                    "DiaryPatchRegistrar.RegisterFragilePatches",
                    DiaryPatchManifest.HookStatus.Failed,
                    e.GetType().Name + ": " + e.Message);
            }

            try
            {
                InjectDiaryTab();
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] Diary tab injection failed: " + e);
            }

            try
            {
                AdvancedFieldCatalog.EnsureApplied(PawnDiaryMod.Settings?.advancedOverrides);
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] Advanced settings override application failed: " + e);
            }

            // Keep the literal "[Pawn Diary] Loaded." prefix — it is a long-standing grep target.
            // The appended manifest summary is the one-glance hook-health line for game updates.
            Log.Message("[Pawn Diary] Loaded. " + DiaryPatchManifest.BuildSummary());
            string degradedHooks = DiaryPatchManifest.BuildDetail();
            if (degradedHooks.Length > 0)
            {
                Log.Warning("[Pawn Diary] Hook problems (a RimWorld update likely changed these "
                    + "targets; the listed fallbacks are active): " + degradedHooks);
            }
        }

        /// <summary>
        /// Re-applies the tab registration once from live UI paths. Static constructors can run before
        /// every modded ThingDef has its final resolved tab list; this keeps registration reliable
        /// without scanning all defs every frame.
        /// </summary>
        public static void EnsureDiaryTabInjected()
        {
            if (postStartupInjectionDone)
            {
                return;
            }

            postStartupInjectionDone = true;
            try
            {
                InjectDiaryTab();
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] Diary tab reinjection failed: " + e);
            }
        }

        /// <summary>
        /// Patches each [HarmonyPatch]-annotated class independently so one failing target cannot
        /// prevent later patches from registering. A single <c>harmony.PatchAll()</c> call would abort
        /// on the first bad attribute patch and leave the rest of the mod's hooks unregistered.
        /// </summary>
        private static void PatchAllSafely(Harmony harmony)
        {
            Type[] types;
            try
            {
                types = typeof(DiaryModStartup).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
                Log.Warning("[Pawn Diary] Harmony patch type scan was partial; attempting available types. " + e);
                DiaryPatchManifest.Report(
                    "startup",
                    "assembly type scan",
                    DiaryPatchManifest.HookStatus.Degraded,
                    "partial ReflectionTypeLoadException; available patch classes attempted");
            }
            catch (Exception e)
            {
                Log.Error("[Pawn Diary] Harmony patch type scan failed: " + e);
                DiaryPatchManifest.Report(
                    "startup",
                    "assembly type scan",
                    DiaryPatchManifest.HookStatus.Failed,
                    e.GetType().Name + ": " + e.Message);
                return;
            }

            foreach (Type type in types)
            {
                if (!HasHarmonyPatch(type))
                {
                    continue;
                }

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    DiaryPatchManifest.Report(
                        "attribute",
                        type.Name,
                        DiaryPatchManifest.HookStatus.Applied);
                }
                catch (Exception e)
                {
                    Log.Error("[Pawn Diary] Harmony patch class failed (" + type.FullName + "): " + e);
                    DiaryPatchManifest.Report(
                        "attribute",
                        type.Name,
                        DiaryPatchManifest.HookStatus.Failed,
                        e.GetType().Name + ": " + e.Message);
                }
            }
        }

        /// <summary>
        /// Returns true when a type carries a class-level [HarmonyPatch] attribute or any method-level
        /// [HarmonyPatch] attribute, matching what <c>harmony.PatchAll()</c> would discover.
        /// </summary>
        private static bool HasHarmonyPatch(Type type)
        {
            if (type == null || !type.IsClass)
            {
                return false;
            }

            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
            {
                return true;
            }

            // Method-level [HarmonyPatch] attributes also drive PatchAll discovery for otherwise-unmarked
            // classes (e.g. a plain static class whose methods each name their own target).
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                {
                    return true;
                }
            }

            return false;
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
