// KnowledgeRelationPolicy.cs — pure direction rules for family knowledge captured from RimWorld's
// relation APIs. The impure adapter supplies stable defName strings; this file prevents the
// parent/child direction from being silently reversed in saved fact text.
using System;

namespace PawnDiary
{
    /// <summary>Converts the observed other-toward-victim relation into victim-from-owner view.</summary>
    internal static class KnowledgeRelationPolicy
    {
        /// <summary>
        /// Defensive per-death family fanout cap. Modded relation graphs can contain very large
        /// sibling/child sets; one death must not allocate unbounded records in the kill hook.
        /// </summary>
        public const int MaximumDeathFamilyOwners = 12;

        /// <summary>True while another close-family owner fits inside the defensive death cap.</summary>
        public static bool CanEmitDeathFamilyOwner(int emittedFamilyOwners)
        {
            return emittedFamilyOwners >= 0
                && emittedFamilyOwners < MaximumDeathFamilyOwners;
        }

        /// <summary>Returns the victim's relation from the surviving owner's point of view.</summary>
        public static string VictimRelationDefName(string observedRelationDefName)
        {
            if (string.Equals(observedRelationDefName, "Parent", StringComparison.OrdinalIgnoreCase))
            {
                return "Child";
            }

            if (string.Equals(observedRelationDefName, "Child", StringComparison.OrdinalIgnoreCase))
            {
                return "Parent";
            }

            return (observedRelationDefName ?? string.Empty).Trim();
        }
    }
}
