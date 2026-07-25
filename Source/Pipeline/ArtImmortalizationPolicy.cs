// Pure Quality Wave H6 policy: when a sculpture immortalizes a colony deed, who writes the diary
// page about it, and whether that particular artwork gets to claim the deed at all.
//
// The impure half (Source/Patches/DiaryArtPatches.cs) resolves the live tale, sculpture, and pawns,
// then hands plain rows here. Nothing in this file touches RimWorld, so the pure test harness covers
// every ordering and boundary rule.
//
// Two rules carry the whole feature:
//   * ONE page per deed, ever. Ownership is keyed on the tale's exact stable identity, never on its
//     translated prose, so a second sculpture about the same deed stays silent even in another
//     language. An identity that cannot be verified fails CLOSED — no page — because a fuzzy key
//     would eventually let one deed be written twice.
//   * The writer is chosen deterministically, so iteration order over colonists cannot change who
//     writes, and reloading cannot hand the deed to someone else.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One pawn considered as the writer of an art-immortalization page. All fields are plain facts
    /// copied from live state by the patch; <see cref="loadId"/> is RimWorld's stable thing ID number,
    /// which is what makes "lowest ID wins" reproducible across saves.
    /// </summary>
    internal sealed class ArtWriterCandidate
    {
        public string pawnId;
        public int loadId;

        /// <summary>Passes IsDiaryEligible right now — only an eligible pawn can be handed the page.</summary>
        public bool eligible;

        /// <summary>The tale's dominant pawn: the one the deed is actually about.</summary>
        public bool dominant;

        /// <summary>The tale concerns this pawn at all (includes the dominant pawn).</summary>
        public bool concerned;

        /// <summary>This pawn created the artwork.</summary>
        public bool sculptor;

        /// <summary>This pawn already has colony diary pages, hot or archived.</summary>
        public bool hasColonyDiary;
    }

    /// <summary>
    /// Owns every pure H6 decision: tale-identity verification, the ownership key, deterministic
    /// sampling, and the writer order.
    /// </summary>
    internal static class ArtImmortalizationPolicy
    {
        /// <summary>Prefix of the exact-ownership key. Stable save token — never localize.</summary>
        public const string OwnershipKeyPrefix = "art-tale|";

        /// <summary>Separator between a tale's def name and its numeric ID inside the identity.</summary>
        public const char IdentitySeparator = '#';

        // Context keys. "art=" is deliberately NOT registered in DiaryEventDomainClassifier, so it
        // falls through to the Interaction domain the XML group declares. The tale's def name is
        // stored as "art_tale=" rather than the obvious "tale=" precisely BECAUSE "tale=" is the
        // Tale-domain marker: reusing it would file every art page under the wrong domain.
        public const string ArtContextKey = "art";
        public const string ArtThingIdContextKey = "art_thing_id";
        public const string LabelContextKey = "label";
        public const string SculptorContextKey = "sculptor";
        public const string TaleDefContextKey = "art_tale";
        public const string TaleIdContextKey = "art_tale_id";

        /// <summary>
        /// Builds a tale's stable identity from its def name and numeric ID. Returns an empty string
        /// when either part is missing or would corrupt the key, so callers fail closed.
        /// </summary>
        public static string TaleIdentity(string taleDefName, int taleId)
        {
            string defName = (taleDefName ?? string.Empty).Trim();
            if (defName.Length == 0 || taleId < 0
                || defName.IndexOf(IdentitySeparator) >= 0
                || defName.IndexOf('|') >= 0
                || defName.IndexOf(';') >= 0
                || defName.IndexOf('=') >= 0)
            {
                return string.Empty;
            }

            return defName + IdentitySeparator + taleId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True only for an identity this policy can safely treat as exact: a non-empty def name, the
        /// separator, and a non-negative integer ID. Anything else — including a translated label that
        /// happened to be passed in — is rejected, because once-per-deed ownership must never degrade
        /// to prose matching.
        /// </summary>
        public static bool IsVerifiedTaleIdentity(string taleIdentity)
        {
            if (string.IsNullOrWhiteSpace(taleIdentity))
            {
                return false;
            }

            string identity = taleIdentity.Trim();
            int separator = identity.IndexOf(IdentitySeparator);
            if (separator <= 0 || separator >= identity.Length - 1)
            {
                return false;
            }

            if (identity.IndexOf(IdentitySeparator, separator + 1) >= 0
                || identity.IndexOf('|') >= 0
                || identity.IndexOf(';') >= 0
                || identity.IndexOf('=') >= 0)
            {
                return false;
            }

            for (int i = separator + 1; i < identity.Length; i++)
            {
                if (identity[i] < '0' || identity[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The exact-ownership key for one deed. Empty for an unverified identity, so a caller that
        /// forwards it straight into a dedup store can never claim ownership on a fuzzy key.
        /// </summary>
        public static string OwnershipKey(string taleIdentity)
        {
            return IsVerifiedTaleIdentity(taleIdentity)
                ? OwnershipKeyPrefix + taleIdentity.Trim()
                : string.Empty;
        }

        /// <summary>
        /// Returns a process-stable FNV-1a roll in [0, 1) for one artwork and one deed. Re-initializing
        /// the same sculpture cannot reroll it, and a second sculpture about the same deed gets its own
        /// independent sample. Deliberately does not touch Verse.Rand, so the mod never perturbs the
        /// gameplay RNG stream (Quality Wave locked decision 17).
        /// </summary>
        public static float DeterministicRoll(string sculptureId, string taleIdentity)
        {
            uint hash = 2166136261u;
            AddStableText(ref hash, sculptureId);
            AddStableText(ref hash, taleIdentity);
            // Twenty-four retained bits map exactly into a float and cannot round the maximum to 1.
            return (hash >> 8) / 16777216f;
        }

        /// <summary>Half-open comparison, matching every other deterministic sample in this repo.</summary>
        public static bool ShouldWrite(float roll, float chance)
        {
            if (float.IsNaN(roll) || float.IsInfinity(roll) || roll < 0f || roll >= 1f)
            {
                return false;
            }

            if (float.IsNaN(chance) || float.IsInfinity(chance) || chance <= 0f)
            {
                return false;
            }

            return chance >= 1f || roll < chance;
        }

        /// <summary>
        /// Picks the writer, in the locked order:
        ///   1. the eligible pawn the deed is about (the tale's dominant pawn);
        ///   2. otherwise the eligible colonist with the lowest stable ID that the deed concerns;
        ///   3. otherwise the eligible sculptor — but ONLY when the deed concerns someone who already
        ///      keeps a colony diary. That is what lets a sculpture about a dead colonist be written by
        ///      the artist, while art a trader hauled in about strangers stays silent.
        /// Returns null when nobody qualifies. Ties inside each tier break on the lower stable ID, then
        /// on the pawn ID, so iteration order over the colony can never change the answer.
        /// </summary>
        public static ArtWriterCandidate SelectWriter(IReadOnlyList<ArtWriterCandidate> candidates)
        {
            if (candidates == null)
            {
                return null;
            }

            ArtWriterCandidate dominant = null;
            ArtWriterCandidate concerned = null;
            ArtWriterCandidate sculptor = null;
            bool deedConcernsADiarist = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                ArtWriterCandidate candidate = candidates[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.pawnId))
                {
                    continue;
                }

                if (candidate.concerned && candidate.hasColonyDiary)
                {
                    deedConcernsADiarist = true;
                }

                if (!candidate.eligible)
                {
                    continue;
                }

                if (candidate.dominant)
                {
                    dominant = Prefer(dominant, candidate);
                }

                if (candidate.concerned)
                {
                    concerned = Prefer(concerned, candidate);
                }

                if (candidate.sculptor)
                {
                    sculptor = Prefer(sculptor, candidate);
                }
            }

            if (dominant != null)
            {
                return dominant;
            }

            if (concerned != null)
            {
                return concerned;
            }

            return deedConcernsADiarist ? sculptor : null;
        }

        /// <summary>Lower stable ID wins; an exact ID tie falls back to the ordinal pawn ID.</summary>
        private static ArtWriterCandidate Prefer(ArtWriterCandidate current, ArtWriterCandidate candidate)
        {
            if (current == null)
            {
                return candidate;
            }

            if (candidate.loadId != current.loadId)
            {
                return candidate.loadId < current.loadId ? candidate : current;
            }

            return string.CompareOrdinal(candidate.pawnId, current.pawnId) < 0 ? candidate : current;
        }

        private static void AddStableText(ref uint hash, string value)
        {
            string text = value ?? string.Empty;
            unchecked
            {
                // Hash the length as four fixed bytes so component boundaries stay unambiguous
                // without allocating a formatted number string.
                int length = text.Length;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(length >> shift);
                    hash *= 16777619u;
                }

                for (int i = 0; i < text.Length; i++)
                {
                    // Hash both bytes of the UTF-16 code unit so non-ASCII IDs remain stable.
                    char current = text[i];
                    hash ^= (byte)(current & 0xFF);
                    hash *= 16777619u;
                    hash ^= (byte)(current >> 8);
                    hash *= 16777619u;
                }
            }
        }
    }
}
