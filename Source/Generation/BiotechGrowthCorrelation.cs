// Short-lived main-thread correlation for the nested Biotech birthday -> growth-letter call stack.
// Saved ownership never lives here: configured letters create PendingBiotechGrowthMoment rows on the
// GameComponent. This scope only lets ConfigureGrowthLetter prove which birthday invoked it.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety" and static cache resets).
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>One active biological-birthday call and its exact before-state.</summary>
    internal sealed class BiotechGrowthBirthdayState
    {
        public Pawn pawn;
        public string pawnId = string.Empty;
        public int birthdayAge;
        public int birthdayTick;
        public int growthTier;
        public string familyArcId = string.Empty;
        public GrowthPawnSnapshot beforeSnapshot;
        public HashSet<string> disabledWorkTypesBefore = new HashSet<string>();
        public bool configuredLetterOwnsBirthday;
        internal BiotechGrowthBirthdayState previous;
    }

    /// <summary>Harmony state spanning one false-to-true MakeChoices transition.</summary>
    internal sealed class BiotechGrowthChoiceState
    {
        public Pawn pawn;
        public PendingBiotechGrowthMoment pending;
        public GrowthPawnSnapshot beforeSnapshot;
        public GrowthCommittedChoice committedChoice;
        public bool choiceWasAlreadyMade;
    }

    /// <summary>
    /// Thread-local nesting scope used only while BirthdayBiological is executing. Harmony hooks are
    /// main-thread game callbacks, but thread-local storage prevents a stray worker call from seeing it.
    /// </summary>
    internal static class BiotechGrowthCorrelation
    {
        [System.ThreadStatic]
        private static BiotechGrowthBirthdayState activeBirthday;

        /// <summary>Pushes one birthday scope so its nested ConfigureGrowthLetter can find it.</summary>
        public static void BeginBirthday(BiotechGrowthBirthdayState state)
        {
            if (state == null)
            {
                return;
            }

            state.previous = activeBirthday;
            activeBirthday = state;
        }

        /// <summary>Returns the active scope only when the stable pawn identity matches.</summary>
        public static BiotechGrowthBirthdayState CurrentBirthdayFor(Pawn pawn)
        {
            if (pawn == null || activeBirthday == null)
            {
                return null;
            }

            return string.Equals(
                activeBirthday.pawnId,
                pawn.GetUniqueLoadID(),
                System.StringComparison.Ordinal)
                ? activeBirthday
                : null;
        }

        /// <summary>Pops one scope; safe to call from both postfix and finalizer.</summary>
        public static void EndBirthday(BiotechGrowthBirthdayState state)
        {
            if (state == null || !ReferenceEquals(activeBirthday, state))
            {
                return;
            }

            activeBirthday = state.previous;
            state.previous = null;
        }

        /// <summary>Clears static correlation across exit-to-menu/new-game/load boundaries.</summary>
        public static void Clear()
        {
            activeBirthday = null;
        }
    }
}
