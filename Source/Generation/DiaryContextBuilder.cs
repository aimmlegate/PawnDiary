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
            parts.Add("PawnDiary.Ctx.Opinion".Translate(FormatOpinion(opinion)));

            string reasons = BuildSocialThoughtsSummary(povPawn, otherPawn);
            if (!string.IsNullOrWhiteSpace(reasons))
            {
                parts.Add("PawnDiary.Ctx.Because".Translate(reasons));
            }

            string latest = LatestDiaryLineAbout(povPawn.GetUniqueLoadID(), otherPawn.GetUniqueLoadID(), events);
            if (!string.IsNullOrWhiteSpace(latest))
            {
                parts.Add("PawnDiary.Ctx.LastWrote".Translate(latest));
            }

            return parts.Count == 0 ? "none" : string.Join("; ", parts.ToArray());
        }

        // The standing social memories that drive the opinion, aggregated by kind and signed
        // (e.g. "shared kind words +12, slept in a poor bedroom -4"). This intentionally reads
        // stored memories only: RimWorld's broader GetSocialThoughts helper also recalculates
        // situational social thoughts, and some vanilla thought workers assume a humanlike "other"
        // pawn. Animal interactions such as Nuzzle can otherwise produce harmless-but-loud log
        // errors while the diary is only trying to build prompt context.
        private static string BuildSocialThoughtsSummary(Pawn povPawn, Pawn otherPawn)
        {
            List<Thought_Memory> memories = povPawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return string.Empty;
            }

            Dictionary<string, float> byLabel = new Dictionary<string, float>();
            for (int i = 0; i < memories.Count; i++)
            {
                Thought_Memory memory = memories[i];
                ISocialThought socialThought = memory as ISocialThought;
                if (memory == null || socialThought == null || memory.otherPawn != otherPawn)
                {
                    continue;
                }

                string label = CleanLine(memory.LabelCap);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                float offset = socialThought.OpinionOffset();
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

            parts.Add((outdoors ? "PawnDiary.Ctx.Outdoors" : "PawnDiary.Ctx.Indoors").Translate());

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
                parts.Add("PawnDiary.Ctx.Cold".Translate(temperature.ToString("0")));
            }
            else if (temperature >= DiaryTuning.Current.hotAboveC)
            {
                parts.Add("PawnDiary.Ctx.Hot".Translate(temperature.ToString("0")));
            }

            // Beauty only when it's notably nice or grim.
            float beauty = BeautyUtility.AverageBeautyPerceptible(pawn.Position, pawn.Map);
            if (beauty >= DiaryTuning.Current.beautyPleasant || beauty <= -DiaryTuning.Current.beautyPleasant)
            {
                parts.Add("PawnDiary.Ctx.Surroundings".Translate(BeautyBucket(beauty)));
            }

            string nearby = BuildNearbyThingsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(nearby))
            {
                parts.Add("PawnDiary.Ctx.Near".Translate(nearby));
            }

            string jobReport = CleanLine(pawn.GetJobReport());
            if (!string.IsNullOrWhiteSpace(jobReport))
            {
                parts.Add("PawnDiary.Ctx.Doing".Translate(jobReport));
            }
            else if (pawn.CurJobDef != null)
            {
                parts.Add("PawnDiary.Ctx.Doing".Translate(CleanLine(pawn.CurJobDef.LabelCap.Resolve())));
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
                parts.Add("PawnDiary.Ctx.Downed".Translate());
            }

            if (pawn.health.InPainShock)
            {
                parts.Add("PawnDiary.Ctx.PainShock".Translate());
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
                return "PawnDiary.Ctx.Fire".Translate();
            }

            Corpse corpse = thing as Corpse;
            if (corpse?.InnerPawn != null)
            {
                return "PawnDiary.Ctx.Corpse".Translate(CleanLine(corpse.InnerPawn.LabelShortCap));
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
                return "PawnDiary.Bucket.Beauty.Beautiful".Translate();
            }

            if (beauty >= t.beautyPleasant)
            {
                return "PawnDiary.Bucket.Beauty.Pleasant".Translate();
            }

            if (beauty > -t.beautyPleasant)
            {
                return "PawnDiary.Bucket.Beauty.Neutral".Translate();
            }

            if (beauty > t.beautyUgly)
            {
                return "PawnDiary.Bucket.Beauty.Ugly".Translate();
            }

            return "PawnDiary.Bucket.Beauty.Hideous".Translate();
        }

        private static string MoodBucket(int moodPercent)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (moodPercent >= t.moodHappy)
            {
                return "PawnDiary.Bucket.Mood.Happy".Translate();
            }

            if (moodPercent >= t.moodStable)
            {
                return "PawnDiary.Bucket.Mood.Stable".Translate();
            }

            if (moodPercent >= t.moodStressed)
            {
                return "PawnDiary.Bucket.Mood.Stressed".Translate();
            }

            return "PawnDiary.Bucket.Mood.Miserable".Translate();
        }

        private static string PainBucket(float pain)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (pain >= t.painSevere)
            {
                return "PawnDiary.Bucket.Pain.Severe".Translate();
            }

            if (pain >= t.painModerate)
            {
                return "PawnDiary.Bucket.Pain.Moderate".Translate();
            }

            return "PawnDiary.Bucket.Pain.Minor".Translate();
        }

        private static string OpinionBucket(int opinion)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (opinion >= t.opinionDevoted)
            {
                return "PawnDiary.Bucket.Opinion.Devoted".Translate();
            }

            if (opinion >= t.opinionFriendly)
            {
                return "PawnDiary.Bucket.Opinion.Friendly".Translate();
            }

            if (opinion > t.opinionNeutralAbove)
            {
                return "PawnDiary.Bucket.Opinion.Neutral".Translate();
            }

            if (opinion > t.opinionStrainedAbove)
            {
                return "PawnDiary.Bucket.Opinion.Strained".Translate();
            }

            return "PawnDiary.Bucket.Opinion.Hostile".Translate();
        }
    }
}
