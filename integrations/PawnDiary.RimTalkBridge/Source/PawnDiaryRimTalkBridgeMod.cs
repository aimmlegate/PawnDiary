// RimWorld mod entry point for the separate "PawnDiary: RimTalk bridge" adapter mod.
// It owns the bridge's saved settings (the integration-level dropdown plus advanced tunables),
// draws the settings window, and installs the Harmony listener that watches RimTalk chat.
//
// The bridge is organized in "integration levels" (see design/RIMTALK_BRIDGE_PLAN.md):
//   0 = Off             — nothing flows in either direction.
//   1 = Shared context  — diary memories are injected into RimTalk prompts; optional persona
//                         synchronization is independently off by default.
//   2 = + Conversations — selected conversations pass through the bounded editorial funnel.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using HarmonyLib;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Which mod owns the pawn persona when shared context is enabled.</summary>
    public enum PersonaSyncDirection
    {
        /// <summary>Pawn Diary's psychotype is exported to RimTalk.</summary>
        PawnDiaryToRimTalk = 0,

        /// <summary>RimTalk's persona is imported as Pawn Diary's outlook.</summary>
        RimTalkToPawnDiary = 1,

        /// <summary>Neither mod overwrites the other's persona/outlook.</summary>
        Off = 2
    }

    /// <summary>
    /// Adapter mod entry point. RimWorld creates this once when the mod list is loaded, before any
    /// game exists, which makes the constructor the right place for process-global registrations.
    /// </summary>
    public class PawnDiaryRimTalkBridgeMod : Mod
    {
        internal const string HarmonyId = "aimmlegate.pawndiary.rimtalkbridge";
        internal const string LogPrefix = "[PawnDiary: RimTalk bridge]";

        /// <summary>Saved bridge settings. Null only before the mod constructor has run.</summary>
        internal static PawnDiaryRimTalkBridgeSettings Settings;

        /// <summary>
        /// True when RimTalk (cj.rimtalk) is in the active mod list. Cached once at startup: the mod
        /// list cannot change while the game is running, and ModsConfig.IsActive walks a list on
        /// every call. Every code path that touches RimTalk types must check this first (see the
        /// "RimTalk-type isolation" rule in design/RIMTALK_BRIDGE_PLAN.md Step 0).
        /// </summary>
        internal static bool RimTalkActive;

        // Text buffers for the numeric settings fields. RimWorld's numeric text fields need a string
        // buffer that survives between frames while the player is typing (half-typed numbers are not
        // valid ints yet). They are UI state only and never saved.
        private string contextEntryCountBuffer;
        private string colonyEventCountBuffer;
        private string conversationQuietTicksBuffer;

        // Scroll state for the settings window. The bridge now has enough rows (level chooser + the
        // advanced toggles and numeric tunables) to overflow the fixed mod-settings rect, especially at
        // larger UI scales or with longer translations, which would clip the bottom controls. The view
        // height self-corrects to the measured content height after the first frame.
        private Vector2 settingsScrollPosition;
        private float settingsViewHeight = 720f;
        private string perPawnDailyCapBuffer;
        private string colonyDailyCapBuffer;
        private string pairMinGapTicksBuffer;
        private string transcriptLineCapBuffer;

        // Multiline editor buffers deliberately live outside ModSettings. Half-written CSV/prompt
        // text must not enter the saved settings until the pure validator accepts it.
        private string reactionTermsBuffer;
        private string assessmentPromptBuffer;

        public PawnDiaryRimTalkBridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiaryRimTalkBridgeSettings>();
            RimTalkActive = ModsConfig.IsActive("cj.rimtalk");

            if (!RimTalkActive)
            {
                // About.xml declares the RimTalk dependency, but RimWorld does not hard-enforce it.
                // Warn once and idle instead of erroring: every bridge feature checks RimTalkActive.
                Log.WarningOnce(
                    LogPrefix + " RimTalk (cj.rimtalk) is not in the active mod list; the bridge stays idle.",
                    "PawnDiaryRimTalkBridge.RimTalkMissing".GetHashCode());
                return;
            }

            // Install the fragile displayed-chat boundary independently and publish its actual health
            // to core. The ambient RimTalk XML fallback suppresses itself only while this exact hook is
            // ready, so an upstream rename degrades to ambient capture instead of total capture loss.
            Harmony harmony = new Harmony(HarmonyId);
            bool displayedConversationCaptureReady = RimTalkCreateInteractionPatch.TryRegister(harmony);
            PawnDiaryApi.SetCaptureCapabilityReady(
                BridgeIds.DisplayedConversationCaptureCapability,
                displayedConversationCaptureReady);
            RefreshConversationFallbackGate();

            // The editor UI is an optional convenience and uses a fragile external type. Register it
            // separately so a RimTalk UI rename cannot abort or disable conversation capture patches.
            RimTalkPersonaEditorOwnershipPatch.TryRegister(harmony);

            // Registrations whose RimTalk metadata calls .Translate() are intentionally deferred to
            // RimTalkBridgeTranslatedRegistration below. Mod constructors run before RimWorld has an
            // active language, so translating here logs "No active language" and stores broken labels.
            try
            {
                RecentDiaryEventCache.Register();          // Level 2: native/other-adapter assessment context
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to register the assessment status listener; recent-event context is disabled: " + e);
            }

            Log.Message(LogPrefix + " initialized.");
        }

        /// <summary>
        /// True when the saved integration level is at least <paramref name="level"/>. Null-safe so
        /// callbacks that can fire before the mod constructor (or from other mods) never throw.
        /// </summary>
        /// <param name="level">Level to test: 1 = Shared context, 2 = Conversations.</param>
        internal static bool LevelAtLeast(int level)
        {
            PawnDiaryRimTalkBridgeSettings settings = Settings;
            return settings != null && settings.integrationLevel >= level;
        }

        /// <summary>Returns the settings-list label shown by RimWorld's mod options menu.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiaryRimTalkBridge.Settings.Category".Translate();
        }

        /// <summary>
        /// Saves the settings, then reconciles dependent runtime state so a change
        /// made from the main menu (no game loaded, so no tick pass runs) takes effect immediately —
        /// including removing the entry when the player turns the feature off. SyncAutoInject is a
        /// cheap no-op when nothing changed.
        /// </summary>
        public override void WriteSettings()
        {
            // Do the validation pass before Scribe writes ModSettings. The editor updates Settings
            // only after a valid edit, so an invalid half-written CSV leaves the last valid value in
            // place and can never enter the settings XML.
            if (reactionTermsBuffer != null)
            {
                ConversationAssessmentPolicyDef policy = ConversationAssessmentPolicyDef.Current;
                ConversationReactionTermsValidationResult validation =
                    ConversationReactionTermsEditor.Validate(
                        reactionTermsBuffer,
                        policy.maxEditableReactionTerms,
                        policy.maxEditableReactionTermChars);
                if (!validation.IsValid)
                {
                    Messages.Message(
                        "PawnDiaryRimTalkBridge.Settings.ReactionTermsNotSaved".Translate(
                            ReactionTermsValidationMessage(validation)),
                        MessageTypeDefOf.RejectInput,
                        false);
                }
            }

            base.WriteSettings();

            // If the rich hook has drifted, Levels 0/1 still mean intentionally no chat capture;
            // switching to Level 2 releases that policy claim so core ambient XML can take over.
            RefreshConversationFallbackGate();
        }

        /// <summary>Draws the bridge settings: the level chooser, then the advanced block. Wrapped in a
        /// scroll view so the full list stays reachable when it is taller than the settings window.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 24px leaves room for the scrollbar so the rightmost content is not hidden under it.
            // A Listing starts a new column when its content passes the listing rect's height unless
            // maxOneColumn is set. That behavior is useful for menus, but fatal inside this vertical
            // scroll view: the extra columns land beyond viewRect.xMax, and CurHeight then measures only
            // the final column. The measured canvas consequently shrinks every frame until nearly every
            // control is off-screen. Keep the canvas at least viewport-height and force one long column.
            float viewHeight = Mathf.Max(inRect.height, settingsViewHeight);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 24f, viewHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect, true);

            Listing_Standard listing = new Listing_Standard
            {
                maxOneColumn = true
            };
            listing.Begin(viewRect);

            if (!RimTalkActive)
            {
                // Everything below still saves fine; the note explains why nothing happens in-game.
                GUI.color = Color.yellow;
                listing.Label("PawnDiaryRimTalkBridge.Settings.RimTalkMissing".Translate());
                GUI.color = Color.white;
                listing.Gap();
            }

            // Integration level: three mutually exclusive radio rows (cumulative levels).
            listing.Label("PawnDiaryRimTalkBridge.Settings.LevelSection".Translate());
            if (listing.RadioButton(
                "PawnDiaryRimTalkBridge.Settings.LevelOff".Translate(),
                Settings.integrationLevel == 0,
                8f,
                "PawnDiaryRimTalkBridge.Settings.LevelOffDesc".Translate()))
            {
                Settings.integrationLevel = 0;
            }

            if (listing.RadioButton(
                "PawnDiaryRimTalkBridge.Settings.LevelSharedContext".Translate(),
                Settings.integrationLevel == 1,
                8f,
                "PawnDiaryRimTalkBridge.Settings.LevelSharedContextDesc".Translate()))
            {
                Settings.integrationLevel = 1;
            }

            if (listing.RadioButton(
                "PawnDiaryRimTalkBridge.Settings.LevelConversations".Translate(),
                Settings.integrationLevel == 2,
                8f,
                "PawnDiaryRimTalkBridge.Settings.LevelConversationsDesc".Translate()))
            {
                Settings.integrationLevel = 2;
            }

            listing.GapLine();
            listing.Label("PawnDiaryRimTalkBridge.Settings.AdvancedSection".Translate());

            bool includeVoice = Settings.includeDiaryVoiceLine;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.IncludeDiaryVoiceLine".Translate(),
                ref includeVoice,
                "PawnDiaryRimTalkBridge.Settings.IncludeDiaryVoiceLineDesc".Translate());
            Settings.includeDiaryVoiceLine = includeVoice;

            // Feature 1: colony situation ({{colony_events}}). Off by default (overlaps RimTalk event mods).
            bool colonyContext = Settings.injectColonyContext;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.InjectColonyContext".Translate(),
                ref colonyContext,
                "PawnDiaryRimTalkBridge.Settings.InjectColonyContextDesc".Translate());
            Settings.injectColonyContext = colonyContext;

            listing.Label("PawnDiaryRimTalkBridge.Settings.PersonaDirection".Translate());
            if (listing.RadioButton("PawnDiaryRimTalkBridge.Settings.PersonaOff".Translate(),
                Settings.personaSyncDirection == PersonaSyncDirection.Off, 8f))
            {
                Settings.personaSyncDirection = PersonaSyncDirection.Off;
            }
            if (listing.RadioButton("PawnDiaryRimTalkBridge.Settings.PersonaToRimTalk".Translate(),
                Settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk, 8f))
            {
                Settings.personaSyncDirection = PersonaSyncDirection.PawnDiaryToRimTalk;
            }
            if (listing.RadioButton("PawnDiaryRimTalkBridge.Settings.PersonaFromRimTalk".Translate(),
                Settings.personaSyncDirection == PersonaSyncDirection.RimTalkToPawnDiary, 8f))
            {
                Settings.personaSyncDirection = PersonaSyncDirection.RimTalkToPawnDiary;
            }
            bool transformPersona = Settings.transformPersonaWithLlm;
            listing.CheckboxLabeled("PawnDiaryRimTalkBridge.Settings.TransformPersona".Translate(),
                ref transformPersona, "PawnDiaryRimTalkBridge.Settings.TransformPersonaDesc".Translate());
            Settings.transformPersonaWithLlm = transformPersona;
            if (Settings.transformPersonaWithLlm
                && Settings.personaSyncDirection != PersonaSyncDirection.Off)
            {
                DrawPersonaTransformDataDisclosure(listing);
            }

            // Level-2 selection mode. Semantic mode uses the core one-shot completion API; local-only
            // mode spends no extra assessment request and applies the stricter XML threshold.
            bool semanticAssessment = Settings.useSemanticConversationAssessment;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.SemanticAssessment".Translate(),
                ref semanticAssessment,
                "PawnDiaryRimTalkBridge.Settings.SemanticAssessmentDesc".Translate());
            Settings.useSemanticConversationAssessment = semanticAssessment;

            ConversationAssessmentPolicyDef assessmentPolicy = ConversationAssessmentPolicyDef.Current;
            DrawReactionTermsEditor(listing, assessmentPolicy);

            if (Settings.useSemanticConversationAssessment)
            {
                DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();
                Rect laneRect = listing.GetRect(28f);
                float laneLabelWidth = Mathf.Min(190f, laneRect.width * 0.44f);
                Rect laneLabelRect = new Rect(laneRect.x, laneRect.y, laneLabelWidth, laneRect.height);
                Rect laneButtonRect = new Rect(
                    laneLabelRect.xMax + 8f,
                    laneRect.y,
                    laneRect.width - laneLabelWidth - 8f,
                    laneRect.height);
                Widgets.Label(laneLabelRect, "PawnDiaryRimTalkBridge.Settings.AssessmentLane".Translate());
                if (Widgets.ButtonText(laneButtonRect, CurrentAssessmentLaneLabel(setup)))
                {
                    Find.WindowStack.Add(new FloatMenu(BuildAssessmentLaneOptions(setup)));
                }

                if (setup == null || setup.activeLaneCount <= 0)
                {
                    Color previous = GUI.color;
                    GUI.color = Color.yellow;
                    listing.Label("PawnDiaryRimTalkBridge.Settings.NoAssessmentLane".Translate());
                    GUI.color = previous;
                }

                DrawAssessmentPromptEditor(listing, assessmentPolicy);
            }

            bool devLogging = Settings.devChatLogging;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.DevChatLogging".Translate(),
                ref devLogging,
                "PawnDiaryRimTalkBridge.Settings.DevChatLoggingDesc".Translate());
            Settings.devChatLogging = devLogging;

            listing.Gap();

            // Numeric tunables. Provisional defaults (plan item U2) — meant for playtest tuning.
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.ContextEntryCount".Translate(),
                ref Settings.contextEntryCount, ref contextEntryCountBuffer, 0f, 10f);
            if (Settings.injectColonyContext)
            {
                listing.TextFieldNumericLabeled(
                    "PawnDiaryRimTalkBridge.Settings.ColonyEventCount".Translate(),
                    ref Settings.colonyEventCount, ref colonyEventCountBuffer, 0f, 6f);
            }
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.ConversationQuietTicks".Translate(),
                ref Settings.conversationQuietTicks, ref conversationQuietTicksBuffer, 250f, 60000f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.PerPawnDailyCap".Translate(),
                ref Settings.perPawnDailyCap, ref perPawnDailyCapBuffer, 0f, 1f);
            listing.Label("PawnDiaryRimTalkBridge.Settings.PawnCooldownInfo".Translate(
                assessmentPolicy.perPawnConversationCooldownTicks));
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.ColonyDailyCap".Translate(),
                ref Settings.colonyDailyCap, ref colonyDailyCapBuffer, 0f, 999f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.PairMinGapTicks".Translate(),
                ref Settings.pairMinGapTicks, ref pairMinGapTicksBuffer, 0f, 600000f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.TranscriptLineCap".Translate(),
                ref Settings.transcriptLineCap, ref transcriptLineCapBuffer, 0f, 16f);

            // Remember the measured height so next frame's scroll view is sized to the real content
            // (rows are gated, so the total varies with which features are on). +12 keeps a little slack.
            settingsViewHeight = listing.CurHeight + 12f;
            listing.End();
            Widgets.EndScrollView();
        }

        // Shows the exact source payload shape beside the active LLM persona option. Settings can be
        // opened without a loaded game, so this is a schema/privacy disclosure rather than one pawn's
        // live values. The two directions really do send different userText strings (see PersonaSync).
        private static void DrawPersonaTransformDataDisclosure(Listing_Standard listing)
        {
            string bodyKey = Settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk
                ? "PawnDiaryRimTalkBridge.Settings.DataSent.Export"
                : "PawnDiaryRimTalkBridge.Settings.DataSent.Import";

            listing.Gap(4f);
            listing.Label("PawnDiaryRimTalkBridge.Settings.DataSent.Title".Translate());
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            listing.Label(bodyKey.Translate());
            Text.Font = previousFont;
            listing.Gap(4f);
        }

        /// <summary>
        /// Keeps the core ambient fallback suppressed when the player intentionally selected Off or
        /// Shared context. At Level 2 only the independently reported hook-health capability suppresses
        /// it, so a missing upstream method automatically enables degraded ambient capture.
        /// </summary>
        internal static void RefreshConversationFallbackGate()
        {
            PawnDiaryApi.SetCaptureCapabilityReady(
                BridgeIds.ConversationCaptureNotRequestedCapability,
                !LevelAtLeast(2));
        }

        private string CurrentAssessmentLaneLabel(DiaryApiSetupSnapshot setup)
        {
            if (Settings.assessmentLaneIndex < 0)
            {
                return "PawnDiaryRimTalkBridge.Settings.AssessmentLaneAuto".Translate();
            }

            if (setup != null && setup.lanes != null)
            {
                for (int i = 0; i < setup.lanes.Count; i++)
                {
                    DiaryApiLaneSnapshot lane = setup.lanes[i];
                    if (lane != null && lane.active && lane.index == Settings.assessmentLaneIndex)
                    {
                        return AssessmentLaneLabel(lane);
                    }
                }
            }

            // A removed/disabled saved lane falls back exactly as RequestLlmCompletion does.
            return "PawnDiaryRimTalkBridge.Settings.AssessmentLaneAuto".Translate();
        }

        private List<FloatMenuOption> BuildAssessmentLaneOptions(DiaryApiSetupSnapshot setup)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PawnDiaryRimTalkBridge.Settings.AssessmentLaneAuto".Translate(), delegate
                {
                    Settings.assessmentLaneIndex = -1;
                })
            };

            if (setup != null && setup.lanes != null)
            {
                for (int i = 0; i < setup.lanes.Count; i++)
                {
                    DiaryApiLaneSnapshot lane = setup.lanes[i];
                    if (lane == null || !lane.active)
                    {
                        continue;
                    }

                    int index = lane.index;
                    string label = AssessmentLaneLabel(lane);
                    options.Add(new FloatMenuOption(label, delegate
                    {
                        Settings.assessmentLaneIndex = index;
                    }));
                }
            }

            return options;
        }

        private static string AssessmentLaneLabel(DiaryApiLaneSnapshot lane)
        {
            string model = !string.IsNullOrWhiteSpace(lane.model)
                ? lane.model
                : (!string.IsNullOrWhiteSpace(lane.url)
                    ? lane.url
                    : "PawnDiaryRimTalkBridge.Settings.AssessmentLaneUnnamed".Translate().ToString());
            return "PawnDiaryRimTalkBridge.Settings.AssessmentLaneFormat".Translate(lane.index + 1, model).Resolve();
        }

        private void DrawReactionTermsEditor(
            Listing_Standard listing,
            ConversationAssessmentPolicyDef policy)
        {
            EnsureAssessmentEditorBuffers(policy);

            Rect header = listing.GetRect(30f);
            float buttonWidth = Mathf.Min(220f, header.width * 0.46f);
            Rect labelRect = new Rect(header.x, header.y, header.width - buttonWidth - 8f, header.height);
            Rect resetRect = new Rect(labelRect.xMax + 8f, header.y, buttonWidth, header.height);
            Widgets.Label(labelRect, "PawnDiaryRimTalkBridge.Settings.ReactionTerms".Translate());
            if (Widgets.ButtonText(resetRect, "PawnDiaryRimTalkBridge.Settings.ResetLocalizedDefault".Translate()))
            {
                reactionTermsBuffer = policy.DefaultReactionTermsCsv();
                Settings.conversationReactionTermsCsv = string.Empty;
            }

            listing.Label("PawnDiaryRimTalkBridge.Settings.ReactionTermsDesc".Translate());
            Rect textRect = listing.GetRect(108f);
            string editedTerms = Widgets.TextArea(textRect, reactionTermsBuffer ?? string.Empty);
            long termCount = Math.Max(1, policy.maxEditableReactionTerms);
            long derivedInputCap = termCount * Math.Max(1, policy.maxEditableReactionTermChars)
                + Math.Max(0L, termCount - 1L) * 2L; // comma + optional space between terms
            int inputCap = derivedInputCap > 32768L ? 32768 : (int)derivedInputCap;
            reactionTermsBuffer = UnicodeText.CapTextElements(editedTerms, inputCap);

            ConversationReactionTermsValidationResult validation =
                ConversationReactionTermsEditor.Validate(
                    reactionTermsBuffer,
                    policy.maxEditableReactionTerms,
                    policy.maxEditableReactionTermChars);
            if (validation.IsValid)
            {
                List<string> defaults = ConversationReactionTermsEditor.Flatten(policy.KeywordLexicon());
                Settings.conversationReactionTermsCsv = ConversationReactionTermsEditor.SameTerms(
                    validation.Terms, defaults)
                    ? string.Empty
                    : validation.NormalizedCsv;
                listing.Label("PawnDiaryRimTalkBridge.Settings.ReactionTermsValid".Translate(
                    validation.Terms.Count));
            }
            else
            {
                listing.Label(ReactionTermsValidationMessage(validation));
            }
        }

        private void DrawAssessmentPromptEditor(
            Listing_Standard listing,
            ConversationAssessmentPolicyDef policy)
        {
            EnsureAssessmentEditorBuffers(policy);

            Rect header = listing.GetRect(30f);
            float buttonWidth = Mathf.Min(220f, header.width * 0.46f);
            Rect labelRect = new Rect(header.x, header.y, header.width - buttonWidth - 8f, header.height);
            Rect resetRect = new Rect(labelRect.xMax + 8f, header.y, buttonWidth, header.height);
            Widgets.Label(labelRect, "PawnDiaryRimTalkBridge.Settings.AssessmentPrompt".Translate());
            if (Widgets.ButtonText(resetRect, "PawnDiaryRimTalkBridge.Settings.ResetLocalizedDefault".Translate()))
            {
                assessmentPromptBuffer = policy.assessmentSystemPrompt ?? string.Empty;
                Settings.assessmentPromptOverride = string.Empty;
            }

            listing.Label("PawnDiaryRimTalkBridge.Settings.AssessmentPromptDesc".Translate());
            Rect textRect = listing.GetRect(156f);
            string edited = Widgets.TextArea(textRect, assessmentPromptBuffer ?? string.Empty);
            assessmentPromptBuffer = UnicodeText.CapUtf16(
                edited,
                Math.Max(1, policy.assessmentPromptOverrideChars));

            string cleaned = ConversationAssessmentPromptEditor.Clean(
                assessmentPromptBuffer,
                policy.assessmentPromptOverrideChars);
            string defaultPrompt = ConversationAssessmentPromptEditor.Clean(
                policy.assessmentSystemPrompt,
                policy.assessmentPromptOverrideChars);
            Settings.assessmentPromptOverride = string.Equals(
                cleaned, defaultPrompt, StringComparison.Ordinal)
                ? string.Empty
                : cleaned;
            listing.Label("PawnDiaryRimTalkBridge.Settings.AssessmentPromptChars".Translate(
                assessmentPromptBuffer.Length,
                policy.assessmentPromptOverrideChars));
        }

        private void EnsureAssessmentEditorBuffers(ConversationAssessmentPolicyDef policy)
        {
            if (reactionTermsBuffer == null)
            {
                reactionTermsBuffer = !string.IsNullOrWhiteSpace(Settings.conversationReactionTermsCsv)
                    ? Settings.conversationReactionTermsCsv
                    : policy.DefaultReactionTermsCsv();

                ConversationReactionTermsValidationResult savedValidation =
                    ConversationReactionTermsEditor.Validate(
                        reactionTermsBuffer,
                        policy.maxEditableReactionTerms,
                        policy.maxEditableReactionTermChars);
                if (!savedValidation.IsValid)
                {
                    // Keep the invalid hand-edited text visible for correction, but make the actual
                    // setting fall back to the safe localized XML list before any future save.
                    Settings.conversationReactionTermsCsv = string.Empty;
                }
            }

            if (assessmentPromptBuffer == null)
            {
                assessmentPromptBuffer = !string.IsNullOrWhiteSpace(Settings.assessmentPromptOverride)
                    ? Settings.assessmentPromptOverride
                    : policy.assessmentSystemPrompt ?? string.Empty;
            }
        }

        private static string ReactionTermsValidationMessage(
            ConversationReactionTermsValidationResult validation)
        {
            string error = validation != null ? validation.Error : ConversationReactionTermsEditor.ErrorEmpty;
            int value = validation != null ? validation.ErrorValue : 0;
            switch (error)
            {
                case ConversationReactionTermsEditor.ErrorNewline:
                    return "PawnDiaryRimTalkBridge.Settings.ReactionTermsErrorNewline".Translate();
                case ConversationReactionTermsEditor.ErrorTooMany:
                    return "PawnDiaryRimTalkBridge.Settings.ReactionTermsErrorTooMany".Translate(value);
                case ConversationReactionTermsEditor.ErrorTooLong:
                    return "PawnDiaryRimTalkBridge.Settings.ReactionTermsErrorTooLong".Translate(value);
                case ConversationReactionTermsEditor.ErrorInvalidTerm:
                    return "PawnDiaryRimTalkBridge.Settings.ReactionTermsErrorInvalid".Translate(value);
                default:
                    return "PawnDiaryRimTalkBridge.Settings.ReactionTermsErrorEmpty".Translate();
            }
        }
    }

    /// <summary>
    /// Registers translated RimTalk variables and injected sections after RimWorld has loaded languages.
    /// <see cref="StaticConstructorOnStartupAttribute"/> constructors run after Def and translation loading,
    /// unlike <see cref="Mod"/> constructors. Each hook stays isolated so one optional API mismatch cannot
    /// disable the other bridge features.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class RimTalkBridgeTranslatedRegistration
    {
        static RimTalkBridgeTranslatedRegistration()
        {
            if (!PawnDiaryRimTalkBridgeMod.RimTalkActive)
            {
                return;
            }

            TryRegister(DiaryContextInjector.RegisterAll,
                "diary-context hooks; Level 1 outbound is disabled");
            TryRegister(PersonaSync.RegisterContextProvider,
                "persona context providers; persona synchronization context is disabled");
            TryRegister(PersonaSync.RegisterExternalGenerator,
                "persona regeneration callback; Pawn Diary's editor regenerate button is disabled");
            TryRegister(ColonyContextInjector.RegisterAll,
                "colony-context hooks; {{colony_events}} is disabled");
        }

        private static void TryRegister(Action register, string failure)
        {
            try
            {
                register();
            }
            catch (Exception e)
            {
                Log.Error(PawnDiaryRimTalkBridgeMod.LogPrefix + " failed to register " + failure + ": " + e);
            }
        }
    }

    /// <summary>
    /// Saved settings for the bridge adapter. Scribe keys are FROZEN once shipped: they live in the
    /// player's mod-settings XML, so renaming one silently resets that setting for every player.
    /// </summary>
    public class PawnDiaryRimTalkBridgeSettings : ModSettings
    {
        // v1 changed a legacy zero cap from "unlimited" to "conversation recording off". Keep an
        // explicit schema key so old zeroes can be migrated once without changing the new UI meaning.
        private const int CurrentSettingsSchemaVersion = 2;
        private int settingsSchemaVersion;

        /// <summary>0 = Off, 1 = Shared context, 2 = + Conversations. Int (not enum) for save stability.</summary>
        public int integrationLevel = 1;

        /// <summary>Developer chat logging. Migrated: reuses the pre-rework "enabled" Scribe key,
        /// which used to be the old diagnostic bridge's single log-chats checkbox.</summary>
        public bool devChatLogging;

        /// <summary>Include the pawn's diary writing-voice line in the RimTalk prompt section.</summary>
        public bool includeDiaryVoiceLine = true;

        /// <summary>Legacy pre-direction opt-in; read only to migrate old users into import mode.</summary>
        public bool personaLedDiaryVoice;

        /// <summary>Authoritative direction for persona synchronization.</summary>
        public PersonaSyncDirection personaSyncDirection = PersonaSyncDirection.Off;

        /// <summary>Rewrite the source persona through Pawn Diary's first active LLM lane.</summary>
        public bool transformPersonaWithLlm;

        /// <summary>Legacy engine-writing key. Still read for migration, intentionally never acted on.</summary>
        public bool useRimTalkEngine;

        /// <summary>Use a small batched semantic assessment before normal diary generation.</summary>
        public bool useSemanticConversationAssessment = true;

        /// <summary>-1 = first active Pawn Diary lane; otherwise the saved configured lane index.</summary>
        public int assessmentLaneIndex = -1;

        /// <summary>Optional comma-separated replacement for the localized XML reaction terms.</summary>
        public string conversationReactionTermsCsv = string.Empty;

        /// <summary>Optional player-authored semantic system prompt; blank keeps the localized Def.</summary>
        public string assessmentPromptOverride = string.Empty;

        /// <summary>Feature 1 (off by default): inject a curated colony-situation line as {{colony_events}}.
        /// Off because it overlaps RimTalk's own live-event mods; opt in for the curated/atmospheric angle.</summary>
        public bool injectColonyContext;

        /// <summary>How many recent diary entries feed the RimTalk prompt section.</summary>
        public int contextEntryCount = 3;

        /// <summary>Feature 1: max colony-situation lines injected per prompt (0 disables the block).</summary>
        public int colonyEventCount = 3;

        /// <summary>Ticks of silence after which a RimTalk conversation counts as finished.</summary>
        public int conversationQuietTicks = 2500;

        /// <summary>Legacy pre-funnel key. Still read from old settings; no longer shown or used.</summary>
        public int minRepliesForImportant = 4;

        /// <summary>Max explicit conversation entries per pawn per in-game day.</summary>
        public int perPawnDailyCap = 1;

        /// <summary>Max explicit conversation entries colony-wide per in-game day.</summary>
        public int colonyDailyCap = 6;

        /// <summary>Minimum ticks between explicit entries for the same pawn pair.</summary>
        public int pairMinGapTicks = 30000;

        /// <summary>Max transcript lines quoted in one conversation entry's prompt context.</summary>
        public int transcriptLineCap = 4;

        public override void ExposeData()
        {
            // "enabled" is the pre-rework key: old installs had a single log-chats checkbox, and the
            // closest surviving meaning is developer chat logging. Never reuse "enabled" for anything else.
            Scribe_Values.Look(ref devChatLogging, "enabled", false);
            Scribe_Values.Look(ref integrationLevel, "integrationLevel", 1);
            Scribe_Values.Look(ref includeDiaryVoiceLine, "includeDiaryVoiceLine", true);
            Scribe_Values.Look(ref personaLedDiaryVoice, "personaLedDiaryVoice", false);
            Scribe_Values.Look(ref personaSyncDirection, "personaSyncDirection", PersonaSyncDirection.Off);
            Scribe_Values.Look(ref transformPersonaWithLlm, "transformPersonaWithLlm", false);
            // Frozen legacy key: retained only so old settings continue to deserialize cleanly.
            Scribe_Values.Look(ref useRimTalkEngine, "useRimTalkEngine", false);
            Scribe_Values.Look(ref useSemanticConversationAssessment, "useSemanticConversationAssessment", true);
            Scribe_Values.Look(ref assessmentLaneIndex, "assessmentLaneIndex", -1);
            Scribe_Values.Look(ref conversationReactionTermsCsv, "conversationReactionTermsCsv", string.Empty);
            Scribe_Values.Look(ref assessmentPromptOverride, "assessmentPromptOverride", string.Empty);
            Scribe_Values.Look(ref injectColonyContext, "injectColonyContext", false);
            // Retired shared-memory keys ("injectSharedMemory", "autoInjectSharedMemory",
            // "sharedMemoryCount") are deliberately no longer read (MEMORY_SYSTEM_REDESIGN_PLAN §6).
            Scribe_Values.Look(ref contextEntryCount, "contextEntryCount", 3);
            Scribe_Values.Look(ref colonyEventCount, "colonyEventCount", 3);
            Scribe_Values.Look(ref conversationQuietTicks, "conversationQuietTicks", 2500);
            Scribe_Values.Look(ref minRepliesForImportant, "minRepliesForImportant", 4);
            Scribe_Values.Look(ref perPawnDailyCap, "perPawnDailyCap", 1);
            Scribe_Values.Look(ref colonyDailyCap, "colonyDailyCap", 6);
            Scribe_Values.Look(ref pairMinGapTicks, "pairMinGapTicks", 30000);
            Scribe_Values.Look(ref transcriptLineCap, "transcriptLineCap", 4);
            Scribe_Values.Look(ref settingsSchemaVersion, "settingsSchemaVersion", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars && settingsSchemaVersion < 1)
            {
                // In bridge v0.2, zero disabled an individual cap rather than recording. Preserve the
                // closest meanings under v0.3: the rolling one-entry pawn gate remains enabled, while
                // the old unlimited colony ceiling becomes the largest supported saved value.
                if (perPawnDailyCap == 0)
                {
                    perPawnDailyCap = 1;
                }

                if (colonyDailyCap == 0)
                {
                    colonyDailyCap = 999;
                }

                settingsSchemaVersion = 1;
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars
                && settingsSchemaVersion < CurrentSettingsSchemaVersion)
            {
                // Import was briefly the serialized default, so v1 files cannot distinguish an explicit
                // import click from an untouched default. The older opt-in checkbox is the only durable
                // evidence of consent: preserve export (which was never a default), preserve old opt-in
                // as import, and otherwise choose the conservative non-writing Off state.
                if (personaLedDiaryVoice)
                {
                    personaSyncDirection = PersonaSyncDirection.RimTalkToPawnDiary;
                }
                else if (personaSyncDirection == PersonaSyncDirection.RimTalkToPawnDiary)
                {
                    personaSyncDirection = PersonaSyncDirection.Off;
                }

                settingsSchemaVersion = CurrentSettingsSchemaVersion;
            }

            // Defensive clamps: hand-edited or corrupted config XML must not wedge the bridge into
            // impossible states (negative caps, level 99, a zero quiet window that flushes every tick).
            integrationLevel = Clamp(integrationLevel, 0, 2);
            if (personaSyncDirection != PersonaSyncDirection.PawnDiaryToRimTalk
                && personaSyncDirection != PersonaSyncDirection.RimTalkToPawnDiary
                && personaSyncDirection != PersonaSyncDirection.Off)
            {
                personaSyncDirection = PersonaSyncDirection.Off;
            }
            contextEntryCount = Clamp(contextEntryCount, 0, 10);
            colonyEventCount = Clamp(colonyEventCount, 0, 6);
            conversationQuietTicks = Clamp(conversationQuietTicks, 250, 60000);
            minRepliesForImportant = Clamp(minRepliesForImportant, 1, 64);
            perPawnDailyCap = Clamp(perPawnDailyCap, 0, 1);
            colonyDailyCap = Clamp(colonyDailyCap, 0, 999);
            pairMinGapTicks = Clamp(pairMinGapTicks, 0, 600000);
            transcriptLineCap = Clamp(transcriptLineCap, 0, 16);
            if (assessmentLaneIndex < -1)
            {
                assessmentLaneIndex = -1;
            }
            conversationReactionTermsCsv = conversationReactionTermsCsv ?? string.Empty;
            assessmentPromptOverride = assessmentPromptOverride ?? string.Empty;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
