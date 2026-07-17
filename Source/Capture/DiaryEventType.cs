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
    internal enum DiaryEventType
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
        Progression,
        ArcReflection,

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
        // External: events pushed by OTHER MODS through the public integration API
        // (PawnDiary.Integration.PawnDiaryApi.SubmitEvent). We never hook the other mod ourselves;
        // an adapter mod calls the API with a stable eventKey string, and External-domain
        // DiaryInteractionGroupDefs (usually shipped by the adapter) own the prompt policy.
        External,

        // Biotech's concrete B1 catalog identities. GrowthMoment is owned by the Phase-1 birthday /
        // growth-letter boundary; FamilyBirth is owned by Phase 3's canonical ApplyBirthOutcome hook.
        GrowthMoment,
        FamilyBirth,
        // Odyssey's canonical successful-landing chapter. Takeoff and travel remain state-only;
        // one landing payload chooses a solo page or one pair-shaped event with at most two POVs.
        GravshipJourney,

        // ── Planned future sources (placeholders only — NOT implemented yet) ──
        // No known live RecordX source remains to migrate; batch/ambient flushers are route sinks.
        // Net-new sources planned (see repo discussion):
        //   MajorThreat, RandomEvent, WorldEvent, AnomalyEvent, IncidentEvent, Health.
    }
}
