// Quality Wave H6 ownership queries. The art patch needs two facts before it may create a page:
// has this exact deed already been written about (anywhere in the colony, hot or archived), and does
// a given pawn keep a colony diary at all.
//
// Both are colony-wide rather than per-pawn, because a deed is owned once for the whole colony: the
// writer chosen today might not be the writer a later sculpture would pick, and "one page per deed"
// has to hold across that difference.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System.Collections.Generic;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// True when some diary page already immortalizes this exact deed. Matching is on the tale's
        /// stable identity, never on its translated prose, so the guarantee survives a language change.
        /// A blank or unverifiable identity returns true — fail CLOSED, because a key we cannot compare
        /// is a key we could silently write about twice.
        /// </summary>
        internal bool HasArtImmortalizationFor(string taleIdentity)
        {
            if (!ArtImmortalizationPolicy.IsVerifiedTaleIdentity(taleIdentity))
            {
                return true;
            }

            string identity = taleIdentity.Trim();

            // Newest-first: a freshly registered claim from this same tick is the likeliest match.
            IReadOnlyList<DiaryEvent> hot = events.AllEvents;
            for (int i = hot.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = hot[i];
                if (diaryEvent != null
                    && DiaryContextFields.FieldEquals(
                        diaryEvent.gameContext,
                        ArtImmortalizationPolicy.TaleIdContextKey,
                        identity))
                {
                    return true;
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.AllEntries;
            for (int i = archived.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (entry != null
                    && DiaryContextFields.FieldEquals(
                        entry.decorationGameContext,
                        ArtImmortalizationPolicy.TaleIdContextKey,
                        identity))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when this pawn already keeps colony diary pages, hot or archived. H6 uses it for the
        /// artist fallback: art about someone who never wrote a line in this colony (a trader's
        /// souvenir, a stranger's deed) stays silent, while art about a colonist who has since died
        /// can still be written by the artist who made it.
        /// </summary>
        internal bool HasColonyDiaryFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord record = FindDiaryByPawnId(pawnId);
            if (record?.eventIds != null && record.eventIds.Count > 0)
            {
                return true;
            }

            return archive.CountForPawn(pawnId) > 0;
        }
    }
}
