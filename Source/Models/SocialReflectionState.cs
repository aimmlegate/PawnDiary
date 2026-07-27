// Saved state for Quality Wave H7's delayed social reflections.
//
// The scheduler never saves live Pawn, InteractionDef, PlayLog, or repository objects. It keeps only
// stable IDs, source facts, frozen timing, and reservation ownership so a save/load cannot reroll the
// reflection or lose its cooldown. The pure admission/timing rules live in
// Source/Pipeline/SocialReflectionPolicy.cs; this file is Verse-facing Scribe plumbing only.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "architecture barriers").
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One accepted interaction waiting for its delayed initiator-side reflection.
    /// </summary>
    internal sealed class PendingSocialReflectionState : IExposable
    {
        public string sourceKey = string.Empty;
        public string sourceEventId = string.Empty;
        public int playLogEntryId = -1;
        public int interactionTick;
        public string interactionDefName = string.Empty;
        public string interactionLabel = string.Empty;
        public string initiatorText = string.Empty;
        public string writerPawnId = string.Empty;
        public string subjectPawnId = string.Empty;
        public string pairKey = string.Empty;
        public string relationSignature = string.Empty;
        public bool sourceProseUnavailable;
        public int acceptedTick;
        public int dueTick;
        public int nextAttemptTick;
        public int retryIntervalTicks;
        public int deadlineTick;

        /// <summary>Scribes the complete frozen schedule and source identity.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref sourceKey, "sourceKey");
            Scribe_Values.Look(ref sourceEventId, "sourceEventId");
            Scribe_Values.Look(ref playLogEntryId, "playLogEntryId", -1);
            Scribe_Values.Look(ref interactionTick, "interactionTick", 0);
            Scribe_Values.Look(ref interactionDefName, "interactionDefName");
            Scribe_Values.Look(ref interactionLabel, "interactionLabel");
            Scribe_Values.Look(ref initiatorText, "initiatorText");
            Scribe_Values.Look(ref writerPawnId, "writerPawnId");
            Scribe_Values.Look(ref subjectPawnId, "subjectPawnId");
            Scribe_Values.Look(ref pairKey, "pairKey");
            Scribe_Values.Look(ref relationSignature, "relationSignature");
            Scribe_Values.Look(ref sourceProseUnavailable, "sourceProseUnavailable", false);
            Scribe_Values.Look(ref acceptedTick, "acceptedTick", 0);
            Scribe_Values.Look(ref dueTick, "dueTick", 0);
            Scribe_Values.Look(ref nextAttemptTick, "nextAttemptTick", 0);
            Scribe_Values.Look(ref retryIntervalTicks, "retryIntervalTicks", 0);
            Scribe_Values.Look(ref deadlineTick, "deadlineTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sourceKey = sourceKey ?? string.Empty;
                sourceEventId = sourceEventId ?? string.Empty;
                interactionDefName = interactionDefName ?? string.Empty;
                interactionLabel = interactionLabel ?? string.Empty;
                initiatorText = initiatorText ?? string.Empty;
                writerPawnId = writerPawnId ?? string.Empty;
                subjectPawnId = subjectPawnId ?? string.Empty;
                pairKey = pairKey ?? string.Empty;
                relationSignature = relationSignature ?? string.Empty;
                interactionTick = ClampTick(interactionTick);
                acceptedTick = ClampTick(acceptedTick);
                dueTick = ClampTick(dueTick);
                nextAttemptTick = ClampTick(nextAttemptTick);
                retryIntervalTicks = ClampTick(retryIntervalTicks);
                deadlineTick = ClampTick(deadlineTick);
            }
        }

        private static int ClampTick(int value)
        {
            return value < 0 ? 0 : value;
        }
    }

    /// <summary>
    /// Saved unordered-pair pacing plus the source that currently owns the reservation.
    /// </summary>
    internal sealed class SocialReflectionPairCooldownState : IExposable
    {
        public string pairKey = string.Empty;
        public string relationSignature = string.Empty;
        public int cooldownUntilTick;
        public string reservationSourceKey = string.Empty;

        /// <summary>Scribes one pair reservation.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref pairKey, "pairKey");
            Scribe_Values.Look(ref relationSignature, "relationSignature");
            Scribe_Values.Look(ref cooldownUntilTick, "cooldownUntilTick", 0);
            Scribe_Values.Look(ref reservationSourceKey, "reservationSourceKey");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pairKey = pairKey ?? string.Empty;
                relationSignature = relationSignature ?? string.Empty;
                reservationSourceKey = reservationSourceKey ?? string.Empty;
                cooldownUntilTick = cooldownUntilTick < 0 ? 0 : cooldownUntilTick;
            }
        }
    }

    /// <summary>
    /// Saved per-writer pacing plus the source that currently owns the reservation.
    /// </summary>
    internal sealed class SocialReflectionWriterCooldownState : IExposable
    {
        public string writerPawnId = string.Empty;
        public int cooldownUntilTick;
        public string reservationSourceKey = string.Empty;

        /// <summary>Scribes one writer reservation.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref writerPawnId, "writerPawnId");
            Scribe_Values.Look(ref cooldownUntilTick, "cooldownUntilTick", 0);
            Scribe_Values.Look(ref reservationSourceKey, "reservationSourceKey");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                writerPawnId = writerPawnId ?? string.Empty;
                reservationSourceKey = reservationSourceKey ?? string.Empty;
                cooldownUntilTick = cooldownUntilTick < 0 ? 0 : cooldownUntilTick;
            }
        }
    }

    /// <summary>
    /// Bounded durable ownership proving one source interaction was accepted for H7.
    /// </summary>
    internal sealed class SocialReflectionHandledSourceState : IExposable
    {
        public string sourceKey = string.Empty;
        public string reflectionEventId = string.Empty;
        public int handledTick;

        /// <summary>Scribes one terminal source owner.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref sourceKey, "sourceKey");
            Scribe_Values.Look(ref reflectionEventId, "reflectionEventId");
            Scribe_Values.Look(ref handledTick, "handledTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sourceKey = sourceKey ?? string.Empty;
                reflectionEventId = reflectionEventId ?? string.Empty;
                handledTick = handledTick < 0 ? 0 : handledTick;
            }
        }
    }
}
