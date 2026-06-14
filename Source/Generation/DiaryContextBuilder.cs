// Builds the compact context strings sent to the LLM (pawn profile, surroundings,
// relationship/continuity, opinions) plus the text/number formatting helpers.
// Static helpers, no state. Split out of DiaryGameComponent.cs. See DOCUMENTATION.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public static class DiaryContextBuilder
    {
        public static string BuildGameContextSummary(InteractionDef interactionDef, string interactionLabel)
        {
            if (interactionDef == null)
            {
                return "unknown";
            }

            List<string> parts = new List<string>
            {
                "def=" + interactionDef.defName,
                "label=" + CleanLine(interactionLabel)
            };

            string worker = interactionDef.Worker?.GetType().Name;
            if (!string.IsNullOrWhiteSpace(worker))
            {
                parts.Add("worker=" + worker);
            }

            if (interactionDef.initiatorThought != null)
            {
                parts.Add("initiatorThought=" + CleanLine(interactionDef.initiatorThought.LabelCap.Resolve()));
            }

            if (interactionDef.recipientThought != null)
            {
                parts.Add("recipientThought=" + CleanLine(interactionDef.recipientThought.LabelCap.Resolve()));
            }

            return string.Join("; ", parts.ToArray());
        }

        // The pov pawn's standing toward the other pawn: relation kind, opinion, the social
        // memories driving that opinion, and the last diary line they wrote about them. The
        // diary line acts as a lightweight memory layer feeding continuity back to the model.
        public static string BuildContinuitySummary(Pawn povPawn, Pawn otherPawn, IReadOnlyList<DiaryEvent> events)
        {
            if (povPawn == null || otherPawn == null)
            {
                return "none";
            }

            List<string> parts = new List<string>();

            PawnRelationDef relation = PawnRelationUtility.GetMostImportantRelation(povPawn, otherPawn);
            if (relation != null)
            {
                string relationLabel = CleanLine(relation.GetGenderSpecificLabelCap(otherPawn));
                if (!string.IsNullOrWhiteSpace(relationLabel))
                {
                    parts.Add(relationLabel);
                }
            }

            int opinion = povPawn.relations?.OpinionOf(otherPawn) ?? 0;
            parts.Add("opinion " + FormatOpinion(opinion));

            string reasons = BuildSocialThoughtsSummary(povPawn, otherPawn);
            if (!string.IsNullOrWhiteSpace(reasons))
            {
                parts.Add("because " + reasons);
            }

            string latest = LatestDiaryLineAbout(povPawn.GetUniqueLoadID(), otherPawn.GetUniqueLoadID(), events);
            if (!string.IsNullOrWhiteSpace(latest))
            {
                parts.Add("last wrote: \"" + latest + "\"");
            }

            return parts.Count == 0 ? "none" : string.Join("; ", parts.ToArray());
        }

        // The standing social memories that drive the opinion, aggregated by kind and signed
        // (e.g. "shared kind words +12, slept in a poor bedroom -4").
        private static string BuildSocialThoughtsSummary(Pawn povPawn, Pawn otherPawn)
        {
            if (povPawn.needs?.mood?.thoughts == null)
            {
                return string.Empty;
            }

            List<ISocialThought> social = new List<ISocialThought>();
            povPawn.needs.mood.thoughts.GetSocialThoughts(otherPawn, social);
            if (social.Count == 0)
            {
                return string.Empty;
            }

            Dictionary<string, float> byLabel = new Dictionary<string, float>();
            for (int i = 0; i < social.Count; i++)
            {
                Thought thought = social[i] as Thought;
                if (thought == null)
                {
                    continue;
                }

                string label = CleanLine(thought.LabelCap);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                float offset = social[i].OpinionOffset();
                byLabel[label] = (byLabel.TryGetValue(label, out float existing) ? existing : 0f) + offset;
            }

            return string.Join(", ", byLabel
                .Where(pair => Mathf.Abs(pair.Value) >= 1f)
                .OrderByDescending(pair => Mathf.Abs(pair.Value))
                .Take(3)
                .Select(pair => pair.Key + " " + FormatSignedNumber(Mathf.RoundToInt(pair.Value)))
                .ToArray());
        }

        private static string LatestDiaryLineAbout(string pawnId, string otherPawnId, IReadOnlyList<DiaryEvent> events)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(otherPawnId) || events == null)
            {
                return string.Empty;
            }

            for (int i = events.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = events[i];
                if (diaryEvent == null || !diaryEvent.Involves(pawnId, otherPawnId))
                {
                    continue;
                }

                string role = diaryEvent.RoleForPawn(pawnId);
                string line = diaryEvent.DisplayTextForRole(role);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    line = CleanLine(line);
                    int max = DiaryTuning.Current.diaryLineMaxChars;
                    return line.Length <= max ? line : line.Substring(0, max) + "...";
                }
            }

            return string.Empty;
        }

        public static string BuildOpinionsSummary(Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null)
            {
                return "unknown";
            }

            int initiatorOpinion = initiator.relations?.OpinionOf(recipient) ?? 0;
            int recipientOpinion = recipient.relations?.OpinionOf(initiator) ?? 0;
            return initiator.LabelShortCap + "->" + recipient.LabelShortCap + " "
                + FormatOpinion(initiatorOpinion) + "; " + recipient.LabelShortCap + "->" + initiator.LabelShortCap + " "
                + FormatOpinion(recipientOpinion);
        }

        public static string BuildSurroundingsSummary(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();

            Room room = pawn.Position.GetRoom(pawn.Map);
            bool outdoors = room == null || room.PsychologicallyOutdoors;

            if (room != null)
            {
                string roomRole = CleanLine(room.GetRoomRoleLabel());
                if (!string.IsNullOrWhiteSpace(roomRole))
                {
                    parts.Add(roomRole);
                }
            }

            parts.Add(outdoors ? "outdoors" : "indoors");

            // Weather and biome only matter when the pawn is exposed to them.
            if (outdoors)
            {
                if (pawn.Map.weatherManager?.CurWeatherPerceived != null)
                {
                    parts.Add(CleanLine(pawn.Map.weatherManager.CurWeatherPerceived.label));
                }

                if (pawn.Map.Biome != null)
                {
                    parts.Add(CleanLine(pawn.Map.Biome.label));
                }
            }

            // Temperature only when it's actually uncomfortable.
            float temperature = pawn.AmbientTemperature;
            if (temperature <= DiaryTuning.Current.coldBelowC)
            {
                parts.Add("cold (" + temperature.ToString("0") + "C)");
            }
            else if (temperature >= DiaryTuning.Current.hotAboveC)
            {
                parts.Add("hot (" + temperature.ToString("0") + "C)");
            }

            // Beauty only when it's notably nice or grim.
            float beauty = BeautyUtility.AverageBeautyPerceptible(pawn.Position, pawn.Map);
            if (beauty >= DiaryTuning.Current.beautyPleasant || beauty <= -DiaryTuning.Current.beautyPleasant)
            {
                parts.Add(BeautyBucket(beauty) + " surroundings");
            }

            string nearby = BuildNearbyThingsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(nearby))
            {
                parts.Add("near " + nearby);
            }

            string jobReport = CleanLine(pawn.GetJobReport());
            if (!string.IsNullOrWhiteSpace(jobReport))
            {
                parts.Add("doing: " + jobReport);
            }
            else if (pawn.CurJobDef != null)
            {
                parts.Add("doing: " + CleanLine(pawn.CurJobDef.LabelCap.Resolve()));
            }

            return string.Join(", ", parts.ToArray());
        }

        public static string BuildPawnSummary(Pawn pawn)
        {
            if (pawn == null)
            {
                return "unknown";
            }

            List<string> parts = new List<string>
            {
                "sex=" + pawn.gender.ToString().ToLowerInvariant()
            };

            if (pawn.ageTracker != null)
            {
                parts.Add("age=" + pawn.ageTracker.AgeBiologicalYears);
            }

            string traits = BuildTraitsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(traits))
            {
                parts.Add("traits=" + traits);
            }

            string mood = BuildMoodSummary(pawn);
            if (!string.IsNullOrWhiteSpace(mood))
            {
                parts.Add("mood=" + mood);
            }

            string health = BuildHealthSummary(pawn);
            if (!string.IsNullOrWhiteSpace(health))
            {
                parts.Add("health=" + health);
            }

            string capacities = BuildLowCapacitiesSummary(pawn);
            if (!string.IsNullOrWhiteSpace(capacities))
            {
                parts.Add("low_capacities=" + capacities);
            }

            string thoughts = BuildTopThoughtsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(thoughts))
            {
                parts.Add("top_thoughts=" + thoughts);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string BuildTraitsSummary(Pawn pawn)
        {
            if (pawn.story?.traits?.TraitsSorted == null)
            {
                return string.Empty;
            }

            return string.Join(", ", pawn.story.traits.TraitsSorted
                .Where(trait => trait != null && !trait.Suppressed)
                .Select(trait => CleanLine(trait.LabelCap))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(3)
                .ToArray());
        }

        private static string BuildMoodSummary(Pawn pawn)
        {
            if (pawn.needs?.mood == null)
            {
                return string.Empty;
            }

            int moodPercent = Mathf.RoundToInt(pawn.needs.mood.CurLevelPercentage * 100f);
            return MoodBucket(moodPercent) + " " + moodPercent + "%";
        }

        private static string BuildHealthSummary(Pawn pawn)
        {
            if (pawn.health == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();

            if (pawn.Downed)
            {
                parts.Add("downed");
            }

            if (pawn.health.InPainShock)
            {
                parts.Add("pain shock");
            }

            float pain = pawn.health.hediffSet?.PainTotal ?? 0f;
            if (pain > DiaryTuning.Current.painVisibleAbove)
            {
                parts.Add("pain=" + PainBucket(pain) + " " + Mathf.RoundToInt(pain * 100f) + "%");
            }

            float bleedRate = pawn.health.hediffSet?.BleedRateTotal ?? 0f;
            if (bleedRate > DiaryTuning.Current.bleedVisibleAbove)
            {
                parts.Add("bleeding=" + bleedRate.ToString("0.##") + "/day");
            }

            string notableHediffs = BuildNotableHediffsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(notableHediffs))
            {
                parts.Add("conditions=" + notableHediffs);
            }

            // Empty when healthy, so the prompt omits health entirely.
            return parts.Count == 0 ? string.Empty : string.Join(", ", parts.ToArray());
        }

        private static string BuildNotableHediffsSummary(Pawn pawn)
        {
            if (pawn.health?.hediffSet?.hediffs == null)
            {
                return string.Empty;
            }

            return string.Join(", ", pawn.health.hediffSet.hediffs
                .Where(hediff => hediff != null && hediff.Visible && (hediff.IsCurrentlyLifeThreatening || hediff.Bleeding || hediff.PainOffset > 0f || hediff.SummaryHealthPercentImpact < -0.05f))
                .OrderByDescending(hediff => hediff.IsCurrentlyLifeThreatening ? 100f : hediff.BleedRate + hediff.PainOffset - hediff.SummaryHealthPercentImpact)
                .Select(hediff => CleanLine(hediff.Label))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(2)
                .ToArray());
        }

        private static string BuildLowCapacitiesSummary(Pawn pawn)
        {
            if (pawn.health?.capacities == null)
            {
                return string.Empty;
            }

            PawnCapacityDef[] relevantCapacities =
            {
                PawnCapacityDefOf.Consciousness,
                PawnCapacityDefOf.Talking,
                PawnCapacityDefOf.Hearing,
                PawnCapacityDefOf.Sight,
                PawnCapacityDefOf.Manipulation,
                PawnCapacityDefOf.Moving
            };

            List<string> parts = new List<string>();
            for (int i = 0; i < relevantCapacities.Length; i++)
            {
                PawnCapacityDef capacity = relevantCapacities[i];
                if (capacity == null)
                {
                    continue;
                }

                float level = pawn.health.capacities.GetLevel(capacity);
                if (level < DiaryTuning.Current.lowCapacityThreshold)
                {
                    parts.Add(CleanLine(capacity.GetLabelFor(pawn)) + "=" + Mathf.RoundToInt(level * 100f) + "%");
                }
            }

            return string.Join(", ", parts.ToArray());
        }

        private static string BuildTopThoughtsSummary(Pawn pawn)
        {
            if (pawn.needs?.mood?.thoughts == null)
            {
                return string.Empty;
            }

            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            return string.Join(", ", thoughts
                .Where(thought => thought != null && thought.VisibleInNeedsTab && Mathf.Abs(thought.MoodOffset()) >= 1f)
                .OrderByDescending(thought => Mathf.Abs(thought.MoodOffset()))
                .Select(thought => CleanLine(thought.LabelCap) + " " + FormatSignedNumber(Mathf.RoundToInt(thought.MoodOffset())))
                .Take(3)
                .ToArray());
        }

        private static string BuildNearbyThingsSummary(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return string.Empty;
            }

            List<string> labels = new List<string>();

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, DiaryTuning.Current.nearbyRadius, true))
            {
                if (thing == null || thing == pawn || thing is Pawn || !ShouldIncludeNearbyThing(thing))
                {
                    continue;
                }

                string label = NearbyThingLabel(thing);
                if (string.IsNullOrWhiteSpace(label) || labels.Contains(label))
                {
                    continue;
                }

                labels.Add(label);
                if (labels.Count >= DiaryTuning.Current.maxNearbyThings)
                {
                    break;
                }
            }

            return string.Join(", ", labels.ToArray());
        }

        private static bool ShouldIncludeNearbyThing(Thing thing)
        {
            if (thing is Corpse || thing is Fire)
            {
                return true;
            }

            ThingDef def = thing.def;
            if (def == null)
            {
                return false;
            }

            return def.category == ThingCategory.Building
                || def.category == ThingCategory.Item
                || def.category == ThingCategory.Plant;
        }

        private static string NearbyThingLabel(Thing thing)
        {
            if (thing is Fire)
            {
                return "fire";
            }

            Corpse corpse = thing as Corpse;
            if (corpse?.InnerPawn != null)
            {
                return "corpse of " + CleanLine(corpse.InnerPawn.LabelShortCap);
            }

            return CleanLine(thing.LabelNoCount);
        }

        public static string CleanLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Replace("\r", " ").Replace("\n", " ");
            cleaned = Regex.Replace(cleaned, "<.*?>", string.Empty);
            return cleaned.Trim();
        }

        private static string FormatOpinion(int opinion)
        {
            string sign = opinion > 0 ? "+" : string.Empty;
            return sign + opinion + " (" + OpinionBucket(opinion) + ")";
        }

        private static string FormatSignedNumber(int value)
        {
            return (value > 0 ? "+" : string.Empty) + value;
        }

        private static string BeautyBucket(float beauty)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (beauty >= t.beautyBeautiful)
            {
                return "beautiful";
            }

            if (beauty >= t.beautyPleasant)
            {
                return "pleasant";
            }

            if (beauty > -t.beautyPleasant)
            {
                return "neutral";
            }

            if (beauty > t.beautyUgly)
            {
                return "ugly";
            }

            return "hideous";
        }

        private static string MoodBucket(int moodPercent)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (moodPercent >= t.moodHappy)
            {
                return "happy";
            }

            if (moodPercent >= t.moodStable)
            {
                return "stable";
            }

            if (moodPercent >= t.moodStressed)
            {
                return "stressed";
            }

            return "miserable";
        }

        private static string PainBucket(float pain)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (pain >= t.painSevere)
            {
                return "severe";
            }

            if (pain >= t.painModerate)
            {
                return "moderate";
            }

            return "minor";
        }

        private static string OpinionBucket(int opinion)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (opinion >= t.opinionDevoted)
            {
                return "devoted";
            }

            if (opinion >= t.opinionFriendly)
            {
                return "friendly";
            }

            if (opinion > t.opinionNeutralAbove)
            {
                return "neutral";
            }

            if (opinion > t.opinionStrainedAbove)
            {
                return "strained";
            }

            return "hostile";
        }
    }
}
