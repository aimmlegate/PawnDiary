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
        public bool solo; // true for events with a single POV (e.g. mental breaks)
        // Per-POV title: short chat-style subject (3-8 words) derived from the generated entry.
        // Populated by the "Generate LLM titles" follow-up call; empty means the diary card
        // header stays date-only. Separate from the per-POV status fields so the main-entry
        // recovery logic is untouched.
        public string initiatorTitle;
        public string recipientTitle;
        public string neutralTitle;
        // Per-POV title-generation status and error. Reuse PendingStatus/CompleteStatus/FailedStatus
        // from the main-entry vocabulary so the title follow-up rides the same status machine.
        public string initiatorTitleStatus;
        public string recipientTitleStatus;
        public string neutralTitleStatus;
        public string initiatorTitleError;
        public string recipientTitleError;
        public string neutralTitleError;

        // Mood impact direction for mood-event diary entries: "positive", "negative", or "neutral".
        // Reflects how the condition actually feels for the pawn (e.g. PsychicSuppressorMale is
        // "neutral" for female pawns). Used in gameContext (mood_impact=) and to select text keys.
        // Empty string for non-mood events.
        public string moodImpact;

        // Save/load hook (runs for BOTH directions). The string tags ("eventId", ...) are the
        // keys written to the save file — renaming one breaks saved games. The PostLoadInit
        // block below keeps loaded fields non-null and normalizes status.
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
            Scribe_Values.Look(ref initiatorLlmEndpoint, "initiatorLlmEndpoint");
            Scribe_Values.Look(ref recipientLlmEndpoint, "recipientLlmEndpoint");
            Scribe_Values.Look(ref neutralLlmEndpoint, "neutralLlmEndpoint");
            Scribe_Values.Look(ref initiatorLlmModel, "initiatorLlmModel");
            Scribe_Values.Look(ref recipientLlmModel, "recipientLlmModel");
            Scribe_Values.Look(ref neutralLlmModel, "neutralLlmModel");
            Scribe_Values.Look(ref moodImpact, "moodImpact");
            Scribe_Values.Look(ref initiatorTitle, "initiatorTitle");
            Scribe_Values.Look(ref recipientTitle, "recipientTitle");
            Scribe_Values.Look(ref neutralTitle, "neutralTitle");
            Scribe_Values.Look(ref initiatorTitleStatus, "initiatorTitleStatus");
            Scribe_Values.Look(ref recipientTitleStatus, "recipientTitleStatus");
            Scribe_Values.Look(ref neutralTitleStatus, "neutralTitleStatus");
            Scribe_Values.Look(ref initiatorTitleError, "initiatorTitleError");
            Scribe_Values.Look(ref recipientTitleError, "recipientTitleError");
            Scribe_Values.Look(ref neutralTitleError, "neutralTitleError");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Keep loaded fields non-null so later code can stay simple.
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

                // Mood impact defaults to neutral when no mood direction was saved.
                // "positive"/"negative" are set at record time for mood-event entries.
                if (string.IsNullOrWhiteSpace(moodImpact))
                {
                    moodImpact = MoodImpact.Neutral;
                }

                if (neutralStatus == null)
                {
                    neutralStatus = NotGeneratedStatus;
                }

                // Empty title fields render a date-only card header.
                if (initiatorTitle == null)
                {
                    initiatorTitle = string.Empty;
                }

                if (recipientTitle == null)
                {
                    recipientTitle = string.Empty;
                }

                if (neutralTitle == null)
                {
                    neutralTitle = string.Empty;
                }

                if (initiatorTitleStatus == null)
                {
                    initiatorTitleStatus = string.Empty;
                }

                if (recipientTitleStatus == null)
                {
                    recipientTitleStatus = string.Empty;
                }

                if (neutralTitleStatus == null)
                {
                    neutralTitleStatus = string.Empty;
                }

                if (initiatorTitleError == null)
                {
                    initiatorTitleError = string.Empty;
                }

                if (recipientTitleError == null)
                {
                    recipientTitleError = string.Empty;
                }

                if (neutralTitleError == null)
                {
                    neutralTitleError = string.Empty;
                }

                // Normalize statuses: treat stale "pending" or empty as not_generated,
                // and upgrade to "complete" if generated text is already present
                initiatorStatus = NormalizeLoadedStatus(initiatorStatus, initiatorGeneratedText);
                recipientStatus = NormalizeLoadedStatus(recipientStatus, recipientGeneratedText);
                neutralStatus = NormalizeLoadedStatus(neutralStatus, neutralGeneratedText);
                // Title requests run on the same async client, but use separate status fields.
                // If a save/load interrupts an in-flight title call, no result will arrive for this
                // session, so pending title statuses must be cleared or the title retry guard will
                // keep treating the missing request as active forever.
                initiatorTitleStatus = NormalizeLoadedTitleStatus(initiatorTitleStatus, initiatorTitle);
                recipientTitleStatus = NormalizeLoadedTitleStatus(recipientTitleStatus, recipientTitle);
                neutralTitleStatus = NormalizeLoadedTitleStatus(neutralTitleStatus, neutralTitle);

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

        /// <summary>
        /// Stores the LLM-generated title for a specific POV role. Mirrors
        /// <see cref="SetLlmMeta"/>; an empty string clears the stored title so the view renders
        /// a date-only card header.
        /// </summary>
        public void SetTitle(string povRole, string title)
        {
            string value = title ?? string.Empty;
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorTitle = value;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientTitle = value;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralTitle = value;
            }
        }

        /// <summary>
        /// Returns the stored LLM title for a POV role, or empty string when none has been set
        /// yet. Public because the title-queue decision in
        /// <c>DiaryGameComponent.Generation.cs</c> reads the same field.
        /// </summary>
        public string TitleForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorTitle ?? string.Empty;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientTitle ?? string.Empty;
            }

            return neutralTitle ?? string.Empty;
        }

        /// <summary>
        /// Marks a POV role's title follow-up as queued. Clears any previous title error.
        /// Distinct from <see cref="MarkQueued"/> so the main-entry recovery scan never sees
        /// a title status.
        /// </summary>
        public void MarkTitleQueued(string povRole)
        {
            SetTitleStatus(povRole, PendingStatus);
            SetTitleError(povRole, null);
        }

        /// <summary>
        /// Marks a POV role's title follow-up as failed and records the error message.
        /// If no previous title exists, the view keeps the card header date-only.
        /// </summary>
        public void MarkTitleFailed(string povRole, string error)
        {
            SetTitleStatus(povRole, FailedStatus);
            SetTitleError(povRole, error);
        }

        /// <summary>
        /// Returns true if a title follow-up is currently pending (queued or generating) for
        /// the given POV role. Mirrors <see cref="IsPending"/> for the main-entry status.
        /// </summary>
        public bool IsTitlePending(string povRole)
        {
            return RoleEquals(TitleStatusFor(povRole), PendingStatus);
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
        /// Public read accessor for the recorded LLM endpoint of a POV role. Used by the
        /// title queueing logic to pin a follow-up title to the same lane the main entry used
        /// (so a paired event stays on one model).
        /// </summary>
        public string LlmEndpointForRole(string povRole)
        {
            return LlmEndpointFor(povRole);
        }

        /// <summary>
        /// Public read accessor for the recorded LLM model of a POV role. Mirrors
        /// <see cref="LlmEndpointForRole"/>; both are returned together so the title queue
        /// can match a lane by endpoint+model pair.
        /// </summary>
        public string LlmModelForRole(string povRole)
        {
            return LlmModelFor(povRole);
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
        /// Applies an LLM generation result to the appropriate POV slot based on result.povRole.
        /// Dispatches to the initiator, recipient, or neutral handler.
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

            // Title for this pawn's POV: stored LLM title only. When empty, EntryHeader renders
            // the date alone with no separator.
            string titleForPov = TitleForRole(povRole);
            bool titlePendingForPov = string.IsNullOrWhiteSpace(titleForPov) && IsTitlePending(povRole);

            // Build a linked entry for the other pawn in a paired event.
            // Solo events (mental breaks) have no link.
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

                // Title for the OTHER pawn's POV, mirrored onto the linked preview when the
                // title-generation follow-up has stored one.
                string otherTitle = TitleForRole(otherRole);

                linkedEntry = new LinkedEntryView(
                    otherPawnId,
                    otherPawnName ?? string.Empty,
                    otherRole,
                    eventId,
                    truncated,
                    otherGenerated,
                    otherTitle);
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
                linkedEntry,
                titleForPov,
                titlePendingForPov);
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
        /// Returns true when this event is an end-of-day reflection (see DiaryGameComponent.DaySummary.cs).
        /// Used to pick the reflection system prompt at dispatch.
        /// </summary>
        public bool IsDayReflection()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("day_reflection=true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The emotional-register cue for this event's group (e.g. "with creeping dread"), or empty
        /// when the group sets no tone. Event-driven and appended to the user prompt as a "tone:"
        /// field for first-person entries.
        /// </summary>
        public string ToneDirective()
        {
            DiaryInteractionGroupDef group = GroupForDisplay();
            return group != null ? group.tone : string.Empty;
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
                        : IsGameEvent()
                            ? GroupDomain.GameEvent
                            : IsWorkEvent()
                                ? GroupDomain.Work
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
        /// Synthetic game events store their event key in interactionDefName; their context starts
        /// with a stable game_event field, which lets UI classification pick the GameEvent domain.
        /// </summary>
        private bool IsGameEvent()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("game_event=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Work scanner events store the real WorkTypeDef in the stable work= field; the saved
        /// interactionDefName is a synthetic group defName such as PawnDiary_WorkPassion.
        /// </summary>
        private bool IsWorkEvent()
        {
            return !string.IsNullOrWhiteSpace(gameContext)
                && gameContext.IndexOf("work=", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private void SetTitleStatus(string povRole, string status)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorTitleStatus = status;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientTitleStatus = status;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralTitleStatus = status;
            }
        }

        private void SetTitleError(string povRole, string error)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorTitleError = error;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientTitleError = error;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralTitleError = error;
            }
        }

        private string TitleStatusFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorTitleStatus;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientTitleStatus;
            }

            return neutralTitleStatus;
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

        private static string NormalizeLoadedTitleStatus(string status, string title)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                return CompleteStatus;
            }

            if (RoleEquals(status, PendingStatus))
            {
                return string.Empty;
            }

            return status ?? string.Empty;
        }

        public static bool RoleEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
