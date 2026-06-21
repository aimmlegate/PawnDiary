// Payload + pure decision for a "vanilla Tale recorded" event (the TaleRecorder.RecordTale hook).
// This is the fifth existing source migrated to the Event Catalog.
//
// Tale is the most complex source migrated so far: a single RecordTale call can branch into five
// different outcomes (Drop / Batched / Pair / Solo / DeathDescription), and the classification
// requires heavy RimWorld introspection (Tale_DoublePawn vs Tale_SinglePawn, DefDatabase lookups
// for batch groups, death-victim role, GameConditionDef duplicate detection). DiaryGameComponent
// pre-computes those impure facts, then this pure Decide chooses the final outcome.
//
// This locks down the load-bearing parts: the TaleDefsCoveredElsewhere skip-list (now a public
// const set, testable) and the base gameContext format (`tale=<defName>; label=…;
// taleClass=…` + optional attachedDef fields). RecordTale keeps the death/batch context builders
// because they pull impure state (DeathContextCache, batch group policy).
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one TaleRecorder event. Filled by DiaryGameComponent.RecordTale from the
    /// live Tale + TaleDef after the impure classification steps run.
    /// </summary>
    public class TaleEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.Tale;

        /// <summary>The tale's defName (e.g. "KilledMan", "Wounded", "DidResearch").</summary>
        public string DefName;

        /// <summary>The first pawn's id (or null for SinglePawn tales where the pawn is missing).
        /// Set by the caller after ExtractTalePawns.</summary>
        public string FirstPawnId;

        /// <summary>The second pawn's id for DoublePawn tales (null otherwise).</summary>
        public string SecondPawnId;

        /// <summary>True if FirstPawn is diary-eligible. Pre-computed by the caller because pawn
        /// eligibility reads RimWorld state. Death-description pawns count as eligible here even
        /// when IsDiaryEligible would return false (post-mortem).</summary>
        public bool FirstEligible;

        /// <summary>True if SecondPawn is diary-eligible (with the same death-description caveat
        /// as FirstEligible).</summary>
        public bool SecondEligible;

        /// <summary>True when the tale's defName is in <see cref="CoveredElsewhere"/>. Pre-computed
        /// by the caller (set lookup) so Decide stays pure.</summary>
        public bool IsCoveredElsewhere;

        /// <summary>True when the tale's defName is ALSO a GameConditionDef — RimWorld records
        /// these as both Tale and GameCondition, and MoodEvent already owns the GameCondition side.
        /// Pre-computed by the caller via DefDatabase lookup.</summary>
        public bool IsGameConditionDuplicate;

        /// <summary>True when the matched Tale group has an enabled per-pawn batch policy. Death
        /// descriptions force this false because they must preserve final chronology.</summary>
        public bool IsBatched;

        /// <summary>True when this Tale should become the pawn's neutral final death page.</summary>
        public bool IsDeathDescription;

        /// <summary>
        /// TaleDefs whose events are already captured by narrower hooks. Skipping them here avoids
        /// double diary entries for one social fight or mental break. Mirrored from the pre-refactor
        /// private TaleDefsCoveredElsewhere set unchanged. Public + const so tests can lock it.
        /// </summary>
        public static readonly HashSet<string> CoveredElsewhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SocialFight",
            "MentalStateBerserk",
            "MentalStateGaveUp",
        };

        /// <summary>
        /// Pure decision for a Tale event. Returns Drop when ANY of:
        ///   - the defName is covered by a narrower hook (covered-elsewhere),
        ///   - the defName is also a GameConditionDef (MoodEvent owns it),
        ///   - the user disabled this signal/tale,
        ///   - neither participant is eligible.
        /// Otherwise returns the final shape the impure sink should execute.
        /// </summary>
        public static CaptureDecision Decide(TaleEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (data.IsCoveredElsewhere || data.IsGameConditionDuplicate)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.SignalEnabled || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!data.FirstEligible && !data.SecondEligible)
            {
                return CaptureDecision.Drop;
            }

            if (data.IsDeathDescription)
            {
                return HasEligibleDistinctPair(data)
                    ? CaptureDecision.GeneratePairDeathDescription
                    : CaptureDecision.GenerateSoloDeathDescription;
            }

            if (data.IsBatched)
            {
                return CaptureDecision.RouteBatch;
            }

            if (HasEligibleDistinctPair(data))
            {
                return CaptureDecision.GeneratePair;
            }

            return CaptureDecision.GenerateSolo;
        }

        private static bool HasEligibleDistinctPair(TaleEventData data)
        {
            return data.FirstEligible
                && data.SecondEligible
                && !string.IsNullOrEmpty(data.FirstPawnId)
                && !string.IsNullOrEmpty(data.SecondPawnId)
                && !string.Equals(data.FirstPawnId, data.SecondPawnId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Pure assembly of the base tale game-context marker. The leading "tale=" marker is load-
        /// bearing: the UI parses it to classify the event into the Tale domain, and the LLM reads
        /// the rest as prompt evidence. The optional attachedDef fields are emitted only when the
        /// caller has one (research project, skill, damage type, crafted object kind, ...).
        ///
        /// RecordTale additionally appends death-description fields via AppendDeathDescriptionContext
        /// when applicable; that logic stays impure because it reads DeathContextCache. Batched
        /// tales use a different builder (BuildTaleBatchGameContext in DiaryGameComponent.TaleBatching).
        /// </summary>
        public static string BuildGameContext(
            string defName, string cleanedLabel, string taleClassName,
            string attachedDefName, string cleanedAttachedLabel)
        {
            List<string> parts = new List<string>
            {
                "tale=" + defName,
                "label=" + cleanedLabel,
                "taleClass=" + taleClassName,
            };

            if (!string.IsNullOrWhiteSpace(attachedDefName))
            {
                parts.Add("attachedDef=" + attachedDefName);
            }

            if (!string.IsNullOrWhiteSpace(cleanedAttachedLabel))
            {
                parts.Add("attachedLabel=" + cleanedAttachedLabel);
            }

            return string.Join("; ", parts.ToArray());
        }
    }
}
