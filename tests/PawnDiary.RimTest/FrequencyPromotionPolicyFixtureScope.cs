// Hermetic RimTest scope for frequency-migration fixtures. The loaded developer configuration may
// have Advanced overrides applied to live interaction-group Defs, so tests that assert promotion
// membership must force only their named policies and restore those exact fields afterward.
using System;
using System.Collections.Generic;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Temporarily enables named interaction promotion policies and restores them exactly.</summary>
    internal sealed class FrequencyPromotionPolicyFixtureScope : IDisposable
    {
        private readonly List<PolicyState> states = new List<PolicyState>();

        public FrequencyPromotionPolicyFixtureScope(params string[] groupKeys)
        {
            // Validate the complete request before touching any live Def. If a compatibility row is
            // missing or malformed, construction fails without stranding an earlier row as enabled.
            for (int i = 0; i < groupKeys.Length; i++)
            {
                string groupKey = groupKeys[i];
                DiaryInteractionGroupDef group =
                    DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail(groupKey);
                if (group == null
                    || group.domain != GroupDomain.Interaction
                    || group.batch == null
                    || group.promotion == null)
                {
                    throw new AssertionException(
                        groupKey + " Interaction batch/promotion Def is required by the frequency fixture.");
                }

                states.Add(new PolicyState
                {
                    group = group,
                    batchEnabled = group.batch.enabled,
                    promotionEnabled = group.promotion.enabled
                });
            }

            for (int i = 0; i < states.Count; i++)
            {
                states[i].group.batch.enabled = true;
                states[i].group.promotion.enabled = true;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < states.Count; i++)
            {
                PolicyState state = states[i];
                state.group.batch.enabled = state.batchEnabled;
                state.group.promotion.enabled = state.promotionEnabled;
            }
        }

        private sealed class PolicyState
        {
            public DiaryInteractionGroupDef group;
            public bool batchEnabled;
            public bool promotionEnabled;
        }
    }
}
