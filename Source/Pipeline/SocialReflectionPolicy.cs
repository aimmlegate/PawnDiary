// PURE admission, sampling, cooldown, relationship, and excerpt policy for delayed social
// reflections. The game-facing scheduler supplies saved rows and live pawn facts; this file makes
// every consequential decision without Verse, Unity, RimWorld, or the shared game RNG.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PawnDiary
{
    /// <summary>One XML-projected probability band keyed by absolute social opinion.</summary>
    public sealed class SocialReflectionChanceBand
    {
        public int minimumAbsoluteOpinion;
        public int maximumAbsoluteOpinion;
        public float chance;
    }

    /// <summary>Frozen prompt tone and reader color for one reflection polarity.</summary>
    public sealed class SocialReflectionStyle
    {
        public string polarity = string.Empty;
        public string tone = string.Empty;
        public string colorCue = string.Empty;
    }

    /// <summary>
    /// Detached, plain-data copy of every H7 tuning value. Defaults keep the feature safe when the
    /// XML Def is missing or malformed.
    /// </summary>
    public sealed class SocialReflectionPolicySnapshot
    {
        public bool enabled = true;
        public int pairCooldownTicks = 900000;
        public int writerCooldownTicks = 180000;
        public int minimumDelayTicks = 15000;
        public int maximumDelayTicks = 60000;
        public int retryIntervalTicks = 2500;
        public int proseGraceTicks = 60000;
        public int maximumPendingRows = 128;
        public int maximumPairCooldownRows = 512;
        public int maximumWriterCooldownRows = 512;
        public int maximumHandledSourceRows = 2048;
        public int maximumProcessedPerTick = 4;
        public int maximumSourceLookupRows = 256;
        public int maximumSourceLabelCharacters = 120;
        public int sourceExcerptSentenceCount = 2;
        public int sourceExcerptMaximumCharacters = 400;
        public int maximumOpinionReasons = 3;
        public int maximumMemoryLines = 2;
        public float veryUnattractiveMaximum = -1.5f;
        public float unattractiveMaximum = -0.5f;
        public float attractiveMinimum = 0.5f;
        public float veryAttractiveMinimum = 1.5f;
        public int negativeOpinionMaximum = -1;
        public int positiveOpinionMinimum = 1;
        // Prompt-visible prose is intentionally empty in code. The impure Def adapter supplies
        // localized DefInjected labels; a missing Def fails soft by omitting the field.
        public string veryUnattractiveLabel = string.Empty;
        public string unattractiveLabel = string.Empty;
        public string ordinaryAppearanceLabel = string.Empty;
        public string attractiveLabel = string.Empty;
        public string veryAttractiveLabel = string.Empty;
        public List<SocialReflectionChanceBand> chanceBands =
            new List<SocialReflectionChanceBand>();
        public List<SocialReflectionStyle> styles = new List<SocialReflectionStyle>();

        /// <summary>Creates the reviewed H7 fallback policy, including all chance and style bands.</summary>
        public static SocialReflectionPolicySnapshot CreateDefault()
        {
            SocialReflectionPolicySnapshot result = new SocialReflectionPolicySnapshot();
            result.chanceBands.Add(new SocialReflectionChanceBand
            {
                minimumAbsoluteOpinion = 0,
                maximumAbsoluteOpinion = 24,
                chance = 0.05f
            });
            result.chanceBands.Add(new SocialReflectionChanceBand
            {
                minimumAbsoluteOpinion = 25,
                maximumAbsoluteOpinion = 49,
                chance = 0.15f
            });
            result.chanceBands.Add(new SocialReflectionChanceBand
            {
                minimumAbsoluteOpinion = 50,
                maximumAbsoluteOpinion = 74,
                chance = 0.35f
            });
            result.chanceBands.Add(new SocialReflectionChanceBand
            {
                minimumAbsoluteOpinion = 75,
                maximumAbsoluteOpinion = 100,
                chance = 0.60f
            });
            result.styles.Add(new SocialReflectionStyle
            {
                polarity = SocialReflectionPolicy.PositivePolarity,
                tone = string.Empty,
                colorCue = "white"
            });
            result.styles.Add(new SocialReflectionStyle
            {
                polarity = SocialReflectionPolicy.NeutralPolarity,
                tone = string.Empty,
                colorCue = "quiet"
            });
            result.styles.Add(new SocialReflectionStyle
            {
                polarity = SocialReflectionPolicy.NegativePolarity,
                tone = string.Empty,
                colorCue = "socialFight"
            });
            return result;
        }
    }

    /// <summary>One directional direct-relation fact used to build a canonical pair signature.</summary>
    public sealed class SocialReflectionRelationFact
    {
        public string ownerPawnId = string.Empty;
        public string otherPawnId = string.Empty;
        public string relationDefName = string.Empty;
    }

    /// <summary>Existing saved state projected into the pure admission decision.</summary>
    public sealed class SocialReflectionAdmissionInput
    {
        public string sourceKey = string.Empty;
        public string writerPawnId = string.Empty;
        public string subjectPawnId = string.Empty;
        public string relationSignature = string.Empty;
        public int nowTick;
        public string pendingSourceKey = string.Empty;
        // Kept separate because the candidate writer can already have a pending row with a third
        // pawn while this unordered pair also has a pending row from the opposite POV.
        public bool writerHasPending;
        public string pendingWriterPawnId = string.Empty;
        public string pendingPairKey = string.Empty;
        public string pairPendingWriterPawnId = string.Empty;
        public string pendingRelationSignature = string.Empty;
        public int writerCooldownUntilTick;
        public int pairCooldownUntilTick;
        public string pairCooldownRelationSignature = string.Empty;
    }

    /// <summary>Pure admission result, including the exact stale state the adapter may replace.</summary>
    public sealed class SocialReflectionAdmissionDecision
    {
        public bool accepted;
        public bool replacePending;
        public bool clearPairCooldown;
        public string pairKey = string.Empty;
        public string rejection = string.Empty;
    }

    /// <summary>Pure H7 policy shared by standalone tests and the game-facing scheduler.</summary>
    public static class SocialReflectionPolicy
    {
        public const string PositivePolarity = "positive";
        public const string NeutralPolarity = "neutral";
        public const string NegativePolarity = "negative";

        // These compact prose abbreviations do not end a sentence when more text follows. Keeping
        // the parser vocabulary here is stable punctuation policy, not player-facing prompt prose.
        private static readonly HashSet<string> NonTerminalPeriodWords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "e.g", "i.e"
            };

        /// <summary>
        /// Stable source identity. Length prefixes keep opaque load IDs and modded defNames from
        /// colliding even when they contain punctuation used by ordinary compound keys.
        /// </summary>
        public static string SourceKey(
            string playLogEntryId,
            int sourceTick,
            string interactionDefName,
            string writerPawnId,
            string subjectPawnId)
        {
            return Segment(Clean(playLogEntryId))
                + Segment(sourceTick.ToString(CultureInfo.InvariantCulture))
                + Segment(Clean(interactionDefName))
                + Segment(Clean(writerPawnId))
                + Segment(Clean(subjectPawnId));
        }

        /// <summary>Returns one order-independent pair key, or empty for missing/equal pawn IDs.</summary>
        public static string PairKey(string firstPawnId, string secondPawnId)
        {
            string first = Clean(firstPawnId);
            string second = Clean(secondPawnId);
            if (first.Length == 0 || second.Length == 0
                || string.Equals(first, second, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return string.CompareOrdinal(first, second) < 0
                ? Segment(first) + Segment(second)
                : Segment(second) + Segment(first);
        }

        /// <summary>
        /// True only when a valid two-segment pair key contains the exact pawn ID. Pair keys are
        /// length-prefixed, not pipe-delimited; parsing their schema prevents Pawn_1/Pawn_10 and
        /// punctuation collisions during a one-pawn Brainwipe cooldown reset.
        /// </summary>
        public static bool PairKeyContainsPawn(string pairKey, string pawnId)
        {
            string wanted = Clean(pawnId);
            if (wanted.Length == 0 || string.IsNullOrEmpty(pairKey)) return false;

            int offset = 0;
            int firstStart;
            int firstLength;
            int secondStart;
            int secondLength;
            if (!TryReadSegment(pairKey, ref offset, out firstStart, out firstLength)
                || !TryReadSegment(pairKey, ref offset, out secondStart, out secondLength)
                || offset != pairKey.Length)
            {
                return false;
            }

            return SegmentValueEquals(pairKey, firstStart, firstLength, wanted)
                || SegmentValueEquals(pairKey, secondStart, secondLength, wanted);
        }

        /// <summary>Finds the configured probability for |opinion|, clamped to the reviewed 0..100 range.</summary>
        public static float ChanceForOpinion(
            int opinion,
            IEnumerable<SocialReflectionChanceBand> bands)
        {
            int absolute = Math.Min(100, Math.Abs(opinion == int.MinValue ? int.MaxValue : opinion));
            if (bands != null)
            {
                foreach (SocialReflectionChanceBand band in bands)
                {
                    if (band == null || absolute < band.minimumAbsoluteOpinion
                        || absolute > band.maximumAbsoluteOpinion)
                    {
                        continue;
                    }

                    return ClampChance(band.chance);
                }
            }

            return 0f;
        }

        /// <summary>
        /// True only when a detached XML chance table is finite, non-overlapping, and covers every
        /// clamped absolute-opinion value from 0 through 100. Invalid tables must retain defaults
        /// rather than silently turning an uncovered opinion range into a zero-percent chance.
        /// </summary>
        public static bool HasCompleteChanceCoverage(
            IEnumerable<SocialReflectionChanceBand> bands)
        {
            if (bands == null)
            {
                return false;
            }

            List<SocialReflectionChanceBand> ordered =
                new List<SocialReflectionChanceBand>();
            foreach (SocialReflectionChanceBand band in bands)
            {
                if (band == null
                    || band.minimumAbsoluteOpinion < 0
                    || band.maximumAbsoluteOpinion < band.minimumAbsoluteOpinion
                    || band.maximumAbsoluteOpinion > 100
                    || float.IsNaN(band.chance)
                    || float.IsInfinity(band.chance)
                    || band.chance < 0f
                    || band.chance > 1f)
                {
                    return false;
                }

                ordered.Add(band);
            }

            ordered.Sort((left, right) =>
                left.minimumAbsoluteOpinion.CompareTo(right.minimumAbsoluteOpinion));
            int expectedMinimum = 0;
            for (int index = 0; index < ordered.Count; index++)
            {
                SocialReflectionChanceBand band = ordered[index];
                if (band.minimumAbsoluteOpinion != expectedMinimum)
                {
                    return false;
                }

                expectedMinimum = band.maximumAbsoluteOpinion + 1;
            }

            return ordered.Count > 0 && expectedMinimum == 101;
        }

        /// <summary>
        /// Produces a culture-independent deterministic unit sample. It deliberately does not touch
        /// Verse.Rand, so diary bookkeeping cannot perturb RimWorld gameplay randomness.
        /// </summary>
        public static double DeterministicRoll(string sourceKey)
        {
            return StableHash(Clean(sourceKey), 2166136261u) / 4294967296d;
        }

        /// <summary>True when the deterministic source sample falls inside its opinion chance band.</summary>
        public static bool PassesChance(
            string sourceKey,
            int opinion,
            IEnumerable<SocialReflectionChanceBand> bands)
        {
            return PassesChance(sourceKey, opinion, bands, 1f);
        }

        /// <summary>
        /// Applies the selected absolute frequency multiplier to the native opinion-band chance while
        /// preserving this source's historical deterministic sample and strict boundary comparison.
        /// </summary>
        public static bool PassesChance(
            string sourceKey,
            int opinion,
            IEnumerable<SocialReflectionChanceBand> bands,
            float frequencyMultiplier)
        {
            float multiplier = float.IsNaN(frequencyMultiplier)
                || float.IsInfinity(frequencyMultiplier)
                ? 1f
                : Math.Max(0f, frequencyMultiplier);
            float chance = ClampChance(ChanceForOpinion(opinion, bands) * multiplier);
            return chance > 0f && DeterministicRoll(sourceKey) < chance;
        }

        /// <summary>Chooses and freezes an inclusive deterministic delay inside the XML-owned window.</summary>
        public static int DelayTicks(string sourceKey, int minimumTicks, int maximumTicks)
        {
            int minimum = Math.Max(0, minimumTicks);
            int maximum = Math.Max(minimum, maximumTicks);
            long width = (long)maximum - minimum + 1L;
            if (width <= 1L)
            {
                return minimum;
            }

            uint hash = StableHash(Clean(sourceKey), 2246822519u);
            return minimum + (int)(hash % (uint)Math.Min(width, uint.MaxValue));
        }

        /// <summary>
        /// Canonical signature of the complete direct-relation defName set in both directions. A changed
        /// signature invalidates only the pair reservation; it does not erase writer pacing.
        /// </summary>
        public static string RelationSignature(
            string firstPawnId,
            string secondPawnId,
            IEnumerable<SocialReflectionRelationFact> facts)
        {
            string pairKey = PairKey(firstPawnId, secondPawnId);
            if (pairKey.Length == 0)
            {
                return string.Empty;
            }

            string first = Clean(firstPawnId);
            string second = Clean(secondPawnId);
            HashSet<string> rows = new HashSet<string>(StringComparer.Ordinal);
            if (facts != null)
            {
                foreach (SocialReflectionRelationFact fact in facts)
                {
                    string owner = Clean(fact == null ? null : fact.ownerPawnId);
                    string other = Clean(fact == null ? null : fact.otherPawnId);
                    string defName = Clean(fact == null ? null : fact.relationDefName);
                    bool connectsPair =
                        (string.Equals(owner, first, StringComparison.Ordinal)
                            && string.Equals(other, second, StringComparison.Ordinal))
                        || (string.Equals(owner, second, StringComparison.Ordinal)
                            && string.Equals(other, first, StringComparison.Ordinal));
                    if (connectsPair && defName.Length > 0)
                    {
                        rows.Add(Segment(owner) + Segment(other) + Segment(defName));
                    }
                }
            }

            List<string> ordered = rows.OrderBy(row => row, StringComparer.Ordinal).ToList();
            return pairKey + (ordered.Count == 0 ? "#none" : "#" + string.Join("#", ordered));
        }

        /// <summary>Maps signed opinion to the three H7 prose styles without exposing the score.</summary>
        public static string PolarityForOpinion(
            int opinion,
            int negativeOpinionMaximum,
            int positiveOpinionMinimum)
        {
            if (opinion <= negativeOpinionMaximum)
            {
                return NegativePolarity;
            }

            if (opinion >= positiveOpinionMinimum)
            {
                return PositivePolarity;
            }

            return NeutralPolarity;
        }

        /// <summary>Finds a detached style for the opinion polarity, with a quiet fail-safe.</summary>
        public static SocialReflectionStyle StyleForOpinion(
            int opinion,
            SocialReflectionPolicySnapshot policy)
        {
            SocialReflectionPolicySnapshot safe = policy ?? SocialReflectionPolicySnapshot.CreateDefault();
            string polarity = PolarityForOpinion(
                opinion,
                safe.negativeOpinionMaximum,
                safe.positiveOpinionMinimum);
            SocialReflectionStyle found = safe.styles == null
                ? null
                : safe.styles.FirstOrDefault(style => style != null
                    && string.Equals(Clean(style.polarity), polarity, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                found = SocialReflectionPolicySnapshot.CreateDefault().styles.First(
                    style => string.Equals(style.polarity, polarity, StringComparison.Ordinal));
            }

            return new SocialReflectionStyle
            {
                polarity = polarity,
                tone = Clean(found.tone),
                colorCue = Clean(found.colorCue)
            };
        }

        /// <summary>Returns the five-way qualitative beauty band index; non-finite values are ordinary.</summary>
        public static int BeautyBandIndex(float beauty, SocialReflectionPolicySnapshot policy)
        {
            SocialReflectionPolicySnapshot safe = policy ?? SocialReflectionPolicySnapshot.CreateDefault();
            if (float.IsNaN(beauty) || float.IsInfinity(beauty))
            {
                return 2;
            }

            if (beauty <= safe.veryUnattractiveMaximum) return 0;
            if (beauty <= safe.unattractiveMaximum) return 1;
            if (beauty < safe.attractiveMinimum) return 2;
            if (beauty < safe.veryAttractiveMinimum) return 3;
            return 4;
        }

        /// <summary>
        /// Applies the one-writer/one-pair rule, writer pacing, relation-sensitive pair pacing, and the
        /// narrow reverse-POV replacement exception approved for a changed relationship.
        /// </summary>
        public static SocialReflectionAdmissionDecision DecideAdmission(
            SocialReflectionAdmissionInput input)
        {
            SocialReflectionAdmissionDecision result = new SocialReflectionAdmissionDecision();
            if (input == null)
            {
                result.rejection = "missing_input";
                return result;
            }

            result.pairKey = PairKey(input.writerPawnId, input.subjectPawnId);
            if (Clean(input.sourceKey).Length == 0 || result.pairKey.Length == 0)
            {
                result.rejection = "invalid_identity";
                return result;
            }

            bool hasPairPending = Clean(input.pendingPairKey).Length > 0
                && string.Equals(input.pendingPairKey, result.pairKey, StringComparison.Ordinal);
            string pairPendingWriter = Clean(input.pairPendingWriterPawnId);
            if (pairPendingWriter.Length == 0)
            {
                // Compatibility for adapters/tests written against the initial H7 DTO.
                pairPendingWriter = Clean(input.pendingWriterPawnId);
            }
            bool sameWriterPending = input.writerHasPending
                || (Clean(input.pendingWriterPawnId).Length > 0
                    && string.Equals(
                        input.pendingWriterPawnId,
                        Clean(input.writerPawnId),
                        StringComparison.Ordinal));
            bool relationChangedFromPending = hasPairPending
                && !string.Equals(
                    Clean(input.pendingRelationSignature),
                    Clean(input.relationSignature),
                    StringComparison.Ordinal);
            bool reverseWriter = hasPairPending
                && pairPendingWriter.Length > 0
                && !string.Equals(
                    pairPendingWriter,
                    Clean(input.writerPawnId),
                    StringComparison.Ordinal);
            bool pairRelationChanged = input.pairCooldownUntilTick > input.nowTick
                && !string.Equals(
                    Clean(input.pairCooldownRelationSignature),
                    Clean(input.relationSignature),
                    StringComparison.Ordinal);
            // A direct-relation change always invalidates pair pacing immediately, even when the
            // independent writer cooldown still rejects this candidate.
            result.clearPairCooldown = pairRelationChanged;

            if (sameWriterPending)
            {
                result.rejection = "writer_pending";
                return result;
            }

            if (hasPairPending && !(reverseWriter && relationChangedFromPending))
            {
                result.rejection = "pair_pending";
                return result;
            }

            if (input.writerCooldownUntilTick > input.nowTick)
            {
                result.rejection = "writer_cooldown";
                return result;
            }

            if (input.pairCooldownUntilTick > input.nowTick && !pairRelationChanged)
            {
                result.rejection = "pair_cooldown";
                return result;
            }

            result.accepted = true;
            result.replacePending = hasPairPending && reverseWriter && relationChangedFromPending;
            result.clearPairCooldown = result.clearPairCooldown || result.replacePending;
            return result;
        }

        /// <summary>True only for a reservation owned by the cancelled pre-registration source.</summary>
        public static bool ReleasesReservation(
            string cancelledSourceKey,
            string reservationSourceKey,
            bool eventWasRegistered)
        {
            return !eventWasRegistered
                && Clean(cancelledSourceKey).Length > 0
                && string.Equals(
                    Clean(cancelledSourceKey),
                    Clean(reservationSourceKey),
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Keeps the final complete sentences from generated source prose and applies a hard character
        /// ceiling. Empty or punctuation-free prose fails soft to a trimmed tail excerpt.
        /// </summary>
        public static string SourceExcerpt(string prose, int sentenceCount, int maximumCharacters)
        {
            string text = Clean(prose);
            int max = Math.Max(0, maximumCharacters);
            if (text.Length == 0 || sentenceCount <= 0 || max == 0)
            {
                return string.Empty;
            }

            List<string> sentences = SplitSentences(text);
            string selected = sentences.Count == 0
                ? text
                : string.Join(" ", sentences.Skip(Math.Max(0, sentences.Count - sentenceCount)));
            if (selected.Length <= max)
            {
                return selected;
            }

            // Keep the newest prose, and avoid leaving half of a UTF-16 surrogate pair at the edge.
            int start = selected.Length - max;
            if (start > 0 && char.IsLowSurrogate(selected[start])
                && char.IsHighSurrogate(selected[start - 1]))
            {
                start++;
            }

            return selected.Substring(Math.Min(start, selected.Length)).TrimStart();
        }

        private static List<string> SplitSentences(string text)
        {
            List<string> result = new List<string>();
            int start = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (value != '.' && value != '!' && value != '?')
                {
                    continue;
                }

                if (value == '.'
                    && (IsDecimalPoint(text, index)
                        || IsNonTerminalAbbreviation(text, index)))
                {
                    continue;
                }

                // An ellipsis or emphatic run such as "...", "?!", or "!!" is one terminator,
                // not several one-character sentences. Include closing quotes/brackets too.
                int end = index + 1;
                while (end < text.Length && IsSentencePunctuation(text[end])) end++;
                while (end < text.Length && IsSentenceCloser(text[end])) end++;
                string sentence = text.Substring(start, end - start).Trim();
                if (sentence.Length > 0)
                {
                    result.Add(sentence);
                }

                start = end;
                while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
                index = start - 1;
            }

            string tail = start < text.Length ? text.Substring(start).Trim() : string.Empty;
            if (tail.Length > 0)
            {
                result.Add(tail);
            }

            return result;
        }

        private static bool IsDecimalPoint(string text, int index)
        {
            return index > 0
                && index + 1 < text.Length
                && char.IsDigit(text[index - 1])
                && char.IsDigit(text[index + 1]);
        }

        private static bool IsNonTerminalAbbreviation(string text, int periodIndex)
        {
            int next = periodIndex + 1;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            if (next >= text.Length)
            {
                return false;
            }

            int start = periodIndex - 1;
            while (start >= 0
                && (char.IsLetter(text[start]) || text[start] == '.'))
            {
                start--;
            }

            string token = text.Substring(start + 1, periodIndex - start - 1).Trim('.');
            if (token.Length == 0)
            {
                return false;
            }

            if (NonTerminalPeriodWords.Contains(token)
                || (token.Length == 1 && char.IsLetter(token[0])))
            {
                return true;
            }

            string[] initials = token.Split('.');
            if (initials.Length <= 1)
            {
                return false;
            }

            for (int index = 0; index < initials.Length; index++)
            {
                if (initials[index].Length != 1 || !char.IsLetter(initials[index][0]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSentencePunctuation(char value)
        {
            return value == '.' || value == '!' || value == '?';
        }

        private static bool IsSentenceCloser(char value)
        {
            return value == '"' || value == '\'' || value == ')'
                || value == ']' || value == '}';
        }

        private static uint StableHash(string value, uint seed)
        {
            uint hash = seed;
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            for (int index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= 16777619u;
            }

            return hash;
        }

        private static float ClampChance(float chance)
        {
            if (float.IsNaN(chance) || float.IsInfinity(chance)) return 0f;
            return Math.Max(0f, Math.Min(1f, chance));
        }

        private static string Segment(string value)
        {
            string clean = value ?? string.Empty;
            return clean.Length.ToString(CultureInfo.InvariantCulture) + ":" + clean;
        }

        private static bool TryReadSegment(
            string key,
            ref int offset,
            out int valueStart,
            out int valueLength)
        {
            valueStart = 0;
            valueLength = 0;
            if (offset < 0 || offset >= key.Length) return false;

            int length = 0;
            int digitCount = 0;
            while (offset < key.Length && key[offset] != ':')
            {
                char value = key[offset];
                if (value < '0' || value > '9' || length > (int.MaxValue - 9) / 10)
                    return false;
                length = (length * 10) + (value - '0');
                digitCount++;
                offset++;
            }
            if (digitCount == 0 || offset >= key.Length || key[offset] != ':' || length <= 0)
                return false;

            offset++;
            valueStart = offset;
            valueLength = length;
            if (length > key.Length - offset) return false;
            offset += length;
            return true;
        }

        private static bool SegmentValueEquals(
            string key,
            int valueStart,
            int valueLength,
            string expected)
        {
            return valueLength == expected.Length
                && string.CompareOrdinal(
                    key, valueStart, expected, 0, expected.Length) == 0;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
