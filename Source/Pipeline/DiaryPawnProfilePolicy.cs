// Pure decisions for the pawn Diary profile's generation switch and truthful voice-preview state.
// The UI keeps edits in a draft, asks this policy what kind of apply or caveat is required, and leaves
// all RimWorld save mutation and API-lane inspection to adapters. No Verse/Unity/settings dependencies.
using System;

namespace PawnDiary
{
    /// <summary>How an explicit profile Save should apply the pawn's generation-switch draft.</summary>
    internal enum DiaryPawnProfileGenerationDecision
    {
        /// <summary>The draft matches the saved switch; no generation mutation is needed.</summary>
        Unchanged,

        /// <summary>Save the switch as disabled. Existing pages and pending facts remain intact.</summary>
        Disable,

        /// <summary>Enable immediately because no resumable page backlog exists.</summary>
        EnableDirect,

        /// <summary>Ask before enabling because the existing setter will queue pending pages.</summary>
        EnableWithConfirmation
    }

    /// <summary>How confidently the profile can describe automatic outlook inclusion across API lanes.</summary>
    internal enum DiaryPawnProfileOutlookPreviewMode
    {
        /// <summary>Every currently usable lane includes the automatic outlook layer.</summary>
        Included,

        /// <summary>No currently usable lane includes the automatic outlook layer.</summary>
        Omitted,

        /// <summary>Usable lanes disagree, so inclusion depends on which lane handles the request.</summary>
        LaneDependent,

        /// <summary>No usable API lane exists, so there is no current request shape to preview.</summary>
        NoActiveLane
    }

    /// <summary>
    /// Read-only profile-preview decision. The UI uses this to label uncertainty without rolling a
    /// pawn's automatic voice or guessing which API lane dispatch will select.
    /// </summary>
    internal struct DiaryPawnProfilePreviewDecision
    {
        public bool automaticVoiceMayChange;
        public DiaryPawnProfileOutlookPreviewMode outlookMode;
    }

    /// <summary>Resolved pin state plus whether Save must persist the displayed base first.</summary>
    internal struct DiaryPawnProfileVoiceWritePlan
    {
        public bool pinned;
        public bool persistDisplayedBase;
    }

    /// <summary>Read-only facts needed to mirror generation's automatic voice-stage change predicates.</summary>
    internal struct DiaryVoiceStagePreviewFacts
    {
        public bool recordExists;
        public bool writingStyleManagedAutomatically;
        public bool psychotypeManagedAutomatically;
        public bool bandStamped;
        public bool bandMatches;
        public bool writingStyleSet;
        public bool psychotypeSet;
        public bool psychotypeIsNeutral;
    }

    /// <summary>Which visible automatic voice layers would change before generation.</summary>
    internal struct DiaryVoiceStagePreviewSnapshot
    {
        public bool writingStyleMayChange;
        public bool psychotypeMayChange;
    }

    /// <summary>Pure policy for applying the profile's per-pawn generation switch.</summary>
    internal static class DiaryPawnProfilePolicy
    {
        /// <summary>
        /// Compares the saved and drafted switch. Only a disabled-to-enabled transition with at least
        /// one resumable page requires confirmation; corrupt negative counts behave as zero.
        /// </summary>
        public static DiaryPawnProfileGenerationDecision DecideGenerationChange(
            bool originalEnabled,
            bool draftEnabled,
            int resumableBacklogCount)
        {
            if (originalEnabled == draftEnabled)
            {
                return DiaryPawnProfileGenerationDecision.Unchanged;
            }

            if (!draftEnabled)
            {
                return DiaryPawnProfileGenerationDecision.Disable;
            }

            return Math.Max(0, resumableBacklogCount) > 0
                ? DiaryPawnProfileGenerationDecision.EnableWithConfirmation
                : DiaryPawnProfileGenerationDecision.EnableDirect;
        }

        /// <summary>
        /// Returns the pin state that Save will persist for one draft voice layer. A changed base or
        /// newly authored nonblank custom rule is an explicit manual choice and therefore pins it.
        /// </summary>
        public static bool ResolveDraftPin(
            bool draftPinned,
            bool baseChanged,
            bool customChanged,
            bool customRuleHasText)
        {
            return draftPinned || baseChanged || (customChanged && customRuleHasText);
        }

