// Context-detail preview drawer for the Prompts settings tab. It builds a synthetic high-context
// event and renders Full / Balanced / Compact through the same pure selector used by live dispatch.
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        private const float ContextPreviewPresetHeight = 286f;

        /// <summary>Draws the user-facing prompt-context preview and cut report.</summary>
        private void DrawContextDetailPreviewDrawer(Listing_Standard listing)
        {
            listing.Gap(12f);
            Rect headerRect = listing.GetRect(82f);
            Widgets.DrawMenuSection(headerRect);
            Rect innerRect = headerRect.ContractedBy(8f);
            Rect buttonRect = new Rect(innerRect.xMax - 118f, innerRect.y, 118f, 28f);
            Rect titleRect = new Rect(innerRect.x, innerRect.y, Mathf.Max(0f, innerRect.width - 126f), 28f);

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(titleRect, "PawnDiary.Settings.ContextPreviewTitle".Translate());
            Text.Font = previousFont;

            if (ButtonTextFit(
                buttonRect,
                (contextDetailPreviewExpanded
                    ? "PawnDiary.Settings.ContextPreviewHide"
                    : "PawnDiary.Settings.ContextPreviewShow").Translate()))
            {
                contextDetailPreviewExpanded = !contextDetailPreviewExpanded;
            }

            Rect helpRect = new Rect(innerRect.x, titleRect.yMax + 4f, innerRect.width, innerRect.yMax - titleRect.yMax - 4f);
            DrawMutedLabel(helpRect, "PawnDiary.Settings.ContextPreviewHelp".Translate().ToString());

            if (!contextDetailPreviewExpanded)
            {
                return;
            }

            listing.Gap(8f);
            DrawContextPreviewPreset(listing, PromptContextDetailLevel.Full);
            listing.Gap(6f);
            DrawContextPreviewPreset(listing, PromptContextDetailLevel.Balanced);
            listing.Gap(6f);
            DrawContextPreviewPreset(listing, PromptContextDetailLevel.Compact);
        }

        private static void DrawContextPreviewPreset(Listing_Standard listing, PromptContextDetailLevel level)
        {
            DiaryPromptPlan plan = BuildContextPreviewPlan(level);
            Rect blockRect = listing.GetRect(ContextPreviewPresetHeight);
            Widgets.DrawMenuSection(blockRect);
            Rect inner = blockRect.ContractedBy(8f);

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(new Rect(inner.x, inner.y, inner.width, 26f), ContextDetailLabel(level).Translate());
            Text.Font = previousFont;

            PromptContextSelectionReport report = plan.contextSelectionReport ?? new PromptContextSelectionReport();
            string summary = "PawnDiary.Settings.ContextPreviewSummary".Translate(
                report.outputChars,
                report.inputChars,
                ApproxTokens(report.outputChars),
                report.cut == null ? 0 : report.cut.Count);
            DrawMutedLabel(new Rect(inner.x, inner.y + 28f, inner.width, 22f), summary);

            Rect promptRect = new Rect(inner.x, inner.y + 54f, inner.width, 116f);
            Widgets.TextArea(promptRect, plan.userPrompt ?? string.Empty);

            Rect cutLabelRect = new Rect(inner.x, promptRect.yMax + 8f, inner.width, 22f);
            Widgets.LabelFit(cutLabelRect, "PawnDiary.Settings.ContextPreviewCuts".Translate());

            Rect cutsRect = new Rect(inner.x, cutLabelRect.yMax + 2f, inner.width, Mathf.Max(0f, inner.yMax - cutLabelRect.yMax - 2f));
            Widgets.TextArea(cutsRect, CutSummary(report));
        }

        private static DiaryPromptPlan BuildContextPreviewPlan(PromptContextDetailLevel level)
        {
            DiaryEventPayload payload = new DiaryEventPayload
            {
                eventId = "settings_context_preview",
                defName = "RaidEnemy",
                label = PreviewText("PawnDiary.Settings.ContextPreview.Sample.Label"),
                eventNoun = PreviewText("PawnDiary.Settings.ContextPreview.Sample.EventNoun"),
                domain = "Raid",
                solo = true,
                gameContext = PreviewText("PawnDiary.Settings.ContextPreview.Sample.GameContext"),
                instruction = PreviewText("PawnDiary.Settings.ContextPreview.Sample.Instruction"),
                neutralText = PreviewText("PawnDiary.Settings.ContextPreview.Sample.NeutralText"),
                initiator = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Initiator,
                    name = PreviewText("PawnDiary.Settings.ContextPreview.Sample.PawnName"),
                    rawText = PreviewText("PawnDiary.Settings.ContextPreview.Sample.NeutralText"),
                    pawnSummary = PreviewText("PawnDiary.Settings.ContextPreview.Sample.PawnSummary"),
                    surroundings = PreviewText("PawnDiary.Settings.ContextPreview.Sample.Surroundings"),
                    continuity = PreviewText("PawnDiary.Settings.ContextPreview.Sample.Continuity"),
                    lastOpener = PreviewText("PawnDiary.Settings.ContextPreview.Sample.LastOpener"),
                    previousEntryEnding = PreviewText("PawnDiary.Settings.ContextPreview.Sample.PreviousEnding"),
                    weapon = PreviewText("PawnDiary.Settings.ContextPreview.Sample.Weapon")
                }
            };

            DiaryPolicySnapshot policy = DiaryPipelineAdapters.PolicyFor(payload);
            if (string.IsNullOrWhiteSpace(policy.group.eventPrompt))
            {
                policy.group.eventPrompt = PreviewText("PawnDiary.Settings.ContextPreview.Sample.EventPrompt");
            }

            if (string.IsNullOrWhiteSpace(policy.group.eventEnhancement))
            {
                policy.group.eventEnhancement = PreviewText("PawnDiary.Settings.ContextPreview.Sample.EventEnhancement");
            }

            return DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = PreviewText("PawnDiary.Settings.ContextPreview.Sample.PersonaVoice"),
                promptEnchantment = PreviewText("PawnDiary.Settings.ContextPreview.Sample.PromptEnchantment"),
                directSpeechInstruction = PreviewText("PawnDiary.Settings.ContextPreview.Sample.DirectSpeech"),
                contextDetailLevel = level,
                maxTokens = 100
            });
        }

        private static string PreviewText(string key)
        {
            return key.Translate().Resolve();
        }

        private static string CutSummary(PromptContextSelectionReport report)
        {
            if (report == null || report.cut == null || report.cut.Count == 0)
            {
                return "PawnDiary.Settings.ContextPreviewNoCuts".Translate();
            }

            List<string> lines = new List<string>();
            int max = Mathf.Min(report.cut.Count, 10);
            for (int i = 0; i < max; i++)
            {
                PromptContextFieldReport field = report.cut[i];
                string label = string.IsNullOrWhiteSpace(field.label) ? field.source : field.label;
                lines.Add("- " + label + ": " + field.reason);
            }

            if (report.cut.Count > max)
            {
                lines.Add("PawnDiary.Settings.ContextPreviewMoreCuts".Translate(report.cut.Count - max));
            }

            return string.Join("\n", lines.ToArray());
        }

        private static int ApproxTokens(int chars)
        {
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, chars) / 4f));
        }
    }
}
