// Plain Royalty contracts and stable schema tokens for the R1 persona, title, and psylink slice.
// Phase-1/2 runtime adapters copy live RimWorld objects into these small values.
// This file deliberately contains no Verse, RimWorld, Unity, Harmony, settings, or Def references.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Canonical shared continuity identity for one persona-weapon bond epoch.</summary>
    internal static class RoyaltyArcKeys
    {
        private const string PersonaPrefix = "royalty-persona|";

        /// <summary>Builds the frozen N0/N1 persona arc key, or empty for an unsafe identity.</summary>
        public static string Persona(string weaponThingId, int bondEpoch)
        {
            string weapon = CleanId(weaponThingId);
            return weapon.Length == 0 || bondEpoch < 1
                ? string.Empty
                : PersonaPrefix + weapon + "|" + bondEpoch;
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }
    }

    /// <summary>Saved relationship phases. These tokens are state, not player-facing prose.</summary>
    internal static class PersonaBondPhaseTokens
    {
        public const string Untracked = "untracked";
        public const string Active = "active";
        public const string SeparationPending = "separation_pending";
        public const string Separated = "separated";
        public const string Ended = "ended";

        public static bool IsLive(string value)
        {
            return value == Active || value == SeparationPending || value == Separated;
        }

        public static bool IsKnown(string value)
        {
            return value == Untracked || IsLive(value) || value == Ended;
        }
    }

    /// <summary>Source-owned persona lifecycle phases mapped to Narrative Continuity evidence.</summary>
    internal static class PersonaNarrativePhaseTokens
    {
        public const string BondFormed = "bond_formed";
        public const string BondSeparated = "bond_separated";
        public const string BondRecovered = "bond_recovered";
        public const string BondEnded = "bond_ended";
        public const string FirstConsequentialKill = "first_consequential_kill";

        public static bool IsKnown(string value)
        {
            return value == BondFormed || value == BondSeparated || value == BondRecovered
                || value == BondEnded || value == FirstConsequentialKill;
        }
    }

    /// <summary>Exact evidence that can change a persona bond state.</summary>
    internal static class PersonaObservationTokens
    {
        public const string Coding = "coding";
        public const string Baseline = "baseline";
        public const string Primary = "primary";
        public const string NotPrimary = "not_primary";
        public const string Unavailable = "unavailable";
        public const string Destroyed = "destroyed";
        public const string PawnDeath = "pawn_death";
        public const string Transfer = "transfer";
        public const string UnknownUncode = "unknown_uncode";
        public const string MapRemoved = "map_removed";
    }

    /// <summary>Cause precedence tokens for a relationship ending.</summary>
    internal static class PersonaEndCauseTokens
    {
        public const string None = "none";
        public const string PawnDeath = "pawn_death";
        public const string WeaponDestroyed = "weapon_destroyed";
        public const string Transfer = "transfer";
        public const string UnknownUncode = "unknown_uncode";
        public const string MapRemoval = "map_removal";
    }

    /// <summary>Events for which structural persona traits may be relevant.</summary>
    internal static class PersonaTraitEventTokens
    {
        public const string Formation = "formation";
        public const string Separation = "separation";
        public const string Recovery = "recovery";
        public const string Ending = "ending";
        public const string Kill = "kill";

        public static bool IsKnown(string value)
        {
            return value == Formation || value == Separation || value == Recovery
                || value == Ending || value == Kill;
        }
    }

    /// <summary>Exact Tale pawn-role tokens. List order is never treated as a role.</summary>
    internal static class RoyaltyTaleRoleTokens
    {
        public const string Initiator = "initiator";
        public const string Recipient = "recipient";

        public static bool IsKnown(string value)
        {
            return value == Initiator || value == Recipient;
        }

        public static bool IsKnownOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value) || IsKnown(value);
        }
    }

    /// <summary>Royal title transition classifications used by later progression ownership.</summary>
    internal static class RoyalTitleTransitionTokens
    {
        public const string FirstTitle = "first_title";
        public const string Promotion = "promotion";
        public const string Demotion = "demotion";
        public const string Loss = "loss";
        public const string NoChange = "no_change";
        public const string Invalid = "invalid";

        /// <summary>True only for source-owned title changes that can become continuity evidence.</summary>
        public static bool IsNarrative(string value)
        {
            return value == FirstTitle || value == Promotion || value == Demotion || value == Loss;
        }
    }

    /// <summary>Stable cause tokens shared by title and psylink mutation correlation.</summary>
    internal static class RoyalMutationCauseTokens
    {
        public const string ImperialBestowing = "imperial_bestowing";
        public const string AnimaLinking = "anima_linking";
        public const string Neuroformer = "neuroformer";
        public const string SuccessionRelated = "succession_related";
        public const string Unknown = "unknown";

        public static bool IsKnown(string value)
        {
            return value == ImperialBestowing || value == AnimaLinking || value == Neuroformer
                || value == SuccessionRelated || value == Unknown;
        }
    }

    /// <summary>Mutation kinds used for exact before/after matching.</summary>
    internal static class RoyalMutationKindTokens
    {
        public const string Title = "title";
        public const string Psylink = "psylink";
    }

    /// <summary>Canonical page owners returned by pure mutation arbitration.</summary>
    internal static class RoyalMutationOwnerTokens
    {
        public const string None = "none";
        public const string Pending = "pending";
        public const string Ritual = "ritual";
        public const string Succession = "succession";
        public const string Progression = "progression";
        public const string FallbackProgression = "fallback_progression";
    }

    /// <summary>Exact award/loss relationship carried by Thought_MemoryRoyalTitle.</summary>
    internal static class RoyalTitleThoughtRelationshipTokens
    {
        public const string Award = "award";
        public const string Loss = "loss";

        public static bool IsKnown(string value)
        {
            return value == Award || value == Loss;
        }
    }

    /// <summary>One persona trait copied and localized on the main thread.</summary>
    internal sealed class PersonaTraitFact
    {
        public string traitDefName = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public string workerTypeToken = string.Empty;
        public bool hasKillThought;
        public bool hasBondedThought;
        public bool hasBondedHediff;
        public bool hasEquippedHediff;
    }

    /// <summary>Detached event-time identity and structural facts for one persona weapon.</summary>
    internal sealed class PersonaWeaponSnapshot
    {
        public string weaponThingId = string.Empty;
        public string weaponDefName = string.Empty;
        public string displayName = string.Empty;
        public string codedPawnId = string.Empty;
        public string codedPawnName = string.Empty;
        public bool isCurrentlyPrimary;
        public bool isDestroyed;
        public List<PersonaTraitFact> traits = new List<PersonaTraitFact>();
    }

    /// <summary>
    /// Plain persona state shared by the pure policy and the Phase-1/2 save adapter.
    /// </summary>
    internal sealed class PersonaBondStateSnapshot
    {
        public string weaponThingId = string.Empty;
        public string weaponDefName = string.Empty;
        public string lastDisplayName = string.Empty;
        public int bondEpoch;
        public string currentPawnId = string.Empty;
        public string currentPawnName = string.Empty;
        public string previousPawnId = string.Empty;
        public string phaseToken = PersonaBondPhaseTokens.Untracked;
        public int bondStartedTick = -1;
        public int pendingSeparationTick = -1;
        public bool separationEmitted;
        public bool firstConsequentialKillObserved;
        public bool firstConsequentialKillEventRecorded;
        public int lastPrimaryObservedTick = -1;
        public int endedTick = -1;
        public string endCauseToken = PersonaEndCauseTokens.None;
        public List<PersonaTraitFact> traits = new List<PersonaTraitFact>();
    }

    /// <summary>One lifecycle observation assembled by a future guarded adapter.</summary>
    internal sealed class PersonaLifecycleObservation
    {
        public string observationToken = string.Empty;
        public PersonaWeaponSnapshot weapon;
        public int tick;
        public bool normalPlay = true;
        public bool groupEnabled = true;
    }

    /// <summary>Pure lifecycle output; state change and page dispatch are deliberately separate.</summary>
    internal sealed class PersonaLifecycleDecision
    {
        public PersonaBondStateSnapshot nextState = new PersonaBondStateSnapshot();
        public string narrativePhase = string.Empty;
        public bool stateChanged;
        public bool shouldEmit;
        public bool deathOwnsEnding;
        public bool includesExactPreviousBond;
    }

    /// <summary>Exact Tale/death/bond facts used to decide the once-per-epoch combat milestone.</summary>
    internal sealed class PersonaMilestoneObservation
    {
        public PersonaBondStateSnapshot bond;
        public PersonaWeaponSnapshot currentWeapon;
        public string taleDefName = string.Empty;
        public string resolvedKillerRoleToken = string.Empty;
        public string deathVictimRoleToken = string.Empty;
        public int significance;
        public bool victimPresent;
        public bool victimDied;
        public bool hasDeathContext;
        public bool deathContextMatchesKiller;
        public bool personaGroupEnabled = true;
        public bool pageAccepted;
        public string eventIdentity = string.Empty;
    }

    /// <summary>Ownership decision for one possible first consequential persona-weapon kill.</summary>
    internal sealed class PersonaMilestoneDecision
    {
        public bool qualifies;
        public bool markObserved;
        public bool markEventRecorded;
        public bool enrichTale;
        public bool forceSoloKillerPov;
        public bool preserveVictimDeathRoute = true;
        public List<PersonaTraitFact> selectedTraits = new List<PersonaTraitFact>();
    }

    /// <summary>One exact XML-owned Tale qualification row.</summary>
    internal sealed class RoyaltyTaleQualificationRule
    {
        public string taleDefName = string.Empty;
        public string killerRoleToken = string.Empty;
        public string victimRoleToken = string.Empty;
        public int minimumSignificance;
        public bool requireVictimDeath = true;
    }

    /// <summary>
    /// Exact participant roles for an ordinary Tale emitted by the same Pawn.Kill call as a
    /// persona milestone. These rows describe duplicate ownership only; they never qualify the
    /// milestone by themselves.
    /// </summary>
    internal sealed class RoyaltyTaleRoleRule
    {
        public string taleDefName = string.Empty;
        public string killerRoleToken = string.Empty;
        public string victimRoleToken = string.Empty;
    }

    /// <summary>One XML-owned worker-category relevance mapping.</summary>
    internal sealed class RoyaltyTraitWorkerRule
    {
        public string workerTypeToken = string.Empty;
        public string eventToken = string.Empty;
        public int weight;
    }

    /// <summary>Bounded exact compatibility correction for one otherwise opaque modded trait.</summary>
    internal sealed class RoyaltyTraitOverrideRule
    {
        public string traitDefName = string.Empty;
        public string eventToken = string.Empty;
        public int weight;
        public bool excluded;
    }

    /// <summary>Detached Royalty tuning consumed by every pure Phase-0 decision.</summary>
    internal sealed class RoyaltyPolicySnapshot
    {
        public bool enabled = true;
        public int separationThresholdTicks = 60000;
        public int reconciliationCadenceTicks = 2500;
        public int maximumSelectedTraits = 2;
        public int maximumTraitCandidates = 32;
        public int maximumTraitLabelCharacters = 80;
        public int maximumTraitDescriptionCharacters = 240;
        public int maximumDutyCategoryTokens = 2;
        public int titleCorrelationTicks = 2500;
        public int psylinkCorrelationTicks = 2500;
        public int titleThoughtCorrelationTicks = 2500;
        public int killThoughtCorrelationTicks = 60;
        public int maximumPendingRoyalMutations = 64;
        public int maximumPendingTitleThoughts = 128;
        public int maximumRoyaltyContextCharacters = 120;
        public int killThoughtWeight = 100;
        public int bondedThoughtWeight = 70;
        public int bondedHediffWeight = 60;
        public int equippedHediffWeight = 40;
        public int exactOverrideMaximumWeight = 50;
        // Prompt-visible prose has no hardcoded English fallback. Missing DefInjected text leaves the
        // optional provider silent while the source-owned diary page continues normally.
        public string personaNarrativeFormat = string.Empty;
        public string titleNarrativeFormat = string.Empty;
        public string titleWithDutiesNarrativeFormat = string.Empty;
        public List<RoyaltyTaleQualificationRule> qualifyingTales =
            new List<RoyaltyTaleQualificationRule>();
        public List<RoyaltyTaleRoleRule> personaKillCompanionTales =
            new List<RoyaltyTaleRoleRule>();
        public List<RoyaltyTraitWorkerRule> traitWorkerRules =
            new List<RoyaltyTraitWorkerRule>();
        public List<RoyaltyTraitOverrideRule> traitOverrides =
            new List<RoyaltyTraitOverrideRule>();
        public List<string> bestowingRitualDefNames = new List<string>();
        public List<string> animaRitualDefNames = new List<string>();
        public List<string> neuroformerThingDefNames = new List<string>();

        /// <summary>Creates safe deterministic fallbacks when the Royalty XML Def is absent or bad.</summary>
        public static RoyaltyPolicySnapshot CreateDefault()
        {
            RoyaltyPolicySnapshot policy = new RoyaltyPolicySnapshot();
            policy.qualifyingTales.Add(new RoyaltyTaleQualificationRule
            {
                taleDefName = "KilledMajorThreat",
                killerRoleToken = RoyaltyTaleRoleTokens.Initiator,
                victimRoleToken = RoyaltyTaleRoleTokens.Recipient,
                minimumSignificance = 1,
                requireVictimDeath = true
            });
            AddCompanionTale(policy, "KilledChild");
            AddCompanionTale(policy, "KilledColonist");
            AddCompanionTale(policy, "KilledColonyAnimal");
            AddCompanionTale(policy, "KilledMortar");
            AddCompanionTale(policy, "KilledLongRange");
            AddCompanionTale(policy, "KilledMelee");
            AddCompanionTale(policy, "DefeatedHostileFactionLeader");
            AddCompanionTale(policy, "KilledCapacity");
            policy.bestowingRitualDefNames.Add("BestowingCeremony");
            policy.animaRitualDefNames.Add("AnimaTreeLinking");
            policy.neuroformerThingDefNames.Add("PsychicAmplifier");
            return policy;
        }

        private static void AddCompanionTale(RoyaltyPolicySnapshot policy, string taleDefName)
        {
            policy.personaKillCompanionTales.Add(new RoyaltyTaleRoleRule
            {
                taleDefName = taleDefName,
                killerRoleToken = RoyaltyTaleRoleTokens.Initiator,
                victimRoleToken = RoyaltyTaleRoleTokens.Recipient
            });
        }
    }

    /// <summary>Exact title facts for one pawn and one faction.</summary>
    internal sealed class RoyalTitleSnapshot
    {
        public string pawnId = string.Empty;
        public string factionId = string.Empty;
        public string factionName = string.Empty;
        public string titleDefName = string.Empty;
        public string titleLabel = string.Empty;
        public int seniority;
        public int receivedTick = -1;
        public bool wasInherited;
        public List<string> dutyCategoryTokens = new List<string>();
    }

    /// <summary>Saved comparison facts for one pawn's highest title in one faction.</summary>
    internal sealed class RoyalTitleObservationSnapshot
    {
        public string factionId = string.Empty;
        public string factionName = string.Empty;
        public string titleDefName = string.Empty;
        public string titleLabel = string.Empty;
        public int seniority;
        public int lastObservedTick = -1;
    }

    /// <summary>Plain immediate before/after title mutation.</summary>
    internal sealed class RoyalTitleMutationSnapshot
    {
        public string pawnId = string.Empty;
        public string factionId = string.Empty;
        public RoyalTitleSnapshot previousTitle;
        public RoyalTitleSnapshot newTitle;
        public string causeToken = RoyalMutationCauseTokens.Unknown;
        public int tick;
        public string correlationId = string.Empty;
    }

    /// <summary>Plain immediate before/after psylink mutation.</summary>
    internal sealed class RoyalPsychicMutationSnapshot
    {
        public string pawnId = string.Empty;
        public int previousPsylinkLevel;
        public int newPsylinkLevel;
        public string causeToken = RoyalMutationCauseTokens.Unknown;
        public int tick;
        public string correlationId = string.Empty;
    }

    /// <summary>Result of classifying one faction-specific title transition.</summary>
    internal sealed class RoyalTitleTransitionDecision
    {
        public string transitionToken = RoyalTitleTransitionTokens.Invalid;
        public bool shouldAdvanceObservation;
        public bool shouldEmit;
        public bool claimedByRicherOwner;
        public List<string> introducedDutyCategoryTokens = new List<string>();
    }

    /// <summary>One normalized mutation row used by common title/psylink arbitration.</summary>
    internal sealed class RoyalMutationFact
    {
        public string kindToken = string.Empty;
        public string pawnId = string.Empty;
        public string factionId = string.Empty;
        public string previousValue = string.Empty;
        public string newValue = string.Empty;
        public int tick;
        public string correlationId = string.Empty;
    }

    /// <summary>Exact transient cause boundary copied without retaining any game object.</summary>
    internal sealed class RoyalMutationCauseScope
    {
        public string causeToken = RoyalMutationCauseTokens.Unknown;
        public string pawnId = string.Empty;
        public string factionId = string.Empty;
        public string previousTitleDefName = string.Empty;
        public string newTitleDefName = string.Empty;
        public int previousPsylinkLevel = -1;
        public int newPsylinkLevel = -1;
        public int openedTick;
        public string correlationId = string.Empty;
    }

    /// <summary>Ownership result for one mutation inside a batch.</summary>
    internal sealed class RoyalMutationDecision
    {
        public RoyalMutationFact mutation;
        public string causeToken = RoyalMutationCauseTokens.Unknown;
        public string ownerToken = RoyalMutationOwnerTokens.None;
        public bool exactCauseMatch;
        public bool suppressProgressionDuplicate;
        public bool advanceObservation;
    }

    /// <summary>One-action ownership plan; owner/fallback dispatch is capped to one page.</summary>
    internal sealed class RoyalMutationBatchPlan
    {
        public List<RoyalMutationDecision> mutations = new List<RoyalMutationDecision>();
        public string ownerToken = RoyalMutationOwnerTokens.None;
        public bool shouldEmitOwnerPage;
        public bool shouldEmitFallbackPage;
        public bool fallbackConsumed;
    }

    /// <summary>
    /// Detached event-time title/psylink facts for one bounded cause action. Runtime correlation may
    /// retain this DTO briefly, but it contains no Pawn, Def, ritual, item, or other game object.
    /// </summary>
    internal sealed class RoyalMutationBatchSnapshot
    {
        public string batchId = string.Empty;
        public string pawnId = string.Empty;
        public string pawnName = string.Empty;
        public string causeToken = RoyalMutationCauseTokens.Unknown;
        public int openedTick;
        public RoyalMutationCauseScope scope;
        public RoyalTitleMutationSnapshot titleMutation;
        public RoyalPsychicMutationSnapshot psylinkMutation;
        public bool claimed;
        public bool fallbackConsumed;

        public List<RoyalMutationFact> MutationFacts()
        {
            List<RoyalMutationFact> result = new List<RoyalMutationFact>();
            RoyalMutationFact title = RoyalMutationOwnershipPolicy.FromTitle(titleMutation);
            RoyalMutationFact psylink = RoyalMutationOwnershipPolicy.FromPsylink(psylinkMutation);
            if (title != null) result.Add(title);
            if (psylink != null) result.Add(psylink);
            return result;
        }
    }

    /// <summary>Plain title-memory fact staged before vanilla invokes the post-title callback.</summary>
    internal sealed class RoyalTitleThoughtSnapshot
    {
        public string pawnId = string.Empty;
        public string titleDefName = string.Empty;
        public string relationshipToken = string.Empty;
        public int tick;
    }

    /// <summary>
    /// Pure evidence mapper for later persona page owners. It uses only the frozen shared N0/N1
    /// contract and authorizes no page, provider lookup, persistence, or prompt attachment itself.
    /// </summary>
    internal static class RoyaltyNarrativeEvidenceFactory
    {
        public const string SourceDomain = "royalty_persona";
        public const string TitleSourceDomain = "royalty_title";

        public static NarrativeEvidence Persona(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            PersonaWeaponSnapshot weapon,
            int bondEpoch,
            string phase,
            string relatedEventId,
            string sourceDefName,
            bool pawnCanKnow)
        {
            string arcKey = RoyaltyArcKeys.Persona(weapon == null ? null : weapon.weaponThingId, bondEpoch);
            if (string.IsNullOrWhiteSpace(eventId) || tick < 0
                || string.IsNullOrWhiteSpace(povPawnId) || weapon == null
                || arcKey.Length == 0 || !PersonaNarrativePhaseTokens.IsKnown(phase))
            {
                return null;
            }

            List<string> topics = new List<string> { "weapons", "bonding", "loyalty" };
            if (phase == PersonaNarrativePhaseTokens.FirstConsequentialKill)
            {
                topics.Add("violence");
                topics.Add("death");
            }

            return new NarrativeEvidence
            {
                eventId = eventId.Trim(),
                tick = tick,
                povPawnId = povPawnId.Trim(),
                povRole = (povRole ?? string.Empty).Trim(),
                facet = NarrativeFacetTokens.BondLifecycle,
                phase = phase,
                subjectKind = NarrativeSubjectKindTokens.Weapon,
                subjectId = weapon.weaponThingId.Trim(),
                subjectLabel = (weapon.displayName ?? string.Empty).Trim(),
                arcKey = arcKey,
                relatedEventId = (relatedEventId ?? string.Empty).Trim(),
                beliefTopics = topics,
                salience = phase == PersonaNarrativePhaseTokens.BondEnded
                    ? NarrativeSalienceTokens.Major
                    : NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = pawnCanKnow,
                sourceDomain = SourceDomain,
                sourceDefName = (sourceDefName ?? string.Empty).Trim()
            };
        }

        /// <summary>
        /// Builds exact pawn identity evidence for one faction-specific title transition. The title
        /// and faction remain source facts; no localized title text is embedded in the stable arc key.
        /// </summary>
        public static NarrativeEvidence TitleTransition(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            string subjectPawnId,
            string subjectPawnName,
            string phase,
            string relatedEventId,
            string sourceDefName,
            bool successionRelated,
            bool pawnCanKnow)
        {
            if (string.IsNullOrWhiteSpace(eventId) || tick < 0
                || string.IsNullOrWhiteSpace(povPawnId)
                || string.IsNullOrWhiteSpace(subjectPawnId)
                || !RoyalTitleTransitionTokens.IsNarrative(phase)) return null;

            List<string> topics = new List<string> { "authority", "status", "duty" };
            if (successionRelated) topics.Add("death");
            return new NarrativeEvidence
            {
                eventId = eventId.Trim(),
                tick = tick,
                povPawnId = povPawnId.Trim(),
                povRole = (povRole ?? string.Empty).Trim(),
                facet = NarrativeFacetTokens.IdentityTransition,
                phase = phase,
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = subjectPawnId.Trim(),
                subjectLabel = (subjectPawnName ?? string.Empty).Trim(),
                // Title/faction labels are event facts, not continuity identities. N0 freezes no
                // Royalty title arc grammar, so this intentionally stays empty.
                arcKey = string.Empty,
                relatedEventId = (relatedEventId ?? string.Empty).Trim(),
                beliefTopics = topics,
                salience = phase == RoyalTitleTransitionTokens.Demotion
                        || phase == RoyalTitleTransitionTokens.Loss
                    ? NarrativeSalienceTokens.Major
                    : NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = pawnCanKnow,
                sourceDomain = TitleSourceDomain,
                sourceDefName = (sourceDefName ?? string.Empty).Trim()
            };
        }
    }
}
