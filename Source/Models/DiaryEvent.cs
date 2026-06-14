// One recorded event (interaction / social fight / mental break) and all of its
// generation state for up to two points of view. Pure model: fields, save/load,
// and prompt/result plumbing. Split out of DiaryGameComponent.cs. See DOCUMENTATION.md.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One recorded event plus all of its generation state for up to two points of view
    /// (initiator/recipient), or a single POV for solo mental breaks. Holds the raw game text,
    /// the context summaries, the prompts, the generated text, and per-POV status/errors. Knows
    /// how to save/load itself and how to apply an <see cref="LlmGenerationResult"/>.
    /// </summary>
    public class DiaryEvent : IExposable
    {
        public const string InitiatorRole = "initiator";
        public const string RecipientRole = "recipient";
        public const string NeutralRole = "neutral";
        public const string DualRole = "dual";
        public const string NotGeneratedStatus = "not_generated";
        public const string PendingStatus = "pending";
        public const string CompleteStatus = "complete";
        public const string FailedStatus = "failed";

        public string eventId;
        public int tick;
        public string date;
        public string interactionDefName;
        public string interactionLabel;
        public string initiatorPawnId;
        public string recipientPawnId;
        public string initiatorName;
        public string recipientName;
        public string initiatorText;
        public string recipientText;
        public string neutralText;
        public string sequenceText;
        public string gameContext;
        public string instruction;
        public string initiatorPawnSummary;
        public string recipientPawnSummary;
        public string initiatorSurroundings;
        public string recipientSurroundings;
        public string opinionsSummary;
        public string initiatorContinuity;
        public string recipientContinuity;
        public string initiatorPrompt;
        public string recipientPrompt;
        public string neutralPrompt;
        public string initiatorGeneratedText;
        public string recipientGeneratedText;
        public string neutralGeneratedText;
        public string initiatorStatus;
        public string recipientStatus;
        public string neutralStatus;
        public string initiatorError;
        public string recipientError;
        public string neutralError;
        public string initiatorLlmEndpoint;
        public string recipientLlmEndpoint;
        public string neutralLlmEndpoint;
        public string initiatorLlmModel;
        public string recipientLlmModel;
        public string neutralLlmModel;
        public string llmEndpoint;
        public string llmModel;
        public bool solo;

        // Save/load hook (runs for BOTH directions). The string tags ("eventId", ...) are the
        // keys written to the save file — renaming one breaks existing saves. The PostLoadInit
        // block below back-fills defaults/normalizes status when loading older saves.
        // See CSHARP-NOTES.md ("IExposable").
        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Values.Look(ref solo, "solo", false);
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref date, "date");
            Scribe_Values.Look(ref interactionDefName, "interactionDefName");
            Scribe_Values.Look(ref interactionLabel, "interactionLabel");
            Scribe_Values.Look(ref initiatorPawnId, "initiatorPawnId");
            Scribe_Values.Look(ref recipientPawnId, "recipientPawnId");
            Scribe_Values.Look(ref initiatorName, "initiatorName");
            Scribe_Values.Look(ref recipientName, "recipientName");
            Scribe_Values.Look(ref initiatorText, "initiatorText");
            Scribe_Values.Look(ref recipientText, "recipientText");
            Scribe_Values.Look(ref neutralText, "neutralText");
            Scribe_Values.Look(ref sequenceText, "sequenceText");
            Scribe_Values.Look(ref gameContext, "gameContext");
            Scribe_Values.Look(ref instruction, "instruction");
            Scribe_Values.Look(ref initiatorPawnSummary, "initiatorPawnSummary");
            Scribe_Values.Look(ref recipientPawnSummary, "recipientPawnSummary");
            Scribe_Values.Look(ref initiatorSurroundings, "initiatorSurroundings");
            Scribe_Values.Look(ref recipientSurroundings, "recipientSurroundings");
            Scribe_Values.Look(ref opinionsSummary, "opinionsSummary");
            Scribe_Values.Look(ref initiatorContinuity, "initiatorContinuity");
            Scribe_Values.Look(ref recipientContinuity, "recipientContinuity");
            Scribe_Values.Look(ref initiatorGeneratedText, "initiatorGeneratedText");
            Scribe_Values.Look(ref recipientGeneratedText, "recipientGeneratedText");
            Scribe_Values.Look(ref neutralGeneratedText, "neutralGeneratedText");
            Scribe_Values.Look(ref initiatorStatus, "initiatorStatus");
            Scribe_Values.Look(ref recipientStatus, "recipientStatus");
            Scribe_Values.Look(ref neutralStatus, "neutralStatus");
            Scribe_Values.Look(ref initiatorError, "initiatorError");
            Scribe_Values.Look(ref recipientError, "recipientError");
            Scribe_Values.Look(ref neutralError, "neutralError");
            Scribe_Values.Look(ref llmEndpoint, "llmEndpoint");
            Scribe_Values.Look(ref llmModel, "llmModel");
            Scribe_Values.Look(ref initiatorLlmEndpoint, "initiatorLlmEndpoint");
            Scribe_Values.Look(ref recipientLlmEndpoint, "recipientLlmEndpoint");
            Scribe_Values.Look(ref neutralLlmEndpoint, "neutralLlmEndpoint");
            Scribe_Values.Look(ref initiatorLlmModel, "initiatorLlmModel");
            Scribe_Values.Look(ref recipientLlmModel, "recipientLlmModel");
            Scribe_Values.Look(ref neutralLlmModel, "neutralLlmModel");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    eventId = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(initiatorStatus))
                {
                    initiatorStatus = NotGeneratedStatus;
                }

                if (string.IsNullOrWhiteSpace(recipientStatus))
                {
                    recipientStatus = NotGeneratedStatus;
                }

                if (string.IsNullOrWhiteSpace(neutralText))
                {
                    neutralText = string.Equals(initiatorText, recipientText, StringComparison.OrdinalIgnoreCase)
                        ? initiatorText
                        : initiatorName + ": " + initiatorText + "\n" + recipientName + ": " + recipientText;
                }

                if (string.IsNullOrWhiteSpace(sequenceText))
                {
                    sequenceText = neutralText;
                }

                if (string.IsNullOrWhiteSpace(gameContext))
                {
                    gameContext = "def=" + interactionDefName + "; label=" + interactionLabel;
                }

                if (string.IsNullOrWhiteSpace(instruction))
                {
                    instruction = interactionLabel;
                }

                if (string.IsNullOrWhiteSpace(initiatorPawnSummary))
                {
                    initiatorPawnSummary = "unknown";
                }

                if (string.IsNullOrWhiteSpace(recipientPawnSummary))
                {
                    recipientPawnSummary = "unknown";
                }

                if (string.IsNullOrWhiteSpace(initiatorSurroundings))
                {
                    initiatorSurroundings = "unknown";
                }

                if (string.IsNullOrWhiteSpace(recipientSurroundings))
                {
                    recipientSurroundings = initiatorSurroundings;
                }

                if (string.IsNullOrWhiteSpace(opinionsSummary))
                {
                    opinionsSummary = "unknown";
                }

                if (string.IsNullOrWhiteSpace(initiatorContinuity))
                {
                    initiatorContinuity = "none";
                }

                if (string.IsNullOrWhiteSpace(recipientContinuity))
                {
                    recipientContinuity = "none";
                }

                if (neutralStatus == null)
                {
                    neutralStatus = NotGeneratedStatus;
                }

                initiatorStatus = NormalizeLoadedStatus(initiatorStatus, initiatorGeneratedText);
                recipientStatus = NormalizeLoadedStatus(recipientStatus, recipientGeneratedText);
                neutralStatus = NormalizeLoadedStatus(neutralStatus, neutralGeneratedText);

                if (string.IsNullOrWhiteSpace(initiatorLlmEndpoint) && !string.IsNullOrWhiteSpace(llmEndpoint))
                {
                    initiatorLlmEndpoint = llmEndpoint;
                }

                if (string.IsNullOrWhiteSpace(recipientLlmEndpoint) && !string.IsNullOrWhiteSpace(llmEndpoint))
                {
                    recipientLlmEndpoint = llmEndpoint;
                }

                if (string.IsNullOrWhiteSpace(neutralLlmEndpoint) && !string.IsNullOrWhiteSpace(llmEndpoint))
                {
                    neutralLlmEndpoint = llmEndpoint;
                }

                if (string.IsNullOrWhiteSpace(initiatorLlmModel) && !string.IsNullOrWhiteSpace(llmModel))
                {
                    initiatorLlmModel = llmModel;
                }

                if (string.IsNullOrWhiteSpace(recipientLlmModel) && !string.IsNullOrWhiteSpace(llmModel))
                {
                    recipientLlmModel = llmModel;
                }

                if (string.IsNullOrWhiteSpace(neutralLlmModel) && !string.IsNullOrWhiteSpace(llmModel))
                {
                    neutralLlmModel = llmModel;
                }
            }
        }

        public void SetPrompt(string povRole, string prompt)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorPrompt = prompt;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientPrompt = prompt;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralPrompt = prompt;
            }
        }

        public void SetLlmMeta(string povRole, string endpoint, string model)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorLlmEndpoint = endpoint;
                initiatorLlmModel = model;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientLlmEndpoint = endpoint;
                recipientLlmModel = model;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralLlmEndpoint = endpoint;
                neutralLlmModel = model;
            }
        }

        private string LlmEndpointFor(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientLlmEndpoint;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                return neutralLlmEndpoint;
            }

            return initiatorLlmEndpoint;
        }

        private string LlmModelFor(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientLlmModel;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                return neutralLlmModel;
            }

            return initiatorLlmModel;
        }

        public void SetDualPrompt(string prompt)
        {
            initiatorPrompt = prompt;
            recipientPrompt = prompt;
        }

        public bool CanQueueGeneration(string povRole)
        {
            string status = StatusFor(povRole);
            return string.IsNullOrWhiteSpace(status) || RoleEquals(status, NotGeneratedStatus);
        }

        public bool CanQueueDual()
        {
            return !solo && CanQueueGeneration(InitiatorRole) && CanQueueGeneration(RecipientRole);
        }

        public static bool RoleIsInitiatorOrRecipient(string povRole)
        {
            return RoleEquals(povRole, InitiatorRole) || RoleEquals(povRole, RecipientRole);
        }

        public void MarkQueued(string povRole)
        {
            SetStatus(povRole, PendingStatus);
            SetError(povRole, null);
        }

        public void MarkFailed(string povRole, string error)
        {
            SetStatus(povRole, FailedStatus);
            SetError(povRole, error);
        }

        public void MarkDualQueued()
        {
            MarkQueued(InitiatorRole);
            MarkQueued(RecipientRole);
        }

        public void MarkDualFailed(string error)
        {
            MarkFailed(InitiatorRole, error);
            MarkFailed(RecipientRole, error);
        }

        public void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null)
            {
                return;
            }

            if (RoleEquals(result.povRole, InitiatorRole))
            {
                ApplyLlmResultToInitiator(result);
                return;
            }

            if (RoleEquals(result.povRole, RecipientRole))
            {
                ApplyLlmResultToRecipient(result);
                return;
            }

            if (RoleEquals(result.povRole, NeutralRole))
            {
                ApplyLlmResultToNeutral(result);
                return;
            }

            if (RoleEquals(result.povRole, DualRole))
            {
                ApplyDualResult(result);
            }
        }

        public DiaryEntryView ToViewFor(string pawnId)
        {
            string povRole = RoleForPawn(pawnId);
            if (string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            return new DiaryEntryView(
                tick,
                date,
                TextFor(povRole),
                GeneratedTextFor(povRole),
                StatusFor(povRole),
                ErrorFor(povRole),
                LlmEndpointFor(povRole),
                LlmModelFor(povRole),
                PromptFor(povRole),
                eventId,
                povRole);
        }

        public string RoleForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            if (pawnId == initiatorPawnId)
            {
                return InitiatorRole;
            }

            if (pawnId == recipientPawnId)
            {
                return RecipientRole;
            }

            return null;
        }

        public bool Involves(string firstPawnId, string secondPawnId)
        {
            return (firstPawnId == initiatorPawnId && secondPawnId == recipientPawnId)
                || (firstPawnId == recipientPawnId && secondPawnId == initiatorPawnId);
        }

        public string TextForRole(string povRole)
        {
            return TextFor(povRole);
        }

        public string DisplayTextForRole(string povRole)
        {
            string generated = GeneratedTextFor(povRole);
            return string.IsNullOrWhiteSpace(generated) ? TextFor(povRole) : generated;
        }

        public string NameForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorName;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientName;
            }

            return "colony";
        }

        public string SurroundingsForRole(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSurroundings;
            }

            return initiatorSurroundings;
        }

        public string ContinuityForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorContinuity;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientContinuity;
            }

            return "none";
        }

        private string TextFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorText;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientText;
            }

            return neutralText;
        }

        private string GeneratedTextFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorGeneratedText;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientGeneratedText;
            }

            return neutralGeneratedText;
        }

        private string StatusFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorStatus;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientStatus;
            }

            return neutralStatus;
        }

        private string ErrorFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorError;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientError;
            }

            return neutralError;
        }

        private string PromptFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorPrompt;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientPrompt;
            }

            return neutralPrompt;
        }

        private void ApplyLlmResultToInitiator(LlmGenerationResult result)
        {
            if (result.success)
            {
                initiatorGeneratedText = result.generatedText;
                initiatorStatus = CompleteStatus;
                initiatorError = null;
            }
            else
            {
                initiatorStatus = FailedStatus;
                initiatorError = result.error;
            }
        }

        private void ApplyLlmResultToRecipient(LlmGenerationResult result)
        {
            if (result.success)
            {
                recipientGeneratedText = result.generatedText;
                recipientStatus = CompleteStatus;
                recipientError = null;
            }
            else
            {
                recipientStatus = FailedStatus;
                recipientError = result.error;
            }
        }

        private void ApplyDualResult(LlmGenerationResult result)
        {
            if (!result.success)
            {
                MarkDualFailed(result.error);
                return;
            }

            string initiatorEntry;
            string recipientEntry;
            ParseDualResponse(result.generatedText, out initiatorEntry, out recipientEntry);

            initiatorGeneratedText = initiatorEntry;
            initiatorStatus = CompleteStatus;
            initiatorError = null;

            recipientGeneratedText = recipientEntry;
            recipientStatus = CompleteStatus;
            recipientError = null;
        }

        private static void ParseDualResponse(string text, out string initiatorEntry, out string recipientEntry)
        {
            initiatorEntry = string.Empty;
            recipientEntry = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string initiatorMarker = DiaryPrompts.Current.initiatorMarker;
            string recipientMarker = DiaryPrompts.Current.recipientMarker;
            int initiatorIndex = text.IndexOf(initiatorMarker, StringComparison.OrdinalIgnoreCase);
            int recipientIndex = text.IndexOf(recipientMarker, StringComparison.OrdinalIgnoreCase);

            if (initiatorIndex >= 0 && recipientIndex >= 0)
            {
                if (initiatorIndex < recipientIndex)
                {
                    int start = initiatorIndex + initiatorMarker.Length;
                    initiatorEntry = text.Substring(start, recipientIndex - start);
                    recipientEntry = text.Substring(recipientIndex + recipientMarker.Length);
                }
                else
                {
                    int start = recipientIndex + recipientMarker.Length;
                    recipientEntry = text.Substring(start, initiatorIndex - start);
                    initiatorEntry = text.Substring(initiatorIndex + initiatorMarker.Length);
                }
            }
            else if (recipientIndex >= 0)
            {
                initiatorEntry = text.Substring(0, recipientIndex);
                recipientEntry = text.Substring(recipientIndex + recipientMarker.Length);
            }
            else if (initiatorIndex >= 0)
            {
                initiatorEntry = text.Substring(initiatorIndex + initiatorMarker.Length);
                recipientEntry = text;
            }
            else
            {
                // No markers returned; fall back to the same text for both POVs.
                initiatorEntry = text;
                recipientEntry = text;
            }

            initiatorEntry = CleanDualEntry(initiatorEntry);
            recipientEntry = CleanDualEntry(recipientEntry);

            if (string.IsNullOrWhiteSpace(initiatorEntry))
            {
                initiatorEntry = CleanDualEntry(text);
            }

            if (string.IsNullOrWhiteSpace(recipientEntry))
            {
                recipientEntry = CleanDualEntry(text);
            }
        }

        private static string CleanDualEntry(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Replace(DiaryPrompts.Current.initiatorMarker, string.Empty)
                         .Replace(DiaryPrompts.Current.recipientMarker, string.Empty);
            return value.Trim();
        }

        private void ApplyLlmResultToNeutral(LlmGenerationResult result)
        {
            if (result.success)
            {
                neutralGeneratedText = result.generatedText;
                neutralStatus = CompleteStatus;
                neutralError = null;
            }
            else
            {
                neutralStatus = FailedStatus;
                neutralError = result.error;
            }
        }

        private void SetStatus(string povRole, string status)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorStatus = status;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientStatus = status;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralStatus = status;
            }
        }

        private void SetError(string povRole, string error)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorError = error;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientError = error;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralError = error;
            }
        }

        private static string NormalizeLoadedStatus(string status, string generatedText)
        {
            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                return CompleteStatus;
            }

            if (RoleEquals(status, PendingStatus))
            {
                return NotGeneratedStatus;
            }

            return string.IsNullOrWhiteSpace(status) ? NotGeneratedStatus : status;
        }

        private static bool RoleEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
