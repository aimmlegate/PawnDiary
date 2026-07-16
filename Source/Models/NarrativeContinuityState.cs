// Scribe-friendly storage for the small Narrative Continuity payload attached to a diary POV. The
// pure NarrativeEvidence/NarrativeReference contracts deliberately do not know about Verse, so this
// file converts between those plain DTOs and additive save fields. Hot events retain evidence,
// references, selected keys, and frozen prompt context; archive rows retain only references and keys.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "DLC-safety").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>Saved representation of one explicitly POV-safe narrative evidence record.</summary>
    internal sealed class NarrativeEvidenceState : IExposable
    {
        public string eventId = string.Empty;
        public int tick;
        public string povPawnId = string.Empty;
        public string povRole = string.Empty;
        public string facet = string.Empty;
        public string phase = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string subjectLabel = string.Empty;
        public string arcKey = string.Empty;
        public string relatedEventId = string.Empty;
        public List<string> beliefTopics = new List<string>();
        public string salience = NarrativeSalienceTokens.Minor;
        // Evidence reaches this state only after the source has authorized this POV. A plain bool is
        // sufficient on disk: false (including a missing old-save key) fails closed on normalization.
        public bool pawnCanKnow;
        public string sourceDomain = string.Empty;
        public string sourceDefName = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref povPawnId, "povPawnId");
            Scribe_Values.Look(ref povRole, "povRole");
            Scribe_Values.Look(ref facet, "facet");
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref subjectKind, "subjectKind");
            Scribe_Values.Look(ref subjectId, "subjectId");
            Scribe_Values.Look(ref subjectLabel, "subjectLabel");
            Scribe_Values.Look(ref arcKey, "arcKey");
            Scribe_Values.Look(ref relatedEventId, "relatedEventId");
            Scribe_Collections.Look(ref beliefTopics, "beliefTopics", LookMode.Value);
            // Missing salience (an older save, or a hand-edited row) loads as the mildest tier instead
            // of null, so the field defends itself even before NormalizeEvidence maps unknown tokens.
            Scribe_Values.Look(ref salience, "salience", NarrativeSalienceTokens.Minor);
            Scribe_Values.Look(ref pawnCanKnow, "pawnCanKnow", false);
            Scribe_Values.Look(ref sourceDomain, "sourceDomain");
            Scribe_Values.Look(ref sourceDefName, "sourceDefName");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                eventId = Clean(eventId);
                povPawnId = Clean(povPawnId);
                povRole = Clean(povRole);
                facet = Clean(facet);
                phase = Clean(phase);
                subjectKind = Clean(subjectKind);
                subjectId = Clean(subjectId);
                subjectLabel = Clean(subjectLabel);
                arcKey = Clean(arcKey);
                relatedEventId = Clean(relatedEventId);
                salience = Clean(salience);
                sourceDomain = Clean(sourceDomain);
                sourceDefName = Clean(sourceDefName);
                if (beliefTopics == null)
                {
                    beliefTopics = new List<string>();
                }
            }
        }

        internal NarrativeEvidence ToContract()
        {
            return new NarrativeEvidence
            {
                eventId = eventId,
                tick = tick,
                povPawnId = povPawnId,
                povRole = povRole,
                facet = facet,
                phase = phase,
                subjectKind = subjectKind,
                subjectId = subjectId,
                subjectLabel = subjectLabel,
                arcKey = arcKey,
                relatedEventId = relatedEventId,
                beliefTopics = beliefTopics == null ? new List<string>() : new List<string>(beliefTopics),
                salience = salience,
                pawnCanKnow = pawnCanKnow,
                sourceDomain = sourceDomain,
                sourceDefName = sourceDefName
            };
        }

        internal static NarrativeEvidenceState FromContract(NarrativeEvidence evidence)
        {
            if (evidence == null)
            {
                return null;
            }

            return new NarrativeEvidenceState
            {
                eventId = evidence.eventId,
                tick = evidence.tick,
                povPawnId = evidence.povPawnId,
                povRole = evidence.povRole,
                facet = evidence.facet,
                phase = evidence.phase,
                subjectKind = evidence.subjectKind,
                subjectId = evidence.subjectId,
                subjectLabel = evidence.subjectLabel,
                arcKey = evidence.arcKey,
                relatedEventId = evidence.relatedEventId,
                beliefTopics = evidence.beliefTopics == null ? new List<string>() : new List<string>(evidence.beliefTopics),
                salience = evidence.salience,
                pawnCanKnow = evidence.pawnCanKnow == true,
                sourceDomain = evidence.sourceDomain,
                sourceDefName = evidence.sourceDefName
            };
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Saved compact reference used by hot-event and archive continuity indexes.</summary>
    internal sealed class NarrativeReferenceState : IExposable
    {
        public string facet = string.Empty;
        public string phase = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        public string arcKey = string.Empty;
        public string sourceEventId = string.Empty;
        public int sourceTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref facet, "facet");
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref subjectKind, "subjectKind");
            Scribe_Values.Look(ref subjectId, "subjectId");
            Scribe_Values.Look(ref arcKey, "arcKey");
            Scribe_Values.Look(ref sourceEventId, "sourceEventId");
            Scribe_Values.Look(ref sourceTick, "sourceTick");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                facet = Clean(facet);
                phase = Clean(phase);
                subjectKind = Clean(subjectKind);
                subjectId = Clean(subjectId);
                arcKey = Clean(arcKey);
                sourceEventId = Clean(sourceEventId);
            }
        }

        internal NarrativeReference ToContract()
        {
            return new NarrativeReference
            {
                facet = facet,
                phase = phase,
                subjectKind = subjectKind,
                subjectId = subjectId,
                arcKey = arcKey,
                sourceEventId = sourceEventId,
                sourceTick = sourceTick
            };
        }

        internal static NarrativeReferenceState FromContract(NarrativeReference reference)
        {
            if (reference == null)
            {
                return null;
            }

            return new NarrativeReferenceState
            {
                facet = reference.facet,
                phase = reference.phase,
                subjectKind = reference.subjectKind,
                subjectId = reference.subjectId,
                arcKey = reference.arcKey,
                sourceEventId = reference.sourceEventId,
                sourceTick = reference.sourceTick
            };
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>
    /// Converts Narrative Continuity contracts to the Verse-facing save models and applies the same
    /// defensive caps on event-time writes and old/corrupt-save loads.
    /// </summary>
    internal static class NarrativeStatePersistence
    {
        // This is a defensive corrupted-save ceiling, not an authored prompt budget. The selector's
        // XML detail budget governs normal output and never truncates a selected factual unit.
        private const int HardNarrativeContextCharacterCap = 4000;

        internal static List<NarrativeEvidenceState> NormalizeEvidenceStates(
            IList<NarrativeEvidenceState> source,
            string fallbackEventId,
            int fallbackTick,
            string fallbackPawnId,
            string fallbackPovRole,
            int policyCap)
        {
            return FromEvidence(NarrativePersistencePolicy.NormalizeEvidence(
                ToEvidence(source), fallbackEventId, fallbackTick, fallbackPawnId, fallbackPovRole, policyCap));
        }

        internal static List<NarrativeEvidenceState> FromEvidence(IList<NarrativeEvidence> source)
        {
            List<NarrativeEvidenceState> result = new List<NarrativeEvidenceState>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                NarrativeEvidenceState state = NarrativeEvidenceState.FromContract(source[i]);
                if (state != null)
                {
                    result.Add(state);
                }
            }

            return result;
        }

        internal static List<NarrativeEvidence> ToEvidence(IList<NarrativeEvidenceState> source)
        {
            List<NarrativeEvidence> result = new List<NarrativeEvidence>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    result.Add(source[i].ToContract());
                }
            }

            return result;
        }

        internal static List<NarrativeReferenceState> NormalizeReferenceStates(IList<NarrativeReferenceState> source)
        {
            return FromReferences(NarrativePersistencePolicy.NormalizeReferences(ToReferences(source)));
        }

        internal static List<NarrativeReferenceState> FromReferences(IList<NarrativeReference> source)
        {
            List<NarrativeReferenceState> result = new List<NarrativeReferenceState>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                NarrativeReferenceState state = NarrativeReferenceState.FromContract(source[i]);
                if (state != null)
                {
                    result.Add(state);
                }
            }

            return result;
        }

        internal static List<NarrativeReference> ToReferences(IList<NarrativeReferenceState> source)
        {
            List<NarrativeReference> result = new List<NarrativeReference>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    result.Add(source[i].ToContract());
                }
            }

            return result;
        }

        internal static List<string> NormalizeSelectedCandidateKeys(IList<string> source)
        {
            return NarrativePersistencePolicy.NormalizeSelectedCandidateKeys(source);
        }

        internal static string NormalizeNarrativeContext(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            return normalized.Length > HardNarrativeContextCharacterCap ? string.Empty : normalized;
        }
    }
}
