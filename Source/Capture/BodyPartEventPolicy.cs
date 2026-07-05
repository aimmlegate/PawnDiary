// Pure body-part diary policy. The RimWorld edge code snapshots live Hediff/Pawn facts into plain
// strings, booleans, and floats, then this helper decides stable classifier/context tokens. Keeping
// this file free of Verse/RimWorld types lets the behavior be tested without loading the game.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Plain snapshot of a pawn's attitude toward body modification. Filled by BodyModContext at the
    /// impure edge, consumed here by pure policy code.
    /// </summary>
    internal class BodyModStanceFacts
    {
        public bool HasCravesTrait;
        public bool HasDespisesTrait;
        public string IdeologyStance;
        public bool IsInhumanized;
        public bool IsGhoul;
    }

    /// <summary>
    /// Stateless token policy for artificial-part gains and natural-part losses.
    /// </summary>
    internal static class BodyPartEventPolicy
    {
        public const string KindAddedPart = "addedpart";
        public const string KindOrganicPart = "organicpart";
        public const string KindMissingPart = "missingpart";

        public const string TierCrude = "crude";
        public const string TierProsthetic = "prosthetic";
        public const string TierBionic = "bionic";
        public const string TierArchotech = "archotech";
        public const string TierAnomalous = "anomalous";

        public const string IdeologyApproves = "approves";
        public const string IdeologyDespises = "despises";
        public const string IdeologyNone = "none";

        public const string AttitudeCraves = "craves";
        public const string AttitudeApproves = "approves";
        public const string AttitudeUneasy = "uneasy";
        public const string AttitudeDespises = "despises";
        public const string AttitudeDetached = "detached";
        public const string AttitudeFascinatedUneasy = "fascinated_uneasy";
        public const string AttitudeHorrified = "horrified";
        public const string AttitudeOpportunity = "opportunity";
        public const string AttitudeGrieving = "grieving";
        public const string AttitudeViolated = "violated";

        public const string CauseSurgery = "surgery";
        public const string CauseViolence = "violence";
        public const string CauseUnknown = "unknown";

        public const float DefaultCrudeEfficiencyBelow = 0.9f;
        public const float DefaultProstheticEfficiencyMax = 1.0f;
        public const float DefaultBionicEfficiencyMax = 1.3f;

        /// <summary>
        /// Returns the synthetic key used by Hediff-domain XML groups. Ordinary hediffs pass through
        /// unchanged so existing exact Pregnancy/Labor/Anomaly groups keep matching.
        /// </summary>
        public static string BuildHediffClassifierKey(
            string defName,
            bool isAddedPart,
            bool isMissingPart,
            bool isOrganicAddedPart)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return string.Empty;
            }

            string partKind = PartKindToken(isAddedPart, isMissingPart, isOrganicAddedPart);
            return string.IsNullOrEmpty(partKind) ? defName : defName + "_" + partKind;
        }

        /// <summary>
        /// Collapses the body-part kind flags to one saved token. Organic parts also include the
        /// added-part token so saved display recovery can rebuild Tentacle_addedpart_organicpart.
        /// </summary>
        public static string PartKindToken(bool isAddedPart, bool isMissingPart, bool isOrganicAddedPart)
        {
            if (isOrganicAddedPart)
            {
                isAddedPart = true;
            }

            string token = string.Empty;
            if (isAddedPart)
            {
                token = KindAddedPart;
                if (isOrganicAddedPart)
                {
                    token += "_" + KindOrganicPart;
                }
            }

            if (isMissingPart)
            {
                token = string.IsNullOrEmpty(token) ? KindMissingPart : token + "_" + KindMissingPart;
            }

            return token;
        }

        /// <summary>
        /// Tests whether a part_kind token contains one exact underscore-separated kind token.
        /// </summary>
        public static bool KindHasToken(string partKindToken, string token)
        {
            if (string.IsNullOrEmpty(partKindToken) || string.IsNullOrEmpty(token))
            {
                return false;
            }

            if (string.Equals(partKindToken, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return partKindToken.StartsWith(token + "_", StringComparison.OrdinalIgnoreCase)
                || partKindToken.EndsWith("_" + token, StringComparison.OrdinalIgnoreCase)
                || partKindToken.IndexOf("_" + token + "_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Resolves an added body's technological/narrative tier from XML overrides first, then safe
        /// generic signals (organic part, removed thing tech level, efficiency).
        /// </summary>
        public static string ResolveTier(
            string defName,
            bool isOrganicAddedPart,
            string spawnThingTechLevel,
            float partEfficiency,
            bool betterThanNatural,
            IReadOnlyList<string> anomalousOverrides,
            IReadOnlyList<string> crudeOverrides,
            IReadOnlyList<string> prostheticOverrides,
            IReadOnlyList<string> bionicOverrides,
            IReadOnlyList<string> archotechOverrides,
            float crudeEfficiencyBelow,
            float prostheticEfficiencyMax,
            float bionicEfficiencyMax)
        {
            if (ContainsName(anomalousOverrides, defName)) return TierAnomalous;
            if (ContainsName(crudeOverrides, defName)) return TierCrude;
            if (ContainsName(prostheticOverrides, defName)) return TierProsthetic;
            if (ContainsName(bionicOverrides, defName)) return TierBionic;
            if (ContainsName(archotechOverrides, defName)) return TierArchotech;

            if (isOrganicAddedPart)
            {
                return TierAnomalous;
            }

            string tierFromTech = TierFromTechLevel(spawnThingTechLevel);
            if (!string.IsNullOrEmpty(tierFromTech))
            {
                return tierFromTech;
            }

            if (betterThanNatural)
            {
                return TierArchotech;
            }

            if (partEfficiency > 0f && !float.IsNaN(partEfficiency))
            {
                float crudeBelow = PositiveOrDefault(crudeEfficiencyBelow, DefaultCrudeEfficiencyBelow);
                float prostheticMax = PositiveOrDefault(prostheticEfficiencyMax, DefaultProstheticEfficiencyMax);
                float bionicMax = PositiveOrDefault(bionicEfficiencyMax, DefaultBionicEfficiencyMax);

                if (partEfficiency < crudeBelow)
                {
                    return TierCrude;
                }

                if (partEfficiency <= prostheticMax)
                {
                    return TierProsthetic;
                }

                if (partEfficiency <= bionicMax)
                {
                    return TierBionic;
                }

                return TierArchotech;
            }

            return TierProsthetic;
        }

        /// <summary>
        /// Resolves how the pawn perceives the body change. Traits outrank ideology; inhumanized/ghoul
        /// state outranks both because the pawn is emotionally detached from ordinary body boundaries.
        /// </summary>
        public static string ResolveAttitude(string partKindToken, string partTierToken, BodyModStanceFacts facts)
        {
            facts = facts ?? new BodyModStanceFacts();
            bool detached = facts.IsInhumanized || facts.IsGhoul;
            bool missing = KindHasToken(partKindToken, KindMissingPart);
            bool anomalous = KindHasToken(partKindToken, KindOrganicPart)
                || string.Equals(partTierToken, TierAnomalous, StringComparison.OrdinalIgnoreCase);

            if (detached)
            {
                return AttitudeDetached;
            }

            if (missing)
            {
                if (facts.HasDespisesTrait) return AttitudeViolated;
                if (facts.HasCravesTrait) return AttitudeOpportunity;
                if (string.Equals(facts.IdeologyStance, IdeologyDespises, StringComparison.OrdinalIgnoreCase))
                {
                    return AttitudeViolated;
                }
                return AttitudeGrieving;
            }

            if (anomalous)
            {
                if (facts.HasDespisesTrait) return AttitudeHorrified;
                if (facts.HasCravesTrait) return AttitudeFascinatedUneasy;
                if (string.Equals(facts.IdeologyStance, IdeologyDespises, StringComparison.OrdinalIgnoreCase))
                {
                    return AttitudeHorrified;
                }
                if (string.Equals(facts.IdeologyStance, IdeologyApproves, StringComparison.OrdinalIgnoreCase))
                {
                    return AttitudeFascinatedUneasy;
                }
                return AttitudeHorrified;
            }

            if (facts.HasDespisesTrait) return AttitudeDespises;
            if (facts.HasCravesTrait) return AttitudeCraves;
            if (string.Equals(facts.IdeologyStance, IdeologyDespises, StringComparison.OrdinalIgnoreCase))
            {
                return AttitudeDespises;
            }
            if (string.Equals(facts.IdeologyStance, IdeologyApproves, StringComparison.OrdinalIgnoreCase))
            {
                return AttitudeApproves;
            }
            return AttitudeUneasy;
        }

        /// <summary>
        /// Classifies a fresh missing-part cause. Non-fresh records do not carry reliable cause data.
        /// </summary>
        public static string CauseToken(bool isFresh, string lastInjuryDefName)
        {
            if (!isFresh)
            {
                return CauseUnknown;
            }

            return string.Equals(lastInjuryDefName, "SurgicalCut", StringComparison.OrdinalIgnoreCase)
                ? CauseSurgery
                : CauseViolence;
        }

        public static string AttitudeCueKey(string attitudeToken)
        {
            if (string.Equals(attitudeToken, AttitudeCraves, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Craves";
            if (string.Equals(attitudeToken, AttitudeApproves, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Approves";
            if (string.Equals(attitudeToken, AttitudeUneasy, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Uneasy";
            if (string.Equals(attitudeToken, AttitudeDespises, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Despises";
            if (string.Equals(attitudeToken, AttitudeDetached, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Detached";
            if (string.Equals(attitudeToken, AttitudeFascinatedUneasy, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.FascinatedUneasy";
            if (string.Equals(attitudeToken, AttitudeHorrified, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Horrified";
            if (string.Equals(attitudeToken, AttitudeOpportunity, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Opportunity";
            if (string.Equals(attitudeToken, AttitudeGrieving, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Grieving";
            if (string.Equals(attitudeToken, AttitudeViolated, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Attitude.Violated";
            return string.Empty;
        }

        public static string TierCueKey(string tierToken)
        {
            if (string.Equals(tierToken, TierCrude, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Tier.Crude";
            if (string.Equals(tierToken, TierProsthetic, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Tier.Prosthetic";
            if (string.Equals(tierToken, TierBionic, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Tier.Bionic";
            if (string.Equals(tierToken, TierArchotech, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Tier.Archotech";
            if (string.Equals(tierToken, TierAnomalous, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Tier.Anomalous";
            return string.Empty;
        }

        public static string CauseCueKey(string causeToken)
        {
            if (string.Equals(causeToken, CauseSurgery, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Cause.Surgery";
            if (string.Equals(causeToken, CauseViolence, StringComparison.OrdinalIgnoreCase))
                return "PawnDiary.Prompt.BodyPart.Cause.Violence";
            return string.Empty;
        }

        private static string TierFromTechLevel(string techLevel)
        {
            if (string.IsNullOrWhiteSpace(techLevel))
            {
                return string.Empty;
            }

            if (string.Equals(techLevel, "Neolithic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(techLevel, "Medieval", StringComparison.OrdinalIgnoreCase))
            {
                return TierCrude;
            }

            if (string.Equals(techLevel, "Industrial", StringComparison.OrdinalIgnoreCase))
            {
                return TierProsthetic;
            }

            if (string.Equals(techLevel, "Spacer", StringComparison.OrdinalIgnoreCase))
            {
                return TierBionic;
            }

            if (string.Equals(techLevel, "Ultra", StringComparison.OrdinalIgnoreCase)
                || string.Equals(techLevel, "Archotech", StringComparison.OrdinalIgnoreCase))
            {
                return TierArchotech;
            }

            return string.Empty;
        }

        private static bool ContainsName(IReadOnlyList<string> values, string value)
        {
            if (string.IsNullOrEmpty(value) || values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static float PositiveOrDefault(float value, float fallback)
        {
            return value > 0f && !float.IsNaN(value) ? value : fallback;
        }
    }
}
