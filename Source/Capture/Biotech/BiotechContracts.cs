// Plain contracts and stable schema tokens for Biotech growth/family work. These types deliberately
// contain no Pawn, Hediff, ChoiceLetter, DefDatabase, settings, or save mutation: guarded runtime
// adapters copy live RimWorld state into these DTOs before calling the pure policies. Verse-facing
// partial declarations add Scribe only in Source/Models, keeping this file standalone-test friendly.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Stable synthetic Def names used by the two B1 catalog event types.</summary>
    internal static class BiotechEventDefNames
    {
        public const string GrowthMoment = "BiotechGrowthMoment";
        public const string FamilyBirth = "BiotechFamilyBirth";
    }

    /// <summary>Additive Scribe keys frozen before any B1 save state is introduced.</summary>
    internal static class BiotechSaveKeys
    {
        public const string FamilyArcs = "biotechFamilyArcs";
        public const string PendingGrowthMoments = "pendingBiotechGrowthMoments";
        public const string PendingBirths = "pendingBiotechBirths";
        public const string PawnProgressionState = "biotechProgressionState";
        public const string FamilyObservationVersion = "familyObservationVersion";
        public const string GrowthObservationVersion = "growthObservationVersion";
    }

    /// <summary>Stable semicolon-context keys. These are schema labels, not localized prompt prose.</summary>
    internal static class BiotechContextKeys
    {
        public const string GrowthMoment = "growth_moment";
        public const string FamilyBirth = "biotech_birth";
        public const string ChildId = "child_id";
        public const string BirthdayAge = "birthday_age";
        public const string GrowthStage = "growth_stage";
        public const string FamilyArcId = "family_arc_id";
        public const string OpportunityBand = "opportunity_band";
        public const string OpportunityDescription = "opportunity_description";
        public const string UpbringingBand = "observed_upbringing_band";
        public const string UpbringingDescription = "observed_upbringing_description";
        public const string SelectedTrait = "selected_trait";
        public const string SelectedTraitDescription = "selected_trait_description";
        public const string NewInterestPrefix = "new_interest_";
        public const string InterestChangePrefix = "interest_change_";
        public const string NicknameChanged = "nickname_changed";
        public const string PreviousName = "previous_name";
        public const string CurrentName = "current_name";
        public const string NewResponsibilities = "new_responsibilities";
        public const string SupporterId = "supporter_id";
        public const string SupporterName = "supporter_name";
        public const string SupporterRole = "supporter_role";
        public const string InitiatorFamilyRole = "initiator_family_role";
        public const string RecipientFamilyRole = "recipient_family_role";
        public const string BirthOutcome = "birth_outcome";
        public const string BirthMethod = "birth_method";
        public const string ChildName = "child_name";
        public const string BirtherDied = "birther_died";
        public const string RitualBirth = "ritual_birth";
    }

    /// <summary>Canonical shared arc-key grammar for B1 family continuity.</summary>
    internal static class BiotechArcKeys
    {
        private const string FamilyPrefix = "biotech-family|";

        /// <summary>Builds the shared family arc key when both stable pregnancy identifiers exist.</summary>
        public static string FamilyFromPregnancy(string birtherId, string pregnancyHediffId)
        {
            string birther = CleanId(birtherId);
            string pregnancy = CleanId(pregnancyHediffId);
            return birther.Length == 0 || pregnancy.Length == 0
                ? string.Empty
                : FamilyPrefix + birther + "|" + pregnancy;
        }

        /// <summary>Builds the child-based fallback family arc key when no pregnancy key survived.</summary>
        public static string FamilyFromChild(string childId)
        {
            string child = CleanId(childId);
            return child.Length == 0 ? string.Empty : FamilyPrefix + child;
        }

        /// <summary>Builds the stable once-per-child-and-age growth correlation key.</summary>
        public static string GrowthCorrelation(string childId, int age)
        {
            string child = CleanId(childId);
            return child.Length == 0 || age <= 0 ? string.Empty : "growth|" + child + "|" + age;
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }
    }

    /// <summary>Frozen source tokens for player-committed and vanilla auto-resolved growth.</summary>
    internal static class BiotechGrowthSourceTokens
    {
        public const string PlayerChoice = "player_choice";
        public const string AutoResolved = "auto_resolved";

        /// <summary>Returns true only for a frozen, evidence-backed growth source token.</summary>
        public static bool IsKnown(string value)
        {
            return value == PlayerChoice || value == AutoResolved;
        }
    }

    /// <summary>Frozen growth-stage tokens; only the three vanilla Biotech choice ages are canonical.</summary>
    internal static class BiotechGrowthStageTokens
    {
        public const string AgeSeven = "age_7";
        public const string AgeTen = "age_10";
        public const string AgeThirteen = "age_13";

        /// <summary>Maps the three canonical growth-choice ages to stable context tokens.</summary>
        public static string ForAge(int age)
        {
            if (age == 7) return AgeSeven;
            if (age == 10) return AgeTen;
            if (age == 13) return AgeThirteen;
            return string.Empty;
        }
    }

    /// <summary>Stable passion tokens copied from live skill snapshots without referencing Passion.</summary>
    internal static class BiotechPassionTokens
    {
        public const string None = "none";
        public const string Minor = "minor";
        public const string Major = "major";

        /// <summary>Normalizes an external passion token without exposing the RimWorld enum.</summary>
        public static string Normalize(string value)
        {
            if (string.Equals(value, Major, System.StringComparison.OrdinalIgnoreCase)) return Major;
            if (string.Equals(value, Minor, System.StringComparison.OrdinalIgnoreCase)) return Minor;
            return None;
        }

        /// <summary>Maps normalized passions to an ordinal used only for before/after comparison.</summary>
        public static int Rank(string value)
        {
            string normalized = Normalize(value);
            if (normalized == Major) return 2;
            if (normalized == Minor) return 1;
            return 0;
        }
    }

    /// <summary>Exact family/supporter role tokens; proximity never creates one of these roles.</summary>
    internal static class BiotechFamilyRoleTokens
    {
        public const string Child = "child";
        public const string Parent = "parent";
        public const string BirthParent = "birth_parent";
        public const string Teacher = "teacher";
        public const string Birther = "birther";
        public const string GeneticMother = "genetic_mother";
        public const string Father = "father";

        /// <summary>Returns true only for a role permitted to support a growth-moment page.</summary>
        public static bool IsSupportRole(string value)
        {
            return value == Parent || value == BirthParent || value == Teacher;
        }
    }

    /// <summary>Stable categories copied from guarded live pregnancy/labor Hediffs.</summary>
    internal static class BiotechFamilyHediffKindTokens
    {
        public const string Pregnancy = "pregnancy";
        public const string Labor = "labor";
    }

    /// <summary>Stable categories for exact observed child-development activity.</summary>
    internal static class BiotechFamilyActivityKindTokens
    {
        public const string BabyPlay = "baby_play";
        public const string Lesson = "lesson";
    }

    /// <summary>Stable orientation tokens for accepted social-memory evidence.</summary>
    internal static class BiotechFamilyMemoryKindTokens
    {
        public const string AdultRememberedLesson = "adult_remembered_lesson";
        public const string ChildRememberedLesson = "child_remembered_lesson";
    }

    /// <summary>Exact birth outcomes proven at the canonical ApplyBirthOutcome boundary.</summary>
    internal static class BiotechBirthOutcomeTokens
    {
        public const string Healthy = "healthy";
        public const string InfantIllness = "infant_illness";
        public const string Stillbirth = "stillbirth";
    }

    /// <summary>Exact birth-method tokens; unknown methods remain empty rather than guessed.</summary>
    internal static class BiotechBirthMethodTokens
    {
        public const string Pregnancy = "pregnancy";
        public const string Surrogacy = "surrogacy";
        public const string GrowthVat = "growth_vat";
    }

    /// <summary>One trait identity copied on the main thread.</summary>
    internal partial class GrowthTraitFact
    {
        public string traitKey = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>One skill/passion row copied before or after a growth choice.</summary>
    internal partial class GrowthSkillFact
    {
        public string skillDefName = string.Empty;
        public string label = string.Empty;
        public string passion = BiotechPassionTokens.None;
        public int level;
    }

    /// <summary>Plain before/after snapshot for one child at a growth birthday.</summary>
    internal partial class GrowthPawnSnapshot
    {
        public string pawnId = string.Empty;
        public string displayName = string.Empty;
        public int biologicalAge;
        public int growthTier;
        public string shortName = string.Empty;
        public bool hasNewResponsibilities;
        public int capturedTick;
        public List<GrowthTraitFact> traits = new List<GrowthTraitFact>();
        public List<GrowthSkillFact> skills = new List<GrowthSkillFact>();
    }

    /// <summary>
    /// Saved, live-reference-free ownership of a configured growth letter. RimWorld saves the letter;
    /// this row saves only the event-time facts needed to finish or safely release its birthday later.
    /// </summary>
    internal partial class PendingBiotechGrowthMoment
    {
        public string pawnId = string.Empty;
        public int birthdayAge;
        public int birthdayTick;
        public int configuredTick;
        public int growthTier;
        public bool newResponsibilities;
        public string correlationId = string.Empty;
        public string familyArcId = string.Empty;
        public GrowthPawnSnapshot birthdaySnapshot;
    }

    /// <summary>Committed-choice facts that the before/after diff must independently verify.</summary>
    internal class GrowthCommittedChoice
    {
        public string selectedTraitKey = string.Empty;
        public List<string> selectedPassionSkillDefNames = new List<string>();
        public string sourceToken = BiotechGrowthSourceTokens.PlayerChoice;
        public string familyArcId = string.Empty;
    }

    /// <summary>One passion that actually increased during the committed choice.</summary>
    internal class PassionMutation
    {
        public string skillDefName = string.Empty;
        public string label = string.Empty;
        public string beforePassion = BiotechPassionTokens.None;
        public string afterPassion = BiotechPassionTokens.None;
    }

    /// <summary>Pure composite result that later orchestration can save and dispatch exactly once.</summary>
    internal class GrowthMomentMutation
    {
        public string childId = string.Empty;
        public int age;
        public string stageToken = string.Empty;
        public GrowthTraitFact selectedTrait;
        public List<string> additionalTraitKeysToConsume = new List<string>();
        public List<PassionMutation> passionChanges = new List<PassionMutation>();
        public string opportunityBand = string.Empty;
        public bool nicknameChanged;
        public string previousShortName = string.Empty;
        public string currentShortName = string.Empty;
        public bool newResponsibilities;
        public string familyArcId = string.Empty;
        public FamilySupportSelection supporter;
        public string sourceToken = string.Empty;
        public string correlationId = string.Empty;
    }

    /// <summary>One XML-owned inclusive growth-tier band copied into pure policy.</summary>
    internal class BiotechOpportunityBandRule
    {
        public int minimumTier;
        public int maximumTier;
        public string token = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>One qualitative observed-upbringing band copied into pure policy.</summary>
    internal class BiotechObservationBandRule
    {
        public int minimumEvidence;
        public string token = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>XML-owned B1 policy snapshot with code fallbacks that contain no prompt prose.</summary>
    internal class BiotechPolicySnapshot
    {
        public int growthPendingExpiryTicks = 180000;
        public int growthFallbackGraceTicks = 60000;
        public int familyActivityPairDedupTicks = 2500;
        public int supporterMinimumEvidence = 2;
        public int maximumSupporterRows = 12;
        public int familyArcRetentionTicks = 3600000;
        public int birthNamingPollTicks = 2500;
        public int birthNamingGraceTicks = 60000;
        public int maximumBirthWriters = 2;
        public string newInterestDescription = string.Empty;
        public string deepenedInterestDescription = string.Empty;
        public List<string> familyActivityExactDefNames = new List<string>();
        public List<string> familyActivityPrefixes = new List<string>();
        public List<string> familyPregnancyHediffDefNames = new List<string>();
        public List<string> familyLaborHediffDefNames = new List<string>();
        public List<string> familyLessonAdultThoughtDefNames = new List<string>();
        public List<string> familyLessonChildThoughtDefNames = new List<string>();
        public List<BiotechOpportunityBandRule> opportunityBands = new List<BiotechOpportunityBandRule>();
        public List<BiotechObservationBandRule> observationBands = new List<BiotechObservationBandRule>();

        /// <summary>Creates a complete safe policy whose fallback bands contain no prompt prose.</summary>
        public static BiotechPolicySnapshot CreateDefault()
        {
            BiotechPolicySnapshot snapshot = new BiotechPolicySnapshot();
            snapshot.opportunityBands.Add(Band(0, 2, "narrow"));
            snapshot.opportunityBands.Add(Band(3, 5, "mixed"));
            snapshot.opportunityBands.Add(Band(6, 7, "broad"));
            snapshot.opportunityBands.Add(Band(8, 8, "exceptional"));
            snapshot.observationBands.Add(ObservationBand(1, "light"));
            snapshot.observationBands.Add(ObservationBand(4, "steady"));
            snapshot.observationBands.Add(ObservationBand(8, "deep"));
            return snapshot;
        }

        private static BiotechOpportunityBandRule Band(int minimum, int maximum, string token)
        {
            return new BiotechOpportunityBandRule
            {
                minimumTier = minimum,
                maximumTier = maximum,
                token = token
            };
        }

        private static BiotechObservationBandRule ObservationBand(int minimum, string token)
        {
            return new BiotechObservationBandRule { minimumEvidence = minimum, token = token };
        }
    }

    /// <summary>Saved observation contract keyed by the exact adult ID; raw counts never reach prompts.</summary>
    internal partial class FamilySupportObservationState
    {
        public string adultId = string.Empty;
        public string lastDisplayName = string.Empty;
        public string relationToken = string.Empty;
        public int lessonCount;
        public int babyPlayCount;
        public int careCount;
        public int summarizedLessonCount;
        public int summarizedBabyPlayCount;
        public int summarizedCareCount;
        public int firstObservedTick;
        public int lastObservedTick;
    }

    /// <summary>Plain candidate used by deterministic supporter selection.</summary>
    internal class FamilySupportCandidate
    {
        public string adultId = string.Empty;
        public string displayName = string.Empty;
        public string relationToken = string.Empty;
        public int lessonCount;
        public int babyPlayCount;
        public int careCount;
        public int unsummarizedEvidenceCount;
        public int lastObservedTick;
        public bool eligible;
        public bool sameMap;
    }

    /// <summary>The one truthful supporter selected for a growth moment, or null when none qualifies.</summary>
    internal class FamilySupportSelection
    {
        public string adultId = string.Empty;
        public string displayName = string.Empty;
        public string roleToken = string.Empty;
        public string observationBand = string.Empty;
        public int evidenceCount;
    }

    /// <summary>Guarded event-time pregnancy or labor fact with no live Pawn/Hediff reference.</summary>
    internal class FamilyHediffSnapshot
    {
        public string kindToken = string.Empty;
        public string hediffId = string.Empty;
        public string birtherId = string.Empty;
        public string birtherName = string.Empty;
        public string geneticMotherId = string.Empty;
        public string geneticMotherName = string.Empty;
        public string fatherId = string.Empty;
        public string fatherName = string.Empty;
        public int observedTick;
    }

    /// <summary>Guarded living-child identity plus exact vanilla parent relation facts.</summary>
    internal class FamilyChildSnapshot
    {
        public string childId = string.Empty;
        public string childName = string.Empty;
        public int observedTick;
        public List<FamilyParticipantFact> parents = new List<FamilyParticipantFact>();
    }

    /// <summary>One exact lesson or BabyPlay observation, detached from live game objects.</summary>
    internal class FamilyActivityObservation
    {
        public string kindToken = string.Empty;
        public string adultId = string.Empty;
        public string adultName = string.Empty;
        public string childId = string.Empty;
        public string childName = string.Empty;
        public string relationToken = string.Empty;
        public int observedTick;
    }

    /// <summary>Runtime-only liveness/reference facts used by the pure retention decision.</summary>
    internal class FamilyArcRetentionInput
    {
        public bool childAliveAndDeveloping;
        public bool hasPendingReference;
        public bool hasSavedEventReference;
    }

    /// <summary>Bounded retention action for a saved family arc.</summary>
    internal enum FamilyArcRetentionAction
    {
        Keep,
        Compact,
        Remove
    }

    /// <summary>Deep-scribed family continuity shared by upbringing, growth, and later birth capture.</summary>
    internal partial class BiotechFamilyArcState
    {
        public string familyArcId = string.Empty;
        public string pregnancyHediffId = string.Empty;
        public string laborHediffId = string.Empty;
        public string childId = string.Empty;
        public string birtherId = string.Empty;
        public string geneticMotherId = string.Empty;
        public string fatherId = string.Empty;
        public string birtherName = string.Empty;
        public string geneticMotherName = string.Empty;
        public string fatherName = string.Empty;
        public int openedTick;
        public int birthTick;
        public int lastObservedTick;
        public string birthOutcomeToken = string.Empty;
        public string birthMethodToken = string.Empty;
        public string childNameAtBirth = string.Empty;
        public string currentChildName = string.Empty;
        public bool namingResolved;
        public bool closed;
        public bool baselineOnly;
        public bool detailsCompacted;
        public List<FamilySupportObservationState> supporters = new List<FamilySupportObservationState>();
        public List<int> recordedGrowthAges = new List<int>();
        public int lastSummarizedObservationTick;
    }

    /// <summary>Exact participant fact; eligibility is captured before pure writer selection.</summary>
    internal class FamilyParticipantFact
    {
        public string pawnId = string.Empty;
        public string displayName = string.Empty;
        public string roleToken = string.Empty;
        public bool eligible;
    }

    /// <summary>Canonical event-time birth snapshot. Distinct roles remain distinct even for one pawn.</summary>
    internal class BirthMutationSnapshot
    {
        public string familyArcId = string.Empty;
        public string childId = string.Empty;
        public string currentChildName = string.Empty;
        public FamilyParticipantFact birther;
        public FamilyParticipantFact geneticMother;
        public FamilyParticipantFact father;
        public FamilyParticipantFact doctor;
        public string outcomeToken = string.Empty;
        public string methodToken = string.Empty;
        public string qualityBand = string.Empty;
        public bool birtherDied;
        public bool ritualBirth;
        public int namingDeadline;
        public bool namingResolved;
        public int birthTick;
        public string correlationId = string.Empty;
    }

    /// <summary>One selected birth writer in deterministic role-certainty order.</summary>
    internal class BirthWriterFact
    {
        public string pawnId = string.Empty;
        public string displayName = string.Empty;
        public string roleToken = string.Empty;
    }

    /// <summary>At most two adult birth writers; the child is always the subject, never a writer.</summary>
    internal class BirthWriterSelection
    {
        public List<BirthWriterFact> writers = new List<BirthWriterFact>();
    }
}
