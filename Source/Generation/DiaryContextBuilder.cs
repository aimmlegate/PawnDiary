// Builds the compact context strings sent to the LLM (pawn profile, surroundings,
// relationship/continuity, opinions) plus the text/number formatting helpers and the
// mood-impact determination for GameCondition entries. Static helpers, no state.
// Split out of DiaryGameComponent.cs. See DOCUMENTATION.md.
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

        // Extracts the first sentence of the pawn's most recent diary entry.
        // Used as "my last opener" so the model avoids repeating the same opening pattern.
        public static string LatestDiaryOpener(string pawnId, IReadOnlyList<DiaryEvent> events)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || events == null)
            {
                return string.Empty;
            }

            for (int i = events.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = events[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                string role = diaryEvent.RoleForPawn(pawnId);
                if (string.IsNullOrWhiteSpace(role))
                {
                    continue;
                }

                string text = diaryEvent.DisplayTextForRole(role);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string opener = ExtractFirstSentence(text);
                if (!string.IsNullOrWhiteSpace(opener))
                {
                    return opener;
                }
            }

            return string.Empty;
        }

        // Extracts the first sentence from a text block.
        // Looks for sentence-ending punctuation (.!?) followed by a space or end of string.
        private static string ExtractFirstSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string cleaned = CleanLine(text);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            // Look for the first sentence-ending punctuation
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (c == '.' || c == '!' || c == '?')
                {
                    // Include the punctuation in the opener
                    string sentence = cleaned.Substring(0, i + 1);
                    return sentence.Trim();
                }
            }

            // No sentence-ending punctuation found; return the whole text (truncated)
            int max = DiaryTuning.Current.diaryLineMaxChars;
            return cleaned.Length <= max ? cleaned : cleaned.Substring(0, max) + "...";
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
                parts.Add("thoughts=" + thoughts);
            }

            return string.Join("; ", parts.ToArray());
        }

        // Returns a random passion from the pawn's skills, weighted so major passions
        // are more likely to be chosen. Used for important events only.
        public static string RandomBurningPassion(Pawn pawn)
        {
            if (pawn?.skills?.skills == null)
            {
                return string.Empty;
            }

            List<SkillRecord> passions = new List<SkillRecord>();
            for (int i = 0; i < pawn.skills.skills.Count; i++)
            {
                SkillRecord skill = pawn.skills.skills[i];
                if (skill != null && (skill.passion == Passion.Major || skill.passion == Passion.Minor))
                {
                    passions.Add(skill);
                }
            }

            if (passions.Count == 0)
            {
                return string.Empty;
            }

            // Weighted selection: major = 3x weight, minor = 1x weight
            float totalWeight = 0f;
            for (int i = 0; i < passions.Count; i++)
            {
                totalWeight += passions[i].passion == Passion.Major ? 3f : 1f;
            }

            float roll = UnityEngine.Random.value * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < passions.Count; i++)
            {
                cumulative += passions[i].passion == Passion.Major ? 3f : 1f;
                if (roll <= cumulative)
                {
                    string label = CleanLine(passions[i].def.LabelCap);
                    return passions[i].passion == Passion.Major
                        ? label + " (burning)"
                        : label;
                }
            }

            // Fallback
            SkillRecord last = passions[passions.Count - 1];
            string lastLabel = CleanLine(last.def.LabelCap);
            return last.passion == Passion.Major
                ? lastLabel + " (burning)"
                : lastLabel;
        }

        // Returns the label of the pawn's currently equipped weapon, or empty if unarmed.
        // For mods with multiple weapons, picks a random one from the inventory.
        public static string EquippedWeapon(Pawn pawn)
        {
            if (pawn?.equipment == null)
            {
                return string.Empty;
            }

            // Check for primary equipped weapon first
            ThingWithComps primary = pawn.equipment.Primary;
            if (primary != null)
            {
                return CleanLine(primary.LabelNoCount);
            }

            // If no primary, check inventory for weapons (for mods that allow multiple)
            if (pawn.inventory?.innerContainer != null)
            {
                List<Thing> weapons = new List<Thing>();
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    Thing thing = pawn.inventory.innerContainer[i];
                    if (thing != null && thing is ThingWithComps twc && twc.def.IsWeapon)
                    {
                        weapons.Add(twc);
                    }
                }

                if (weapons.Count > 0)
                {
                    Thing chosen = weapons[UnityEngine.Random.Range(0, weapons.Count)];
                    return CleanLine(chosen.LabelNoCount);
                }
            }

            return string.Empty;
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
                PawnCapacityDefOf.Moving,
                PawnCapacityDefOf.Talking,
                PawnCapacityDefOf.Sight,
                PawnCapacityDefOf.Hearing
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
                    string keyword = CapacityKeyword(capacity, level);
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        parts.Add(keyword);
                    }
                }
            }

            return string.Join(", ", parts.ToArray());
        }

        // Converts a capacity level to a localized keyword label.
        private static string CapacityKeyword(PawnCapacityDef capacity, float level)
        {
            if (capacity == PawnCapacityDefOf.Moving)
            {
                return "PawnDiary.Capacity.Moving".Translate();
            }

            if (capacity == PawnCapacityDefOf.Talking)
            {
                return "PawnDiary.Capacity.Talking".Translate();
            }

            if (capacity == PawnCapacityDefOf.Sight)
            {
                return "PawnDiary.Capacity.Sight".Translate();
            }

            if (capacity == PawnCapacityDefOf.Hearing)
            {
                return "PawnDiary.Capacity.Hearing".Translate();
            }

            return string.Empty;
        }

        private static string BuildTopThoughtsSummary(Pawn pawn)
        {
            if (pawn.needs?.mood?.thoughts == null)
            {
                return string.Empty;
            }

            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            List<Thought> visible = thoughts
                .Where(t => t != null && t.VisibleInNeedsTab && Mathf.Abs(t.MoodOffset()) >= 1f)
                .ToList();

            List<Thought> positive = visible.Where(t => t.MoodOffset() > 0f).ToList();
            List<Thought> negative = visible.Where(t => t.MoodOffset() < 0f).ToList();

            string posStr = PickWeightedThought(positive);
            string negStr = PickWeightedThought(negative);

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(posStr))
            {
                parts.Add(posStr);
            }

            if (!string.IsNullOrWhiteSpace(negStr))
            {
                parts.Add(negStr);
            }

            return string.Join(", ", parts.ToArray());
        }

        // Picks one thought from the list using weighted random: weight = |moodOffset|,
        // so thoughts with stronger effect are more likely to be chosen.
        private static string PickWeightedThought(List<Thought> thoughts)
        {
            if (thoughts == null || thoughts.Count == 0)
            {
                return string.Empty;
            }

            float totalWeight = 0f;
            for (int i = 0; i < thoughts.Count; i++)
            {
                totalWeight += Mathf.Abs(thoughts[i].MoodOffset());
            }

            if (totalWeight <= 0f)
            {
                return string.Empty;
            }

            float roll = UnityEngine.Random.value * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < thoughts.Count; i++)
            {
                cumulative += Mathf.Abs(thoughts[i].MoodOffset());
                if (roll <= cumulative)
                {
                    return CleanLine(thoughts[i].LabelCap) + " " + FormatSignedNumber(Mathf.RoundToInt(thoughts[i].MoodOffset()));
                }
            }

            // Fallback: return the last one (shouldn't reach here normally)
            Thought last = thoughts[thoughts.Count - 1];
            return CleanLine(last.LabelCap) + " " + FormatSignedNumber(Mathf.RoundToInt(last.MoodOffset()));
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

        // Builds a short atmospheric phrase combining mood + relationship context.
        // This gives small models an emotional anchor without requiring complex inference.
        // Example outputs: "tense hostility", "fragile warmth", "bitter resentment"
        public static string BuildAtmosphere(Pawn povPawn, Pawn otherPawn, string instruction)
        {
            if (povPawn == null)
            {
                return string.Empty;
            }

            int moodPercent = povPawn.needs?.mood != null
                ? Mathf.RoundToInt(povPawn.needs.mood.CurLevelPercentage * 100f)
                : 50;

            string moodWord = MoodAtmosphereWord(moodPercent);
            string relationWord = string.Empty;

            if (otherPawn != null && povPawn.relations != null)
            {
                int opinion = povPawn.relations.OpinionOf(otherPawn);
                relationWord = OpinionAtmosphereWord(opinion);
            }

            // Combine into a short phrase
            if (!string.IsNullOrWhiteSpace(moodWord) && !string.IsNullOrWhiteSpace(relationWord))
            {
                return moodWord + " " + relationWord;
            }

            if (!string.IsNullOrWhiteSpace(relationWord))
            {
                return relationWord;
            }

            return moodWord;
        }

        // Maps mood percentage to an evocative atmosphere word
        private static string MoodAtmosphereWord(int moodPercent)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (moodPercent >= t.moodHappy)
            {
                return "PawnDiary.Atmosphere.Mood.Bright".Translate();
            }

            if (moodPercent >= t.moodStable)
            {
                return string.Empty; // neutral moods add no atmosphere
            }

            if (moodPercent >= t.moodStressed)
            {
                return "PawnDiary.Atmosphere.Mood.Tense".Translate();
            }

            return "PawnDiary.Atmosphere.Mood.Bleak".Translate();
        }

        // Maps opinion to an evocative relationship atmosphere word
        private static string OpinionAtmosphereWord(int opinion)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (opinion >= t.opinionDevoted)
            {
                return "PawnDiary.Atmosphere.Opinion.Devotion".Translate();
            }

            if (opinion >= t.opinionFriendly)
            {
                return "PawnDiary.Atmosphere.Opinion.Warmth".Translate();
            }

            if (opinion > t.opinionNeutralAbove)
            {
                return string.Empty; // neutral opinions add no atmosphere
            }

            if (opinion > t.opinionStrainedAbove)
            {
                return "PawnDiary.Atmosphere.Opinion.Friction".Translate();
            }

            return "PawnDiary.Atmosphere.Opinion.Hostility".Translate();
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

        // Determines whether a GameCondition has a positive, negative, or neutral mood impact
        // on a specific pawn. Uses the condition's thought definitions for direction, then checks
        // pawn-specific factors (e.g. PsychicDrone targets one gender; PsychicSuppressor only
        // hurts the suppressed gender). Returns "positive", "negative", or "neutral" for use in
        // the gameContext mood_impact field and the event text.
        // conditionThoughtOffset is the pawn-independent offset from the condition's thoughts,
        // computed once by the caller (GetMoodOffsetFromConditionThoughts scans the whole ThoughtDef
        // database, so we pass it in rather than repeat the scan for every affected colonist).
        public static string DetermineMoodImpact(GameCondition condition, Pawn pawn, float conditionThoughtOffset)
        {
            if (condition == null || condition.def == null || pawn == null)
            {
                return "neutral";
            }

            GameConditionDef def = condition.def;
            string defName = def.defName;

            // First, check if this pawn is unaffected by the condition due to gender targeting.
            // GameCondition_PsychicEmanation (psychic drone / soothe) targets one gender.
            if (condition is GameCondition_PsychicEmanation emanationCondition)
            {
                if (emanationCondition.gender != pawn.gender)
                {
                    return "neutral";
                }
            }

            // GameCondition_PsychicSuppression also targets one gender.
            if (condition is GameCondition_PsychicSuppression suppressionCondition)
            {
                if (suppressionCondition.gender != pawn.gender)
                {
                    return "neutral";
                }
            }

            // Combine the pawn-independent condition offset (precomputed by the caller) with this
            // pawn's own active thoughts from the condition; the larger magnitude wins. RimWorld's
            // relationship is inverted: GameConditionDef does not hold thought references; instead
            // ThoughtDef has a `gameCondition` field that references the GameConditionDef it belongs to.
            float bestOffset = conditionThoughtOffset;
            float pawnOffset = GetMoodOffsetFromPawnThoughts(condition, pawn);
            if (Mathf.Abs(pawnOffset) > Mathf.Abs(bestOffset))
            {
                bestOffset = pawnOffset;
            }

            // If we found a meaningful mood offset, return the direction.
            if (bestOffset > 0.5f)
            {
                return "positive";
            }

            if (bestOffset < -0.5f)
            {
                return "negative";
            }

            // If no thought offsets found (some conditions apply thoughts programmatically),
            // fall back to name-based heuristics for known condition families.
            if (IsKnownPositiveCondition(defName))
            {
                return "positive";
            }

            if (IsKnownNegativeCondition(defName))
            {
                return "negative";
            }

            // If we can't determine the impact, default to neutral. The LLM will use the
            // condition label and gameContext to figure out how the pawn feels.
            return "neutral";
        }

        // Scans DefDatabase<ThoughtDef> for any thoughts that reference the given
        // GameConditionDef via their `gameCondition` field, then sums the baseMoodEffect
        // across all stages. RimWorld's relationship is inverted: ThoughtDef points to
        // GameConditionDef, not vice versa. Returns 0 if no matching thoughts are found.
        public static float GetMoodOffsetFromConditionThoughts(GameConditionDef conditionDef)
        {
            if (conditionDef == null)
            {
                return 0f;
            }

            float totalOffset = 0f;
            List<ThoughtDef> allThoughtDefs = DefDatabase<ThoughtDef>.AllDefsListForReading;
            for (int i = 0; i < allThoughtDefs.Count; i++)
            {
                ThoughtDef td = allThoughtDefs[i];
                if (td == null || td.gameCondition != conditionDef)
                {
                    continue;
                }

                if (td.stages == null)
                {
                    continue;
                }

                for (int j = 0; j < td.stages.Count; j++)
                {
                    if (td.stages[j] != null)
                    {
                        totalOffset += td.stages[j].baseMoodEffect;
                    }
                }
            }

            return totalOffset;
        }

        // Checks the pawn's current mood thoughts for any that come from the given GameCondition,
        // matching by the thought's `gameCondition` field pointing to our condition def.
        // Returns the total mood offset from matching thoughts, or 0 if none are found.
        private static float GetMoodOffsetFromPawnThoughts(GameCondition condition, Pawn pawn)
        {
            if (condition == null || condition.def == null || pawn?.needs?.mood?.thoughts == null)
            {
                return 0f;
            }

            float totalOffset = 0f;
            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            for (int i = 0; i < thoughts.Count; i++)
            {
                Thought thought = thoughts[i];
                if (thought == null || thought.def == null)
                {
                    continue;
                }

                // Match thoughts whose def references this condition (DefDatabase link).
                if (thought.def.gameCondition == condition.def)
                {
                    totalOffset += thought.MoodOffset();
                }
            }

            return totalOffset;
        }

        // Known GameConditionDefs that are always positive for every colonist.
        private static bool IsKnownPositiveCondition(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return false;
            }

            return string.Equals(defName, "Aurora", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "Party", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "PsychicSoothe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "PsychicEmanation", StringComparison.OrdinalIgnoreCase);
        }

        // Known GameConditionDefs that are always negative for affected colonists.
        // Excludes condition causers and gender-targeted effects (those vary per pawn).
        private static bool IsKnownNegativeCondition(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return false;
            }

            return string.Equals(defName, "Eclipse", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "ToxicFallout", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "VolcanicWinter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "Flashstorm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, "GrayPall", StringComparison.OrdinalIgnoreCase);
        }
    }
}
