// Builds the compact context strings sent to the LLM (pawn profile, surroundings,
// relationship/continuity, opinions) plus the text/bucket formatting helpers and the
// mood-impact determination for GameCondition entries. Static helpers, no state.
// Split out of DiaryGameComponent.cs. See DOCUMENTATION.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public static class DiaryContextBuilder
    {
        private const int MaxActiveMapConditions = 3;
        private const int MaxThreatLetterScanBack = 30;
        private const int RecentThreatTimeoutTicks = 7500;
        private static readonly Regex RichTextTagRegex = new Regex("<.*?>");

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

        // The standing social memories that drive the opinion, aggregated by kind and direction
        // (e.g. "shared kind words (positive), insulted (strong negative)"). This intentionally reads
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
                .Select(pair => pair.Key + " (" + EffectBucket(pair.Value) + ")")
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
        // Used by LatestDiaryOpener so the prompt can ask the model not to repeat the same
        // opening line style too often.
        public static string ExtractFirstSentence(string text)
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

        // Cached WeatherDef.defName -> mention chance lookup, rebuilt only when the tuning Def
        // instance changes. The chances themselves live in DiaryTuningDef (XML-tunable).
        private static DiaryTuningDef weatherChanceTuning;
        private static Dictionary<string, float> weatherChanceLookup;

        // Rolls the per-weather chance (from DiaryTuningDef.weatherMentionChances) to decide whether
        // this outdoor entry mentions the weather. Clear (mapped to 0) never passes; the harshest
        // weather almost always does. Mild weather was dominating diary openings, so it is weighted low.
        private static bool ShouldMentionWeather(WeatherDef weather)
        {
            return weather != null && Rand.Chance(WeatherMentionChanceFor(weather));
        }

        private static float WeatherMentionChanceFor(WeatherDef weather)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            if (weather.defName != null && WeatherChanceLookup(tuning).TryGetValue(weather.defName, out float chance))
            {
                return chance;
            }

            // Unknown weather (DLC/modded not listed): lean on favorability so severity still drives it.
            switch (weather.favorability)
            {
                case Favorability.VeryBad: return tuning.weatherChanceVeryBad;
                case Favorability.Bad: return tuning.weatherChanceBad;
                case Favorability.Neutral: return tuning.weatherChanceNeutral;
                default: return tuning.weatherChanceDefault; // Good / OuterSpace
            }
        }

        private static Dictionary<string, float> WeatherChanceLookup(DiaryTuningDef tuning)
        {
            if (ReferenceEquals(weatherChanceTuning, tuning) && weatherChanceLookup != null)
            {
                return weatherChanceLookup;
            }

            Dictionary<string, float> lookup = new Dictionary<string, float>();
            List<WeatherMentionRule> rules = tuning?.weatherMentionChances;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    WeatherMentionRule rule = rules[i];
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.weather))
                    {
                        lookup[rule.weather] = rule.chance;
                    }
                }
            }

            weatherChanceTuning = tuning;
            weatherChanceLookup = lookup;
            return lookup;
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

            // Weather and biome only matter when the pawn is exposed to them, and weather is added
            // only when a severity-weighted roll passes (see ShouldMentionWeather): clear skies were
            // dominating diary openings, so mild weather is rarely noted and dramatic weather almost
            // always is.
            if (outdoors)
            {
                WeatherDef weather = pawn.Map.weatherManager?.CurWeatherPerceived;
                if (ShouldMentionWeather(weather))
                {
                    parts.Add(CleanLine(weather.label));
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
                parts.Add("PawnDiary.Ctx.Cold".Translate());
            }
            else if (temperature >= DiaryTuning.Current.hotAboveC)
            {
                parts.Add("PawnDiary.Ctx.Hot".Translate());
            }

            // Beauty only when it's notably nice or grim.
            float beauty = BeautyUtility.AverageBeautyPerceptible(pawn.Position, pawn.Map);
            if (beauty >= DiaryTuning.Current.beautyPleasant || beauty <= -DiaryTuning.Current.beautyPleasant)
            {
                parts.Add("PawnDiary.Ctx.Surroundings".Translate(BeautyBucket(beauty)));
            }

            string activeConditions = BuildActiveMapConditionsSummary(pawn.Map);
            if (!string.IsNullOrWhiteSpace(activeConditions))
            {
                parts.Add(DiaryContextReactions.Format(DiaryContextReactions.ActiveMapConditions, activeConditions));
            }

            string recentThreat = BuildRecentThreatSummary(pawn.Map);
            if (!string.IsNullOrWhiteSpace(recentThreat))
            {
                parts.Add(DiaryContextReactions.Format(DiaryContextReactions.RecentThreatLetter, recentThreat));
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

        // Returns visible, map-wide conditions that are active right now. These are prompt context,
        // not diary events: they color the entry without creating duplicate records for conditions
        // already handled by the GameCondition start patch.
        private static string BuildActiveMapConditionsSummary(Map map)
        {
            DiaryContextReactionDef policy = DiaryContextReactions.ForKey(DiaryContextReactions.ActiveMapConditions);
            if (!policy.enabled)
            {
                return string.Empty;
            }

            List<GameCondition> conditions = map?.gameConditionManager?.ActiveConditions;
            if (conditions == null || conditions.Count == 0)
            {
                return string.Empty;
            }

            List<string> labels = new List<string>();
            HashSet<string> seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int maxConditions = Math.Max(0, DiaryContextReactions.MaxItems(
                DiaryContextReactions.ActiveMapConditions,
                MaxActiveMapConditions));
            for (int i = 0; i < conditions.Count && labels.Count < maxConditions; i++)
            {
                GameCondition condition = conditions[i];
                if (condition?.def == null || (policy.displayOnUiOnly && !condition.def.displayOnUI))
                {
                    continue;
                }

                string label = CleanLine(condition.Label);
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = CleanLine(condition.def.LabelCap.Resolve());
                }

                if (!string.IsNullOrWhiteSpace(label) && seenLabels.Add(label))
                {
                    labels.Add(label);
                }
            }

            return string.Join(", ", labels.ToArray());
        }

        // Reads RimWorld's letter archive for the newest fresh threat letter while the home map is
        // still in danger. This keeps raid/siege/manhunter context near the event but avoids stale
        // archive messages becoming background flavor hours later.
        private static string BuildRecentThreatSummary(Map map)
        {
            DiaryContextReactionDef policy = DiaryContextReactions.ForKey(DiaryContextReactions.RecentThreatLetter);
            if (!policy.enabled || map == null || Find.Archive == null)
            {
                return string.Empty;
            }

            if (policy.requireHomeMap && !map.IsPlayerHome)
            {
                return string.Empty;
            }

            if (policy.requireDanger
                && (map.dangerWatcher == null || map.dangerWatcher.DangerRating == StoryDanger.None))
            {
                return string.Empty;
            }

            List<IArchivable> archivables = Find.Archive.ArchivablesListForReading;
            if (archivables == null || archivables.Count == 0)
            {
                return string.Empty;
            }

            int nowTicks = Find.TickManager != null ? Find.TickManager.TicksGame : -1;
            int scanBack = Math.Max(0, DiaryContextReactions.ScanBack(
                DiaryContextReactions.RecentThreatLetter,
                MaxThreatLetterScanBack));
            int timeoutTicks = Math.Max(0, DiaryContextReactions.TimeoutTicks(
                DiaryContextReactions.RecentThreatLetter,
                RecentThreatTimeoutTicks));
            int scanned = 0;
            for (int i = archivables.Count - 1; i >= 0 && scanned < scanBack; i--, scanned++)
            {
                IArchivable archivable = archivables[i];
                Letter letter = archivable as Letter;
                if (letter?.def == null)
                {
                    continue;
                }

                if (!DiaryContextReactions.LetterDefAllowed(policy, letter.def.defName))
                {
                    continue;
                }

                if (ThreatLetterIsStale(archivable, nowTicks, timeoutTicks))
                {
                    return string.Empty;
                }

                string label = SafeArchivedLabel(archivable);
                return label;
            }

            return string.Empty;
        }

        private static bool ThreatLetterIsStale(IArchivable archivable, int nowTicks, int timeoutTicks)
        {
            if (archivable == null || nowTicks < 0 || timeoutTicks <= 0)
            {
                return false;
            }

            try
            {
                int createdTicks = archivable.CreatedTicksGame;
                return createdTicks > 0 && nowTicks - createdTicks > timeoutTicks;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeArchivedLabel(IArchivable archivable)
        {
            if (archivable == null)
            {
                return string.Empty;
            }

            try
            {
                return CleanLine(archivable.ArchivedLabel);
            }
            catch
            {
                return string.Empty;
            }
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

            string age = AgeBucket(pawn);
            if (!string.IsNullOrWhiteSpace(age))
            {
                parts.Add("life_stage=" + age);
            }

            // DLC identity (Biotech xenotype / Royalty title / Ideology faith). Each accessor
            // returns empty without its DLC, so a no-DLC game simply omits these lines — see
            // DlcContext and AGENTS.md ("DLC-safety"). The labels are structured prompt schema
            // (like sex=/life_stage=), so they stay English per the localization carve-out.
            string xenotype = DlcContext.Xenotype(pawn);
            if (!string.IsNullOrWhiteSpace(xenotype))
            {
                parts.Add("xenotype=" + xenotype);
            }

            string royalTitle = DlcContext.RoyalTitle(pawn);
            if (!string.IsNullOrWhiteSpace(royalTitle))
            {
                parts.Add("title=" + royalTitle);
            }

            string faith = DlcContext.Ideoligion(pawn);
            if (!string.IsNullOrWhiteSpace(faith))
            {
                parts.Add("faith=" + faith);
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
            return MoodBucket(moodPercent);
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
                parts.Add("pain=" + PainBucket(pain));
            }

            float bleedRate = pawn.health.hediffSet?.BleedRateTotal ?? 0f;
            if (bleedRate > DiaryTuning.Current.bleedVisibleAbove)
            {
                parts.Add("bleeding=" + BleedingBucket(bleedRate));
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
                    return CleanLine(thoughts[i].LabelCap) + " (" + EffectBucket(thoughts[i].MoodOffset()) + ")";
                }
            }

            // Fallback: return the last one (shouldn't reach here normally)
            Thought last = thoughts[thoughts.Count - 1];
            return CleanLine(last.LabelCap) + " (" + EffectBucket(last.MoodOffset()) + ")";
        }

        private static string BuildNearbyThingsSummary(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return string.Empty;
            }

            List<Thing> candidates = new List<Thing>();
            HashSet<string> seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nearbyCandidateCap = Mathf.Max(1, DiaryTuning.Current.maxNearbyThings);

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, DiaryTuning.Current.nearbyRadius, true))
            {
                if (thing == null || thing == pawn || thing is Pawn || !ShouldIncludeNearbyThing(thing))
                {
                    continue;
                }

                string label = NearbyThingLabel(thing);
                if (string.IsNullOrWhiteSpace(label) || !seenLabels.Add(label))
                {
                    continue;
                }

                candidates.Add(thing);
                if (candidates.Count >= nearbyCandidateCap)
                {
                    break;
                }
            }

            if (candidates.Count == 0)
            {
                return string.Empty;
            }

            int maxNearbyToPick = Mathf.Min(2, candidates.Count);
            int minNearbyToPick = Mathf.Min(1, maxNearbyToPick);
            int nearbyToPick = maxNearbyToPick == minNearbyToPick
                ? maxNearbyToPick
                : UnityEngine.Random.Range(minNearbyToPick, maxNearbyToPick + 1);

            List<string> selectedLabels = PickWeightedNearbyThings(candidates, nearbyToPick);
            return string.Join(", ", selectedLabels.ToArray());
        }

        // Picks 1-2 nearby things with weighted random and no replacement, so high-value objects like
        // fire/corpse/buildings appear more often across repeated diary entries while still keeping
        // variety.
        private static List<string> PickWeightedNearbyThings(List<Thing> candidates, int maxCount)
        {
            if (candidates == null || candidates.Count == 0 || maxCount <= 0)
            {
                return new List<string>();
            }

            List<Thing> pool = new List<Thing>(candidates);
            List<string> selected = new List<string>(Mathf.Min(maxCount, pool.Count));
            int take = Mathf.Min(maxCount, pool.Count);

            for (int pick = 0; pick < take; pick++)
            {
                float totalWeight = 0f;
                for (int i = 0; i < pool.Count; i++)
                {
                    totalWeight += Mathf.Max(0.0001f, NearbyThingWeight(pool[i]));
                }

                if (totalWeight <= 0f)
                {
                    break;
                }

                float roll = UnityEngine.Random.value * totalWeight;
                float cumulative = 0f;
                int selectedIndex = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    cumulative += Mathf.Max(0.0001f, NearbyThingWeight(pool[i]));
                    if (roll <= cumulative)
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                string label = NearbyThingLabel(pool[selectedIndex]);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    selected.Add(label);
                }

                pool.RemoveAt(selectedIndex);
            }

            return selected;
        }

        // Fire and corpses are story-relevant more often; buildings usually matter for location/context;
        // items and plants are still useful but less likely than those anchors.
        private static float NearbyThingWeight(Thing thing)
        {
            if (thing is Fire)
            {
                return 4f;
            }

            if (thing is Corpse)
            {
                return 3.5f;
            }

            if (thing?.def == null)
            {
                return 1f;
            }

            if (thing.def.category == ThingCategory.Building)
            {
                return 2.2f;
            }

            if (thing.def.category == ThingCategory.Item)
            {
                return 1.2f;
            }

            if (thing.def.category == ThingCategory.Plant)
            {
                return 1.1f;
            }

            return 1f;
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
            cleaned = RichTextTagRegex.Replace(cleaned, string.Empty);
            return cleaned.Trim();
        }

        private static string FormatOpinion(int opinion)
        {
            return OpinionBucket(opinion);
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

        private static string BleedingBucket(float bleedRate)
        {
            if (bleedRate >= 2f)
            {
                return "PawnDiary.Bucket.Bleeding.Severe".Translate();
            }

            if (bleedRate >= 1f)
            {
                return "PawnDiary.Bucket.Bleeding.Moderate".Translate();
            }

            return "PawnDiary.Bucket.Bleeding.Minor".Translate();
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

        private static string AgeBucket(Pawn pawn)
        {
            if (pawn?.ageTracker == null)
            {
                return string.Empty;
            }

            int years = pawn.ageTracker.AgeBiologicalYears;
            if (years < 13)
            {
                return "PawnDiary.Bucket.Age.Child".Translate();
            }

            if (years < 20)
            {
                return "PawnDiary.Bucket.Age.Teen".Translate();
            }

            if (years < 45)
            {
                return "PawnDiary.Bucket.Age.Adult".Translate();
            }

            if (years < 65)
            {
                return "PawnDiary.Bucket.Age.OlderAdult".Translate();
            }

            return "PawnDiary.Bucket.Age.Elder".Translate();
        }

        private static string EffectBucket(float effect)
        {
            if (effect >= 8f)
            {
                return "PawnDiary.Bucket.Effect.StrongPositive".Translate();
            }

            if (effect > 0f)
            {
                return "PawnDiary.Bucket.Effect.Positive".Translate();
            }

            if (effect <= -8f)
            {
                return "PawnDiary.Bucket.Effect.StrongNegative".Translate();
            }

            return "PawnDiary.Bucket.Effect.Negative".Translate();
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
                return MoodImpact.Neutral;
            }

            GameConditionDef def = condition.def;
            string defName = def.defName;

            // First, check if this pawn is unaffected by the condition due to gender targeting.
            // GameCondition_PsychicEmanation (psychic drone / soothe) targets one gender.
            if (condition is GameCondition_PsychicEmanation emanationCondition)
            {
                if (emanationCondition.gender != pawn.gender)
                {
                    return MoodImpact.Neutral;
                }
            }

            // Royalty's psychic suppression also targets one gender. Avoid naming the DLC type; if a
            // live condition's defName says suppression, reflect the same gender field vanilla uses.
            if (defName != null
                && defName.IndexOf("PsychicSuppress", StringComparison.OrdinalIgnoreCase) >= 0
                && TryReadConditionGender(condition, out Gender suppressionGender))
            {
                if (suppressionGender != pawn.gender)
                {
                    return MoodImpact.Neutral;
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

            // If we found a meaningful mood offset, return the direction. (We can't use
            // MoodImpact.Classify here because a within-threshold offset must fall through to the
            // name-based heuristics below rather than short-circuit to neutral.)
            if (bestOffset > MoodImpact.MeaningfulThreshold)
            {
                return MoodImpact.Positive;
            }

            if (bestOffset < -MoodImpact.MeaningfulThreshold)
            {
                return MoodImpact.Negative;
            }

            // If no thought offsets found (some conditions apply thoughts programmatically),
            // fall back to name-based heuristics for known condition families.
            if (IsKnownPositiveCondition(defName))
            {
                return MoodImpact.Positive;
            }

            if (IsKnownNegativeCondition(defName))
            {
                return MoodImpact.Negative;
            }

            // If we can't determine the impact, default to neutral. The LLM will use the
            // condition label and gameContext to figure out how the pawn feels.
            return MoodImpact.Neutral;
        }

        private static bool TryReadConditionGender(GameCondition condition, out Gender gender)
        {
            gender = Gender.None;
            if (condition == null)
            {
                return false;
            }

            FieldInfo field = condition.GetType().GetField("gender", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Gender))
            {
                return false;
            }

            gender = (Gender)field.GetValue(condition);
            return true;
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

        // Known GameConditionDefs that are always positive for every colonist. The list is XML-tuned
        // (DiaryTuningDef.positiveMoodConditionDefNames); entries are plain strings so a DLC-only
        // condition simply never appears without its DLC. See AGENTS.md ("DLC-safety").
        private static bool IsKnownPositiveCondition(string defName)
        {
            return DefNameListContains(DiaryTuning.Current.positiveMoodConditionDefNames, defName);
        }

        // Known GameConditionDefs that are always negative for affected colonists. Excludes condition
        // causers and gender-targeted effects (those vary per pawn). XML-tuned via
        // DiaryTuningDef.negativeMoodConditionDefNames.
        private static bool IsKnownNegativeCondition(string defName)
        {
            return DefNameListContains(DiaryTuning.Current.negativeMoodConditionDefNames, defName);
        }

        // Case-insensitive exact defName membership used by the mood-condition fallbacks above.
        // Null/empty defName never matches; a null/empty list never matches.
        private static bool DefNameListContains(List<string> defNames, string defName)
        {
            if (string.IsNullOrEmpty(defName) || defNames == null)
            {
                return false;
            }

            for (int i = 0; i < defNames.Count; i++)
            {
                if (string.Equals(defNames[i], defName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
