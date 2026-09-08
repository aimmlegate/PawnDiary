// Dialog_MemoryLibrary.Editing.cs — protects detached wording drafts during explicit navigation.
//
// Confirmation is a UI action only. It never writes memory, and game/session teardown bypasses it
// so a stale window cannot keep the old game's objects alive. Unchanged editors close immediately.
using System;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_MemoryLibrary
    {
        private bool draftDiscardConfirmationOpen;

        /// <summary>Protects unsaved text on the built-in X and Escape paths as well as Back.</summary>
        public override void Close(bool doCloseSound = true)
        {
            if (IsCurrentLibrarySession()
                && !ConfirmDiscardDraft(() => Close(doCloseSound))) return;
            base.Close(doCloseSound);
        }

        /// <summary>
        /// Returns true for safe immediate navigation. Otherwise retains the draft and offers one
        /// confirmation; accepting it retries the same action after discarding only that draft.
        /// </summary>
        private bool ConfirmDiscardDraft(Action retryNavigation)
        {
            if (!MemoryLibraryUiPolicy.HasUnsavedDraft(session.editDraft)) return true;
            if (draftDiscardConfirmationOpen) return false;
            MemoryLibraryUiEditDraft capturedDraft = session.editDraft;
            draftDiscardConfirmationOpen = true;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                T("PawnDiary.Memory.Library.DiscardDraftPrompt"),
                delegate
                {
                    draftDiscardConfirmationOpen = false;
                    if (!IsCurrentLibrarySession()) return;
                    if (ReferenceEquals(session.editDraft, capturedDraft)) session.editDraft = null;
                    retryNavigation();
                },
                delegate { draftDiscardConfirmationOpen = false; },
                true, T("PawnDiary.Memory.Library.DiscardDraftTitle")));
            return false;
        }

        private bool IsCurrentLibrarySession()
        {
            return openedGeneration == lifecycleGeneration
                && component != null && component == DiaryGameComponent.Instance
                && MemorySystemActivationGate.IsCurrentRelease
                && Current.ProgramState == ProgramState.Playing && Current.Game != null;
        }
    }
}
