// Game events — synthetic map/story discoveries that vanilla does not expose as TaleDefs.
// These hooks keep diary ownership on the pawn who caused the discovery: monolith methods pass the
// pawn directly, while fog reveals use a short-lived cache from FogGrid.Notify_PawnEnteringDoor.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string GameAncientMechRevealedDefName = "PawnDiary_GameAncientMechRevealed";
        private const string GameRevealedThingDefName = "PawnDiary_GameRevealedThing";
        private const string GameMonolithInvestigatedDefName = "PawnDiary_GameMonolithInvestigated";
        private const string GameMonolithActivatedDefName = "PawnDiary_GameMonolithActivated";
        private const float RevealThingSearchRadius = 30f;

        /// <summary>
        /// Records a pawn-triggered fog reveal when it exposes an ancient mech threat or another
        /// object that vanilla marks with CompLetterOnRevealed.
        /// </summary>
        public void RecordAreaRevealed(Pawn discoverer, Map map, IntVec3 root, FloodUnfogResult result)
        {
            if (PawnDiaryMod.Settings == null || map == null)
            {
                return;
            }

            Pawn pawn = ResolveDiscoverer(discoverer, map, root);
            if (pawn == null)
            {
                return;
            }

            Thing revealedThing = result.mechanoidFound ? null : FindNearbyLetterOnRevealedThing(map, root);
            if (!result.mechanoidFound && revealedThing == null)
            {
                return;
            }

            string defName = result.mechanoidFound ? GameAncientMechRevealedDefName : GameRevealedThingDefName;
            if (!PawnDiaryMod.Settings.IsGameEventEnabled(defName))
            {
                return;
            }

            string objectKey = revealedThing != null ? revealedThing.GetUniqueLoadID() : root.ToString();
            string dedupKey = "gameevent|" + defName + "|map=" + map.uniqueID + "|object=" + objectKey;
            if (RecentlyRecorded(recentGameEvents, dedupKey, DiaryTuning.Current.taleDedupTicks))
            {
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
        }

        /// <summary>
        /// Records a pawn's first investigation of a void/fallen monolith.
        /// </summary>
        public void RecordMonolithInvestigated(Pawn pawn, Building_VoidMonolith monolith)
        {
            RecordMonolithEvent(pawn, monolith, GameMonolithInvestigatedDefName,
                "PawnDiary.Event.MonolithInvestigatedLabel", "PawnDiary.Event.MonolithInvestigated");
        }

        /// <summary>
        /// Records the pawn who activates the monolith, a separate beat from first investigation.
        /// </summary>
        public void RecordMonolithActivated(Pawn pawn, Building_VoidMonolith monolith)
        {
            RecordMonolithEvent(pawn, monolith, GameMonolithActivatedDefName,
                "PawnDiary.Event.MonolithActivatedLabel", "PawnDiary.Event.MonolithActivated");
        }

        private void RecordMonolithEvent(Pawn pawn, Building_VoidMonolith monolith, string defName,
            string labelKey, string textKey)
        {
            if (pawn == null || monolith == null || PawnDiaryMod.Settings == null || !IsDiaryEligible(pawn))
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGameEventEnabled(defName))
            {
                return;
            }

            string dedupKey = "gameevent|" + defName + "|monolith=" + monolith.GetUniqueLoadID()
                + "|pawn=" + pawn.GetUniqueLoadID();
            if (RecentlyRecorded(recentGameEvents, dedupKey, DiaryTuning.Current.taleDedupTicks))
            {
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
    }
}
