// One recorded event (interaction / social fight / mental break / notable tale) and all of its
// generation state for up to two points of view. Pure model: fields, save/load,
// and prompt/result plumbing. Split out of DiaryGameComponent.cs. See DOCUMENTATION.md.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One recorded event plus all of its generation state for up to two points of view
    /// (initiator/recipient), or a single POV for solo mental breaks and single-pawn tales. Holds the raw game text,
    /// the context summaries, the prompts, the generated text, and per-POV status/errors. Knows
    /// how to save/load itself and how to apply an <see cref="LlmGenerationResult"/>.
    /// </summary>
    public class DiaryEvent : IExposable
    {
        // POV-role and generation-status string constants used across the diary system
        // for comparing/switching on roles and statuses.
        public const string InitiatorRole = "initiator";
        public const string RecipientRole = "recipient";
        public const string NeutralRole = "neutral";
        public const string DualRole = "dual";
        public const string NotGeneratedStatus = "not_generated";
        public const string PendingStatus = "pending";
        public const string CompleteStatus = "complete";
        public const string FailedStatus = "failed";

        public string eventId; // unique ID for this event, stable across saves
        public List<int> playLogEntryIds = new List<int>(); // Verse.LogEntry ids that produced this diary event
        public int tick; // game tick when the event was recorded
        public string date; // human-readable date string at event time
        public string interactionDefName; // RimWorld InteractionDef defName (e.g. "Chat")
        public string interactionLabel; // display label for the interaction type
        public string initiatorPawnId; // RimWorld unique load ID of the initiating pawn
        public string recipientPawnId; // RimWorld unique load ID of the receiving pawn
        public string initiatorName; // display name of the initiator
        public string recipientName; // display name of the recipient
        public string initiatorText; // raw game-side description from the initiator's POV
        public string recipientText; // raw game-side description from the recipient's POV
        public string neutralText; // merged description combining both POVs for neutral view
        public string sequenceText; // used by legacy dual-POV generation mode
        public string gameContext; // metadata string describing game state at event time
        public string instruction; // group-specific prompt instruction appended to LLM calls
        public string initiatorPawnSummary; // LLM-oriented summary of initiator's traits/backstory
        public string recipientPawnSummary; // LLM-oriented summary of recipient's traits/backstory
        public string initiatorSurroundings; // textual description of initiator's surroundings
        public string recipientSurroundings; // textual description of recipient's surroundings
        public string opinionsSummary; // social opinions between the two involved pawns
        public string initiatorContinuity; // context string carrying forward initiator's prior entries
        public string recipientContinuity; // context string carrying forward recipient's prior entries
        public string initiatorLastOpener; // first sentence of initiator's last diary entry (avoid repeats)
        public string recipientLastOpener; // first sentence of recipient's last diary entry (avoid repeats)
        public string initiatorBurningPassion; // random burning passion of initiator (important events only)
        public string recipientBurningPassion; // random burning passion of recipient (important events only)
        public string initiatorWeapon; // currently equipped weapon of initiator (important/combat only)
        public string recipientWeapon; // currently equipped weapon of recipient (important/combat only)
        public string initiatorAtmosphere; // short emotional atmosphere phrase for initiator POV
        public string recipientAtmosphere; // short emotional atmosphere phrase for recipient POV
        public string initiatorPrompt; // assembled prompt text sent to the LLM for initiator POV
        public string recipientPrompt; // assembled prompt text sent to the LLM for recipient POV
        public string neutralPrompt; // assembled prompt text sent to the LLM for neutral POV
        public string initiatorGeneratedText; // text returned by the LLM for initiator POV
        public string recipientGeneratedText; // text returned by the LLM for recipient POV
        public string neutralGeneratedText; // text returned by the LLM for neutral POV
        public string initiatorStatus; // generation status for initiator POV
        public string recipientStatus; // generation status for recipient POV
        public string neutralStatus; // generation status for neutral POV
        public string initiatorError; // error message from a failed initiator generation
        public string recipientError; // error message from a failed recipient generation
        public string neutralError; // error message from a failed neutral generation
        public string initiatorLlmEndpoint; // LLM endpoint used for initiator POV generation
        public string recipientLlmEndpoint; // LLM endpoint used for recipient POV generation
        public string neutralLlmEndpoint; // LLM endpoint used for neutral POV generation
        public string initiatorLlmModel; // LLM model name used for initiator POV generation
        public string recipientLlmModel; // LLM model name used for recipient POV generation
        public string neutralLlmModel; // LLM model name used for neutral POV generation
        public string llmEndpoint; // legacy flat field, migrated to per-POV fields on load
        public string llmModel; // legacy flat field, migrated to per-POV fields on load
        public bool solo; // true for events with a single POV (e.g. mental breaks)

        // Mood impact direction for mood-event diary entries: "positive", "negative", or "neutral".
        // Reflects how the condition actually feels for the pawn (e.g. PsychicSuppressorMale is
        // "neutral" for female pawns). Used in gameContext (mood_impact=) and to select text keys.
        // Empty string for non-mood events.
        public string moodImpact;

        // Save/load hook (runs for BOTH directions). The string tags ("eventId", ...) are the
        // keys written to the save file — renaming one breaks existing saves. The PostLoadInit
        // block below back-fills defaults/normalizes status when loading older saves.
        // See AGENTS.md ("IExposable").
        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Collections.Look(ref playLogEntryIds, "playLogEntryIds", LookMode.Value);
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
            Scribe_Values.Look(ref initiatorLastOpener, "initiatorLastOpener");
            Scribe_Values.Look(ref recipientLastOpener, "recipientLastOpener");
            Scribe_Values.Look(ref initiatorBurningPassion, "initiatorBurningPassion");
            Scribe_Values.Look(ref recipientBurningPassion, "recipientBurningPassion");
            Scribe_Values.Look(ref initiatorWeapon, "initiatorWeapon");
            Scribe_Values.Look(ref recipientWeapon, "recipientWeapon");
            Scribe_Values.Look(ref initiatorAtmosphere, "initiatorAtmosphere");
            Scribe_Values.Look(ref recipientAtmosphere, "recipientAtmosphere");
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
            Scribe_Values.Look(ref moodImpact, "moodImpact");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Backfill defaults for fields that may be absent in older saves
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    eventId = Guid.NewGuid().ToString("N");
                }

                if (playLogEntryIds == null)
                {
                    playLogEntryIds = new List<int>();
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

                if (string.IsNullOrWhiteSpace(initiatorLastOpener))
                {
                    initiatorLastOpener = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(recipientLastOpener))
                {
                    recipientLastOpener = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(initiatorBurningPassion))
                {
                    initiatorBurningPassion = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(recipientBurningPassion))
                {
                    recipientBurningPassion = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(initiatorWeapon))
                {
                    initiatorWeapon = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(recipientWeapon))
                {
                    recipientWeapon = string.Empty;
                }

                // Mood impact defaults to neutral for older saves that don't have this field.
                // "positive"/"negative" are set at record time for mood-event entries.
                if (string.IsNullOrWhiteSpace(moodImpact))
                {
                    moodImpact = MoodImpact.Neutral;
                }

                if (neutralStatus == null)
                {
                    neutralStatus = NotGeneratedStatus;
                }

                // Normalize statuses: treat stale "pending" or empty as not_generated,
                // and upgrade to "complete" if generated text is already present
                initiatorStatus = NormalizeLoadedStatus(initiatorStatus, initiatorGeneratedText);
                recipientStatus = NormalizeLoadedStatus(recipientStatus, recipientGeneratedText);
                neutralStatus = NormalizeLoadedStatus(neutralStatus, neutralGeneratedText);

                // Migrate legacy flat LLM fields into per-POV fields when per-POV slots are empty
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

        /// <summary>
        /// Stores the assembled prompt text for a specific POV role.
        /// </summary>
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

        /// <summary>
        /// Records which LLM endpoint and model were used for a specific POV role.
        /// </summary>
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

        /// <summary>
        /// Sets the same prompt text for both initiator and recipient POVs (dual-POV mode).
        /// </summary>
        public void SetDualPrompt(string prompt)
        {
            initiatorPrompt = prompt;
            recipientPrompt = prompt;
        }

        /// <summary>
        /// Returns true if this POV role can be queued for LLM generation (not already pending/complete/failed).
        /// </summary>
        public bool CanQueueGeneration(string povRole)
        {
            string status = StatusFor(povRole);
            return string.IsNullOrWhiteSpace(status) || RoleEquals(status, NotGeneratedStatus);
        }

        /// <summary>
        /// Returns true if both initiator and recipient POVs can be queued (non-solo dual-POV event).
        /// </summary>
        public bool CanQueueDual()
        {
            return !solo && CanQueueGeneration(InitiatorRole) && CanQueueGeneration(RecipientRole);
        }

        /// <summary>
        /// Returns true if the given role is either initiator or recipient (not neutral/dual).
        /// </summary>
        public static bool RoleIsInitiatorOrRecipient(string povRole)
        {
            return RoleEquals(povRole, InitiatorRole) || RoleEquals(povRole, RecipientRole);
        }

        /// <summary>
        /// Marks a POV role as pending generation and clears any previous error.
        /// </summary>
        public void MarkQueued(string povRole)
        {
            SetStatus(povRole, PendingStatus);
            SetError(povRole, null);
        }

        /// <summary>
        /// Returns true if the given POV role is currently marked pending (queued or generating).
        /// </summary>
        public bool IsPending(string povRole)
        {
            return RoleEquals(StatusFor(povRole), PendingStatus);
        }

        /// <summary>
        /// Resets a pending POV role back to not-generated (clearing any error) so it can be re-queued.
        /// Used to recover an entry orphaned on "Generating" when its in-flight request was dropped
        /// (e.g. a session restart cancelled it). No-op if the role is not pending.
        /// </summary>
        public void ResetPendingToNotGenerated(string povRole)
        {
            if (IsPending(povRole))
            {
                SetStatus(povRole, NotGeneratedStatus);
                SetError(povRole, null);
            }
        }

        /// <summary>
        /// Marks a POV role as failed and records the error message.
        /// </summary>
        public void MarkFailed(string povRole, string error)
        {
            SetStatus(povRole, FailedStatus);
            SetError(povRole, error);
        }

        /// <summary>
        /// Marks both initiator and recipient POVs as pending generation.
        /// </summary>
        public void MarkDualQueued()
        {
            MarkQueued(InitiatorRole);
            MarkQueued(RecipientRole);
        }

        /// <summary>
        /// Marks both initiator and recipient POVs as failed with the given error.
        /// </summary>
        public void MarkDualFailed(string error)
        {
            MarkFailed(InitiatorRole, error);
            MarkFailed(RecipientRole, error);
        }

        /// <summary>
        /// Applies an LLM generation result to the appropriate POV slot based on result.povRole.
        /// Dispatches to the initiator, recipient, neutral, or dual handler.
        /// </summary>
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

        /// <summary>
        /// Builds a read-only view of this event for the given pawn's POV, or null if the pawn is not involved.
        /// For two-pawn events, includes a LinkedEntryView previewing the other pawn's entry.
        /// </summary>
        public DiaryEntryView ToViewFor(string pawnId)
        {
            string povRole = RoleForPawn(pawnId);
            if (string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            if (HasDeathDescription())
            {
                if (!IsDeathDescriptionFor(pawnId))
                {
                    return null;
                }

                povRole = NeutralRole;
            }

            if (HasArrivalDescription())
            {
                if (!IsArrivalDescriptionFor(pawnId))
                {
                    return null;
                }

                povRole = NeutralRole;
            }

            DiaryInteractionGroupDef group = GroupForDisplay();

            // Build a linked entry for the other pawn in a paired event.
            // Solo events (mental breaks) and legacy entries have no link.
            LinkedEntryView linkedEntry = null;
            if (!solo && RoleIsInitiatorOrRecipient(povRole))
            {
                string otherRole = RoleEquals(povRole, InitiatorRole) ? RecipientRole : InitiatorRole;
                string otherPawnId = RoleEquals(otherRole, InitiatorRole) ? initiatorPawnId : recipientPawnId;
                string otherPawnName = RoleEquals(otherRole, InitiatorRole) ? initiatorName : recipientName;
                string otherGeneratedText = GeneratedTextFor(otherRole);
                bool otherGenerated = !string.IsNullOrWhiteSpace(otherGeneratedText);
                string truncated = TruncateForPreview(otherGenerated
                    ? otherGeneratedText
                    : TextFor(otherRole));

                linkedEntry = new LinkedEntryView(
                    otherPawnId,
                    otherPawnName ?? string.Empty,
                    otherRole,
                    eventId,
                    truncated,
                    otherGenerated);
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
                povRole,
                group?.label ?? interactionLabel ?? string.Empty,
                group == null || group.important,
                linkedEntry);
        }

        /// <summary>
        /// Truncates text to a short preview suitable for the linked-entry card.
        /// Takes the first line (or up to ~120 characters), appending an ellipsis if truncated.
        /// </summary>
        private static string TruncateForPreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int newlineIdx = normalized.IndexOf('\n');
            string firstLine = newlineIdx >= 0 ? normalized.Substring(0, newlineIdx) : normalized;
            firstLine = firstLine.Trim();

            const int MaxPreviewLength = 120;
            if (firstLine.Length > MaxPreviewLength)
            {
                return firstLine.Substring(0, MaxPreviewLength) + "...";
            }

            return firstLine;
        }

        /// <summary>
        /// Returns the POV role string for a pawn involved in this event, or null if the pawn is not part of it.
        /// </summary>
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

        /// <summary>
        /// Returns true when this event has a neutral colonist-death description and the requested
        /// pawn is the deceased colonist. The UI then shows the death description instead of a
        /// persona-based diary entry from the dead pawn's POV.
        /// </summary>
        public bool IsDeathDescriptionFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(gameContext))
            {
                return false;
            }

            return HasDeathDescription()
                && gameContext.IndexOf("death_victim_id=" + pawnId, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true when this event owns a neutral colonist-death description request.
        /// </summary>
        public bool HasDeathDescription()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("death_description=true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true when this event has a neutral colony-arrival description and the requested
        /// pawn is the colonist who joined.
        /// </summary>
        public bool IsArrivalDescriptionFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(gameContext))
            {
                return false;
            }

            return HasArrivalDescription()
                && gameContext.IndexOf("arrival_pawn_id=" + pawnId, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true when this event owns a neutral colony-arrival description request.
        /// </summary>
        public bool HasArrivalDescription()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("arrival_description=true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Remembers the RimWorld social-log row(s) that were folded into this diary event.
        /// Small-talk batching can add several ids to one event; direct interactions add one.
        /// </summary>
        public void AddPlayLogEntryId(int playLogEntryId)
        {
            if (playLogEntryId < 0)
            {
                return;
            }

            if (playLogEntryIds == null)
            {
                playLogEntryIds = new List<int>();
            }

            if (!playLogEntryIds.Contains(playLogEntryId))
            {
                playLogEntryIds.Add(playLogEntryId);
            }
        }

        /// <summary>
        /// True when a clicked RimWorld social-log entry maps back to this diary event.
        /// </summary>
        public bool MatchesPlayLogEntry(int playLogEntryId)
        {
            return playLogEntryId >= 0
                && playLogEntryIds != null
                && playLogEntryIds.Contains(playLogEntryId);
        }

        /// <summary>
        /// Returns true if the two given pawn IDs are the initiator and recipient of this event (in either order).
        /// </summary>
        public bool Involves(string firstPawnId, string secondPawnId)
        {
            return (firstPawnId == initiatorPawnId && secondPawnId == recipientPawnId)
                || (firstPawnId == recipientPawnId && secondPawnId == initiatorPawnId);
        }

        /// <summary>
        /// Returns the raw game description text for the given POV role.
        /// </summary>
        public string TextForRole(string povRole)
        {
            return TextFor(povRole);
        }

        /// <summary>
        /// Returns the LLM-generated text for the role if available, otherwise falls back to the raw game text.
        /// </summary>
        public string DisplayTextForRole(string povRole)
        {
            string generated = GeneratedTextFor(povRole);
            return string.IsNullOrWhiteSpace(generated) ? TextFor(povRole) : generated;
        }

        /// <summary>
        /// Returns the display name for the given role, or "colony" for neutral.
        /// </summary>
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

            return "PawnDiary.Prompt.Colony".Translate();
        }

        /// <summary>
        /// Returns the surroundings description for the given role (defaults to initiator's surroundings for neutral).
        /// </summary>
        public string SurroundingsForRole(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSurroundings;
            }

            return initiatorSurroundings;
        }

        /// <summary>
        /// Returns the atmospheric tone phrase for the specified POV role.
        /// </summary>
        public string AtmosphereForRole(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientAtmosphere;
            }

            return initiatorAtmosphere;
        }

        /// <summary>
        /// Resolves the event group used by the Diary tab for labels and importance coloring.
        /// Saved events only keep the defName string, so this reuses the XML classifiers.
        /// </summary>
        private DiaryInteractionGroupDef GroupForDisplay()
        {
            GroupDomain domain = IsTaleEvent()
                ? GroupDomain.Tale
                : IsMoodEventEvent()
                    ? GroupDomain.MoodEvent
                    : IsThoughtEvent()
                        ? GroupDomain.Thought
                        : IsMentalStateEvent() ? GroupDomain.MentalState : GroupDomain.Interaction;
            return InteractionGroups.ClassifyDefName(domain, interactionDefName);
        }

        /// <summary>
        /// Returns true if this event belongs to an important group (not chit chat, small talk, etc.).
        /// Used to decide whether to include burning passion in the prompt context.
        /// </summary>
        public bool IsImportant()
        {
            DiaryInteractionGroupDef group = GroupForDisplay();
            return group == null || group.important;
        }

        /// <summary>
        /// Returns true if this event is combat-related (social fights, mental breaks with violence).
        /// Used to decide whether to include equipped weapon in the prompt context.
        /// </summary>
        public bool IsCombatRelated()
        {
            // Mental state events (social fights, violent breaks) are combat-related
            if (IsMentalStateEvent())
            {
                return true;
            }

            // Otherwise rely on a data-driven flag set per group in XML, rather than sniffing
            // defName substrings (which silently breaks if a group is renamed or a new one added).
            DiaryInteractionGroupDef group = GroupForDisplay();
            return group != null && group.combat;
        }

        /// <summary>
        /// Returns true if the weapon should be included in the prompt (important or combat events).
        /// </summary>
        public bool ShouldShowWeapon()
        {
            return IsImportant() || IsCombatRelated();
        }

        /// <summary>
        /// Mental-state events store their state defName in interactionDefName too; their context
        /// starts with a stable mental_state field, which lets UI classification pick the right domain.
        /// </summary>
        private bool IsMentalStateEvent()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("mental_state=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Tale events store their TaleDef defName in interactionDefName too; their context
        /// starts with a stable tale field, which lets UI classification pick the Tale domain.
        /// </summary>
        private bool IsTaleEvent()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("tale=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Mood-affecting GameCondition events store their GameConditionDef defName in
        /// interactionDefName; their context starts with a stable mood_event field, which
        /// lets UI classification pick the MoodEvent domain.
        /// </summary>
        private bool IsMoodEventEvent()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("mood_event=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Thought events store their ThoughtDef defName in interactionDefName; their context
        /// starts with a stable thought field, which lets UI classification pick the Thought domain.
        /// </summary>
        private bool IsThoughtEvent()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("thought=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns the continuity context string for the given role, or "none" for neutral.
        /// </summary>
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

        /// <summary>
        /// Returns the last opener (first sentence of previous diary entry) for the given role,
        /// or empty string for neutral. Used to avoid repetitive openings.
        /// </summary>
        public string LastOpenerForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorLastOpener;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientLastOpener;
            }

            return string.Empty;
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

        public static bool RoleEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
