// Pure policy that rolls a pawn's PSYCHOTYPE (their outlook/temperament lens) from already-collected
// skill-passion facts. This is the second per-pawn voice layer, independent of the writing-style
// (persona) roll: writing style controls sentence mechanics, psychotype controls what a pawn notices,
// values, and fears. Rolling the two independently keeps diary voices diverse.
//
// This file has NO RimWorld/Verse dependency: the impure adapter (Source/Generation/PsychotypeRolls.cs)
// snapshots pawn skills/traits/creepjoiner flags on the main thread into the plain DTOs below, then
// calls Roll with an injected Func<float> rand01 (Rand.Value at runtime, a seeded PRNG in tests).
//
// Two-stage adult roll (see design/PSYCHOTYPE_PLAN.md):
//   Stage 0  BuildProfile   - fold the 12 skill passions into five domains + summary signals.
//   Stage 1  FamilyWeights  - weight the four families (grounded/inward/intense/anxious) from the
//                             profile plus the pawn's trait pull (PsychotypeTraitAffinities).
//   Stage 2  MemberWeights  - weight the members inside the rolled family (skill nudges + combo
//                             signatures + trait member bonuses + child-continuity nudge + duplicate
//                             penalty), then jitter. Trait-gated candidates (requiredTraitKey) are
//                             only eligible for pawns holding the trait — on every branch.
// A wildcard branch (XML-owned wildcardChance) skips all profile logic and rolls flat over stage-appropriate
// candidates. Children (stageBand == Child) always roll flat over the child catalog.
//
// New to C#/RimWorld? See AGENTS.md. Everything here is plain C# and unit-tested in DiaryPipelineTests.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>One skill the pawn has a passion in. <see cref="level"/> is 1 (minor) or 2 (burning).</summary>
    internal sealed class PsychotypeSkillPassion
    {
        public string skillDefName = string.Empty;
        public int level;
    }

    /// <summary>
    /// One rollable psychotype option, projected from a <c>DiaryPsychotypeDef</c>. <see cref="family"/>
    /// is one of the <see cref="PsychotypeRollPolicy"/> family constants for adults; child options set
    /// <see cref="stage"/> to <see cref="PsychotypeRollPolicy.StageChild"/>. <see cref="skillAffinities"/>
    /// maps a skill defName to its nudge points (the model-facing stage-2 data authored on the def).
    /// <see cref="requiredTraitKey"/> is the canonical trait key gating a trait-only psychotype
    /// (blank for the ordinary catalog); see <see cref="PsychotypeTraitAffinities"/>.
    /// </summary>
    internal sealed class PsychotypeCandidate
    {
        public string defName = string.Empty;
        public string family = string.Empty;
        public string stage = PsychotypeRollPolicy.StageAdult;
        public Dictionary<string, int> skillAffinities = new Dictionary<string, int>();
        public string requiredTraitKey = string.Empty;
    }

    /// <summary>
    /// All numeric tuning for one psychotype roll, snapshotted from the XML policy Def by the impure
    /// adapter. These are the odds/weights/thresholds named in AGENTS.md rule #3: they live in XML so a
    /// player can retune the catalog without a recompile, and they flow in here as plain floats so the
    /// pure algorithm never depends on Verse. Each field defaults to the shipped value, so a roll that
    /// supplies a fresh <c>PsychotypeRollWeights()</c> (including every unit test) behaves exactly like
    /// the old compile-time constants. See design/PSYCHOTYPE_PLAN.md "Tuning knobs" for the intent.
    /// </summary>
    internal sealed class PsychotypeRollWeights
    {
        // Family bases: grounded is the broad default, the three skewed families start rarer.
        public float familyBaseGrounded = 6f;
        public float familyBaseSkewed = 2f;
        // Inward leans for a pawn with no passions at all (nothing pulls them outward) and for creepjoiners.
        public float zeroPassionInwardBonus = 4f;
        public float creepjoinerInwardBonus = 4f;
        // Grounded picks up a small "settled" nudge when the pawn has passions but none burning.
        public float groundedNoBurningBonus = 1f;
        // Intense leans when several passions burn at once; anxious leans when one domain dominates.
        public int burningIntenseThreshold = 3;
        public float burningIntenseBonus = 2f;
        public float focusThreshold = 2f / 3f;
        public int focusMinTotalPoints = 3;
        public float focusAnxiousBonus = 2f;
        // Stage-2 member weighting.
        public float memberBaseWeight = 1f;
        public float comboBonus = 2f;
        public float continuityBonus = 1f;
        // Wildcard branch: skip all profile logic this often and roll flat over the stage candidates.
        public float wildcardChance = 0.12f;
        public float wildcardGroundedBase = 2f;
        public float wildcardSkewedBase = 1f;
        // Per-candidate jitter multiplier range and the soft duplicate penalty applied per existing holder.
        public float jitterMin = 0.8f;
        public float jitterMax = 1.3f;
        public float duplicatePenalty = 0.25f;
    }

    /// <summary>
    /// Everything the pure roll needs, snapshotted from live pawn/colony state by the impure adapter.
    /// </summary>
    internal sealed class PsychotypeRollInput
    {
        // XML-owned odds/weights/thresholds for this roll; defaults reproduce the original constants.
        public PsychotypeRollWeights weights = new PsychotypeRollWeights();
        public List<PsychotypeSkillPassion> passions = new List<PsychotypeSkillPassion>();
        // Anomaly creepjoiners lean inward (they read as detached/uncanny). Null-safe DLC flag.
        public bool isCreepJoiner;
        // Trait vetoes: a Psychopath never rolls Dependent; a Kind pawn never rolls Ruthless.
        public bool blockDependent;
        public bool blockRuthless;
        // Canonical trait keys (PsychotypeTraitAffinities.CanonicalTraitKey) for the pawn's supported
        // traits. They add family/member weight toward compatible psychotypes and unlock trait-gated
        // candidates; traits outside the table are simply absent here.
        public List<string> traitKeys = new List<string>();
        // XML-owned trait mapping/bonuses snapshotted by the impure adapter for this roll.
        public PsychotypeTraitAffinityPolicy traitPolicy = new PsychotypeTraitAffinityPolicy();
        // The child psychotype this pawn crystallized from, if any, so the adult roll can keep a thread
        // of continuity (a +1 nudge toward the mapped adult members). Empty for a first adult roll.
        public string childPsychotypeDefName = string.Empty;
        // How many living same-band colonists already hold each psychotype defName (soft duplicate penalty).
        public Dictionary<string, int> usedCounts = new Dictionary<string, int>();
        // Which catalog band to roll from: adult (full two-stage roll) or child (flat over the child catalog).
        public string stageBand = PsychotypeRollPolicy.StageAdult;
    }

    /// <summary>
    /// Domain point totals and summary signals folded out of the pawn's passions. Domains: Violence
    /// (Shooting, Melee), Making (Construction, Mining, Crafting), Nurture (Cooking, Plants, Animals,
    /// Medicine), Mind (Intellectual, Artistic), People (Social). A minor passion is 1 point, a burning
    /// passion is 2. <see cref="focus"/> is the share of all points sitting in the single top domain.
    /// </summary>
    internal sealed class PsychotypeProfile
    {
        public int violence;
        public int making;
        public int nurture;
        public int mind;
        public int people;
        public int total;
        public int burningCount;
        public int passionCount;
        public int domainsWithPoints;
        public float focus;
    }

    /// <summary>Pure psychotype selection. See file header for the staged design.</summary>
    internal static class PsychotypeRollPolicy
    {
        // ---- Stage bands ----
        public const string StageAdult = "adult";
        public const string StageChild = "child";

        // ---- Families (adult) ----
        public const string FamilyGrounded = "grounded";
        public const string FamilyInward = "inward";
        public const string FamilyIntense = "intense";
        public const string FamilyAnxious = "anxious";

        // ---- Defensive floor (not a tunable; rule #3 carve-out) ----
        // WeightFloor is a tiny epsilon that keeps every candidate pick above zero so a vetoed/duplicate-
        // weighted member can never divide the cumulative total by 0 in WeightedPick. It is a defensive
        // cap, not game-balance policy, so it stays here as a compile-time constant. Every other odds/
        // weight/threshold for the roll is XML-owned and arrives via PsychotypeRollInput.weights.
        private const float WeightFloor = 0.0001f;

        // ---- Skill vocabulary ----
        public const string SkillShooting = "Shooting";
        public const string SkillMelee = "Melee";
        public const string SkillConstruction = "Construction";
        public const string SkillMining = "Mining";
        public const string SkillCrafting = "Crafting";
        public const string SkillCooking = "Cooking";
        public const string SkillPlants = "Plants";
        public const string SkillAnimals = "Animals";
        public const string SkillMedicine = "Medicine";
        public const string SkillIntellectual = "Intellectual";
        public const string SkillArtistic = "Artistic";
        public const string SkillSocial = "Social";

        // ---- Combo target defNames (constants; moving these into XML is out of scope, see the plan) ----
        private const string DefContent = "DiaryPsychotype_Content";
        private const string DefAmbitious = "DiaryPsychotype_Ambitious";
        private const string DefSuperstitious = "DiaryPsychotype_Superstitious";
        private const string DefRuthless = "DiaryPsychotype_Ruthless";
        private const string DefTheatrical = "DiaryPsychotype_Theatrical";
        private const string DefDependent = "DiaryPsychotype_Dependent";
        private const string DefPerfectionist = "DiaryPsychotype_Perfectionist";

        // Ordered so family selection is deterministic given the same rand01 stream.
        private static readonly string[] Families =
        {
            FamilyGrounded, FamilyInward, FamilyIntense, FamilyAnxious
        };

        // Child psychotype -> adult members that keep a thread of continuity (each gets continuityBonus).
        private static readonly Dictionary<string, string[]> ChildContinuity = new Dictionary<string, string[]>
        {
            { "DiaryPsychotype_WideEyed", new[] { DefSuperstitious, DefContent } },
            { "DiaryPsychotype_BraveFront", new[] { "DiaryPsychotype_Dutiful", "DiaryPsychotype_Resentful" } },
            { "DiaryPsychotype_ShyWatcher", new[] { "DiaryPsychotype_Avoidant", "DiaryPsychotype_Detached" } },
            { "DiaryPsychotype_WildThing", new[] { "DiaryPsychotype_Volatile", DefAmbitious } },
            { "DiaryPsychotype_LittleAdult", new[] { DefPerfectionist, "DiaryPsychotype_Dutiful" } },
        };

        /// <summary>
        /// Normalizes a family string to one of the four adult family constants, defaulting to
        /// <see cref="FamilyGrounded"/> for blank/unknown input. Pure helper shared by the settings
        /// psychotype catalog editor so a hand-edited or custom family never falls outside a roll bucket.
        /// </summary>
        public static string NormalizeFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family))
            {
                return FamilyGrounded;
            }

            switch (family.Trim().ToLowerInvariant())
            {
                case FamilyInward:
                    return FamilyInward;
                case FamilyIntense:
                    return FamilyIntense;
                case FamilyAnxious:
                    return FamilyAnxious;
                default:
                    return FamilyGrounded;
            }
        }

        /// <summary>
        /// Rolls a psychotype defName for the pawn. Returns the winning candidate's defName, or empty
        /// string when no usable candidates were supplied (the adapter then falls back to Neutral).
        /// <paramref name="rand01"/> returns a value in [0,1); it is called for the takeover gate
        /// (only when a gated psychotype is unlocked), the wildcard gate, each weighted pick, and each
        /// candidate's jitter.
        /// </summary>
        public static string Roll(PsychotypeRollInput input, IReadOnlyList<PsychotypeCandidate> candidates,
            Func<float> rand01)
        {
            if (input == null || candidates == null || candidates.Count == 0 || rand01 == null)
            {
                return string.Empty;
            }

            // Children roll flat over the child catalog: no family/profile signals at that age.
            if (string.Equals(input.stageBand, StageChild, StringComparison.OrdinalIgnoreCase))
            {
                return FlatRoll(input, StageCandidates(candidates, StageChild), rand01, childRoll: true);
            }


            List<PsychotypeCandidate> adults = StageCandidates(candidates, StageAdult);
            if (adults.Count == 0)
            {
                return string.Empty;
            }

            // Extreme-trait takeover: when the pawn's traits unlock gated psychotypes, adopt one of
            // them outright at the XML policy's takeover rate (duplicate penalty + jitter still apply).
            // Trait-less pawns skip this draw entirely, keeping their rand stream unchanged.
            List<PsychotypeCandidate> gated = UnlockedGated(adults, input);
            float takeoverChance = Math.Max(0f, Math.Min(1f, input.traitPolicy?.gatedTakeoverChance ?? 0f));
            if (gated.Count > 0 && rand01() < takeoverChance)
            {
                Dictionary<string, float> takeover = new Dictionary<string, float>();
                for (int i = 0; i < gated.Count; i++)
                {
                    takeover[gated[i].defName] = Math.Max(
                        Weights(input).memberBaseWeight * DuplicateMultiplier(gated[i].defName, input), WeightFloor);
                }

                return JitteredPick(takeover, input, rand01);
            }

            // Wildcard: ignore the whole profile and roll flat over the adult catalog (grounded/skewed
            // base + duplicate penalty + jitter only). Draw the gate first so the rand stream is stable.
            if (rand01() < Weights(input).wildcardChance)
            {
                return FlatRoll(input, adults, rand01, childRoll: false);
            }

            PsychotypeProfile profile = BuildProfile(input.passions);
            Dictionary<string, float> familyWeights = FamilyWeights(profile, input);
            string family = WeightedPick(familyWeights, Families, rand01);
            if (string.IsNullOrEmpty(family))
            {
                return FlatRoll(input, adults, rand01, childRoll: false);
            }

            Dictionary<string, float> memberWeights = MemberWeights(family, profile, input, adults);
            if (memberWeights.Count == 0)
            {
                // Chosen family had no usable members (e.g. all vetoed) — fall back to a flat catalog roll.
                return FlatRoll(input, adults, rand01, childRoll: false);
            }

            return JitteredPick(memberWeights, input, rand01);
        }

        /// <summary>
        /// Folds the pawn's passions into the five domains plus the summary signals used by the family
        /// weights. Passion level is clamped to 1 (minor) or 2 (burning); unknown skills contribute nothing.
        /// </summary>
        public static PsychotypeProfile BuildProfile(List<PsychotypeSkillPassion> passions)
        {
            PsychotypeProfile profile = new PsychotypeProfile();
            if (passions == null)
            {
                return profile;
            }

            for (int i = 0; i < passions.Count; i++)
            {
                PsychotypeSkillPassion passion = passions[i];
                if (passion == null || string.IsNullOrWhiteSpace(passion.skillDefName) || passion.level <= 0)
                {
                    continue;
                }

                int points = passion.level >= 2 ? 2 : 1;
                profile.passionCount++;
                if (points >= 2)
                {
                    profile.burningCount++;
                }

                switch (passion.skillDefName)
                {
                    case SkillShooting:
                    case SkillMelee:
                        profile.violence += points;
                        break;
                    case SkillConstruction:
                    case SkillMining:
                    case SkillCrafting:
                        profile.making += points;
                        break;
                    case SkillCooking:
                    case SkillPlants:
                    case SkillAnimals:
                    case SkillMedicine:
                        profile.nurture += points;
                        break;
                    case SkillIntellectual:
                    case SkillArtistic:
                        profile.mind += points;
                        break;
                    case SkillSocial:
                        profile.people += points;
                        break;
                }
            }

            profile.total = profile.violence + profile.making + profile.nurture + profile.mind + profile.people;
            int top = 0;
            foreach (int domain in new[] { profile.violence, profile.making, profile.nurture, profile.mind, profile.people })
            {
                if (domain > 0)
                {
                    profile.domainsWithPoints++;
                }

                if (domain > top)
                {
                    top = domain;
                }
            }

            profile.focus = profile.total > 0 ? (float)top / profile.total : 0f;
            return profile;
        }

        /// <summary>
        /// Weights the four families from the profile (see the plan's Stage-1 table). Deterministic:
        /// the jitter and the pick happen in <see cref="Roll"/>, so this is directly unit-testable.
        /// </summary>
        public static Dictionary<string, float> FamilyWeights(PsychotypeProfile profile, PsychotypeRollInput input)
        {
            PsychotypeProfile safeProfile = profile ?? new PsychotypeProfile();
            bool creepjoiner = input != null && input.isCreepJoiner;
            PsychotypeRollWeights w = Weights(input);

            float grounded = w.familyBaseGrounded + safeProfile.nurture;
            if (safeProfile.passionCount > 0 && safeProfile.burningCount == 0)
            {
                grounded += w.groundedNoBurningBonus;
            }

            float inward = w.familyBaseSkewed + safeProfile.mind;
            if (safeProfile.passionCount == 0)
            {
                inward += w.zeroPassionInwardBonus;
            }

            if (creepjoiner)
            {
                inward += w.creepjoinerInwardBonus;
            }

            float intense = w.familyBaseSkewed + safeProfile.violence + safeProfile.people;
            if (safeProfile.burningCount >= w.burningIntenseThreshold)
            {
                intense += w.burningIntenseBonus;
            }

            float anxious = w.familyBaseSkewed + safeProfile.making;
            if (safeProfile.focus >= w.focusThreshold && safeProfile.total >= w.focusMinTotalPoints)
            {
                anxious += w.focusAnxiousBonus;
            }

            // Trait pull (PsychotypeTraitAffinities): additive on top of the passion signals, so a
            // Sanguine shooter still weighs both halves of who they are.
            List<string> traitKeys = input?.traitKeys;
            grounded += PsychotypeTraitAffinities.FamilyBonus(FamilyGrounded, traitKeys, input?.traitPolicy);
            inward += PsychotypeTraitAffinities.FamilyBonus(FamilyInward, traitKeys, input?.traitPolicy);
            intense += PsychotypeTraitAffinities.FamilyBonus(FamilyIntense, traitKeys, input?.traitPolicy);
            anxious += PsychotypeTraitAffinities.FamilyBonus(FamilyAnxious, traitKeys, input?.traitPolicy);

            return new Dictionary<string, float>
            {
                { FamilyGrounded, Math.Max(grounded, WeightFloor) },
                { FamilyInward, Math.Max(inward, WeightFloor) },
                { FamilyIntense, Math.Max(intense, WeightFloor) },
                { FamilyAnxious, Math.Max(anxious, WeightFloor) },
            };
        }

        /// <summary>
        /// Weights the members inside one family: flat base + per-skill nudges + combo signatures +
        /// trait member bonuses + child-continuity nudge, all times the soft duplicate penalty. Vetoed
        /// candidates (Dependent for a Psychopath, Ruthless for a Kind pawn) and trait-locked
        /// candidates are dropped before weighting. Deterministic — jitter is applied by
        /// <see cref="Roll"/>. Exposed for tests.
        /// </summary>
        public static Dictionary<string, float> MemberWeights(string family, PsychotypeProfile profile,
            PsychotypeRollInput input, IReadOnlyList<PsychotypeCandidate> candidates)
        {
            Dictionary<string, float> weights = new Dictionary<string, float>();
            if (candidates == null)
            {
                return weights;
            }

            HashSet<string> comboTargets = ComboTargets(input, profile);
            for (int i = 0; i < candidates.Count; i++)
            {
                PsychotypeCandidate candidate = candidates[i];
                if (candidate == null
                    || string.IsNullOrWhiteSpace(candidate.defName)
                    || !string.Equals(candidate.family, family, StringComparison.OrdinalIgnoreCase)
                    || IsVetoed(candidate.defName, input)
                    || IsTraitLocked(candidate, input))
                {
                    continue;
                }

                float weight = Weights(input).memberBaseWeight + SkillNudge(candidate, input);
                if (comboTargets.Contains(candidate.defName))
                {
                    weight += Weights(input).comboBonus;
                }

                weight += PsychotypeTraitAffinities.MemberBonus(
                    candidate.defName, input?.traitKeys, input?.traitPolicy);
                weight += ContinuityNudge(candidate.defName, input);
                weight *= DuplicateMultiplier(candidate.defName, input);
                weights[candidate.defName] = Math.Max(weight, WeightFloor);
            }

            return weights;
        }

        // Flat roll shared by the wildcard branch (adult catalog, grounded/skewed base) and the child
        // catalog (uniform base). Duplicate penalty and jitter always apply; vetoes and trait gates
        // always apply (a gated psychotype must be unreachable without its trait on EVERY branch).
        private static string FlatRoll(PsychotypeRollInput input, IReadOnlyList<PsychotypeCandidate> candidates,
            Func<float> rand01, bool childRoll)
        {
            Dictionary<string, float> weights = new Dictionary<string, float>();
            for (int i = 0; i < candidates.Count; i++)
            {
                PsychotypeCandidate candidate = candidates[i];
                if (candidate == null
                    || string.IsNullOrWhiteSpace(candidate.defName)
                    || IsVetoed(candidate.defName, input)
                    || IsTraitLocked(candidate, input))
                {
                    continue;
                }

                float baseWeight = childRoll
                    ? Weights(input).memberBaseWeight
                    : (string.Equals(candidate.family, FamilyGrounded, StringComparison.OrdinalIgnoreCase)
                        ? Weights(input).wildcardGroundedBase
                        : Weights(input).wildcardSkewedBase);
                weights[candidate.defName] = Math.Max(baseWeight * DuplicateMultiplier(candidate.defName, input), WeightFloor);
            }

            return weights.Count == 0 ? string.Empty : JitteredPick(weights, input, rand01);
        }

        // Applies one jitter multiplier per candidate, then a standard weighted pick over the results.
        private static string JitteredPick(Dictionary<string, float> weights, PsychotypeRollInput input,
            Func<float> rand01)
        {
            PsychotypeRollWeights w = Weights(input);
            Dictionary<string, float> jittered = new Dictionary<string, float>(weights.Count);
            foreach (KeyValuePair<string, float> pair in weights)
            {
                float jitter = w.jitterMin + rand01() * (w.jitterMax - w.jitterMin);
                jittered[pair.Key] = Math.Max(pair.Value * jitter, WeightFloor);
            }

            return WeightedPick(jittered, null, rand01);
        }

        // Standard cumulative weighted pick. When <paramref name="order"/> is supplied the keys are
        // walked in that order (deterministic given a fixed rand stream); otherwise the dictionary's
        // own enumeration order is used.
        private static string WeightedPick(Dictionary<string, float> weights, IReadOnlyList<string> order,
            Func<float> rand01)
        {
            if (weights == null || weights.Count == 0)
            {
                return string.Empty;
            }

            List<string> keys = new List<string>();
            if (order != null)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    if (weights.ContainsKey(order[i]))
                    {
                        keys.Add(order[i]);
                    }
                }
            }
            else
            {
                keys.AddRange(weights.Keys);
            }

            float total = 0f;
            for (int i = 0; i < keys.Count; i++)
            {
                total += weights[keys[i]];
            }

            if (total <= 0f)
            {
                return keys.Count > 0 ? keys[0] : string.Empty;
            }

            float roll = rand01() * total;
            float cumulative = 0f;
            for (int i = 0; i < keys.Count; i++)
            {
                cumulative += weights[keys[i]];
                if (roll <= cumulative)
                {
                    return keys[i];
                }
            }

            return keys[keys.Count - 1];
        }

        // Sum of per-skill nudge points: for each of the pawn's passions, the candidate's declared
        // affinity for that skill times the passion level (minor 1 / burning 2).
        private static float SkillNudge(PsychotypeCandidate candidate, PsychotypeRollInput input)
        {
            if (candidate?.skillAffinities == null || candidate.skillAffinities.Count == 0
                || input?.passions == null)
            {
                return 0f;
            }

            float nudge = 0f;
            for (int i = 0; i < input.passions.Count; i++)
            {
                PsychotypeSkillPassion passion = input.passions[i];
                if (passion == null || string.IsNullOrWhiteSpace(passion.skillDefName) || passion.level <= 0)
                {
                    continue;
                }

                if (candidate.skillAffinities.TryGetValue(passion.skillDefName, out int points))
                {
                    nudge += points * (passion.level >= 2 ? 2 : 1);
                }
            }

            return nudge;
        }

        // The set of member defNames boosted by a matching combo signature for this passion set.
        private static HashSet<string> ComboTargets(PsychotypeRollInput input, PsychotypeProfile profile)
        {
            HashSet<string> targets = new HashSet<string>(StringComparer.Ordinal);
            if (input?.passions == null)
            {
                return targets;
            }

            HashSet<string> skills = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < input.passions.Count; i++)
            {
                PsychotypeSkillPassion passion = input.passions[i];
                if (passion != null && !string.IsNullOrWhiteSpace(passion.skillDefName) && passion.level > 0)
                {
                    skills.Add(passion.skillDefName);
                }
            }

            // Reuse the profile the caller already folded; only rebuild if a direct caller passed none.
            PsychotypeProfile prof = profile ?? BuildProfile(input.passions);

            // Artistic + Social -> Theatrical
            if (skills.Contains(SkillArtistic) && skills.Contains(SkillSocial))
            {
                targets.Add(DefTheatrical);
            }

            // Intellectual + Artistic without Social -> Superstitious
            if (skills.Contains(SkillIntellectual) && skills.Contains(SkillArtistic) && !skills.Contains(SkillSocial))
            {
                targets.Add(DefSuperstitious);
            }

            // Shooting + Melee with no other passions -> Ruthless
            if (skills.Contains(SkillShooting) && skills.Contains(SkillMelee) && skills.Count == 2)
            {
                targets.Add(DefRuthless);
            }

            // Medicine + Social -> Dependent
            if (skills.Contains(SkillMedicine) && skills.Contains(SkillSocial))
            {
                targets.Add(DefDependent);
            }

            // >= 2 of Cooking/Plants/Animals -> Content
            int nurtureCraftCount = 0;
            if (skills.Contains(SkillCooking)) nurtureCraftCount++;
            if (skills.Contains(SkillPlants)) nurtureCraftCount++;
            if (skills.Contains(SkillAnimals)) nurtureCraftCount++;
            if (nurtureCraftCount >= 2)
            {
                targets.Add(DefContent);
            }

            // Exactly one passion and it is burning -> Perfectionist and Ambitious (+1 each, so both
            // reach the combo target set; the +comboBonus below applies to whichever family is rolled).
            // Read straight off the profile (one passion, and that passion burns) rather than tracking a
            // loose "last passion level" local that only happened to be correct when passionCount == 1.
            if (prof.passionCount == 1 && prof.burningCount == 1)
            {
                targets.Add(DefPerfectionist);
                targets.Add(DefAmbitious);
            }

            // >= 4 passions across >= 3 domains -> Ambitious
            if (prof.passionCount >= 4 && prof.domainsWithPoints >= 3)
            {
                targets.Add(DefAmbitious);
            }

            return targets;
        }

        private static float ContinuityNudge(string defName, PsychotypeRollInput input)
        {
            if (string.IsNullOrEmpty(input?.childPsychotypeDefName)
                || !ChildContinuity.TryGetValue(input.childPsychotypeDefName, out string[] mapped)
                || mapped == null)
            {
                return 0f;
            }

            for (int i = 0; i < mapped.Length; i++)
            {
                if (string.Equals(mapped[i], defName, StringComparison.Ordinal))
                {
                    return Weights(input).continuityBonus;
                }
            }

            return 0f;
        }

        private static float DuplicateMultiplier(string defName, PsychotypeRollInput input)
        {
            if (input?.usedCounts == null || string.IsNullOrEmpty(defName)
                || !input.usedCounts.TryGetValue(defName, out int used) || used <= 0)
            {
                return 1f;
            }

            return (float)Math.Pow(Weights(input).duplicatePenalty, used);
        }

        // Resolves the weights for a roll, defending against a null input (e.g. direct unit-test call to
        // FamilyWeights/MemberWeights). A fresh PsychotypeRollWeights reproduces the shipped defaults.
        private static PsychotypeRollWeights Weights(PsychotypeRollInput input)
        {
            return input?.weights ?? DefaultWeights;
        }

        private static readonly PsychotypeRollWeights DefaultWeights = new PsychotypeRollWeights();

        private static bool IsVetoed(string defName, PsychotypeRollInput input)
        {
            if (input == null || string.IsNullOrEmpty(defName))
            {
                return false;
            }

            if (input.blockDependent && string.Equals(defName, DefDependent, StringComparison.Ordinal))
            {
                return true;
            }

            return input.blockRuthless && string.Equals(defName, DefRuthless, StringComparison.Ordinal);
        }

        // A trait-gated candidate (non-blank requiredTraitKey) is only rollable when the pawn holds
        // the matching canonical trait key. Hard eligibility, same standing as IsVetoed.
        private static bool IsTraitLocked(PsychotypeCandidate candidate, PsychotypeRollInput input)
        {
            return !PsychotypeTraitAffinities.IsUnlocked(candidate.requiredTraitKey, input?.traitKeys);
        }

        // The gated candidates this pawn's traits unlock (feeds the takeover branch in Roll).
        private static List<PsychotypeCandidate> UnlockedGated(IReadOnlyList<PsychotypeCandidate> candidates,
            PsychotypeRollInput input)
        {
            List<PsychotypeCandidate> unlocked = new List<PsychotypeCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                PsychotypeCandidate candidate = candidates[i];
                if (candidate != null
                    && !string.IsNullOrWhiteSpace(candidate.defName)
                    && !string.IsNullOrWhiteSpace(candidate.requiredTraitKey)
                    && !IsTraitLocked(candidate, input)
                    && !IsVetoed(candidate.defName, input))
                {
                    unlocked.Add(candidate);
                }
            }

            return unlocked;
        }

        private static List<PsychotypeCandidate> StageCandidates(IReadOnlyList<PsychotypeCandidate> candidates,
            string stage)
        {
            List<PsychotypeCandidate> result = new List<PsychotypeCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                PsychotypeCandidate candidate = candidates[i];
                if (candidate != null
                    && !string.IsNullOrWhiteSpace(candidate.defName)
                    && string.Equals(candidate.stage ?? StageAdult, stage, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(candidate);
                }
            }

            return result;
        }
    }
}
