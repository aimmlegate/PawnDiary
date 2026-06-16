// Game events — synthetic map/story discoveries that vanilla does not expose as TaleDefs.
// Known status: discovery generation is non-functional in current RimWorld 1.6 testing. The
// recorder is kept here for future repair, but the broken Harmony hooks are disabled in
// Source/Patches/DiscoveryPatches.cs and do not call these methods at runtime.
// These hooks keep diary ownership on the pawn who caused the discovery when vanilla exposes that
// pawn directly. Monolith methods pass the pawn; fog reveals only expose map/root/result in RimWorld
// 1.6, so the recorder chooses the nearest eligible colonist as the discoverer.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string GameAncientMechRevealedDefName = "PawnDiary_GameAncientMechRevealed";
        private const string GameAncientDangerRevealedDefName = "PawnDiary_GameAncientDangerRevealed";
        private const string GameRevealedThingDefName = "PawnDiary_GameRevealedThing";
        private const string GameMonolithInvestigatedDefName = "PawnDiary_GameMonolithInvestigated";
        private const string GameMonolithActivatedDefName = "PawnDiary_GameMonolithActivated";
        private const float RevealThingSearchRadius = 30f;
        internal static bool DiscoveryEventsEnabled => false;

        /// <summary>
        /// Records a pawn-triggered fog reveal when it exposes an ancient mech threat or another
        /// object that vanilla marks with CompLetterOnRevealed.
        /// </summary>
        public void RecordAreaRevealed(Pawn discoverer, Map map, IntVec3 root, FloodUnfogResult result)
        {
            if (!ShouldRecordDiscoveryEvent("area reveal"))
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || map == null)
            {
                LogGameEventDebug("area reveal skipped: settings or map missing");
                return;
            }

            Pawn pawn = ResolveDiscoverer(discoverer, map, root);
            if (pawn == null)
            {
                LogGameEventDebug("area reveal skipped: no eligible discoverer at " + root);
                return;
            }

            Thing revealedThing = result.mechanoidFound ? null : FindNearbyLetterOnRevealedThing(map, root);
            if (!result.mechanoidFound && revealedThing == null)
            {
                LogGameEventDebug("area reveal skipped: no mechanoid flag and no nearby CompLetterOnRevealed thing at " + root);
                return;
            }

            string defName = result.mechanoidFound ? GameAncientMechRevealedDefName : GameRevealedThingDefName;
            if (!PawnDiaryMod.Settings.IsGameEventEnabled(defName))
            {
                LogGameEventDebug("area reveal skipped: group disabled for " + defName);
                return;
            }

            string objectKey = revealedThing != null ? revealedThing.GetUniqueLoadID() : root.ToString();
            string dedupKey = "gameevent|" + defName + "|map=" + map.uniqueID + "|object=" + objectKey;
            if (RecentlyRecorded(recentGameEvents, dedupKey, DiaryTuning.Current.taleDedupTicks))
            {
                LogGameEventDebug("area reveal skipped: recently recorded " + dedupKey);
                return;
            }

            string label = result.mechanoidFound
                ? "PawnDiary.Event.AncientMechRevealedLabel".Translate().Resolve()
                : "PawnDiary.Event.RevealedThingLabel".Translate().Resolve();
            string revealedLabel = result.mechanoidFound
                ? label
                : DiaryContextBuilder.CleanLine(revealedThing.LabelShortCap);
            string text = result.mechanoidFound
                ? "PawnDiary.Event.AncientMechRevealed".Translate(pawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.RevealedThing".Translate(pawn.LabelShortCap, revealedLabel).Resolve();
            string gameContext = BuildRevealContext(defName, label, map, root, result, revealedThing, revealedLabel);

            DiaryEvent diaryEvent = AddSoloEvent(pawn, null, defName, label, text,
                PawnDiaryMod.Settings.InstructionForGameEvent(defName), gameContext);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            LogGameEventDebug("area reveal queued: " + defName + " pawn=" + pawn.LabelShortCap + " root=" + root);
        }

        /// <summary>
        /// Records vanilla ancient-danger reveal triggers, which are emitted by TriggerUnfogged
        /// and can fire even when the broader FloodUnfogResult does not expose a useful flag.
        /// </summary>
        public void RecordAncientDangerRevealed(Pawn discoverer, Map map, IntVec3 root, Thing trigger)
        {
            if (!ShouldRecordDiscoveryEvent("ancient danger"))
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || map == null)
            {
                LogGameEventDebug("ancient danger skipped: settings or map missing");
                return;
            }

            Pawn pawn = ResolveDiscoverer(discoverer, map, root);
            if (pawn == null)
            {
                LogGameEventDebug("ancient danger skipped: no eligible discoverer at " + root);
                return;
            }

            if (!PawnDiaryMod.Settings.IsGameEventEnabled(GameAncientDangerRevealedDefName))
            {
                LogGameEventDebug("ancient danger skipped: group disabled");
                return;
            }

            string objectKey = trigger != null ? trigger.GetUniqueLoadID() : root.ToString();
            string dedupKey = "gameevent|" + GameAncientDangerRevealedDefName + "|map=" + map.uniqueID + "|object=" + objectKey;
            if (RecentlyRecorded(recentGameEvents, dedupKey, DiaryTuning.Current.taleDedupTicks))
            {
                LogGameEventDebug("ancient danger skipped: recently recorded " + dedupKey);
                return;
            }

            string label = "PawnDiary.Event.AncientDangerRevealedLabel".Translate().Resolve();
            string text = "PawnDiary.Event.AncientDangerRevealed".Translate(pawn.LabelShortCap).Resolve();
            string gameContext = "game_event=" + GameAncientDangerRevealedDefName
                + "; label=" + DiaryContextBuilder.CleanLine(label)
                + "; map=" + map.uniqueID
                + "; root_cell=" + root
                + "; trigger=" + (trigger?.def?.defName ?? "unknown");

            DiaryEvent diaryEvent = AddSoloEvent(pawn, null, GameAncientDangerRevealedDefName, label, text,
                PawnDiaryMod.Settings.InstructionForGameEvent(GameAncientDangerRevealedDefName), gameContext);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            LogGameEventDebug("ancient danger queued: pawn=" + pawn.LabelShortCap + " root=" + root);
        }

        /// <summary>
        /// Records a pawn's first investigation of a void/fallen monolith.
        /// </summary>
        public void RecordMonolithInvestigated(Pawn pawn, Thing monolith)
        {
            RecordMonolithEvent(pawn, monolith, GameMonolithInvestigatedDefName,
                "PawnDiary.Event.MonolithInvestigatedLabel", "PawnDiary.Event.MonolithInvestigated");
        }

        /// <summary>
        /// Records the pawn who activates the monolith, a separate beat from first investigation.
        /// </summary>
        public void RecordMonolithActivated(Pawn pawn, Thing monolith)
        {
            RecordMonolithEvent(pawn, monolith, GameMonolithActivatedDefName,
                "PawnDiary.Event.MonolithActivatedLabel", "PawnDiary.Event.MonolithActivated");
        }

        private void RecordMonolithEvent(Pawn pawn, Thing monolith, string defName,
            string labelKey, string textKey)
        {
            if (!ShouldRecordDiscoveryEvent(defName))
            {
                return;
            }

            if (pawn == null || monolith == null || PawnDiaryMod.Settings == null || !IsDiaryEligible(pawn))
            {
                LogGameEventDebug("monolith skipped: pawn/monolith/settings/eligibility failed for " + defName);
                return;
            }

            if (!PawnDiaryMod.Settings.IsGameEventEnabled(defName))
            {
                LogGameEventDebug("monolith skipped: group disabled for " + defName);
                return;
            }

            string dedupKey = "gameevent|" + defName + "|monolith=" + monolith.GetUniqueLoadID();
            if (RecentlyRecorded(recentGameEvents, dedupKey, DiaryTuning.Current.taleDedupTicks))
            {
                LogGameEventDebug("monolith skipped: recently recorded " + dedupKey);
                return;
            }

            string label = labelKey.Translate().Resolve();
            string monolithLabel = DiaryContextBuilder.CleanLine(monolith.LabelShortCap);
            string text = textKey.Translate(pawn.LabelShortCap, monolithLabel).Resolve();
            string gameContext = "game_event=" + defName
                + "; label=" + DiaryContextBuilder.CleanLine(label)
                + "; monolith=" + monolith.def.defName
                + "; monolith_label=" + monolithLabel;

            DiaryEvent diaryEvent = AddSoloEvent(pawn, null, defName, label, text,
                PawnDiaryMod.Settings.InstructionForGameEvent(defName), gameContext);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            LogGameEventDebug("monolith queued: " + defName + " pawn=" + pawn.LabelShortCap);
        }

        private bool ShouldRecordDiscoveryEvent(string eventKind)
        {
            if (DiscoveryEventsEnabled)
            {
                return true;
            }

            LogGameEventDebug(eventKind + " skipped: discovery generation is disabled; hooks are known non-functional");
            return false;
        }

        private Pawn ResolveDiscoverer(Pawn discoverer, Map map, IntVec3 root)
        {
            if (IsDiaryEligible(discoverer))
            {
                return discoverer;
            }

            if (map?.mapPawns == null)
            {
                return null;
            }

            Pawn nearest = null;
            int nearestDistance = int.MaxValue;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn candidate = colonists[i];
                if (!IsDiaryEligible(candidate) || !candidate.Spawned || candidate.Map != map)
                {
                    continue;
                }

                int distance = (candidate.Position - root).LengthHorizontalSquared;
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static Thing FindNearbyLetterOnRevealedThing(Map map, IntVec3 root)
        {
            List<Thing> things = map?.listerThings?.AllThings;
            if (things == null)
            {
                return null;
            }

            Thing closest = null;
            int closestDistance = int.MaxValue;
            for (int i = 0; i < things.Count; i++)
            {
                ThingWithComps thing = things[i] as ThingWithComps;
                if (thing == null || !thing.Spawned || thing.Map != map)
                {
                    continue;
                }

                if (!thing.Position.InHorDistOf(root, RevealThingSearchRadius) || map.fogGrid.IsFogged(thing.Position))
                {
                    continue;
                }

                if (thing.GetComp<CompLetterOnRevealed>() == null)
                {
                    continue;
                }

                int distance = (thing.Position - root).LengthHorizontalSquared;
                if (distance < closestDistance)
                {
                    closest = thing;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private static string BuildRevealContext(string defName, string label, Map map, IntVec3 root,
            FloodUnfogResult result, Thing revealedThing, string revealedLabel)
        {
            List<string> parts = new List<string>
            {
                "game_event=" + defName,
                "label=" + DiaryContextBuilder.CleanLine(label),
                "map=" + map.uniqueID,
                "root_cell=" + root,
                "cells_unfogged=" + result.cellsUnfogged,
                "ancient_mech_found=" + (result.mechanoidFound ? "true" : "false")
            };

            if (revealedThing != null)
            {
                parts.Add("revealed_thing=" + revealedThing.def.defName);
                parts.Add("revealed_label=" + DiaryContextBuilder.CleanLine(revealedLabel));
            }

            return string.Join("; ", parts.ToArray());
        }

        private static void LogGameEventDebug(string message)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Log.Message("[PawnDiary debug] game event: " + message);
        }
    }
}
