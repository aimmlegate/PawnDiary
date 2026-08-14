// Focused pure tests for the Pawn Diary profile generation-switch transition. These pin the one
// confirmation boundary independently of RimWorld UI and save-state adapters.
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryPawnProfilePolicy()
        {
            AssertProfileDecision(
                "enabled unchanged ignores backlog",
                DiaryPawnProfileGenerationDecision.Unchanged,
                true,
                true,
                7);
            AssertProfileDecision(
                "disabled unchanged ignores backlog",
                DiaryPawnProfileGenerationDecision.Unchanged,
                false,
                false,
                7);
            AssertProfileDecision(
                "enabled to disabled applies directly",
                DiaryPawnProfileGenerationDecision.Disable,
                true,
                false,
                7);
            AssertProfileDecision(
                "disabled to enabled without backlog applies directly",
                DiaryPawnProfileGenerationDecision.EnableDirect,
                false,
                true,
                0);
            AssertProfileDecision(
                "disabled to enabled with backlog confirms",
                DiaryPawnProfileGenerationDecision.EnableWithConfirmation,
                false,
                true,
                1);
            AssertProfileDecision(
                "negative corrupt backlog normalizes to empty",
                DiaryPawnProfileGenerationDecision.EnableDirect,
                false,
                true,
                -12);

            AssertOutlookPreview(
                "all Full/Balanced lanes include automatic outlook",
                DiaryPawnProfileOutlookPreviewMode.Included,
                false,
                2,
                2);
            AssertOutlookPreview(
                "all Compact lanes omit automatic outlook",
                DiaryPawnProfileOutlookPreviewMode.Omitted,
                false,
                2,
                0);
            AssertOutlookPreview(
                "mixed lane overrides remain conditional",
                DiaryPawnProfileOutlookPreviewMode.LaneDependent,
                false,
                2,
                1);
            AssertOutlookPreview(
                "no usable lane remains conditional",
                DiaryPawnProfileOutlookPreviewMode.NoActiveLane,
                false,
                0,
                0);
            AssertOutlookPreview(
                "external outlook bypasses Compact lane gate",
                DiaryPawnProfileOutlookPreviewMode.Included,
                true,
                2,
                0);
            AssertOutlookPreview(
                "external outlook still has no writer without an active lane",
                DiaryPawnProfileOutlookPreviewMode.NoActiveLane,
                true,
                0,
                0);
            AssertApiLanePreview(
                "prompt-test mode uses one synthetic Full request",
                1,
                1,
                true,
                false,
                true,
                0,
                0);
            AssertApiLanePreview(
                "prompt-test Compact ignores configured lane overrides",
                1,
                0,
                true,
                false,
                false,
                2,
                1);
            AssertApiLanePreview(
                "empty configured list previews the default global lane",
                1,
                1,
                false,
                true,
                true,
                0,
                0);
            AssertApiLanePreview(
                "configured mixed lanes retain their own counts",
                2,
                1,
                false,
                false,
                false,
                2,
                1);

            AssertPreviewProvisional("stable saved voice", false, false, false);
            AssertPreviewProvisional("style assignment pending", true, true, false);
            AssertPreviewProvisional("outlook restage pending", true, false, true);

            AssertDraftPin("unchanged unpinned layer stays automatic", false, false, false, false, false, false);
            AssertDraftPin("changed base pins layer", true, false, false, true, false, false);
            AssertDraftPin("new nonblank custom rule pins layer", true, false, false, false, true, true);
            AssertDraftPin("typed then cleared custom rule stays automatic", false, false, false, false, true, false);
            AssertDraftPin("explicit unpin wins over a changed base", false, false, true, true, false, false);
            AssertVoiceWritePlan(
                "recordless custom-only edit pins without replacing automatic base",
                true,
                false,
                false,
                false,
                false,
                false,
                true,
                true);
            AssertVoiceWritePlan(
                "explicit pin-only transition persists displayed base",
                true,
                true,
                false,
                true,
                true,
                false,
                false,
                false);
            AssertVoiceWritePlan(
                "base choice persists and pins without explicit pin toggle",
                true,
                true,
                false,
                false,
                false,
                true,
                false,
                false);
            AssertVoiceWritePlan(
                "explicit unpin remains authoritative after a base choice",
                false,
                true,
                false,
                false,
                true,
                true,
                false,
                false);

            AssertVoiceStagePreview(
                "recordless automatic layers will be assigned",
                true,
                true,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = false,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true
                });
            AssertVoiceStagePreview(
                "stable saved adult voice stays exact",
                false,
                false,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = true,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true,
                    bandStamped = true,
                    bandMatches = true,
                    writingStyleSet = true,
                    psychotypeSet = true
                });
            AssertVoiceStagePreview(
                "stale saved band restages both automatic layers",
                true,
                true,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = true,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true,
                    bandStamped = true,
                    bandMatches = false,
                    writingStyleSet = true,
                    psychotypeSet = true
                });
            AssertVoiceStagePreview(
                "established legacy Neutral voice is already truthful",
                false,
                false,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = true,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true,
                    bandStamped = false,
                    writingStyleSet = true,
                    psychotypeSet = true,
                    psychotypeIsNeutral = true
                });
            AssertVoiceStagePreview(
                "unstamped missing legacy style receives a fallback",
                true,
                false,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = true,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true,
                    bandStamped = false,
                    writingStyleSet = false,
                    psychotypeSet = true,
                    psychotypeIsNeutral = true
                });
            AssertVoiceStagePreview(
                "unstamped empty legacy outlook stays conservatively provisional",
                false,
                true,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = true,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true,
                    bandStamped = false,
                    writingStyleSet = true,
                    psychotypeSet = false,
                    psychotypeIsNeutral = false
                });
            AssertVoiceStagePreview(
                "unstamped non-Neutral legacy outlook stays conservatively provisional",
                false,
                true,
                new DiaryVoiceStagePreviewFacts
                {
                    recordExists = true,
                    writingStyleManagedAutomatically = true,
                    psychotypeManagedAutomatically = true,
                    bandStamped = false,
                    writingStyleSet = true,
                    psychotypeSet = true,
                    psychotypeIsNeutral = false
                });
        }

        private static void AssertProfileDecision(
            string label,
            DiaryPawnProfileGenerationDecision expected,
            bool originalEnabled,
            bool draftEnabled,
            int backlogCount)
        {
            assertions++;
            DiaryPawnProfileGenerationDecision actual =
                DiaryPawnProfilePolicy.DecideGenerationChange(
                    originalEnabled,
                    draftEnabled,
                    backlogCount);
            if (actual != expected)
            {
                throw new System.Exception(
                    label + ": expected " + expected + ", got " + actual);
            }
        }

        private static void AssertOutlookPreview(
            string label,
            DiaryPawnProfileOutlookPreviewMode expected,
            bool externalOverride,
            int activeLaneCount,
            int lanesAllowingPsychotypes)
        {
            assertions++;
            DiaryPawnProfileOutlookPreviewMode actual =
                DiaryPawnProfilePolicy.DecideOutlookPreview(
                    externalOverride,
                    activeLaneCount,
                    lanesAllowingPsychotypes);
            if (actual != expected)
            {
                throw new System.Exception(label + ": expected " + expected + ", got " + actual);
            }
        }

        private static void AssertPreviewProvisional(
            string label,
            bool expected,
            bool writingStyleMayChange,
            bool psychotypeMayChange)
        {
            assertions++;
            DiaryPawnProfilePreviewDecision actual = DiaryPawnProfilePolicy.DecidePreview(
                DiaryPawnProfileOutlookPreviewMode.Included,
                writingStyleMayChange,
                psychotypeMayChange);
            if (actual.automaticVoiceMayChange != expected)
            {
                throw new System.Exception(
                    label + ": expected provisional=" + expected
                    + ", got " + actual.automaticVoiceMayChange);
            }
        }

        private static void AssertApiLanePreview(
            string label,
            int expectedActiveLaneCount,
            int expectedLanesAllowingPsychotype,
            bool promptTestMode,
            bool configuredLaneListMissingOrEmpty,
            bool globalContextAllowsPsychotype,
            int activeConfiguredLaneCount,
            int configuredLanesAllowingPsychotype)
        {
            assertions++;
            DiaryPawnProfileApiLaneSnapshot actual =
                DiaryPawnProfilePolicy.DecideApiLanePreview(
                    promptTestMode,
                    configuredLaneListMissingOrEmpty,
                    globalContextAllowsPsychotype,
                    activeConfiguredLaneCount,
                    configuredLanesAllowingPsychotype);
            if (actual.activeLaneCount != expectedActiveLaneCount
                || actual.lanesAllowingAutomaticPsychotype != expectedLanesAllowingPsychotype)
            {
                throw new System.Exception(
                    label + ": expected active/allowing="
                    + expectedActiveLaneCount + "/" + expectedLanesAllowingPsychotype
                    + ", got " + actual.activeLaneCount + "/"
                    + actual.lanesAllowingAutomaticPsychotype);
            }
        }

        private static void AssertDraftPin(
            string label,
            bool expected,
            bool draftPinned,
            bool pinChoiceExplicitlyEdited,
            bool baseChanged,
            bool customChanged,
            bool customHasText)
        {
            assertions++;
            bool actual = DiaryPawnProfilePolicy.ResolveDraftPin(
                draftPinned,
                pinChoiceExplicitlyEdited,
                baseChanged,
                customChanged,
                customHasText);
            if (actual != expected)
            {
                throw new System.Exception(label + ": expected pin=" + expected + ", got " + actual);
            }
        }

        private static void AssertVoiceWritePlan(
            string label,
            bool expectedPinned,
            bool expectedPersistBase,
            bool originalPinned,
            bool draftPinned,
            bool pinChoiceExplicitlyEdited,
            bool baseChanged,
            bool customChanged,
            bool customHasText)
        {
            assertions++;
            DiaryPawnProfileVoiceWritePlan actual = DiaryPawnProfilePolicy.PlanVoiceWrite(
                originalPinned,
                draftPinned,
                pinChoiceExplicitlyEdited,
                baseChanged,
                customChanged,
                customHasText);
            if (actual.pinned != expectedPinned
                || actual.persistDisplayedBase != expectedPersistBase)
            {
                throw new System.Exception(
                    label + ": expected pin/persist-base="
                    + expectedPinned + "/" + expectedPersistBase
                    + ", got " + actual.pinned + "/" + actual.persistDisplayedBase);
            }
        }

        private static void AssertVoiceStagePreview(
            string label,
            bool expectedWritingStyleChange,
            bool expectedPsychotypeChange,
            DiaryVoiceStagePreviewFacts facts)
        {
            assertions++;
            DiaryVoiceStagePreviewSnapshot actual =
                DiaryPawnProfilePolicy.DecideVoiceStagePreview(facts);
            if (actual.writingStyleMayChange != expectedWritingStyleChange
                || actual.psychotypeMayChange != expectedPsychotypeChange)
            {
                throw new System.Exception(
                    label + ": expected style/outlook="
                    + expectedWritingStyleChange + "/" + expectedPsychotypeChange
                    + ", got " + actual.writingStyleMayChange + "/" + actual.psychotypeMayChange);
            }
        }
    }
}
