// Player-authored diary-page adapter. The UI supplies detached text buffers and stable page identity;
// this partial sanitizes them on the main thread, then updates the hot event store and/or compact archive
// without leaking UI objects into persistence. Existing canonical generatedText/title fields are reused so
// search, memory, export, save/load, and retention all observe exactly one version of the page.
using System;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Stable schema/classifier tokens, never player-facing prose.
        private const string ManualEntryDefName = "PawnDiary_ManualEntry";
        private const string ManualEntryGameContext = ManualDiaryEntryFacts.GameContext;

        /// <summary>XML-backed maximum body length shared by the editor and persistence adapter.</summary>
        internal static int ManualEntryBodyMaxCharacters
        {
            get { return DiaryTuning.IntegrationDirectTextMaxChars; }
        }

        /// <summary>XML-backed maximum title length shared by the editor and persistence adapter.</summary>
        internal static int ManualEntryTitleMaxCharacters
        {
            get { return DiaryTuning.IntegrationDirectTitleMaxChars; }
        }

        /// <summary>
        /// Returns a detached editable snapshot only when the exact pawn/event/POV page is currently
        /// owned by hot or archived diary storage. A hot owner reference wins over a leftover archive
        /// duplicate because that is the page the reader renders; mixed-retention archive ownership is
        /// used when the shared pair event remains hot only for its other pawn.
        /// </summary>
        internal bool TryGetManualEntrySnapshot(
            string pawnId,
            string eventId,
            string povRole,
            out ManualDiaryEntrySnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(pawnId)
                || string.IsNullOrWhiteSpace(eventId)
                || !IsKnownPovRole(povRole))
            {
                return false;
            }

            DiaryEvent diaryEvent = events.FindEvent(eventId);
            string hotRole = string.Empty;
            bool hotRoleMatches = diaryEvent != null
                && diaryEvent.TryGetDisplayRoleForPawn(pawnId, out hotRole)
                && DiaryEvent.RoleEquals(hotRole, povRole);
            bool hotOwned = hotRoleMatches && HasHotEntryReference(pawnId, eventId);
            if (hotOwned)
            {
                PlayerEntryTypeSnapshot hotType = diaryEvent.PlayerEntryTypeForRole(hotRole);
                DiaryEntryView hotView = diaryEvent.ToViewFor(pawnId);
                snapshot = new ManualDiaryEntrySnapshot(
                    pawnId,
                    eventId,
                    hotRole,
                    diaryEvent.DisplayTextForRole(hotRole),
                    diaryEvent.TitleForRole(hotRole),
                    diaryEvent.EntryTypeKeyForRole(hotRole),
                    diaryEvent.HasArrivalDescription() || diaryEvent.HasDeathDescription(),
                    hotType?.label ?? hotView?.GroupLabel ?? diaryEvent.interactionLabel,
                    hotType?.description
                        ?? "PawnDiary.EntryComposer.EntryType.Source.Description".Translate().Resolve(),
                    false,
                    ManualDiaryEntryFacts.IsPlayerCreated(diaryEvent.gameContext));
                return true;
            }

            ArchivedDiaryEntry archived = archive.Find(eventId, pawnId, povRole);
            if (archived == null)
            {
                return false;
            }

            snapshot = new ManualDiaryEntrySnapshot(
                pawnId,
                eventId,
                archived.povRole,
                string.IsNullOrWhiteSpace(archived.generatedText) ? archived.text : archived.generatedText,
                archived.title,
                archived.entryTypeKey,
                archived.arrivalDescription || archived.deathDescription,
                string.IsNullOrWhiteSpace(archived.entryTypeKey)
                    ? archived.groupLabel
                    : DiaryPlayerEntryTypes.ResolveOrPersonal(archived.entryTypeKey).label,
                string.IsNullOrWhiteSpace(archived.entryTypeKey)
                    ? "PawnDiary.EntryComposer.EntryType.Source.Description".Translate().Resolve()
                    : DiaryPlayerEntryTypes.ResolveOrPersonal(archived.entryTypeKey).description,
                true,
                ManualDiaryEntryFacts.IsPlayerCreated(archived.decorationGameContext));
            return true;
        }

        /// <summary>
        /// Replaces one exact owned page with player-authored final prose. In mixed retention the archive
        /// row and its still-hot shared role are both updated, then every archived partner preview is
        /// refreshed so no view/export path can surface stale text or title.
        /// </summary>
        internal bool TryEditManualEntry(
            ManualDiaryEntrySnapshot expected,
            string body,
            string title)
        {
            // Compatibility path: preserving the exact snapshot key is essential for a legacy page
            // whose blank slot still derives type from its captured source.
            return TryEditManualEntry(expected, body, title, expected?.EntryTypeKey);
        }

        /// <summary>
        /// Replaces prose/title and optionally changes the exact POV's player category under one CAS.
        /// Unknown keys and arrival/death category changes are rejected below the UI.
        /// </summary>
        internal bool TryEditManualEntry(
            ManualDiaryEntrySnapshot expected,
            string body,
            string title,
            string entryTypeKey)
        {
            if (expected == null)
            {
                return false;
            }

            ManualDiaryEntrySnapshot current;
            if (!TryGetManualEntrySnapshot(
                    expected.PawnId,
                    expected.EventId,
                    expected.PovRole,
                    out current)
                // Compare the exact detached values the dialog opened with. Do not cap/normalize them:
                // an old save may legitimately exceed today's manual-entry limits, and unchanged hot
                // -> archive compaction must still pass this optimistic-concurrency gate.
                || !string.Equals(current.Body, expected.Body, StringComparison.Ordinal)
                || !string.Equals(current.Title, expected.Title, StringComparison.Ordinal)
                || !string.Equals(current.EntryTypeKey, expected.EntryTypeKey, StringComparison.Ordinal))
            {
                return false;
            }

            // Snapshot XML caps and the detached Def catalog before crossing into the pure mutation
            // planner. The editor uses this same contract, but persistence repeats it defensively so a
            // direct/internal caller cannot bypass validation.
            PlayerEntryMutationPlan mutation = PlayerEntryMutationPolicy.Plan(
                new PlayerEntryMutationRequest
                {
                    creating = false,
                    entryTypeLocked = current.EntryTypeLocked,
                    originalBody = current.Body,
                    originalTitle = current.Title,
                    originalEntryTypeKey = current.EntryTypeKey,
                    requestedBody = body,
                    requestedTitle = title,
                    requestedEntryTypeKey = entryTypeKey,
                    bodyMaxCharacters = ManualEntryBodyMaxCharacters,
                    titleMaxCharacters = ManualEntryTitleMaxCharacters
                },
                DiaryPlayerEntryTypes.ForUi());
            if (!mutation.valid) return false;

            string cleanedBody = mutation.body;
            string cleanedTitle = mutation.title;
            string requestedEntryTypeKey = mutation.entryTypeKey;
            bool typeChanged = mutation.typeChanged;
            PlayerEntryTypeSnapshot requestedEntryType = null;
            if (typeChanged
                && !DiaryPlayerEntryTypes.TryResolve(requestedEntryTypeKey, out requestedEntryType))
            {
                return false;
            }

            if (mutation.noChange)
            {
                return true;
            }

            string pawnId = current.PawnId;
            string eventId = current.EventId;
            ArchivedDiaryEntry archived = archive.Find(eventId, pawnId, current.PovRole);
            DiaryEvent diaryEvent = events.FindEvent(eventId);
            string hotRole = string.Empty;
            bool hotRoleMatches = diaryEvent != null
                && diaryEvent.TryGetDisplayRoleForPawn(pawnId, out hotRole)
                && DiaryEvent.RoleEquals(hotRole, current.PovRole);

            // A shared pair can stay in the hot repository for its other pawn after this exact POV has
            // compacted. Archive ownership is enough authority to update that matching hidden slot too.
            bool textChanged = mutation.textChanged;
            bool hotChanged = hotRoleMatches
                && (HasHotEntryReference(pawnId, eventId) || archived != null)
                && ((!textChanged || ReplaceHotEntryWithManualText(
                        diaryEvent, hotRole, cleanedBody, cleanedTitle))
                    && (!typeChanged || diaryEvent.TrySetEntryTypeKey(
                        hotRole, requestedEntryTypeKey, bumpVersion: !textChanged)));
            bool archiveChanged = false;
            if (archived != null)
            {
                bool archiveTextChanged = !textChanged
                    || archive.ReplaceWithManualText(
                        eventId,
                        pawnId,
                        current.PovRole,
                        cleanedBody,
                        cleanedTitle);
                bool archiveTypeChanged = !typeChanged || archived.TrySetEntryType(requestedEntryType);
                archiveChanged = archiveTextChanged && archiveTypeChanged;
            }

            if (!hotChanged && !archiveChanged)
            {
                return false;
            }

            if (!archiveChanged)
            {
                archive.RefreshLinkedPreview(
                    eventId,
                    pawnId,
                    current.PovRole,
                    cleanedBody,
                    cleanedTitle);
            }

            // DiaryEvent owns its own invalidation. An archive-only edit has no hot setter to do that.
            if (!hotChanged)
            {
                DiaryStateVersion.Bump();
            }

            NotifyManualEntryStatusChanged(eventId, pawnId, current.PovRole);

            return true;
        }

        /// <summary>
        /// Creates a completed player-authored solo page for a normal diary-eligible pawn. Manual pages
        /// intentionally bypass the automatic-generation toggle and incapacitation gate, but not base
        /// ownership or life-boundary checks. Retention runs only after final prose is present, allowing a
        /// zero/small active cap to archive the page safely. Creation never increments unread badges.
        /// </summary>
        internal bool TryCreateManualEntry(
            Pawn pawn,
            string body,
            string title,
            string localizedLabel,
            out string eventId)
        {
            return TryCreateManualEntryCore(
                pawn, body, title, localizedLabel, string.Empty, false, out eventId);
        }

        /// <summary>Creates a player page with one validated per-POV entry category.</summary>
        internal bool TryCreateManualEntry(
            Pawn pawn,
            string body,
            string title,
            string localizedLabel,
            string entryTypeKey,
            out string eventId)
        {
            return TryCreateManualEntryCore(
                pawn, body, title, localizedLabel, entryTypeKey, true, out eventId);
        }

        private bool TryCreateManualEntryCore(
            Pawn pawn,
            string body,
            string title,
            string localizedLabel,
            string entryTypeKey,
            bool requireEntryType,
            out string eventId)
        {
            eventId = string.Empty;
            if (pawn == null || pawn.Dead || !IsDiaryEligible(pawn))
            {
                return false;
            }

            string requestedEntryTypeKey = string.IsNullOrWhiteSpace(entryTypeKey)
                ? PlayerEntryComposerPolicy.PersonalEntryTypeKey
                : entryTypeKey.Trim();
            PlayerEntryMutationPlan mutation = PlayerEntryMutationPolicy.Plan(
                new PlayerEntryMutationRequest
                {
                    creating = true,
                    requestedBody = body,
                    requestedTitle = title,
                    requestedEntryTypeKey = requestedEntryTypeKey,
                    bodyMaxCharacters = ManualEntryBodyMaxCharacters,
                    titleMaxCharacters = ManualEntryTitleMaxCharacters
                },
                DiaryPlayerEntryTypes.ForUi());
            if (!mutation.valid) return false;

            PlayerEntryTypeSnapshot requestedEntryType = null;
            if (requireEntryType
                && !DiaryPlayerEntryTypes.TryResolve(mutation.entryTypeKey, out requestedEntryType))
            {
                return false;
            }

            string cleanedBody = mutation.body;
            string cleanedTitle = mutation.title;
            string cleanedLabel = ExternalEventRequestText.CleanEventLabel(localizedLabel);
            string rawText = DiarySentenceExcerpt.FirstSentence(
                cleanedBody,
                ManualEntryBodyMaxCharacters);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                rawText = cleanedBody;
            }

            DiaryEvent diaryEvent = AddManualEntryEvent(pawn, cleanedLabel, rawText);
            if (diaryEvent == null
                || (requireEntryType
                    && !diaryEvent.TrySetEntryTypeKey(
                        DiaryEvent.InitiatorRole, requestedEntryType.entryTypeKey, bumpVersion: false))
                || !ReplaceHotEntryWithManualText(
                    diaryEvent,
                    DiaryEvent.InitiatorRole,
                    cleanedBody,
                    cleanedTitle))
            {
                if (diaryEvent != null)
                {
                    RollBackNewEventCommit(diaryEvent.eventId);
                }
                return false;
            }

            eventId = diaryEvent.eventId;
            ApplyDiaryEventLimits();

            string pawnId = pawn.GetUniqueLoadID();
            bool retained = HasHotEntryReference(pawnId, eventId)
                || archive.Contains(eventId, pawnId, DiaryEvent.InitiatorRole);
            if (!retained)
            {
                eventId = string.Empty;
                return false;
            }

            NotifyManualEntryStatusChanged(eventId, pawnId, DiaryEvent.InitiatorRole);
            return true;
        }

        /// <summary>
        /// Makes a player-authored page replacement the terminal owner of this POV while preserving
        /// the M2 audit/exposure history of any provider work that already crossed its permit fence.
        /// </summary>
        private bool ReplaceHotEntryWithManualText(
            DiaryEvent diaryEvent,
            string povRole,
            string body,
            string title)
        {
            if (diaryEvent == null) return false;
            SettleActiveMemoryRequestForPageReplacement(diaryEvent, povRole);
            return diaryEvent.ReplaceWithManualText(povRole, body, title);
        }

        private bool HasHotEntryReference(string pawnId, string eventId)
        {
            PawnDiaryRecord diary = LookupDiaryByPawnId(pawnId);
            return diary != null && ContainsEventId(diary.eventIds, eventId);
        }

        private static bool IsKnownPovRole(string povRole)
        {
            return DiaryEvent.RoleEquals(povRole, DiaryEvent.InitiatorRole)
                || DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole)
                || DiaryEvent.RoleEquals(povRole, DiaryEvent.NeutralRole);
        }
    }
}
