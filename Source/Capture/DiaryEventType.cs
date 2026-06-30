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
        Arrival,
        Death,
        Work,
        ThoughtProgression,
        DayReflection,

        // ── Net-new sources added on top of the Event Catalog ──
        // Romance: pair events for Lover/Spouse/ExLover/ExSpouse relation changes (the first
        // source designed FROM SCRATCH onto the catalog, proving the pattern handles additions
        // not just migrations of pre-existing RecordX methods).
        Romance,
        // Raid: colony-wide fan-out for IncidentWorker_Raid (RaidEnemy/RaidFriendly/RaidBeacon).
        // Each eligible colonist on the raid's target map gets a solo entry. Minimal realization:
        // only incident defName + raider faction defName + raid points are captured.
        Raid,
        // Quest: colony-wide fan-out across the quest lifecycle. Only accepted quests are recorded
        // (Quest.Accept hook). Quest.End records Success as "completed" and Fail as "failed". The
        // Signal field on QuestEventData routes prompt group selection, not this enum value.
        Quest,
        // Ritual: Ideology ritual completion fan-out from LordJob_Ritual.ApplyOutcome. Each
        // eligible organizer/target/participant/spectator gets a solo entry with role-specific
        // instruction and ritual role/title context.
        Ritual,
        // Ability: successful pawn Ability.Activate calls. Short-cooldown abilities are sampled
        // less often than long-cooldown abilities to avoid spam.
        Ability,
        // SkillMilestone: a colonist crosses an XML-configured skill level threshold.
        SkillMilestone,
        // FactionRelation: the player's diplomatic relation kind with another faction changes.
        FactionRelation,
        // TradeDeal: a negotiator completes an XML-thresholded trade or gift deal.
        TradeDeal,
        // CaravanJourney: player caravans depart from or arrive at a map.
        CaravanJourney,

        // ── Planned future sources (placeholders only — NOT implemented yet) ──
        // No known live RecordX source remains to migrate; batch/ambient flushers are route sinks.
        // Net-new sources planned (see repo discussion):
        //   MajorThreat, RandomEvent, WorldEvent, AnomalyEvent, IncidentEvent, Health.
    }
}