        /// <summary>
        /// Plans one voice layer's Save. A base choice or explicit pin must persist the base displayed
        /// in the draft. A custom-rule-only edit still auto-pins, but deliberately preserves the hidden
        /// automatic base roll that record creation supplies for use if the custom rule is later cleared.
        /// </summary>
        public static DiaryPawnProfileVoiceWritePlan PlanVoiceWrite(
            bool originalPinned,
            bool explicitDraftPinned,
            bool baseChanged,
            bool customChanged,
            bool customRuleHasText)
        {
            return new DiaryPawnProfileVoiceWritePlan
            {
                pinned = ResolveDraftPin(
                    explicitDraftPinned,
                    baseChanged,
                    customChanged,
                    customRuleHasText),
                persistDisplayedBase = baseChanged
                    || (!originalPinned && explicitDraftPinned)
            };
        }

        /// <summary>
        /// Summarizes automatic outlook inclusion across currently usable API lanes. An external outlook
        /// override bypasses the automatic feature gate. Mixed lanes remain conditional, while no usable
        /// lane has no current request shape at all.
        /// </summary>
        public static DiaryPawnProfileOutlookPreviewMode DecideOutlookPreview(
            bool externalPsychotypeOverrideActive,
            int activeLaneCount,
            int lanesAllowingAutomaticPsychotype)
        {
            int laneCount = Math.Max(0, activeLaneCount);
            int allowingCount = Math.Max(0, Math.Min(laneCount, lanesAllowingAutomaticPsychotype));
            DiaryPawnProfileOutlookPreviewMode outlookMode;
            if (laneCount == 0)
            {
                outlookMode = DiaryPawnProfileOutlookPreviewMode.NoActiveLane;
            }
            else if (externalPsychotypeOverrideActive)
            {
                outlookMode = DiaryPawnProfileOutlookPreviewMode.Included;
            }
            else if (allowingCount > 0 && allowingCount < laneCount)
            {
                outlookMode = DiaryPawnProfileOutlookPreviewMode.LaneDependent;
            }
            else
            {
                outlookMode = allowingCount == 0
                    ? DiaryPawnProfileOutlookPreviewMode.Omitted
                    : DiaryPawnProfileOutlookPreviewMode.Included;
            }

            return outlookMode;
        }

        /// <summary>Combines the lane summary with a read-only adapter's exact stage-change predicates.</summary>
        public static DiaryPawnProfilePreviewDecision DecidePreview(
            DiaryPawnProfileOutlookPreviewMode outlookMode,
            bool writingStyleMayChange,
            bool psychotypeMayChange)
        {
            return new DiaryPawnProfilePreviewDecision
            {
                automaticVoiceMayChange = writingStyleMayChange || psychotypeMayChange,
                outlookMode = outlookMode
            };
        }

        /// <summary>
        /// Mirrors the assignment/restage branches of generation's voice-stage maintenance using only a
        /// read-only facts snapshot. A legacy unstamped style is retained; an unstamped Neutral outlook
        /// is stable while other legacy outlook states are conservatively provisional; a missing record
        /// assigns each effective automatic layer on creation.
        /// </summary>
        public static DiaryVoiceStagePreviewSnapshot DecideVoiceStagePreview(
            DiaryVoiceStagePreviewFacts facts)
        {
            if (!facts.recordExists)
            {
                return new DiaryVoiceStagePreviewSnapshot
                {
                    writingStyleMayChange = facts.writingStyleManagedAutomatically,
                    psychotypeMayChange = facts.psychotypeManagedAutomatically
                };
            }

            bool legacyUnstamped = !facts.bandStamped;
            bool bandChanged = facts.bandStamped && !facts.bandMatches;
            DiaryVoiceStagePreviewSnapshot snapshot = new DiaryVoiceStagePreviewSnapshot
            {
                writingStyleMayChange = facts.writingStyleManagedAutomatically
                    && !legacyUnstamped
                    && (bandChanged || !facts.writingStyleSet)
            };

            if (!facts.psychotypeManagedAutomatically)
            {
                return snapshot;
            }

            snapshot.psychotypeMayChange = legacyUnstamped
                ? !facts.psychotypeIsNeutral
                : !facts.psychotypeSet || bandChanged;
            return snapshot;
        }
    }
}
