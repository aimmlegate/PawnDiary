// Pure formatting for the optional Memory prompt field (design/MEMORY_SYSTEM_DESIGN.md §9.6),
// mirroring NarrativeContextPrompt step for step. The recalled memory lines are already selected
// and frozen before they reach this class; the policy-owned instruction is simply joined without
// inventing or truncating any memory. Empty input yields an empty field value, which the prompt
// assembler drops — an unused memory field costs zero tokens.
//
// New to C#/RimWorld? See AGENTS.md ("localization" and "architecture barriers").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable writing-format tokens used by Recall v2's four-state projection.</summary>
    internal static class MemoryRecallWritingFormats
    {
        public const string Full = "Full";
        public const string Balanced = "Balanced";
        public const string Compact = "Compact";
        public const string Off = "Off";

        /// <summary>True only for Full, Balanced, Compact, or Off.</summary>
        public static bool IsKnown(string value)
        {
            return value == Full || value == Balanced || value == Compact || value == Off;
        }
    }

    /// <summary>Stable bounded provenance kinds emitted for one Recall v2 prompt line.</summary>
    internal static class MemoryRecallDiagnosticKinds
    {
        public const string EpisodicMemory = "episodic_memory";
        public const string CurrentState = "current_state";

        /// <summary>True only for one provenance kind emitted by the M3 recall projector.</summary>
        public static bool IsKnown(string value)
        {
            return value == EpisodicMemory || value == CurrentState;
        }
    }

    /// <summary>
    /// One selected episodic line plus a separate replaceable-current-state line and exact M2
    /// receipt/diagnostic provenance. The two text fields are never concatenated upstream.
    /// </summary>
    internal sealed class MemoryRecallPromptLine
    {
        public string historicalText = string.Empty;
        public string currentStateText = string.Empty;
        public MemoryEvidenceIdentity evidence;
        public List<MemoryGuardIdentity> guards = new List<MemoryGuardIdentity>();
        public List<MemoryDiagnosticIdentity> diagnostics = new List<MemoryDiagnosticIdentity>();
    }

    /// <summary>Bounded prompt text and the exact provenance rows actually present in it.</summary>
    internal sealed class MemoryRecallPromptProjection
    {
        public string text = string.Empty;
        public List<MemoryRecallPromptLine> lines = new List<MemoryRecallPromptLine>();
        public List<MemoryEvidenceIdentity> evidence = new List<MemoryEvidenceIdentity>();
        public List<MemoryGuardIdentity> guards = new List<MemoryGuardIdentity>();
        public List<MemoryDiagnosticIdentity> diagnostics = new List<MemoryDiagnosticIdentity>();
    }

    /// <summary>Stable prompt source token and pure formatter for the optional memory-context field.</summary>
    internal static class MemoryContextPrompt
    {
        // This is a structured prompt-schema token, intentionally English and stable across locales.
        public const string Source = "MemoryContext";

        /// <summary>
        /// Returns no field value for an empty recalled context; otherwise prefixes the
        /// XML/DefInjected usage instruction when supplied. The recalled memory lines remain
        /// complete and in order (direct first, associative second).
        /// </summary>
        public static string Compose(string memoryContext, string instruction)
        {
            string memories = Trim(memoryContext);
            if (memories.Length == 0)
            {
                return string.Empty;
            }

            string guidance = Trim(instruction);
            return guidance.Length == 0 ? memories : guidance + "\n" + memories;
        }

        /// <summary>Returns Recall v2's hard episodic-line cap for one writing format.</summary>
        public static int MaximumLines(string writingFormat)
        {
            if (writingFormat == MemoryRecallWritingFormats.Full) return 1;
            if (writingFormat == MemoryRecallWritingFormats.Balanced) return 1;
            return 0;
        }

        /// <summary>
        /// Returns the independent second column of §10.3's format matrix. Pawn background is a
        /// separate bounded authored field, so its own switch controls Full/Balanced eligibility and
        /// episodic-memory enablement or line capacity never enters this decision.
        /// </summary>
        public static bool AllowsPawnBackground(string writingFormat, bool usePawnBackground)
        {
            if (!usePawnBackground) return false;
            return writingFormat == MemoryRecallWritingFormats.Full
                || writingFormat == MemoryRecallWritingFormats.Balanced;
        }

        /// <summary>
        /// Projects already selected lines into a frozen prompt field. Full/Balanced/Compact/Off are
        /// hard-capped at 1/1/0/0. Character/evidence/guard/diagnostic overflow drops a complete tail
        /// line; no fact is truncated, no guard is omitted from an emitted line, and an instruction
        /// without any surviving memory is never emitted by itself.
        /// </summary>
        public static MemoryRecallPromptProjection ProjectV2(
            string writingFormat,
            string instruction,
            string currentStateInstruction,
            List<MemoryRecallPromptLine> selectedLines,
            int maximumCharacters,
            int maximumEvidenceEntries,
            int maximumGuardEntries,
            int maximumDiagnosticEntries)
        {
            MemoryRecallPromptProjection result = new MemoryRecallPromptProjection();
            int lineCap = MaximumLines(writingFormat);
            if (lineCap == 0
                || maximumCharacters <= 0
                || maximumEvidenceEntries <= 0
                || maximumGuardEntries <= 0
                || maximumDiagnosticEntries <= 0)
            {
                return result;
            }

            HashSet<string> evidenceKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> guardKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                selectedLines != null
                && index < selectedLines.Count
                && result.lines.Count < lineCap;
                index++)
            {
                MemoryRecallPromptLine candidate = NormalizeLine(selectedLines[index]);
                if (!ValidLine(candidate)) continue;

                string evidenceKey = EvidenceTuple(candidate.evidence);
                if (evidenceKeys.Contains(evidenceKey)
                    || result.evidence.Count + 1 > maximumEvidenceEntries)
                {
                    continue;
                }

                List<MemoryGuardIdentity> newGuards = NewGuards(candidate.guards, guardKeys);
                if (newGuards == null
                    || result.guards.Count + newGuards.Count > maximumGuardEntries
                    || result.diagnostics.Count + candidate.diagnostics.Count
                        > maximumDiagnosticEntries)
                {
                    continue;
                }

                result.lines.Add(candidate);
                string projected = BuildProjectionText(
                    instruction,
                    currentStateInstruction,
                    result.lines);
                if (projected.Length > maximumCharacters)
                {
                    // Ranking order is authoritative: over-budget tail lines are dropped whole.
                    result.lines.RemoveAt(result.lines.Count - 1);
                    break;
                }

                evidenceKeys.Add(evidenceKey);
                result.evidence.Add(CopyEvidence(candidate.evidence));
                for (int guardIndex = 0; guardIndex < newGuards.Count; guardIndex++)
                {
                    MemoryGuardIdentity guard = newGuards[guardIndex];
                    guardKeys.Add(GuardTuple(guard));
                    result.guards.Add(guard);
                }
                for (int diagnosticIndex = 0;
                    diagnosticIndex < candidate.diagnostics.Count;
                    diagnosticIndex++)
                {
                    MemoryDiagnosticIdentity diagnostic =
                        CopyDiagnostic(candidate.diagnostics[diagnosticIndex]);
                    diagnostic.lineOrdinal = result.lines.Count - 1;
                    result.diagnostics.Add(diagnostic);
                }
            }

            if (result.lines.Count == 0) return new MemoryRecallPromptProjection();
            result.guards.Sort(CompareGuards);
            result.diagnostics.Sort(CompareDiagnostics);
            result.text = BuildProjectionText(
                instruction,
                currentStateInstruction,
                result.lines);
            return result;
        }

        private static MemoryRecallPromptLine NormalizeLine(MemoryRecallPromptLine source)
        {
            if (source == null) return null;
            MemoryRecallPromptLine copy = new MemoryRecallPromptLine
            {
                historicalText = PromptTextSanitizer.OneLine(source.historicalText),
                currentStateText = PromptTextSanitizer.OneLine(source.currentStateText),
                evidence = CopyEvidence(source.evidence)
            };
            for (int index = 0; source.guards != null && index < source.guards.Count; index++)
            {
                MemoryGuardIdentity guard = source.guards[index];
                copy.guards.Add(guard == null ? null : new MemoryGuardIdentity
                {
                    guardKind = guard.guardKind ?? string.Empty,
                    guardKey = guard.guardKey ?? string.Empty
                });
            }
            for (int index = 0;
                source.diagnostics != null && index < source.diagnostics.Count;
                index++)
            {
                copy.diagnostics.Add(CopyDiagnostic(source.diagnostics[index]));
            }
            return copy;
        }

        private static bool ValidLine(MemoryRecallPromptLine line)
        {
            if (line == null
                || string.IsNullOrWhiteSpace(line.historicalText)
                || line.evidence == null
                || !ValidCompositeIdentity(line.evidence.recordId)
                || !ValidCompositeIdentity(line.evidence.sourceOccurrenceId)
                || !ValidOptionalCompositeIdentity(line.evidence.rootIdOrEmpty)
                || line.guards == null
                || line.guards.Count == 0
                || line.diagnostics == null
                || line.diagnostics.Count == 0)
            {
                return false;
            }


            HashSet<string> localGuards = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < line.guards.Count; index++)
            {
                MemoryGuardIdentity guard = line.guards[index];
                if (guard == null
                    || !MemoryRepetitionGuardPolicy.IsCanonicalIdentity(
                        guard.guardKind,
                        guard.guardKey)
                    || !localGuards.Add(GuardTuple(guard))) return false;
            }

            bool foundEpisodic = false;
            bool foundCurrent = false;
            HashSet<string> localDiagnostics = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < line.diagnostics.Count; index++)
            {
                MemoryDiagnosticIdentity diagnostic = line.diagnostics[index];
                if (diagnostic == null
                    || !MemoryRecallDiagnosticKinds.IsKnown(diagnostic.provenanceKindToken)
                    || !ValidCompositeIdentity(diagnostic.sourceId)
                    || !ValidOptionalCompositeIdentity(diagnostic.recordIdOrEmpty)
                    || !ValidOptionalCompositeIdentity(diagnostic.sourceOccurrenceIdOrEmpty)
                    || !ValidOptionalCompositeIdentity(diagnostic.rootIdOrEmpty)
                    || !localDiagnostics.Add(DiagnosticTuple(diagnostic))) return false;

                bool matchesEvidence = string.Equals(
                        diagnostic.recordIdOrEmpty,
                        line.evidence.recordId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        diagnostic.sourceOccurrenceIdOrEmpty,
                        line.evidence.sourceOccurrenceId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        diagnostic.rootIdOrEmpty ?? string.Empty,
                        line.evidence.rootIdOrEmpty ?? string.Empty,
                        StringComparison.Ordinal);
                if (diagnostic.provenanceKindToken
                    == MemoryRecallDiagnosticKinds.EpisodicMemory)
                {
                    if (foundEpisodic || !matchesEvidence
                        || !string.Equals(
                            diagnostic.sourceId,
                            line.evidence.sourceOccurrenceId,
                            StringComparison.Ordinal)) return false;
                    foundEpisodic = true;
                }
                else if (diagnostic.provenanceKindToken
                    == MemoryRecallDiagnosticKinds.CurrentState)
                {
                    if (foundCurrent || !matchesEvidence) return false;
                    foundCurrent = true;
                }
            }
            return foundEpisodic
                && foundCurrent == !string.IsNullOrWhiteSpace(line.currentStateText);
        }

        private static List<MemoryGuardIdentity> NewGuards(
            List<MemoryGuardIdentity> source,
            HashSet<string> existing)
        {
            List<MemoryGuardIdentity> result = new List<MemoryGuardIdentity>();
            HashSet<string> local = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < source.Count; index++)
            {
                MemoryGuardIdentity guard = source[index];
                string tuple = GuardTuple(guard);
                if (!local.Add(tuple)) return null;
                if (!existing.Contains(tuple))
                {
                    result.Add(new MemoryGuardIdentity
                    {
                        guardKind = guard.guardKind,
                        guardKey = guard.guardKey
                    });
                }
            }
            return result;
        }

        private static string BuildProjectionText(
            string instruction,
            string currentStateInstruction,
            List<MemoryRecallPromptLine> lines)
        {
            List<string> blocks = new List<string>();
            string memoryInstruction = Trim(instruction);
            if (memoryInstruction.Length > 0) blocks.Add(memoryInstruction);
            string stateInstruction = Trim(currentStateInstruction);
            for (int index = 0; index < lines.Count; index++)
            {
                MemoryRecallPromptLine line = lines[index];
                blocks.Add(line.historicalText);
                if (!string.IsNullOrWhiteSpace(line.currentStateText))
                {
                    if (stateInstruction.Length > 0) blocks.Add(stateInstruction);
                    blocks.Add(line.currentStateText);
                }
            }
            return string.Join("\n", blocks.ToArray());
        }

        private static MemoryEvidenceIdentity CopyEvidence(MemoryEvidenceIdentity source)
        {
            return source == null ? null : new MemoryEvidenceIdentity
            {
                recordId = source.recordId ?? string.Empty,
                sourceOccurrenceId = source.sourceOccurrenceId ?? string.Empty,
                rootIdOrEmpty = source.rootIdOrEmpty ?? string.Empty
            };
        }

        private static MemoryDiagnosticIdentity CopyDiagnostic(MemoryDiagnosticIdentity source)
        {
            return source == null ? null : new MemoryDiagnosticIdentity
            {
                provenanceKindToken = source.provenanceKindToken ?? string.Empty,
                sourceId = source.sourceId ?? string.Empty,
                recordIdOrEmpty = source.recordIdOrEmpty ?? string.Empty,
                sourceOccurrenceIdOrEmpty = source.sourceOccurrenceIdOrEmpty ?? string.Empty,
                rootIdOrEmpty = source.rootIdOrEmpty ?? string.Empty,
                lineOrdinal = source.lineOrdinal
            };
        }

        private static string EvidenceTuple(MemoryEvidenceIdentity evidence)
        {
            return OrdinalSegmentCodec.Segment(evidence.recordId)
                + OrdinalSegmentCodec.Segment(evidence.sourceOccurrenceId)
                + OrdinalSegmentCodec.Segment(evidence.rootIdOrEmpty);
        }

        private static string GuardTuple(MemoryGuardIdentity guard)
        {
            return OrdinalSegmentCodec.Segment(guard.guardKind)
                + OrdinalSegmentCodec.Segment(guard.guardKey);
        }

        private static string DiagnosticTuple(MemoryDiagnosticIdentity diagnostic)
        {
            return OrdinalSegmentCodec.Segment(diagnostic.provenanceKindToken)
                + OrdinalSegmentCodec.Segment(diagnostic.sourceId)
                + OrdinalSegmentCodec.Segment(diagnostic.recordIdOrEmpty)
                + OrdinalSegmentCodec.Segment(diagnostic.sourceOccurrenceIdOrEmpty)
                + OrdinalSegmentCodec.Segment(diagnostic.rootIdOrEmpty);
        }

        private static int CompareGuards(MemoryGuardIdentity left, MemoryGuardIdentity right)
        {
            int kind = string.CompareOrdinal(left.guardKind, right.guardKind);
            return kind != 0 ? kind : string.CompareOrdinal(left.guardKey, right.guardKey);
        }

        private static int CompareDiagnostics(
            MemoryDiagnosticIdentity left,
            MemoryDiagnosticIdentity right)
        {
            int line = left.lineOrdinal.CompareTo(right.lineOrdinal);
            if (line != 0) return line;
            int kind = string.CompareOrdinal(left.provenanceKindToken, right.provenanceKindToken);
            if (kind != 0) return kind;
            int source = string.CompareOrdinal(left.sourceId, right.sourceId);
            if (source != 0) return source;
            int record = string.CompareOrdinal(left.recordIdOrEmpty, right.recordIdOrEmpty);
            if (record != 0) return record;
            int occurrence = string.CompareOrdinal(
                left.sourceOccurrenceIdOrEmpty,
                right.sourceOccurrenceIdOrEmpty);
            return occurrence != 0
                ? occurrence
                : string.CompareOrdinal(left.rootIdOrEmpty, right.rootIdOrEmpty);
        }

        private static bool ValidCompositeIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool ValidOptionalCompositeIdentity(string value)
        {
            return value != null
                && value.Length <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
