// One recorded event (interaction / social fight / mental break / notable tale) and all of its
// generation state for up to two points of view. Per-POV (initiator/recipient/neutral) state lives
// in three PovSlot value-typed fields and is reached through SlotFor(role), the single place that
// maps a role to its storage; the historical public field names survive as facade properties. Pure
// model: fields, save/load, and prompt/result plumbing. Split out of DiaryGameComponent.cs. See
// DOCUMENTATION.md.
using System;
using System.Collections.Generic;
using RimWorld;
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
        public const string NotGeneratedStatus = DiaryGenerationStatus.NotGenerated;
        public const string PendingStatus = DiaryGenerationStatus.Pending;
        public const string CompleteStatus = DiaryGenerationStatus.Complete;
        public const string FailedStatus = DiaryGenerationStatus.Failed;
        public const string SkippedStatus = DiaryGenerationStatus.Skipped;
        public const string PromptOnlyStatus = DiaryGenerationStatus.PromptOnly;
        public const string CombatColorCue = "combat";
        public const string SocialFightColorCue = "socialFight";
        public const string MentalBreakColorCue = "mentalBreak";
        public const string DazeColorCue = "daze";
        public const string ExtremeDarkColorCue = "extremeDark";
        public const string StrangeChatColorCue = "strangeChat";
        public const string WhiteColorCue = "white";
        public const string QuietColorCue = "quiet";
        private const int MaxPersistedRawResponseChars = 4000;

        // ---------------------------------------------------------------------------------------------
        // Per-POV storage.
        //
        // Every point-of-view (initiator / recipient / neutral) used to be a full triplicated field
        // family (initiatorStatus / recipientStatus / neutralStatus, ...). Those ~60 fields are now
        // three PovSlot value-typed fields below, and role -> storage is decided in exactly one place:
        // SlotFor(role). The historical public field names (initiatorStatus, recipientPawnId,
        // neutralTitle, ...) survive as facade properties after the slot fields so external callers
        // and object initializers compile unchanged.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds the per-point-of-view state for ONE role (initiator, recipient, or neutral). All
        /// three roles share the generation-pipeline fields (prompt, generated text, raw response,
        /// status/error, LLM lane, title + title status/error, raw text). The pawn-specific fields
        /// (pawn id/name/summary/surroundings/continuity/last-opener/weapon/staggered intensity/
        /// text-decoration facts) are only ever populated for the initiator and recipient slots; the
        /// neutral slot leaves them empty and they are never read through a pawn path. This is a
        /// value type so the three slots live inline on <see cref="DiaryEvent"/> and Scribe can take
        /// refs to their fields directly (a class would need an extra allocation and indirection).
        /// </summary>
        public struct PovSlot
        {
            // Generation-pipeline fields, persisted for every role (including neutral).
            public string prompt;
            public string generatedText;
            public string rawResponse;
            public string status;
            public string error;
            public string llmEndpoint;
            public string llmModel;
            public string title;
            public string titleStatus;
            public string titleError;
            public string text;
            // Pawn-specific fields, persisted only for initiator/recipient; always empty for neutral.
            public string pawnId;
            public string name;
            public string pawnSummary;
            public string surroundings;
            public string continuity;
            public string lastOpener;
            public string weapon;
            public int staggeredIntensity;
            public string textDecorationFacts;
        }

        // The three POV storage slots. Value-typed, so they live inline here and are mutated in
        // place through SlotFor(role) or the facade properties below.
        private PovSlot initiatorSlot;
        private PovSlot recipientSlot;
        private PovSlot neutralSlot;

        // ---------------------------------------------------------------------------------------------
        // Event-level (non-POV) fields. These describe the event itself, not any one POV.
        // ---------------------------------------------------------------------------------------------

        public string eventId; // unique ID for this event, stable across saves
        public List<int> playLogEntryIds = new List<int>(); // Verse.LogEntry ids that produced this diary event
        public int tick; // game tick when the event was recorded
        public string date; // human-readable date string at event time
        public string interactionDefName; // RimWorld InteractionDef defName (e.g. "Chat")
        // A real InteractionDef defName usable to build the generated-speech Social-log row. For
        // combined interaction batches interactionDefName is a synthetic group name that does not
        // resolve as an InteractionDef, so the originating interaction's def is kept here for
        // injection. Empty for events with no underlying InteractionDef (mental states, tales).
        public string playLogInteractionDefName;
        public string interactionLabel; // display label for the interaction type
        public string gameContext; // metadata string describing game state at event time
        public string instruction; // group-specific prompt instruction appended to LLM calls
        public string colorCue; // stable semantic UI color key; empty older saves derive it from group/context
        public bool solo; // true for events with a single POV (e.g. mental breaks)

        // Mood impact direction for mood-event diary entries: "positive", "negative", or "neutral".
        // Reflects how the condition actually feels for the pawn (e.g. PsychicSuppressorMale is
        // "neutral" for female pawns). Used in gameContext (mood_impact=) and to select text keys.
        // Empty string for non-mood events.
        public string moodImpact;

        // LogID of the optional generated direct-speech Social-log row. -1 means no row has been
        // added for this event. Stored so a duplicate result or later load will not add another.
        public int generatedSpeechPlayLogEntryId = -1;

        // ---------------------------------------------------------------------------------------------
        // Public per-POV field facades.
        //
        // These preserve the historical public field names so external callers, object initializers,
        // and direct member reads/writes compile unchanged. They are PROPERTIES (not fields): they
        // read/write the matching field on the role's PovSlot above. Because properties cannot be
        // passed by ref, save/load and internal mutation go through the slot fields and SlotFor(role)
        // instead — never through these facades.
        // ---------------------------------------------------------------------------------------------

        // Pawn identity / display (initiator + recipient only; neutral has no pawn).
        public string initiatorPawnId { get => initiatorSlot.pawnId; set => initiatorSlot.pawnId = value; }
        public string recipientPawnId { get => recipientSlot.pawnId; set => recipientSlot.pawnId = value; }
        public string initiatorName { get => initiatorSlot.name; set => initiatorSlot.name = value; }
        public string recipientName { get => recipientSlot.name; set => recipientSlot.name = value; }

        // Raw game-side description text (all three roles).
        public string initiatorText { get => initiatorSlot.text; set => initiatorSlot.text = value; }
        public string recipientText { get => recipientSlot.text; set => recipientSlot.text = value; }
        public string neutralText { get => neutralSlot.text; set => neutralSlot.text = value; }

        // Pawn context summaries (initiator + recipient only).
        public string initiatorPawnSummary { get => initiatorSlot.pawnSummary; set => initiatorSlot.pawnSummary = value; }
        public string recipientPawnSummary { get => recipientSlot.pawnSummary; set => recipientSlot.pawnSummary = value; }
        public string initiatorSurroundings { get => initiatorSlot.surroundings; set => initiatorSlot.surroundings = value; }
        public string recipientSurroundings { get => recipientSlot.surroundings; set => recipientSlot.surroundings = value; }
        public string initiatorContinuity { get => initiatorSlot.continuity; set => initiatorSlot.continuity = value; }
        public string recipientContinuity { get => recipientSlot.continuity; set => recipientSlot.continuity = value; }
        public string initiatorLastOpener { get => initiatorSlot.lastOpener; set => initiatorSlot.lastOpener = value; }
        public string recipientLastOpener { get => recipientSlot.lastOpener; set => recipientSlot.lastOpener = value; }
        public string initiatorWeapon { get => initiatorSlot.weapon; set => initiatorSlot.weapon = value; }
        public string recipientWeapon { get => recipientSlot.weapon; set => recipientSlot.weapon = value; }

        // Generated LLM output (all three roles).
        public string initiatorGeneratedText { get => initiatorSlot.generatedText; set => initiatorSlot.generatedText = value; }
        public string recipientGeneratedText { get => recipientSlot.generatedText; set => recipientSlot.generatedText = value; }
        public string neutralGeneratedText { get => neutralSlot.generatedText; set => neutralSlot.generatedText = value; }

        // Generation status (all three roles).
        public string initiatorStatus { get => initiatorSlot.status; set => initiatorSlot.status = value; }
        public string recipientStatus { get => recipientSlot.status; set => recipientSlot.status = value; }
        public string neutralStatus { get => neutralSlot.status; set => neutralSlot.status = value; }

        // Recorded LLM lane (all three roles).
        public string initiatorLlmEndpoint { get => initiatorSlot.llmEndpoint; set => initiatorSlot.llmEndpoint = value; }
        public string recipientLlmEndpoint { get => recipientSlot.llmEndpoint; set => recipientSlot.llmEndpoint = value; }
        public string neutralLlmEndpoint { get => neutralSlot.llmEndpoint; set => neutralSlot.llmEndpoint = value; }
        public string initiatorLlmModel { get => initiatorSlot.llmModel; set => initiatorSlot.llmModel = value; }
        public string recipientLlmModel { get => recipientSlot.llmModel; set => recipientSlot.llmModel = value; }
        public string neutralLlmModel { get => neutralSlot.llmModel; set => neutralSlot.llmModel = value; }

        // Per-POV title: short chat-style subject derived from the generated entry.
        // Populated by the "Generate LLM titles" follow-up call; empty means the diary card
        // header stays date-only. Separate from the per-POV status fields so the main-entry
        // recovery logic is untouched.
        public string initiatorTitle { get => initiatorSlot.title; set => initiatorSlot.title = value; }
        public string recipientTitle { get => recipientSlot.title; set => recipientSlot.title = value; }
        public string neutralTitle { get => neutralSlot.title; set => neutralSlot.title = value; }

        // Per-POV title-generation status. Reuse PendingStatus/CompleteStatus/FailedStatus
        // from the main-entry vocabulary so the title follow-up rides the same status machine.
        public string initiatorTitleStatus { get => initiatorSlot.titleStatus; set => initiatorSlot.titleStatus = value; }
        public string recipientTitleStatus { get => recipientSlot.titleStatus; set => recipientSlot.titleStatus = value; }
        public string neutralTitleStatus { get => neutralSlot.titleStatus; set => neutralSlot.titleStatus = value; }

        // Display-only typography intensity captured from the live POV pawn at record time.
        // 0 = normal; 1..4 = increasingly staggered variable-size words for intoxication or low
        // Consciousness. Kept for old saves/prompt contracts; current display decorations are driven
        // by the XML text-decoration context fields below.
        public int initiatorStaggeredIntensity { get => initiatorSlot.staggeredIntensity; set => initiatorSlot.staggeredIntensity = value; }
        public int recipientStaggeredIntensity { get => recipientSlot.staggeredIntensity; set => recipientSlot.staggeredIntensity = value; }

        // Compact serialized hediff/trait facts captured from the POV pawn at record time. The UI
        // combines these with event metadata and lets XML DiaryTextDecorationDef rules choose visual
        // text decorations without reading live pawn state.
        public string initiatorTextDecorationFacts { get => initiatorSlot.textDecorationFacts; set => initiatorSlot.textDecorationFacts = value; }
        public string recipientTextDecorationFacts { get => recipientSlot.textDecorationFacts; set => recipientSlot.textDecorationFacts = value; }

        // Save/load hook (runs for BOTH directions). The string tags ("eventId", ...) are the keys
        // written to the save file. Per-POV state is persisted straight out of the three PovSlot
        // fields via ScribePawnSlot / ScribeNeutralSlot under the same flat key names as before the
        // refactor, so the save shape is unchanged. The PostLoadInit block below keeps loaded fields
        // non-null and normalizes status. See AGENTS.md ("IExposable").
        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Collections.Look(ref playLogEntryIds, "playLogEntryIds", LookMode.Value);
            Scribe_Values.Look(ref solo, "solo", false);
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref date, "date");
            Scribe_Values.Look(ref interactionDefName, "interactionDefName");
            Scribe_Values.Look(ref playLogInteractionDefName, "playLogInteractionDefName");
            Scribe_Values.Look(ref interactionLabel, "interactionLabel");
            Scribe_Values.Look(ref gameContext, "gameContext");
            Scribe_Values.Look(ref instruction, "instruction");
            Scribe_Values.Look(ref colorCue, "colorCue");
            Scribe_Values.Look(ref moodImpact, "moodImpact");

            // Per-POV storage: each slot is scribed under its historical flat keys. Initiator and
            // recipient carry the full field set; neutral carries only the generation-pipeline
            // fields (no pawn-specific state), matching the original neutral save shape.
            ScribePawnSlot(InitiatorRole, ref initiatorSlot);
            ScribePawnSlot(RecipientRole, ref recipientSlot);
            ScribeNeutralSlot(ref neutralSlot);

            Scribe_Values.Look(ref generatedSpeechPlayLogEntryId, "generatedSpeechPlayLogEntryId", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeOnLoad();
            }
        }

        // Post-load cleanup. Event-level fields get non-null defaults / derived fallbacks; each POV
        // slot is normalized via NormalizeLoadedSlot; then the two cross-slot defaults that borrow a
        // sibling's already-normalized value are applied (recipient surroundings <- initiator
        // surroundings; neutral raw text <- merged initiator+recipient text).
        //
        // The pure default/merge/clamp logic lives in DiarySaveNormalization so it can be tested
        // without RimWorld. Two impure steps stay here: minting a fresh eventId for pre-id saves
        // (Guid is non-deterministic) and resolving the color cue (ResolveColorCue hits DefDatabase
        // via GroupForDisplay). See DOCUMENTATION.md §9.
        private void NormalizeOnLoad()
        {
            // Keep loaded event-level fields non-null and derive fallback context/instruction.
            if (string.IsNullOrWhiteSpace(eventId))
            {
                eventId = Guid.NewGuid().ToString("N");
            }

            if (playLogEntryIds == null)
            {
                playLogEntryIds = new List<int>();
            }

            date = DiarySaveNormalization.NormalizeString(date);
            interactionDefName = DiarySaveNormalization.NormalizeString(interactionDefName);
            playLogInteractionDefName = DiarySaveNormalization.NormalizeString(playLogInteractionDefName);
            interactionLabel = DiarySaveNormalization.NormalizeString(interactionLabel);

            if (string.IsNullOrWhiteSpace(gameContext))
            {
                gameContext = DiarySaveNormalization.BuildDefaultGameContext(interactionDefName, interactionLabel);
            }

            if (string.IsNullOrWhiteSpace(instruction))
            {
                instruction = DiarySaveNormalization.BuildDefaultInstruction(interactionLabel);
            }

            // Color-cue resolution is the one impure default: it classifies via GroupForDisplay, which
            // reads the loaded DefDatabase. Stays here so the pure helper has no Def dependency.
            if (string.IsNullOrWhiteSpace(colorCue))
            {
                colorCue = ResolveColorCue(interactionDefName, gameContext);
            }

            // Mood impact defaults to neutral when no mood direction was saved.
            // "positive"/"negative" are set at record time for mood-event entries.
            if (string.IsNullOrWhiteSpace(moodImpact))
            {
                moodImpact = DiarySaveNormalization.DefaultMoodImpact;
            }

            // Per-slot normalization: null/blank pipeline fields are cleaned and statuses upgraded
            // or cleared. Neutral has no pawn-specific fields to normalize.
            NormalizeLoadedSlot(ref initiatorSlot, hasPawnFields: true);
            NormalizeLoadedSlot(ref recipientSlot, hasPawnFields: true);
            NormalizeLoadedSlot(ref neutralSlot, hasPawnFields: false);

            // Cross-slot defaults that depend on an already-normalized sibling slot. The initiator
            // falls back to "unknown"; the recipient then borrows the initiator's value when blank.
            string resolvedInitiatorSurroundings;
            string resolvedRecipientSurroundings;
            DiarySaveNormalization.ResolveSurroundingsChain(
                initiatorSlot.surroundings,
                recipientSlot.surroundings,
                out resolvedInitiatorSurroundings,
                out resolvedRecipientSurroundings);
            initiatorSlot.surroundings = resolvedInitiatorSurroundings;
            recipientSlot.surroundings = resolvedRecipientSurroundings;

            // Neutral raw text is not pawn-authored; if none was saved, merge both POVs' raw text.
            neutralSlot.text = DiarySaveNormalization.BuildDefaultNeutralText(
                neutralSlot.text,
                initiatorSlot.name,
                initiatorSlot.text,
                recipientSlot.name,
                recipientSlot.text);
        }

        // Scribes the full per-POV field set for an initiator/recipient slot under the historical
        // flat keys ("<prefix>PawnId", "<prefix>Status", ...). The slot is passed by ref so Scribe
        // can take refs to its fields directly. Save format is unchanged from before the refactor.
        private static void ScribePawnSlot(string prefix, ref PovSlot slot)
        {
            Scribe_Values.Look(ref slot.pawnId, prefix + "PawnId");
            Scribe_Values.Look(ref slot.name, prefix + "Name");
            Scribe_Values.Look(ref slot.text, prefix + "Text");
            Scribe_Values.Look(ref slot.pawnSummary, prefix + "PawnSummary");
            Scribe_Values.Look(ref slot.surroundings, prefix + "Surroundings");
            Scribe_Values.Look(ref slot.continuity, prefix + "Continuity");
            Scribe_Values.Look(ref slot.lastOpener, prefix + "LastOpener");
            Scribe_Values.Look(ref slot.weapon, prefix + "Weapon");
            Scribe_Values.Look(ref slot.prompt, prefix + "Prompt");
            Scribe_Values.Look(ref slot.generatedText, prefix + "GeneratedText");
            Scribe_Values.Look(ref slot.rawResponse, prefix + "RawResponse");
            Scribe_Values.Look(ref slot.status, prefix + "Status");
            Scribe_Values.Look(ref slot.error, prefix + "Error");
            Scribe_Values.Look(ref slot.llmEndpoint, prefix + "LlmEndpoint");
            Scribe_Values.Look(ref slot.llmModel, prefix + "LlmModel");
            Scribe_Values.Look(ref slot.title, prefix + "Title");
            Scribe_Values.Look(ref slot.titleStatus, prefix + "TitleStatus");
            Scribe_Values.Look(ref slot.titleError, prefix + "TitleError");
            Scribe_Values.Look(ref slot.staggeredIntensity, prefix + "StaggeredIntensity", 0);
            Scribe_Values.Look(ref slot.textDecorationFacts, prefix + "TextDecorationFacts");
        }

        // Neutral chronicle pages carry only the generation-pipeline fields (no pawn id/name/summary/
        // surroundings/continuity/weapon/decoration), so only those are persisted, under the original
        // "neutral*" keys. This keeps the neutral save shape identical to pre-refactor rows.
        private static void ScribeNeutralSlot(ref PovSlot slot)
        {
            Scribe_Values.Look(ref slot.text, "neutralText");
            Scribe_Values.Look(ref slot.prompt, "neutralPrompt");
            Scribe_Values.Look(ref slot.generatedText, "neutralGeneratedText");
            Scribe_Values.Look(ref slot.rawResponse, "neutralRawResponse");
            Scribe_Values.Look(ref slot.status, "neutralStatus");
            Scribe_Values.Look(ref slot.error, "neutralError");
            Scribe_Values.Look(ref slot.llmEndpoint, "neutralLlmEndpoint");
            Scribe_Values.Look(ref slot.llmModel, "neutralLlmModel");
            Scribe_Values.Look(ref slot.title, "neutralTitle");
            Scribe_Values.Look(ref slot.titleStatus, "neutralTitleStatus");
            Scribe_Values.Look(ref slot.titleError, "neutralTitleError");
        }

        // Cleans one slot's loaded state. Generation-pipeline fields are null-coalesced and their
        // statuses upgraded/cleared for every role. Pawn-specific fields are normalized only for
        // initiator/recipient (hasPawnFields); surroundings is left to the caller, because the
        // recipient borrows the initiator's already-normalized surroundings value. The default/clamp
        // math delegates to DiarySaveNormalization; status tokens delegate to DiaryGenerationStatus.
        private static void NormalizeLoadedSlot(ref PovSlot slot, bool hasPawnFields)
        {
            slot.text = DiarySaveNormalization.NormalizeString(slot.text);
            slot.generatedText = DiarySaveNormalization.NormalizeString(slot.generatedText);
            slot.error = DiarySaveNormalization.NormalizeString(slot.error);
            slot.llmEndpoint = DiarySaveNormalization.NormalizeString(slot.llmEndpoint);
            slot.llmModel = DiarySaveNormalization.NormalizeString(slot.llmModel);
            slot.rawResponse = DiarySaveNormalization.NormalizeString(slot.rawResponse);
            slot.prompt = DiarySaveNormalization.NormalizeString(slot.prompt);
            slot.title = DiarySaveNormalization.NormalizeString(slot.title);
            slot.titleError = DiarySaveNormalization.NormalizeString(slot.titleError);
            // Normalize statuses: treat stale "pending" or empty as not_generated, and upgrade to
            // "complete" if generated text/title is already present.
            slot.status = DiaryGenerationStatus.NormalizeLoadedMainStatus(slot.status, slot.generatedText);
            slot.titleStatus = DiaryGenerationStatus.NormalizeLoadedTitleStatus(slot.titleStatus, slot.title);

            if (!hasPawnFields)
            {
                return;
            }

            slot.pawnId = DiarySaveNormalization.NormalizeString(slot.pawnId);
            slot.name = DiarySaveNormalization.NormalizeString(slot.name);
            slot.pawnSummary = DiarySaveNormalization.NormalizeWhitespaceOrDefault(slot.pawnSummary, DiarySaveNormalization.DefaultPawnSummary);
            slot.continuity = DiarySaveNormalization.NormalizeWhitespaceOrDefault(slot.continuity, DiarySaveNormalization.DefaultContinuity);
            slot.lastOpener = DiarySaveNormalization.NormalizeString(slot.lastOpener);
            slot.weapon = DiarySaveNormalization.NormalizeString(slot.weapon);
            slot.staggeredIntensity = DiarySaveNormalization.ClampStaggeredIntensity(slot.staggeredIntensity);
            slot.textDecorationFacts = DiarySaveNormalization.NormalizeString(slot.textDecorationFacts);
        }

        /// <summary>
        /// The ONE place that maps a POV role to its storage. Returns the slot by ref so callers can
        /// read or mutate it in place (<c>SlotFor(role).status = x</c>). Under C# 7.3 a ref return is
        /// the idiomatic way to hand out interior access to a value-typed field (ref struct fields,
        /// a newer feature, are unavailable here). Unknown roles fall through to the initiator slot;
        /// this branch is unreachable in practice (real callers always pass one of the three role
        /// constants or a validated role from <see cref="RoleForPawn"/> / a generation result), and
        /// the original readers were themselves inconsistent here (most fell back to neutral, while
        /// the LLM-endpoint/model readers fell back to initiator).
        /// </summary>
        private ref PovSlot SlotFor(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return ref recipientSlot;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                return ref neutralSlot;
            }

            return ref initiatorSlot;
        }

        /// <summary>
        /// Stores the assembled prompt text for a specific POV role.
        /// </summary>
        public void SetPrompt(string povRole, string prompt)
        {
            SlotFor(povRole).prompt = prompt;
        }

        /// <summary>
        /// Records which LLM endpoint and model were used for a specific POV role.
        /// </summary>
        public void SetLlmMeta(string povRole, string endpoint, string model)
        {
            ref PovSlot slot = ref SlotFor(povRole);
            slot.llmEndpoint = endpoint;
            slot.llmModel = model;
        }

        /// <summary>
        /// Stores the LLM-generated title for a specific POV role. Mirrors
        /// <see cref="SetLlmMeta"/>; an empty/null string clears the stored title so the view renders
        /// a date-only card header.
        /// </summary>
        public void SetTitle(string povRole, string title)
        {
            DiaryStateVersion.Bump();
            SlotFor(povRole).title = title ?? string.Empty;
        }

        /// <summary>
        /// Returns the stored LLM title for a POV role, or empty string when none has been set
        /// yet. Public because the title-queue decision in
        /// <c>DiaryGameComponent.Generation.cs</c> reads the same field.
        /// </summary>
        public string TitleForRole(string povRole)
        {
            return SlotFor(povRole).title ?? string.Empty;
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
        /// Stores a successful title and marks the title follow-up complete for the same POV role.
        /// </summary>
        public void MarkTitleComplete(string povRole, string title)
        {
            SetTitle(povRole, title);
            SetTitleStatus(povRole, CompleteStatus);
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

        /// <summary>
        /// Returns true when a title request may be queued for this POV. Failed title attempts stay
        /// failed so the periodic recovery sweep does not retry them every few seconds.
        /// </summary>
        public bool CanQueueTitleGeneration(string povRole)
        {
            string status = TitleStatusFor(povRole);
            return string.IsNullOrWhiteSpace(status) || RoleEquals(status, NotGeneratedStatus);
        }

        private string LlmEndpointFor(string povRole)
        {
            return SlotFor(povRole).llmEndpoint;
        }

        private string LlmModelFor(string povRole)
        {
            return SlotFor(povRole).llmModel;
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
        /// Returns true if this POV role can be queued for LLM generation
        /// (not already pending/complete/failed/skipped).
        /// </summary>
        public bool CanQueueGeneration(string povRole)
        {
            string status = StatusFor(povRole);
            return string.IsNullOrWhiteSpace(status) || RoleEquals(status, NotGeneratedStatus);
        }

        /// <summary>
        /// Returns true when this POV was intentionally skipped and should not be retried.
        /// </summary>
        public bool IsSkipped(string povRole)
        {
            string status = StatusFor(povRole);
            return RoleEquals(status, SkippedStatus) || RoleEquals(status, PromptOnlyStatus);
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
        /// Prepares an existing POV to be written again from the current prompt/model settings.
        /// The old generated page is kept visible until a new result replaces it; transient request
        /// metadata, raw response, error, and title fields are cleared so the fresh run is honest.
        /// </summary>
        public void PrepareForRegeneration(string povRole)
        {
            if (string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            DiaryStateVersion.Bump();
            ref PovSlot slot = ref SlotFor(povRole);
            slot.prompt = string.Empty;
            slot.rawResponse = string.Empty;
            slot.status = NotGeneratedStatus;
            slot.error = null;
            slot.llmEndpoint = string.Empty;
            slot.llmModel = string.Empty;
            slot.title = string.Empty;
            slot.titleStatus = NotGeneratedStatus;
            slot.titleError = null;
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
        /// Marks a POV role as intentionally skipped, so background scans do not retry it.
        /// </summary>
        public void MarkSkipped(string povRole, string reason)
        {
            SetStatus(povRole, SkippedStatus);
            SetError(povRole, reason);
        }

        /// <summary>
        /// Marks a POV role as prompt-only: the prompt was captured for inspection and no LLM
        /// request should be sent or retried.
        /// </summary>
        public void MarkPromptOnly(string povRole, string reason)
        {
            SetStatus(povRole, PromptOnlyStatus);
            SetError(povRole, reason);
        }

        /// <summary>
        /// Applies an LLM generation result to the appropriate POV slot based on result.povRole.
        /// Unknown roles are ignored (no slot is mutated), preserving the historical no-op
        /// fall-through. The per-role bodies collapsed into <see cref="ApplyLlmResultToSlot"/>.
        /// </summary>
        public void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null)
            {
                return;
            }

            // A result changes generated text/status, which the diary tab renders. Invalidate its
            // per-frame view cache (see DiaryRenderToken).
            DiaryStateVersion.Bump();

            if (RoleEquals(result.povRole, InitiatorRole))
            {
                ApplyLlmResultToSlot(result, ref initiatorSlot);
                return;
            }

            if (RoleEquals(result.povRole, RecipientRole))
            {
                ApplyLlmResultToSlot(result, ref recipientSlot);
                return;
            }

            if (RoleEquals(result.povRole, NeutralRole))
            {
                ApplyLlmResultToSlot(result, ref neutralSlot);
            }

        }

        /// <summary>
        /// Builds a read-only view of this event for the given pawn's POV, or null if the pawn is not involved.
        /// For two-pawn events, includes a LinkedEntryView previewing the other pawn's entry.
        /// </summary>
        public DiaryEntryView ToViewFor(string pawnId, bool archivedForScans = false)
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
            bool titlePendingForPov = !archivedForScans
                && string.IsNullOrWhiteSpace(titleForPov)
                && IsTitlePending(povRole);
            string generatedTextForPov = GeneratedTextFor(povRole);
            bool archivedGenerationStale = DiaryGenerationStatus.IsArchivedGenerationStale(
                archivedForScans,
                StatusFor(povRole),
                generatedTextForPov,
                PromptFor(povRole));

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
                generatedTextForPov,
                StatusFor(povRole),
                ErrorFor(povRole),
                LlmEndpointFor(povRole),
                LlmModelFor(povRole),
                PromptFor(povRole),
                eventId,
                povRole,
                group?.label ?? interactionLabel ?? string.Empty,
                ColorCueForDisplay(),
                AtmosphereCueForDisplay(povRole),
                StaggeredIntensityForRole(povRole),
                DistortDirectSpeechForDisplay(povRole),
                group == null || group.important,
                linkedEntry,
                titleForPov,
                titlePendingForPov,
                RawResponseFor(povRole),
                TextDecorationContextForRole(povRole),
                archivedGenerationStale);
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

            string normalized = StripSpeechMarkersForPreview(text).Replace("\r\n", "\n").Replace('\r', '\n');
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

        private static string StripSpeechMarkersForPreview(string text)
        {
            return DiaryDirectSpeechParser.RemoveMarkers(
                text,
                DiaryDirectSpeechParser.DefaultOpenMarker,
                DiaryDirectSpeechParser.DefaultCloseMarker);
        }

        /// <summary>
        /// Resolves the POV role the Diary tab would display for this pawn without building a full
        /// <see cref="DiaryEntryView" />. Arrival/death boundary pages are neutral entries even though
        /// the pawn is not stored in the ordinary initiator/recipient slots.
        /// </summary>
        public bool TryGetDisplayRoleForPawn(string pawnId, out string povRole)
        {
            povRole = RoleForPawn(pawnId);

            if (HasDeathDescription())
            {
                if (!IsDeathDescriptionFor(pawnId))
                {
                    povRole = null;
                    return false;
                }

                povRole = NeutralRole;
                return true;
            }

            if (HasArrivalDescription())
            {
                if (!IsArrivalDescriptionFor(pawnId))
                {
                    povRole = null;
                    return false;
                }

                povRole = NeutralRole;
                return true;
            }

            return !string.IsNullOrWhiteSpace(povRole);
        }

        /// <summary>
        /// Cheap status snapshot for Diary-tab indexing and badges. It mirrors the visibility/status
        /// decisions made by <see cref="ToViewFor" /> but avoids group lookup, linked preview building,
        /// and text-decoration parsing for entries outside the selected year.
        /// </summary>
        public bool TryGetTabStateForPawn(
            string pawnId,
            bool archivedForScans,
            out string povRole,
            out bool hasGeneratedText,
            out bool archivedGenerationStale,
            out bool generating,
            out bool promptOnly,
            out bool titlePending)
        {
            hasGeneratedText = false;
            archivedGenerationStale = false;
            generating = false;
            promptOnly = false;
            titlePending = false;

            if (!TryGetDisplayRoleForPawn(pawnId, out povRole))
            {
                return false;
            }

            string generatedText = GeneratedTextFor(povRole);
            string status = StatusFor(povRole);
            hasGeneratedText = !string.IsNullOrWhiteSpace(generatedText);
            archivedGenerationStale = DiaryGenerationStatus.IsArchivedGenerationStale(
                archivedForScans,
                status,
                generatedText,
                PromptFor(povRole));
            generating = RoleEquals(status, PendingStatus) && !archivedGenerationStale;
            promptOnly = RoleEquals(status, PromptOnlyStatus);
            titlePending = !archivedForScans
                && string.IsNullOrWhiteSpace(TitleForRole(povRole))
                && IsTitlePending(povRole);
            return true;
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
                && string.Equals(DiaryContextFields.Value(gameContext, "death_victim_id"), pawnId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns true when this event owns a neutral colonist-death description request.
        /// </summary>
        public bool HasDeathDescription()
        {
            return DiaryContextFields.IsTrue(gameContext, "death_description");
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
                && string.Equals(DiaryContextFields.Value(gameContext, "arrival_pawn_id"), pawnId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns true when this event owns a neutral colony-arrival description request.
        /// </summary>
        public bool HasArrivalDescription()
        {
            return DiaryContextFields.IsTrue(gameContext, "arrival_description");
        }

        /// <summary>
        /// Returns true when this event is an end-of-day reflection (see DiaryGameComponent.DaySummary.cs).
        /// Used to pick the reflection system prompt at dispatch.
        /// </summary>
        public bool IsDayReflection()
        {
            return DiaryContextFields.IsTrue(gameContext, "day_reflection");
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
        /// True once this event has added its optional generated direct-speech Social-log row.
        /// </summary>
        public bool HasGeneratedSpeechPlayLogEntry()
        {
            return generatedSpeechPlayLogEntryId >= 0;
        }

        /// <summary>
        /// Forgets a generated Social-log row after RimWorld prunes it, allowing a later successful
        /// result to inject a fresh row instead of being blocked by a stale LogID.
        /// </summary>
        public void ClearGeneratedSpeechPlayLogEntry()
        {
            if (generatedSpeechPlayLogEntryId >= 0 && playLogEntryIds != null)
            {
                playLogEntryIds.Remove(generatedSpeechPlayLogEntryId);
            }

            generatedSpeechPlayLogEntryId = -1;
        }

        /// <summary>
        /// Remembers the generated direct-speech Social-log row and lets the existing click bridge
        /// open this diary entry when that row is clicked later.
        /// </summary>
        public void MarkGeneratedSpeechPlayLogEntry(int playLogEntryId)
        {
            if (playLogEntryId < 0)
            {
                return;
            }

            generatedSpeechPlayLogEntryId = playLogEntryId;
            AddPlayLogEntryId(playLogEntryId);
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
        /// Returns true only when this POV has completed LLM diary text, not just raw fallback text.
        /// </summary>
        public bool HasGeneratedTextForRole(string povRole)
        {
            return !string.IsNullOrWhiteSpace(GeneratedTextFor(povRole));
        }

        /// <summary>
        /// Returns the surroundings description for the given role (defaults to initiator's surroundings for neutral).
        /// Neutral deliberately borrows the initiator's stored value rather than keeping its own copy.
        /// </summary>
        public string SurroundingsForRole(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSlot.surroundings;
            }

            return initiatorSlot.surroundings;
        }

        /// <summary>
        /// Resolves the event group used by the Diary tab for labels, importance, and color cue
        /// fallback. Saved events keep only source markers and defName, so this reuses the XML
        /// classifiers instead of storing localized labels.
        /// </summary>
        private DiaryInteractionGroupDef GroupForDisplay()
        {
            return GroupForDisplay(gameContext, interactionDefName);
        }

        /// <summary>
        /// Returns the saved semantic color cue, deriving one for old saves that predate colorCue.
        /// </summary>
        private string ColorCueForDisplay()
        {
            if (IsStrangeChatDefName(interactionDefName))
            {
                return StrangeChatColorCue;
            }

            return string.IsNullOrWhiteSpace(colorCue)
                ? ResolveColorCue(interactionDefName, gameContext)
                : colorCue;
        }

        /// <summary>
        /// Returns the rare display-only layout cue for this entry. This is intentionally derived
        /// from saved event facts, not generated prose, so it never rewrites the page or changes
        /// prompts. Only extreme entries opt in.
        /// </summary>
        private string AtmosphereCueForDisplay(string povRole)
        {
            if (RoleEquals(povRole, NeutralRole) && HasDeathDescription())
            {
                return DiaryEntryView.AtmosphereMemorial;
            }

            string cue = ColorCueForDisplay();
            if (string.Equals(cue, StrangeChatColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(cue, ExtremeDarkColorCue, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryEntryView.AtmosphereUnsettled;
            }

            if (string.Equals(cue, MentalBreakColorCue, StringComparison.OrdinalIgnoreCase))
            {
                return DiaryEntryView.AtmosphereFractured;
            }

            return string.Empty;
        }

        private bool DistortDirectSpeechForDisplay(string povRole)
        {
            return RoleEquals(povRole, InitiatorRole) && IsStrangeChatDefName(interactionDefName);
        }

        /// <summary>
        /// Builds the pure decoration context for a POV. This combines saved pawn facts with stable
        /// event metadata; callers can select XML rules from the returned data without touching Pawn.
        /// </summary>
        public DiaryTextDecorationContext TextDecorationContextForRole(string povRole)
        {
            DiaryTextDecorationContext context = new DiaryTextDecorationContext
            {
                povRole = povRole,
                defName = interactionDefName,
                colorCue = ColorCueForDisplay(),
                atmosphereCue = AtmosphereCueForDisplay(povRole),
                domain = DecorationDomainForContext(gameContext),
                gameContext = gameContext
            };
            DiaryTextDecorations.AddEventTagsFromContext(context, gameContext);
            DiaryTextDecorations.AddSerializedPawnFacts(context, TextDecorationFactsForRole(povRole));
            return context;
        }

        private string TextDecorationFactsForRole(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSlot.textDecorationFacts ?? string.Empty;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                return string.Empty;
            }

            return initiatorSlot.textDecorationFacts ?? string.Empty;
        }

        /// <summary>
        /// Stores the serialized hediff/trait fact string for a POV role. The value is collected from
        /// the live pawn by <see cref="PawnFactCapture.TextDecorationFacts"/> at record time, so this
        /// setter is pure: it only dispatches by role and writes the plain saved field. Neutral pages
        /// carry no pawn facts and are ignored.
        /// </summary>
        public void SetTextDecorationFacts(string povRole, string facts)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorSlot.textDecorationFacts = facts;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientSlot.textDecorationFacts = facts;
            }
        }

        /// <summary>
        /// Stores the display-only "staggered handwriting" intensity (0..4) for a POV role. The value
        /// is collected from the live pawn by <see cref="PawnFactCapture.StaggeredIntensity"/> at
        /// record time; this setter is pure and only dispatches by role. Neutral pages are ignored.
        /// </summary>
        public void SetStaggeredIntensity(string povRole, int intensity)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorSlot.staggeredIntensity = intensity;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientSlot.staggeredIntensity = intensity;
            }
        }

        private int StaggeredIntensityForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return ClampStaggeredIntensity(initiatorSlot.staggeredIntensity);
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return ClampStaggeredIntensity(recipientSlot.staggeredIntensity);
            }

            // Neutral chronicle pages are not written by the pawn, so they never get staggered words.
            return 0;
        }

        private static int ClampStaggeredIntensity(int intensity)
        {
            if (intensity < 0)
            {
                return 0;
            }

            return intensity > 4 ? 4 : intensity;
        }

        private static string DecorationDomainForContext(string context)
        {
            return DiaryEventDomainClassifier.DomainForContext(context);
        }

        /// <summary>
        /// Chooses the stable display color key for a diary event. This is intentionally based on
        /// source defNames and XML group metadata, not localized labels or LLM-generated titles.
        /// </summary>
        public static string ResolveColorCue(string defName, string context)
        {
            if (IsStrangeChatDefName(defName))
            {
                return StrangeChatColorCue;
            }

            if (IsMentalStateEvent(context))
            {
                if (string.Equals(defName, "SocialFighting", StringComparison.OrdinalIgnoreCase))
                {
                    return SocialFightColorCue;
                }

                if (IsDazeMentalStateDefName(defName))
                {
                    return DazeColorCue;
                }
            }

            if (IsExtremeDarkDefName(defName))
            {
                return ExtremeDarkColorCue;
            }

            DiaryInteractionGroupDef group = GroupForDisplay(context, defName);
            if (!string.IsNullOrWhiteSpace(group?.colorCue))
            {
                return group.colorCue;
            }

            if (group != null && group.combat)
            {
                return CombatColorCue;
            }

            if (IsMentalStateEvent(context))
            {
                return MentalBreakColorCue;
            }

            if (group != null && !group.important)
            {
                return QuietColorCue;
            }

            return string.Empty;
        }

        private static DiaryInteractionGroupDef GroupForDisplay(string context, string defName)
        {
            string domainName = DiaryEventDomainClassifier.DomainForContext(context);
            GroupDomain domain;
            if (!Enum.TryParse(domainName, out domain))
            {
                domain = GroupDomain.Interaction;
            }

            string classifierKey = DiaryEventDomainClassifier.GroupClassifierKey(domainName, context, defName);
            return InteractionGroups.ClassifyDefName(domain, classifierKey);
        }

        /// <summary>
        /// Returns true if this event belongs to an important group (not chit chat, small talk, etc.).
        /// Used by prompt policy and the UI's visual importance marker.
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
        /// Returns true if the weapon should be included in the prompt.
        /// </summary>
        public bool ShouldShowWeapon()
        {
            return IsCombatRelated();
        }

        /// <summary>
        /// Mental-state events store their state defName in interactionDefName too; their context
        /// starts with a stable mental_state field, which lets UI classification pick the right domain.
        /// </summary>
        private bool IsMentalStateEvent()
        {
            return IsMentalStateEvent(gameContext);
        }

        private static bool IsMentalStateEvent(string context)
        {
            return HasContextMarker(context, "mental_state=");
        }

        /// <summary>
        /// Tale events store their TaleDef defName in interactionDefName too; their context
        /// starts with a stable tale field, which lets UI classification pick the Tale domain.
        /// </summary>
        private bool IsTaleEvent()
        {
            return IsTaleEvent(gameContext);
        }

        private static bool IsTaleEvent(string context)
        {
            return HasContextMarker(context, "tale=");
        }

        /// <summary>
        /// Mood-affecting GameCondition events store their GameConditionDef defName in
        /// interactionDefName; their context starts with a stable mood_event field, which
        /// lets UI classification pick the MoodEvent domain.
        /// </summary>
        private bool IsMoodEventEvent()
        {
            return IsMoodEventEvent(gameContext);
        }

        private static bool IsMoodEventEvent(string context)
        {
            return HasContextMarker(context, "mood_event=");
        }

        /// <summary>
        /// Thought events store their ThoughtDef defName in interactionDefName; their context
        /// starts with a stable thought field, which lets UI classification pick the Thought domain.
        /// </summary>
        private bool IsThoughtEvent()
        {
            return IsThoughtEvent(gameContext);
        }

        private static bool IsThoughtEvent(string context)
        {
            return HasContextMarker(context, "thought=");
        }

        /// <summary>
        /// Inspiration events store their InspirationDef defName in interactionDefName; their context
        /// starts with a stable inspiration field, which lets UI classification pick the Inspiration domain.
        /// </summary>
        private bool IsInspirationEvent()
        {
            return IsInspirationEvent(gameContext);
        }

        private static bool IsInspirationEvent(string context)
        {
            return HasContextMarker(context, "inspiration=");
        }

        /// <summary>
        /// Work scanner events store the real WorkTypeDef in the stable work= field; the saved
        /// interactionDefName is a synthetic group defName such as PawnDiary_WorkPassion.
        /// </summary>
        private bool IsWorkEvent()
        {
            return IsWorkEvent(gameContext);
        }

        private static bool IsWorkEvent(string context)
        {
            return HasContextMarker(context, "work=");
        }

        /// <summary>
        /// Hediff events store their HediffDef defName in interactionDefName; their context starts
        /// with a stable hediff field, which lets UI classification pick the Hediff domain.
        /// </summary>
        private bool IsHediffEvent()
        {
            return IsHediffEvent(gameContext);
        }

        private static bool IsHediffEvent(string context)
        {
            return HasContextMarker(context, "hediff=");
        }

        private static bool HasContextMarker(string context, string marker)
        {
            return DiaryContextFields.HasMarker(context, marker);
        }

        private static bool IsDazeMentalStateDefName(string defName)
        {
            return !string.IsNullOrWhiteSpace(defName)
                && (defName.IndexOf("Daze", StringComparison.OrdinalIgnoreCase) >= 0
                    || defName.IndexOf("Wander", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsExtremeDarkDefName(string defName)
        {
            return !string.IsNullOrWhiteSpace(defName)
                && (defName.IndexOf("UnnaturalDarkness", StringComparison.OrdinalIgnoreCase) >= 0
                    || defName.IndexOf("Darkness", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsStrangeChatDefName(string defName)
        {
            return string.Equals(defName, "DisturbingChat", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the continuity context string for the given role, or "none" for neutral.
        /// Neutral is a hardcoded default rather than a stored field, so it stays special-cased
        /// here instead of routing through the slot.
        /// </summary>
        public string ContinuityForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorSlot.continuity;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSlot.continuity;
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
                return initiatorSlot.lastOpener;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSlot.lastOpener;
            }

            return string.Empty;
        }

        private string TextFor(string povRole)
        {
            return SlotFor(povRole).text;
        }

        private string GeneratedTextFor(string povRole)
        {
            return SlotFor(povRole).generatedText;
        }

        private string StatusFor(string povRole)
        {
            return SlotFor(povRole).status;
        }

        private string ErrorFor(string povRole)
        {
            return SlotFor(povRole).error;
        }

        private string PromptFor(string povRole)
        {
            return SlotFor(povRole).prompt ?? string.Empty;
        }

        private string RawResponseFor(string povRole)
        {
            return SlotFor(povRole).rawResponse;
        }

        /// <summary>
        /// Writes a generation result into one POV slot. Replaces the former per-role
        /// ApplyLlmResultToInitiator/Recipient/Neutral triplet; the slot is passed by ref so the
        /// value-typed storage is mutated in place.
        /// </summary>
        private void ApplyLlmResultToSlot(LlmGenerationResult result, ref PovSlot slot)
        {
            if (result.success)
            {
                slot.generatedText = result.generatedText;
                slot.rawResponse = TrimPersistedRawResponse(result.rawResponse);
                slot.status = CompleteStatus;
                slot.error = null;
            }
            else
            {
                slot.status = FailedStatus;
                slot.error = result.error;
            }
        }

        private static string TrimPersistedRawResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return string.Empty;
            }

            string trimmed = rawResponse.Trim();
            if (trimmed.Length <= MaxPersistedRawResponseChars)
            {
                return trimmed;
            }

            return trimmed.Substring(0, MaxPersistedRawResponseChars).TrimEnd()
                + "\n[raw response truncated]";
        }

        private void SetStatus(string povRole, string status)
        {
            // Status drives which entries the tab shows and its "writing…" indicator; invalidate the
            // tab's per-frame view cache (see DiaryRenderToken).
            DiaryStateVersion.Bump();
            SlotFor(povRole).status = status;
        }

        private void SetError(string povRole, string error)
        {
            SlotFor(povRole).error = error;
        }

        private void SetTitleStatus(string povRole, string status)
        {
            DiaryStateVersion.Bump();
            SlotFor(povRole).titleStatus = status;
        }

        private void SetTitleError(string povRole, string error)
        {
            SlotFor(povRole).titleError = error;
        }

        private string TitleStatusFor(string povRole)
        {
            return SlotFor(povRole).titleStatus;
        }

        public static bool RoleEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
