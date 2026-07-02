// The whole example adapter: one GameComponent that submits one harmless external event per
// in-game day through Pawn Diary's public API. This is the live demonstration of the contract in
// INTEGRATIONS.md — a real adapter replaces the daily timer with hooks into its target mod
// (a Harmony patch, an event the target mod raises, etc.) and keeps everything else the same.
//
// RimWorld auto-instantiates every GameComponent subclass that has a (Game) constructor, so this
// needs no Harmony and no registration: being in a loaded assembly is enough.
//
// Notes for adapter authors copying this file:
//   • Only the PawnDiary.Integration namespace is used — never core internals.
//   • All text the adapter sends (summaryText, eventLabel) is localized by the ADAPTER via its
//     own Keyed strings; Pawn Diary cannot translate another mod's content.
//   • SubmitEvent never throws and returns false when declined (no game, off-thread, unclaimed
//     eventKey) — safe to call without try/catch on the main thread.
using System.Collections.Generic;
using PawnDiary.Integration;
using RimWorld;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Submits one example diary event per in-game day for a random free colonist, proving the
    /// Pawn Diary integration API end-to-end. Template code — copy and replace the timer with
    /// your mod's real trigger.
    /// </summary>
    public class ExampleAdapterGameComponent : GameComponent
    {
        // The stable classifier this adapter owns. The External-domain group in
        // 1.6/Defs/DiaryExternalGroups_Example.xml claims exactly this key; renaming a shipped
        // key would orphan saved events, so treat it like save data.
        private const string EventKey = "exampleadapter_quiet_moment";

        // Our packageId, so Pawn Diary's one-time log messages name this adapter.
        private const string SourceId = "aimmlegate.pawndiary.adapter.example";

        private const int FirstDelayTicks = 2500;   // ~1 in-game hour after load
        private const int RepeatDelayTicks = 60000; // one in-game day
        private const int CheckIntervalTicks = 250; // cheap gate: test the timer 4x/sec

        // Transient on purpose: not saved, so each session starts its own daily rhythm. A real
        // adapter driven by its target mod's events usually needs no timer state at all.
        private int nextSubmitTick = -1;

        public ExampleAdapterGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckIntervalTicks != 0)
            {
                return;
            }

            if (nextSubmitTick < 0)
            {
                nextSubmitTick = now + FirstDelayTicks;
                return;
            }

            if (now < nextSubmitTick)
            {
                return;
            }

            nextSubmitTick = now + RepeatDelayTicks;
            SubmitExampleEvent();
        }

        private static void SubmitExampleEvent()
        {
            // IsReady is redundant inside a GameComponentTick (a game is obviously loaded), but a
            // real adapter calling from its own hooks should keep this guard.
            if (!PawnDiaryApi.IsReady)
            {
                return;
            }

            Map map = Find.AnyPlayerHomeMap;
            List<Pawn> colonists = map?.mapPawns?.FreeColonists;
            if (colonists == null || colonists.Count == 0)
            {
                return;
            }

            Pawn subject = colonists.RandomElement();
            PawnDiaryApi.SubmitEvent(new ExternalEventRequest
            {
                sourceId = SourceId,
                eventKey = EventKey,
                subject = subject,
                // Adapter-owned Keyed strings — see Languages/English/Keyed/ExampleAdapter.xml.
                summaryText = "PawnDiaryExampleAdapter.QuietMomentSummary".Translate(subject.LabelShortCap).Resolve(),
                eventLabel = "PawnDiaryExampleAdapter.QuietMomentLabel".Translate().Resolve(),
                extraContext = new List<string>
                {
                    "origin=example_adapter_daily_timer"
                }
            });
        }
    }
}
