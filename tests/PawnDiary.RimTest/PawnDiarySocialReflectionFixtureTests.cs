// Loaded-contract fixture for Quality Wave H7 social reflections. These checks never submit an LLM
// request and never mutate a pawn or the game clock: they inspect the loaded XML Defs, the pure
// capture/formatting contracts, and a constructed event's prompt-routing policy. This complements
// the standalone no-RimWorld tests by proving the actual in-game catalogs are wired to those pure
// seams.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Pins H7's loaded feature/group/template/event-prompt contracts, both-pawn eligibility gates,
    /// five localized beauty bands, dedicated 200-token route, hidden IDs, and central reflection
    /// exclusion. No test calls the live generator.
    /// </summary>
    [TestSuite]
    public static class PawnDiarySocialReflectionFixtureTests
    {
        /// <summary>The shipped XML rows load, validate, and preserve the independent source toggles.</summary>
        [Test]
        public static void LoadedDefsExposeReviewedSocialReflectionPolicy()
        {
            DiarySocialReflectionDef feature =
                DefDatabase<DiarySocialReflectionDef>.GetNamedSilentFail("Diary_SocialReflection");
            PawnDiaryRimTestScope.Require(
                feature != null,
                "The shipped Diary_SocialReflection tuning Def was not loaded.");

            List<string> errors = feature.ConfigErrors()
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .ToList();
            PawnDiaryRimTestScope.Require(
                errors.Count == 0,
                "Diary_SocialReflection reported ConfigErrors: " + string.Join(" | ", errors));

            SocialReflectionPolicySnapshot policy = DiarySocialReflectionPolicy.Snapshot();
            PawnDiaryRimTestScope.Require(
                policy.enabled,
                "The shipped H7 feature policy must be enabled by default.");
            PawnDiaryRimTestScope.Require(
                policy.chanceBands != null && policy.chanceBands.Count == 4,
                "The loaded H7 policy must carry four contiguous absolute-opinion chance bands.");
            PawnDiaryRimTestScope.Require(
                policy.styles != null && policy.styles.Count == 3,
                "The loaded H7 policy must carry positive, neutral, and negative styles.");
            PawnDiaryRimTestScope.Require(
                policy.maximumOpinionReasons == 3 && policy.maximumMemoryLines == 2,
                "The loaded H7 qualitative-reason/memory caps drifted from 3/2.");
            PawnDiaryRimTestScope.Require(
                policy.pairCooldownTicks == 900000
                    && policy.writerCooldownTicks == 180000
                    && policy.minimumDelayTicks == 15000
                    && policy.maximumDelayTicks == 60000
                    && policy.retryIntervalTicks == 2500
                    && policy.proseGraceTicks == 60000,
                "The loaded H7 15-day/3-day/6-24-hour/hourly/24-hour timing contract drifted.");
            PawnDiaryRimTestScope.Require(
                policy.maximumHandledSourceRows >= policy.maximumPendingRows,
                "Handled-source ownership must be able to retain every accepted pending row.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(policy.veryUnattractiveLabel)
                    && !string.IsNullOrWhiteSpace(policy.unattractiveLabel)
                    && !string.IsNullOrWhiteSpace(policy.ordinaryAppearanceLabel)
                    && !string.IsNullOrWhiteSpace(policy.attractiveLabel)
                    && !string.IsNullOrWhiteSpace(policy.veryAttractiveLabel),
                "The five loaded H7 beauty labels require localized DefInjected text.");

            // Source eligibility is XML-owned. Meaningful two-colonist exchanges opt in; routine
            // small talk remains excluded. The reflection itself has its own independent toggle.
            DiaryInteractionGroupDef heartfelt = InteractionGroups.ByKey("heartfelt");
            DiaryInteractionGroupDef smallTalk = InteractionGroups.ByKey("smalltalk");
            DiaryInteractionGroupDef reflection = InteractionGroups.ByKey("socialReflection");
            PawnDiaryRimTestScope.Require(
                heartfelt != null && heartfelt.socialReflectionEligible,
                "The meaningful heartfelt source group did not opt into H7.");
            PawnDiaryRimTestScope.Require(
                smallTalk != null && !smallTalk.socialReflectionEligible,
                "Routine small talk must remain excluded from H7 opportunities.");
            PawnDiaryRimTestScope.Require(
                reflection != null
                    && reflection.defaultEnabled
                    && reflection.important
                    && reflection.domain == GroupDomain.Reflection,
                "The socialReflection group must be an enabled, important, independently toggleable "
                    + "Reflection-domain row.");

            string[] expectedEligible =
            {
                "anomaly",
                "counsel",
                "conversion",
                "heartfelt",
                "insults",
                "recruit",
                "romance",
                "slavery",
                "strangechat",
                "trial"
            };
            string[] actualEligible = InteractionGroups.All
                .Where(group => group != null && group.socialReflectionEligible)
                .Select(group => group.defName)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            PawnDiaryRimTestScope.Require(
                actualEligible.SequenceEqual(expectedEligible),
                "The loaded H7 allowlist must be exactly the ten reviewed social groups. Got: "
                    + string.Join(", ", actualEligible));
        }

        /// <summary>The catalog requires both pawns and every ordinary capture gate.</summary>
        [Test]
        public static void CapturePolicyRequiresBothEligiblePawnsAndFeatureGates()
        {
            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.SocialReflection);
            PawnDiaryRimTestScope.Require(
                spec != null && spec.EventType == DiaryEventType.SocialReflection,
                "DiaryEventCatalog did not register SocialReflectionEventSpec.");

            SocialReflectionEventData data = ValidData();
            CaptureContext context = ValidContext();
            RequireDecision(
                "all H7 eligibility gates",
                CaptureDecision.GenerateSolo,
                SocialReflectionEventData.Decide(data, context));

            data.WriterEligible = false;
            RequireDecision(
                "writer eligibility",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, context));
            data.WriterEligible = true;

            data.SubjectEligible = false;
            RequireDecision(
                "subject eligibility",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, context));
            data.SubjectEligible = true;

            data.FeatureEnabled = false;
            RequireDecision(
                "feature toggle",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, context));
            data.FeatureEnabled = true;

            context.Eligible = false;
            RequireDecision(
                "capture eligibility",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, context));
            context.Eligible = true;

            context.UserEnabled = false;
            RequireDecision(
                "group user toggle",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, context));
            context.UserEnabled = true;

            context.SignalEnabled = false;
            RequireDecision(
                "signal toggle",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, context));

            data.SubjectPawnId = data.WriterPawnId;
            RequireDecision(
                "distinct subject identity",
                CaptureDecision.Drop,
                SocialReflectionEventData.Decide(data, ValidContext()));
        }

        /// <summary>The loaded qualitative labels drive all five beauty bands without score leakage.</summary>
        [Test]
        public static void LoadedBeautyBandsFormatQualitatively()
        {
            SocialReflectionPolicySnapshot policy = DiarySocialReflectionPolicy.Snapshot();
            float[] samples =
            {
                policy.veryUnattractiveMaximum,
                (policy.veryUnattractiveMaximum + policy.unattractiveMaximum) / 2f,
                (policy.unattractiveMaximum + policy.attractiveMinimum) / 2f,
                policy.attractiveMinimum,
                policy.veryAttractiveMinimum
            };
            string[] labels =
            {
                policy.veryUnattractiveLabel,
                policy.unattractiveLabel,
                policy.ordinaryAppearanceLabel,
                policy.attractiveLabel,
                policy.veryAttractiveLabel
            };

            for (int index = 0; index < samples.Length; index++)
            {
                SocialReflectionFormattedContext formatted =
                    SocialReflectionContextFormatter.Format(
                        new SocialReflectionSubjectFacts
                        {
                            displayName = "Mira",
                            biologicalAgeYears = 27,
                            lifeStageLabel = "adult",
                            beauty = samples[index],
                            primaryRelationLabel = "friend",
                            opinion = 83,
                            opinionBandLabel = "warmly regarded",
                            socialReasons = new List<string> { "shared trust" }
                        },
                        policy);
                PawnDiaryRimTestScope.Require(
                    string.Equals(
                        formatted.subjectAppearance,
                        labels[index],
                        StringComparison.Ordinal),
                    "Loaded H7 beauty band " + index + " rendered '"
                        + formatted.subjectAppearance + "' instead of its localized label.");

                string visible = string.Join(
                    "|",
                    new[]
                    {
                        formatted.subjectAppearance,
                        formatted.subjectSentiment,
                        formatted.subjectReasons,
                        formatted.polarity,
                        formatted.tone,
                        formatted.colorCue
                    });
                PawnDiaryRimTestScope.Require(
                    visible.IndexOf("83", StringComparison.Ordinal) < 0,
                    "The H7 formatter leaked a raw opinion score into prompt-visible fields.");
            }
        }

        /// <summary>
        /// A constructed saved event resolves the exact loaded H7 prompt rows, not generic SoloImportant.
        /// Hidden source/subject IDs remain available to adapters but never render into the prompt.
        /// </summary>
        [Test]
        public static void DedicatedTemplateRoutesImportantReflectionAtTwoHundredTokens()
        {
            DiaryPromptTemplateDef template =
                DefDatabase<DiaryPromptTemplateDef>.GetNamedSilentFail(
                    "DiaryPromptTemplate_SoloSocialReflection");
            DiaryEventPromptDef eventPrompt =
                DefDatabase<DiaryEventPromptDef>.GetNamedSilentFail(
                    "DiaryEventPrompt_SocialReflection");
            PawnDiaryRimTestScope.Require(
                template != null
                    && string.Equals(
                        template.templateKey,
                        DiaryPromptTemplates.SoloSocialReflection,
                        StringComparison.Ordinal)
                    && template.maxTokens == 200
                    && !template.appendDirectSpeechInstruction,
                "The loaded SoloSocialReflection template is missing or lost its 200-token/no-speech policy.");
            PawnDiaryRimTestScope.Require(
                eventPrompt != null
                    && string.Equals(
                        eventPrompt.eventType,
                        SocialReflectionEventData.DefNameToken,
                        StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(eventPrompt.prompt)
                    && !string.IsNullOrWhiteSpace(eventPrompt.enhancement),
                "The exact PawnDiary_SocialReflection event-prompt policy was not loaded.");

            List<DiaryPromptFieldDef> fields = template.fields ?? new List<DiaryPromptFieldDef>();
            PawnDiaryRimTestScope.Require(
                fields.Count == 21,
                "SoloSocialReflection must keep its reviewed 21-field shape; got " + fields.Count + ".");
            PawnDiaryRimTestScope.Require(
                fields.Count(field => field != null
                    && string.Equals(
                        field.source,
                        MemoryContextPrompt.Source,
                        StringComparison.Ordinal)) == 1,
                "SoloSocialReflection must project exactly one subject-memory field.");
            string[] hiddenKeys =
            {
                SocialReflectionEventData.SubjectIdContextKey,
                SocialReflectionEventData.SourceEventIdContextKey,
                SocialReflectionEventData.SourceKeyContextKey,
                SocialReflectionEventData.SourceTickContextKey,
                SocialReflectionEventData.SourcePlayLogIdContextKey,
                SocialReflectionEventData.SourceInteractionDefContextKey
            };
            for (int index = 0; index < hiddenKeys.Length; index++)
            {
                string hiddenKey = hiddenKeys[index];
                PawnDiaryRimTestScope.Require(
                    !fields.Any(field => field != null
                        && string.Equals(
                            field.contextKey,
                            hiddenKey,
                            StringComparison.Ordinal)),
                    "SoloSocialReflection exposed hidden context key '" + hiddenKey + "' as a prompt field.");
            }

            const string subjectId = "PDTEST_H7_SUBJECT_ID_49";
            const string sourceEventId = "PDTEST_H7_SOURCE_EVENT_ID_73";
            const string sourceKey = "PDTEST_H7_SOURCE_KEY_91";
            SocialReflectionPolicySnapshot socialPolicy = DiarySocialReflectionPolicy.Snapshot();
            SocialReflectionFormattedContext subject =
                SocialReflectionContextFormatter.Format(
                    new SocialReflectionSubjectFacts
                    {
                        displayName = "Mira",
                        biologicalAgeYears = 27,
                        lifeStageLabel = "adult",
                        beauty = 0f,
                        primaryRelationLabel = "friend",
                        opinion = 0,
                        opinionBandLabel = "neutral",
                        socialReasons = new List<string> { "worked together" }
                    },
                    socialPolicy);
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = "PDTEST_H7_EVENT",
                interactionDefName = SocialReflectionEventData.DefNameToken,
                interactionLabel = "social reflection",
                initiatorPawnId = "PDTEST_H7_WRITER_ID",
                initiatorName = "Ada",
                initiatorText = "Ada thought back to the exchange.",
                instruction = "Reflect on Mira and the earlier exchange.",
                solo = true,
                // Deliberately disagree with the neutral context's fallback cue. Regeneration must
                // project this saved event-time polarity cue instead of reclassifying later.
                colorCue = DiaryEvent.SocialFightColorCue,
                gameContext = SocialReflectionEventData.BuildGameContext(
                    "They discussed the broken greenhouse.",
                    "Under cracked glass",
                    "The rain kept tapping.",
                    subject,
                    subjectId,
                    sourceEventId,
                    sourceKey,
                    sourceTick: 12345,
                    sourcePlayLogEntryId: 73,
                    sourceInteractionDefName: "DeepTalk")
            };
            const string memorySentinel = "PDTEST_H7_SUBJECT_MEMORY";
            diaryEvent.SetMemoryContext(
                DiaryEvent.InitiatorRole,
                "- (earlier) " + memorySentinel);

            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                personaRule: string.Empty,
                psychotypeRule: string.Empty,
                promptEnchantment: string.Empty,
                humorCue: string.Empty,
                priorInitiatorEntry: null,
                entryText: null,
                titleRequest: false,
                maxTokens: 0,
                contextDetailLevel: PromptContextDetailLevel.Full);
            string routedTemplate = DiaryPromptPlanner.TemplateKeyFor(request);
            PawnDiaryRimTestScope.Require(
                request.payload.socialReflection
                    && string.Equals(
                        request.payload.domain,
                        DiaryEventDomainClassifier.Reflection,
                        StringComparison.Ordinal)
                    && string.Equals(
                        routedTemplate,
                        DiaryPipelineTemplates.SoloSocialReflection,
                        StringComparison.Ordinal),
                "The saved social_reflection marker did not route to the dedicated Reflection template.");
            PawnDiaryRimTestScope.Require(
                request.payload.display != null
                    && string.Equals(
                        request.payload.display.colorCue,
                        DiaryEvent.SocialFightColorCue,
                        StringComparison.Ordinal)
                    && string.Equals(
                        request.policy.group.colorCue,
                        DiaryEvent.SocialFightColorCue,
                        StringComparison.Ordinal),
                "H7 regeneration did not preserve the event's saved polarity cue through ToPayload.");
            PawnDiaryRimTestScope.Require(
                request.policy.group != null
                    && request.policy.group.important
                    && string.Equals(
                        request.policy.group.eventPromptKey,
                        SocialReflectionEventData.DefNameToken,
                        StringComparison.Ordinal),
                "The constructed H7 request lost its important group or exact event-prompt key.");
            PawnDiaryRimTestScope.Require(
                request.policy.Template(routedTemplate).maxTokens == 200,
                "The resolved H7 template policy did not drive the dedicated 200-token cap.");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(request.directSpeechInstruction),
                "H7 unexpectedly inherited the ordinary interaction direct-speech instruction.");

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(request);
            PawnDiaryRimTestScope.Require(
                plan != null
                    && plan.userPrompt != null
                    && plan.userPrompt.IndexOf(memorySentinel, StringComparison.Ordinal) >= 0,
                "The dedicated H7 prompt did not render its subject-specific relevant-past field.");
            for (int index = 0; index < hiddenKeys.Length; index++)
            {
                string hiddenValue;
                switch (index)
                {
                    case 0:
                        hiddenValue = subjectId;
                        break;
                    case 1:
                        hiddenValue = sourceEventId;
                        break;
                    case 2:
                        hiddenValue = sourceKey;
                        break;
                    case 3:
                        hiddenValue = "12345";
                        break;
                    case 4:
                        hiddenValue = "73";
                        break;
                    default:
                        hiddenValue = "DeepTalk";
                        break;
                }
                PawnDiaryRimTestScope.Require(
                    plan.userPrompt.IndexOf(hiddenValue, StringComparison.Ordinal) < 0,
                    "The dedicated H7 prompt leaked hidden value '" + hiddenValue + "'.");
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(
                GroupDomain.Reflection,
                SocialReflectionEventData.DefNameToken);
            PawnDiaryRimTestScope.Require(
                group != null
                    && string.Equals(group.defName, "socialReflection", StringComparison.Ordinal)
                    && group.important
                    && diaryEvent.IsImportant(),
                "PawnDiary_SocialReflection did not resolve its important socialReflection group.");
        }

        /// <summary>The shared reflection classifier rejects H7 as evidence for every recap collector.</summary>
        [Test]
        public static void SocialReflectionIsClassifiedAsReflectionEvidence()
        {
            MethodInfo classifier = typeof(DiaryGameComponent).GetMethod(
                "IsReflectionDefName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            PawnDiaryRimTestScope.Require(
                classifier != null && classifier.ReturnType == typeof(bool),
                "DiaryGameComponent.IsReflectionDefName was unavailable to the H7 exclusion fixture.");

            bool social = (bool)classifier.Invoke(
                null,
                new object[] { SocialReflectionEventData.DefNameToken });
            bool ordinary = (bool)classifier.Invoke(null, new object[] { "DeepTalk" });
            PawnDiaryRimTestScope.Require(
                social && !ordinary,
                "The central reflection classifier must reject H7 from day/quadrum/arc/H7 evidence "
                    + "without classifying an ordinary direct interaction as reflection.");
        }

        private static SocialReflectionEventData ValidData()
        {
            return new SocialReflectionEventData
            {
                PawnId = "Pawn_1",
                Tick = 1000,
                WriterPawnId = "Pawn_1",
                SubjectPawnId = "Pawn_2",
                SourceKey = "source-key",
                WriterEligible = true,
                SubjectEligible = true,
                FeatureEnabled = true
            };
        }

        private static CaptureContext ValidContext()
        {
            return new CaptureContext
            {
                Eligible = true,
                UserEnabled = true,
                SignalEnabled = true,
                AmbientSignalEnabled = true,
                Now = 1000
            };
        }

        private static void RequireDecision(
            string gate,
            CaptureDecision expected,
            CaptureDecision actual)
        {
            PawnDiaryRimTestScope.Require(
                actual == expected,
                "H7 " + gate + " expected " + expected + " but got " + actual + ".");
        }
    }
}
