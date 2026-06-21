// The catalog of Diary event sources. Each value names one kind of RimWorld gameplay moment we
// capture (a thought gained, an inspiration started, a quest accepted, ...). Adding a new event
// source starts here: pick a value, then add XxxEventData + XxxEventSpec + a Register() call.
//
// This is the Diary equivalent of Redux "action types": a single enumerated list of everything the
// diary knows how to react to. Keeping it in one place makes it easy to see the mod's coverage at a
// glance and gives future tooling (dev tab, weight table, XML-driven enable/disable) a stable key.
//
// Every value in this enum has a registered Spec and pure decision tests. Planned future sources
// stay out of the enum until they have an XxxEventData, XxxEventSpec, Register() call, and tests.
namespace PawnDiary.Capture
{
    public enum DiaryEventType
    {
        // ── Migrated to the Event Catalog pattern ──
        Thought,
        Inspiration,
        MoodEvent,
        MentalState,
        Tale,
        Hediff,
        Interaction,

        // ── Net-new sources added on top of the Event Catalog ──
        // Romance: pair events for Lover/Spouse/ExLover/ExSpouse relation changes (the first
        // source designed FROM SCRATCH onto the catalog, proving the pattern handles additions
        // not just migrations of pre-existing RecordX methods).
        Romance,

        // ── Planned future sources (placeholders only — NOT implemented yet) ──
        // Existing sources to migrate source-by-source in later slices:
        //   Arrival, Death.
        // Net-new sources planned (see repo discussion):
        //   Quest, Raid, MajorThreat, RandomEvent, WorldEvent, AnomalyEvent, IncidentEvent,
        //   Health.
    }
}
