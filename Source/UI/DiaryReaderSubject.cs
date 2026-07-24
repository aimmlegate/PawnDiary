// Plain identity snapshot for one diary reader subject.
// A live Pawn is optional because compact archive rows can outlive the world pawn object; the stable
// load ID and cached display name are enough for the journal's pure/indexed paths.
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Identifies the pawn whose diary is being rendered, including archive-only subjects.
    /// </summary>
    internal struct DiaryReaderSubject
    {
        public Pawn Pawn;
        public string PawnId;
        public string DisplayName;
        public bool Alive;

        /// <summary>
        /// Creates a reader subject from a live or dead pawn object.
        /// </summary>
        public static DiaryReaderSubject FromPawn(Pawn pawn)
        {
            return new DiaryReaderSubject
            {
                Pawn = pawn,
                PawnId = pawn?.GetUniqueLoadID() ?? string.Empty,
                DisplayName = pawn?.LabelShortCap ?? string.Empty,
                Alive = pawn != null && !pawn.Dead
            };
        }

        /// <summary>
        /// True when the subject contains the stable identity required by the indexed reader paths.
        /// </summary>
        public bool IsValid
        {
            get { return !string.IsNullOrWhiteSpace(PawnId); }
        }
    }
}
