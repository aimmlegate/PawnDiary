// The VSIE gathering bridge: the one piece of this adapter that needs C#. VSIE's group gatherings
// (birthday parties, funerals) are a separate system from interactions/thoughts/relations — they emit
// no InteractionDef or TaleDef, so the pure-XML DiaryInteractionGroupDefs in 1.6/Defs/ cannot see them.
// This file hooks the BASE-GAME choke point every gathering flows through and forwards the two
// colony-important ones to Pawn Diary's public integration API as External events.
//
// Startup + settings live in VsieBridgeMod.cs; this file is only the Harmony patch. The mod constructor
// installs it (guarded on VSIE being active), so this class is only ever reached with VSIE present.
//
// Why this is safe when VSIE is absent (AGENTS.md "DLC-safety" pattern):
//   * We patch RimWorld.GatheringWorker.TryExecute — a BASE-GAME method, always present — never a VSIE
//     type. So there is no TypeLoadException risk and no assembly reference to VSIE.
//   * We match __instance.def.defName as a plain STRING (VsieGatheringMap). Without VSIE those defNames
//     never appear, so even if the patch were installed it would find no match.
//   * The mod constructor skips PatchAll unless VSIE is active, so with VSIE absent we never touch the
//     gathering path at all.
// Verified against the installed VSIE 1.6 source on 2026-07-11: neither GatheringWorker_Funeral nor
// GatheringWorker_BirthdayParty overrides TryExecute (they override CreateLordJob/SendLetter/CanExecute),
// so a Postfix on the base method fires for both.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo ("Harmony patches", "Optional-mod hooks").
using System;
using HarmonyLib;
using PawnDiary.Integration;
using PawnDiaryVsie.Pure;
using RimWorld;
using Verse;

namespace PawnDiaryVsie
{
    /// <summary>
    /// Observes the base-game gathering launcher and forwards VSIE birthdays/funerals to Pawn Diary.
    /// We only READ the gathering; we never change how RimWorld or VSIE run it.
    /// </summary>
    [HarmonyPatch(typeof(GatheringWorker), nameof(GatheringWorker.TryExecute))]
    public static class GatheringWorkerTryExecutePatch
    {
        /// <summary>
        /// Harmony Postfix for <c>GatheringWorker.TryExecute</c>. Fires for every gathering (vanilla and
        /// VSIE) because VSIE's funeral/birthday workers do not override TryExecute; the defName filter
        /// keeps us to the two we mean to capture, and <paramref name="__result"/> gates us to gatherings
        /// that actually started.
        /// </summary>
        public static void Postfix(GatheringWorker __instance, Pawn organizer, bool __result)
        {
            // A postfix with no ref/out parameters can be wrapped directly; on any failure we log once and
            // let the game's gathering flow continue untouched.
            try
            {
                if (!__result || __instance == null || __instance.def == null)
                {
                    return;
                }

                VsieGatheringPlan plan = VsieGatheringMap.Plan(__instance.def.defName);
                if (plan == null)
                {
                    return;
                }

                // Player opt-out: the adapter's own settings decide whether this gathering type records.
                VsieBridgeSettings settings = VsieBridgeMod.Settings;
                if (settings != null && !settings.AllowsEventKey(plan.EventKey))
                {
                    return;
                }

                // The organizer is the living colonist the gathering is built around (VSIE's SendLetter
                // uses them too). If a gathering somehow launched without one, we have no clean
                // point-of-view subject, so we skip rather than guess — the attendees' feelings still
                // reach the diary through the vsie_thoughts group. Pawn Diary re-validates eligibility.
                if (organizer == null)
                {
                    return;
                }

                ExternalEventRequest request = new ExternalEventRequest
                {
                    sourceId = VsieBridgeMod.SourceId,
                    eventKey = plan.EventKey,
                    subject = organizer,
                    // Localized on the main thread (gatherings run in the game sim), so .Translate() is safe.
                    eventLabel = plan.LabelKey.Translate().Resolve(),
                    summaryText = plan.SummaryKey.Translate(organizer.LabelShort).Resolve(),
                };

                // forceRecord stays false on purpose: gatherings are rare, so respecting the player's
                // external-event budget and dedup window is the correct production default. (Repeated
                // funerals for one organizer collapsing into a single "we buried our dead" entry within
                // the dedup window is desirable, not a bug.)
                PawnDiaryApi.SubmitEvent(request);
            }
            catch (Exception e)
            {
                Log.ErrorOnce(
                    VsieBridgeMod.LogPrefix + " failed while recording a VSIE gathering: " + e,
                    "PawnDiaryVsie.Gathering.Exception".GetHashCode());
            }
        }
    }
}
