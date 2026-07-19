// The registry that maps DiaryEventType → DiaryEventSpec. This is the single place that lists every
// event source the diary can decide on. Adding a new source = add a value to DiaryEventType, write a
// XxxEventData + XxxEventSpec, then Register() the spec here.
//
// Every current DiaryEventType value is registered here. The registry is initialized lazily on the
// first Get() call so it does not run during RimWorld's early type load, which would be too early to
// read DefDatabase-backed settings. Specs read those lazily inside Decide(), not in constructors.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    internal static class DiaryEventCatalog
    {
        private static readonly Dictionary<DiaryEventType, DiaryEventSpec> Specs =
            new Dictionary<DiaryEventType, DiaryEventSpec>();

        private static bool initialized;

        /// <summary>
        /// Looks up the Spec for one event type. Returns null for unregistered/future-planned types
        /// (callers treat null as "drop the event silently").
        /// </summary>
        public static DiaryEventSpec Get(DiaryEventType type)
        {
            EnsureInitialized();
            DiaryEventSpec spec;
            return Specs.TryGetValue(type, out spec) ? spec : null;
        }

        /// <summary>
        /// Adds a Spec to the registry. Called once per source in EnsureInitialized; exposed for
        /// tests/dev tools that want to swap a Spec temporarily.
        /// </summary>
        public static void Register(DiaryEventSpec spec)
        {
            if (spec == null)
            {
                return;
            }
            Specs[spec.EventType] = spec;
        }

        /// <summary>
        /// Resets the registry to its built-in defaults. Test-only: tests register their own Specs
        /// after calling this to start from a clean slate. RimWorld code never calls this.
        /// </summary>
        internal static void Reset()
        {
            Specs.Clear();
            initialized = false;
            EnsureInitialized();
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;

            // One Register call per migrated source. New sources added in future slices register here.
            Register(new ThoughtEventSpec());
            Register(new InspirationEventSpec());
            Register(new MoodEventSpec());
            Register(new MentalStateEventSpec());
            Register(new TaleEventSpec());
            Register(new HediffEventSpec());
            Register(new InteractionEventSpec());
            Register(new ArrivalEventSpec());
            Register(new DeathEventSpec());
            Register(new WorkEventSpec());
            Register(new ThoughtProgressionEventSpec());
            Register(new DayReflectionEventSpec());
            Register(new ProgressionEventSpec());
            Register(new ArcReflectionEventSpec());
            Register(new RomanceEventSpec());
            Register(new RaidEventSpec());
            Register(new QuestEventSpec());
            Register(new RitualEventSpec());
            Register(new AbilityEventSpec());
            Register(new ExternalEventSpec());
            // Biotech growth and canonical family birth are live B1 catalog routes.
            Register(new GrowthMomentEventSpec());
            Register(new FamilyBirthEventSpec());
            Register(new GravshipJourneyEventSpec());
            Register(new PersonaWeaponEventSpec());
            Register(new RoyalPermitEventSpec());
        }
    }
}
