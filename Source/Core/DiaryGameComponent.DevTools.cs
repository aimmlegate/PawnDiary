// Dev-only helpers for stress-testing diary UI behavior. These methods are still compiled into the
// mod DLL, but each public entry point checks RimWorld dev mode before mutating saved diary data.
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string DevMockDefName = "PawnDiary_DevMock";
        private const int DevMockDaysBetweenEntries = 3;

        /// <summary>
        /// Dev-mode helper used by the Diary tab to seed a pawn with many completed fake pages.
        /// Entries are complete immediately, span many display dates, and never enter the LLM queue.
        /// </summary>
        public int FillMockDiaryEntriesForDev(Pawn pawn, int targetCount)
        {
            if (!Prefs.DevMode || !IsDiaryEligible(pawn) || targetCount <= 0)
            {
                return 0;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return 0;
            }

            if (diary.eventIds == null)
            {
                diary.eventIds = new System.Collections.Generic.List<string>();
            }

            string pawnId = pawn.GetUniqueLoadID();
            int existingMockCount = CountMockDiaryEntries(diary, pawnId);
            int entriesToAdd = Math.Max(0, targetCount - existingMockCount);
            if (entriesToAdd <= 0)
            {
                return 0;
            }

            int startTicksAbs = Find.TickManager.TicksAbs;
            int visibleTickBase = MockVisibleTickBase(pawnId, diary, Find.TickManager.TicksGame, targetCount);
            for (int i = 0; i < entriesToAdd; i++)
            {
                int mockIndex = existingMockCount + i;
                DiaryEvent diaryEvent = BuildMockDiaryEvent(pawn, mockIndex, targetCount, startTicksAbs, visibleTickBase);
                events.Register(diaryEvent);
                diary.eventIds.Add(diaryEvent.eventId);
            }

            diary.pawnName = pawn.LabelShortCap;
            return entriesToAdd;
        }

        private int CountMockDiaryEntries(PawnDiaryRecord diary, string pawnId)
        {
            if (diary == null || diary.eventIds == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (diaryEvent != null
                    && string.Equals(diaryEvent.interactionDefName, DevMockDefName, StringComparison.Ordinal)
                    && diaryEvent.initiatorPawnId == pawnId)
                {
                    count++;
                }
            }

            return count;
        }

        private static DiaryEvent BuildMockDiaryEvent(
            Pawn pawn,
            int mockIndex,
            int targetCount,
            int startTicksAbs,
            int visibleTickBase)
        {
            int displayNumber = mockIndex + 1;
            int ticksAbs = MockDateTicksAbs(startTicksAbs, mockIndex, targetCount);
            int ticksGame = MockGameTick(visibleTickBase, mockIndex);
            string pawnName = pawn.LabelShortCap;
            string generatedText = "PawnDiary.Dev.MockBody".Translate(displayNumber, pawnName).Resolve();
            string label = "PawnDiary.Dev.MockLabel".Translate().Resolve();

            return new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = ticksGame,
                date = GenDate.DateFullStringAt(ticksAbs, Vector2.zero),
                interactionDefName = DevMockDefName,
                interactionLabel = label,
                initiatorPawnId = pawn.GetUniqueLoadID(),
                recipientPawnId = string.Empty,
                initiatorName = pawnName,
                recipientName = string.Empty,
                initiatorText = "PawnDiary.Dev.MockRawText".Translate(pawnName, displayNumber).Resolve(),
                recipientText = string.Empty,
                neutralText = generatedText,
                gameContext = "dev_mock=true; mock_index=" + displayNumber,
                instruction = "dev mock entry",
                initiatorPawnSummary = "dev mock pawn",
                recipientPawnSummary = "n/a",
                initiatorSurroundings = "dev mock surroundings",
                recipientSurroundings = "n/a",
                initiatorContinuity = "none",
                recipientContinuity = "none",
                initiatorLastOpener = string.Empty,
                recipientLastOpener = string.Empty,
                initiatorWeapon = string.Empty,
                recipientWeapon = string.Empty,
                initiatorGeneratedText = generatedText,
                recipientGeneratedText = string.Empty,
                neutralGeneratedText = string.Empty,
                initiatorStatus = DiaryEvent.CompleteStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus,
                initiatorLlmEndpoint = string.Empty,
                recipientLlmEndpoint = string.Empty,
                neutralLlmEndpoint = string.Empty,
                initiatorLlmModel = "dev-mock",
                recipientLlmModel = string.Empty,
                neutralLlmModel = string.Empty,
                solo = true,
                initiatorTitle = "PawnDiary.Dev.MockTitle".Translate(displayNumber).Resolve(),
                recipientTitle = string.Empty,
                neutralTitle = string.Empty,
                initiatorTitleStatus = DiaryEvent.CompleteStatus,
                recipientTitleStatus = string.Empty,
                neutralTitleStatus = string.Empty,
                moodImpact = MoodImpact.Neutral
            };
        }

        private static int MockDateTicksAbs(int startTicksAbs, int mockIndex, int targetCount)
        {
            long daysFromNewest = (long)(targetCount - 1 - mockIndex) * DevMockDaysBetweenEntries;
            long offsetTicks = daysFromNewest * GenDate.TicksPerDay;
            long candidate = (long)startTicksAbs - offsetTicks;

            if (candidate < 0L)
            {
                candidate = (long)startTicksAbs + (long)mockIndex * DevMockDaysBetweenEntries * GenDate.TicksPerDay;
            }

            return ClampToIntTick(candidate);
        }

        private int MockVisibleTickBase(string pawnId, PawnDiaryRecord diary, int startTicksGame, int targetCount)
        {
            int firstAllowedTick = FirstArrivalTickFor(pawnId, diary) ?? 0;
            int? finalDeathTick = FinalDeathTickFor(pawnId, diary);
            if (finalDeathTick.HasValue)
            {
                long deadPawnBase = Math.Max((long)firstAllowedTick, (long)finalDeathTick.Value - targetCount - 1L);
                return ClampToIntTick(deadPawnBase);
            }

            long livingPawnBase = Math.Max((long)startTicksGame + 1L, (long)firstAllowedTick + 1L);
            return ClampToIntTick(livingPawnBase);
        }

        private static int MockGameTick(int visibleTickBase, int mockIndex)
        {
            return ClampToIntTick((long)visibleTickBase + mockIndex);
        }

        private static int ClampToIntTick(long value)
        {
            if (value < 0L)
            {
                return 0;
            }

            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)value;
        }
    }
}
