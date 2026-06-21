// The catalog of Diary event sources. Each value names one kind of RimWorld gameplay moment we
// capture (a thought gained, an inspiration started, a quest accepted, ...). Adding a new event
// source starts here: pick a value, then add XxxEventData + XxxEventSpec + a Register() call.
//
// This is the Diary equivalent of Redux "action types": a single enumerated list of everything the
// diary knows how to react to. Keeping it in one place makes it easy to see the mod's coverage at a
// glance and gives future tooling (dev tab, weight table, XML-driven enable/disable) a stable key.
//
// Today only Thought and Inspiration are wired end-to-end. The TODO entries below name the sources
// we plan to migrate or add in later slices — they are listed so the design intent is visible, but
// they MUST NOT be referenced by code yet (no spec, no payload, no patch).
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

        // ── Planned future sources (placeholders only — NOT implemented yet) ──
        // Existing sources to migrate source-by-source in later slices:
        //   Interaction, Arrival, Death.
        // Net-new sources planned (see repo discussion):
        //   Quest, Raid, MajorThreat, RandomEvent, WorldEvent, AnomalyEvent, IncidentEvent,
        //   Health, Romance.
    }
}
