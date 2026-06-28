// Generation gates and rule resolution. DiaryGenerationEnabledFor answers "should this POV
// generate at all" (diary enabled, in-bounds, not waiting on the arrival scan). The
// incapacitation helpers skip first-person generation for pawns below the XML-tuned Consciousness
// floor. PersonaRuleFor / PromptEnchantmentRuleFor / HumorCueFor resolve the per-POV writing-style,
// enchantment, and humor text the prompt planner folds in. The live-pawn snapshot/lookup helpers
// here back all of those checks.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Pawns below the XML-tuned Consciousness floor (DiaryTuningDef.minimumConsciousnessForFirstPersonGeneration,
        // default 0.11) should not write first-person entries. Events still record, and neutral
        // death/arrival descriptions still generate, but non-neutral LLM work waits until the pawn
        // is conscious enough again. Read via DiaryTuning.Current so it can be retuned in XML.

        /// <summary>
        /// Checks whether the pawn for a given POV role in an event has diary generation enabled,
        /// resolving the pawn via its saved diary record.
        /// </summary>
        private bool DiaryGenerationEnabledFor(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = null)
        {
            // DiaryEvents store pawn IDs, not Pawn instances, so queue-time checks resolve through
            // the saved diary record for that POV.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return true;
            }

            if (initialArrivalScanPending && !diaryEvent.HasArrivalDescription())
            {
                return false;
            }

            if (EventFallsOutsideDiaryBoundsForPawn(diaryEvent, pawnId, boundsCache))
            {
                return false;
            }

            return FindDiaryByPawnId(pawnId)?.diaryGenerationEnabled ?? true;
        }

        /// <summary>
        /// Returns false only for a live pawn whose Consciousness capacity is below the hard
        /// first-person generation floor. Missing pawn/capacity data is treated as allowed so
        /// off-map or unusual saves do not permanently strand queued work.
        /// </summary>
        private static bool PawnConsciousEnoughForGeneration(Pawn pawn)
        {
            if (pawn?.health?.capacities == null || PawnCapacityDefOf.Consciousness == null)
            {
                return true;
            }

            return pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness)
                >= DiaryTuning.Current.minimumConsciousnessForFirstPersonGeneration;
        }

        /// <summary>
        /// Marks a non-neutral POV as skipped when its live pawn is below the Consciousness floor.
        /// Returns true when generation should stop for this POV.
        /// </summary>
        private bool TryMarkIncapacitatedPovSkipped(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null
                || !DiaryEvent.RoleIsInitiatorOrRecipient(povRole)
                || !diaryEvent.CanQueueGeneration(povRole))
            {
                return false;
            }

            Pawn pawn = FindLivePawnByLoadId(PawnIdForRole(diaryEvent, povRole), livePawnsById);
            if (!ShouldSkipFirstPersonGenerationForIncapacitation(pawn))
            {
                return false;
            }

            diaryEvent.MarkSkipped(povRole, IncapacitatedSkipReason());
            return true;
        }

        /// <summary>
        /// Shared event-factory hook: skips first-person generation immediately when the event is
        /// recorded for a pawn who is already too incapacitated to write.
        /// </summary>
        private static void MarkIncapacitatedPovSkipped(DiaryEvent diaryEvent, string povRole, Pawn pawn)
        {
            if (diaryEvent == null || !DiaryEvent.RoleIsInitiatorOrRecipient(povRole))
            {
                return;
            }

            if (ShouldSkipFirstPersonGenerationForIncapacitation(pawn))
            {
                diaryEvent.MarkSkipped(povRole, IncapacitatedSkipReason());
            }
        }

        private static bool ShouldSkipFirstPersonGenerationForIncapacitation(Pawn pawn)
        {
            return pawn != null && !PawnConsciousEnoughForGeneration(pawn);
        }

        private static string IncapacitatedSkipReason()
        {
            return "PawnDiary.Error.SkippedIncapacitated".Translate(
                Mathf.RoundToInt(DiaryTuning.Current.minimumConsciousnessForFirstPersonGeneration * 100f)).Resolve();
        }

        /// <summary>
        /// Resolves the LLM writing-style rule string for a given POV in an event, falling back to the XML default.
        /// Active hediff override rules may temporarily replace the saved style while the condition is present.
        /// </summary>
        private string PersonaRuleFor(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            // Missing records fall back to the XML default writing style.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            Pawn pawn = FindLivePawnByLoadId(pawnId, livePawnsById);
            return HediffPersonaOverrides.RuleFor(pawn, diary?.personaDefName);
        }

        /// <summary>
        /// Resolves the optional live prompt enchantment for the POV pawn. Missing live pawn data
        /// simply means no enchantment, preserving neutral death/arrival and title flows. Dev prompt
        /// suite fixtures are deliberately isolated from live health/event-window state so one manual
        /// test (for example metalhorror suspicion) cannot bleed into later captured prompt fixtures.
        /// </summary>
        private string PromptEnchantmentRuleFor(DiaryEvent diaryEvent, string povRole,
            Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent != null && DiaryContextFields.HasMarker(diaryEvent.gameContext, DevPromptSuiteMarkerKey))
            {
                return string.Empty;
            }

            if (!DiaryPromptBuilder.ShouldResolvePromptEnchantment(diaryEvent))
            {
                return string.Empty;
            }

            string pawnId = PawnIdForRole(diaryEvent, povRole);
            Pawn pawn = FindLivePawnByLoadId(pawnId, livePawnsById);
            float normalCandidateWeightMultiplier;
            List<PromptEnchantmentCandidate> eventWindowCandidates =
                ActiveEventWindowPromptCandidates(pawn, out normalCandidateWeightMultiplier);
            return PromptEnchantments.RuleFor(
                pawn,
                diaryEvent != null && diaryEvent.IsImportant(),
                eventWindowCandidates,
                normalCandidateWeightMultiplier);
        }

        /// <summary>
        /// Resolves the optional per-entry humor cue for an event. Reuses the same
        /// <see cref="DiaryPromptBuilder.ShouldResolvePromptEnchantment"/> gate as prompt
        /// enchantments, so humor only applies to first-person templates that also allow persona and
        /// enchantment text — neutral death/arrival/title prompts stay humor-free. The cue needs only
        /// the event (no live pawn), so it works even when the pawn is offline.
        /// </summary>
        private string HumorCueFor(DiaryEvent diaryEvent)
        {
            if (!DiaryPromptBuilder.ShouldResolvePromptEnchantment(diaryEvent))
            {
                return string.Empty;
            }

            return HumorCues.CueFor(diaryEvent);
        }

        private static Dictionary<string, Pawn> SnapshotLivePawnsByLoadId()
        {
            Dictionary<string, Pawn> pawnsById = new Dictionary<string, Pawn>();
            if (Find.Maps != null)
            {
                for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
                {
                    Map map = Find.Maps[mapIndex];
                    if (map?.mapPawns?.AllPawns == null)
                    {
                        continue;
                    }

                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        AddLivePawnToSnapshot(pawnsById, pawn);
                    }
                }
            }

            if (Find.WorldPawns?.AllPawnsAlive != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    AddLivePawnToSnapshot(pawnsById, pawn);
                }
            }

            return pawnsById;
        }

        private static void AddLivePawnToSnapshot(Dictionary<string, Pawn> pawnsById, Pawn pawn)
        {
            if (pawnsById == null || pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (!string.IsNullOrWhiteSpace(pawnId) && !pawnsById.ContainsKey(pawnId))
            {
                pawnsById[pawnId] = pawn;
            }
        }

        private static Pawn FindLivePawnByLoadId(string pawnId, Dictionary<string, Pawn> livePawnsById)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            if (livePawnsById != null)
            {
                Pawn pawn;
                return livePawnsById.TryGetValue(pawnId, out pawn) ? pawn : null;
            }

            return FindLivePawnByLoadId(pawnId);
        }

        /// <summary>
        /// Finds a currently loaded pawn by RimWorld's stable unique load ID. Diary events save IDs,
        /// not Pawn references, so prompt-time state checks need this small lookup.
        /// </summary>
        private static Pawn FindLivePawnByLoadId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            if (Find.Maps != null)
            {
                for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
                {
                    Map map = Find.Maps[mapIndex];
                    if (map?.mapPawns?.AllPawns == null)
                    {
                        continue;
                    }

                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn != null && pawn.GetUniqueLoadID() == pawnId)
                        {
                            return pawn;
                        }
                    }
                }
            }

            if (Find.WorldPawns?.AllPawnsAlive != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && pawn.GetUniqueLoadID() == pawnId)
                    {
                        return pawn;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the pawn ID for a given POV role in a DiaryEvent (initiator or recipient).
        /// </summary>
        private static string PawnIdForRole(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            if (string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnId;
            }

            if (string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.initiatorPawnId;
            }

            return null;
        }
    }
}
