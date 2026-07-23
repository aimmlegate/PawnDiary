// Builds the compact context strings sent to the LLM (pawn profile, surroundings,
// relationship/continuity, opinions). This file now holds only the IMPURE collectors that read live
// Pawn/Map/Archive state; the pure one-line text cleaner (DiaryLineCleaner), the localized bucket
// formatters (DiaryBuckets), and the GameCondition mood-impact policy (MoodImpactClassifier) were
// split out so each concern has its own home and the pure pieces can be tested without the game.
// Static helpers, no state. Split out of DiaryGameComponent.cs. See repowiki/README.md.
using System;
using System.Collections.Generic;
using System.Linq;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal static class DiaryContextBuilder
    {
        private const int MaxActiveMapConditions = 3;
        private const int MaxThreatLetterScanBack = 30;
        private const int RecentThreatTimeoutTicks = 7500;

        public static string BuildGameContextSummary(InteractionDef interactionDef, string interactionLabel)
        {
            if (interactionDef == null)
            {
                return "unknown";
            }

            List<string> parts = new List<string>
            {
                "def=" + interactionDef.defName,
                "label=" + ExternalText(interactionLabel)
            };

            string worker = interactionDef.Worker?.GetType().Name;
            if (!string.IsNullOrWhiteSpace(worker))
            {
                parts.Add("worker=" + worker);
            }

            if (interactionDef.initiatorThought != null)
            {
                parts.Add("initiatorThought=" + ExternalText(interactionDef.initiatorThought.LabelCap.Resolve()));
            }

            if (interactionDef.recipientThought != null)
            {
                parts.Add("recipientThought=" + ExternalText(interactionDef.recipientThought.LabelCap.Resolve()));
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
                string relationLabel = ExternalText(relation.GetGenderSpecificLabelCap(otherPawn));
                if (!string.IsNullOrWhiteSpace(relationLabel))
                {
                    parts.Add(relationLabel);
                }
            }

            // OpinionOf walks live social-thought lists and can throw while another game/mod path is
            // changing them. Reuse the component's fail-soft reader so one bad continuity snapshot
            // contributes neutral opinion instead of aborting the entire interaction-batch tick.
            int opinion;
            DiaryGameComponent.TryReadOpinion(povPawn, otherPawn, out opinion);
            parts.Add("PawnDiary.Ctx.Opinion".Translate(DiaryBuckets.FormatOpinion(opinion)));

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

                string label = ExternalText(memory.LabelCap);
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
                .Select(pair => pair.Key + " (" + DiaryBuckets.EffectBucket(pair.Value) + ")")
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
                    line = DiaryLineCleaner.CleanLine(line);
                    int max = DiaryTuning.Current.diaryLineMaxChars;
                    return line.Length <= max ? line : TextTruncation.SafePrefix(line, max) + "...";
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

        // Extracts the ending of the pawn's most recent diary entry. This is separate from
        // LatestDiaryOpener: the opener helps avoid repetitive starts, while the ending gives the next
        // model call a short bridge it can continue from.
        public static string LatestDiaryEnding(string pawnId, IReadOnlyList<DiaryEvent> events)
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

                string role;
                if (!diaryEvent.TryGetDisplayRoleForPawn(pawnId, out role))
                {
                    continue;
                }

                string text = diaryEvent.DisplayTextForRole(role);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                DiaryTuningDef tuning = DiaryTuning.Current;
                string ending = DiarySentenceExcerpt.LastSentences(
                    text,
                    Math.Max(1, tuning.previousEntryEndingSentenceCount),
                    Math.Max(40, tuning.previousEntryEndingMaxChars));
                if (!string.IsNullOrWhiteSpace(ending))
                {
                    return ending;
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

            string cleaned = DiaryLineCleaner.CleanLine(text);
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
            return cleaned.Length <= max ? cleaned : TextTruncation.SafePrefix(cleaned, max) + "...";
        }

        // Cached WeatherDef.defName -> mention chance lookup, rebuilt only when the tuning Def
        // instance changes. The chances themselves live in DiaryTuningDef (XML-tunable).
        private static DiaryTuningDef weatherChanceTuning;
        private static Dictionary<string, float> weatherChanceLookup;

        // Rolls the per-weather chance (from DiaryTuningDef.weatherMentionChances) to decide whether
        // this outdoor entry mentions the weather. Clear (mapped to 0) never passes; the harshest
        // weather almost always does. Mild weather was dominating diary openings, so it is weighted low.
        private static bool ShouldMentionWeather(WeatherDef weather, Pawn pawn)
        {
            if (weather == null || pawn == null)
            {
                return false;
            }

            // Prompt flavor must not advance RimWorld's gameplay RNG. A stable pawn/weather seed
            // also makes Regenerate rebuild the same visible surroundings for the same live facts.
            Rand.PushState(HumorChancePolicy.StableSeed(
                pawn.GetUniqueLoadID(), weather.defName ?? string.Empty));
            try
            {
                return Rand.Chance(WeatherMentionChanceFor(weather));
            }
            finally
            {
                Rand.PopState();
            }
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
                string roomRole = TryReadRoomRoleLabel(room);
                if (!string.IsNullOrWhiteSpace(roomRole))
                {
                    parts.Add(roomRole);
                }
            }

            parts.Add((outdoors ? "PawnDiary.Ctx.Outdoors" : "PawnDiary.Ctx.Indoors").Translate());

            string odysseyLocationLabel = string.Empty;
            OdysseyMobileHomeSnapshot mobileHome;
            if (DlcContext.TryCaptureOdysseyMobileHome(pawn, out mobileHome))
            {
                odysseyLocationLabel = mobileHome.location?.visibleLabel ?? string.Empty;
                parts.Add(string.IsNullOrWhiteSpace(odysseyLocationLabel)
                    ? "PawnDiary.Ctx.GravshipHome".Translate(mobileHome.shipName)
                    : "PawnDiary.Ctx.GravshipHomeAt".Translate(mobileHome.shipName, odysseyLocationLabel));
            }

            // Weather and biome only matter when the pawn is exposed to them, and weather is added
            // only when a severity-weighted roll passes (see ShouldMentionWeather): clear skies were
            // dominating diary openings, so mild weather is rarely noted and dramatic weather almost
            // always is.
            if (outdoors)
            {
                WeatherDef weather = pawn.Map.weatherManager?.CurWeatherPerceived;
                if (ShouldMentionWeather(weather, pawn))
                {
                    parts.Add(ExternalText(weather.label));
                }

                if (pawn.Map.Biome != null
                    && !string.Equals(
                        DiaryLineCleaner.CleanLine(odysseyLocationLabel),
                        DiaryLineCleaner.CleanLine(pawn.Map.Biome.label),
                        StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(ExternalText(pawn.Map.Biome.label));
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
                parts.Add("PawnDiary.Ctx.Surroundings".Translate(DiaryBuckets.BeautyBucket(beauty)));
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

            string jobReport = ExternalText(pawn.GetJobReport());
            if (!string.IsNullOrWhiteSpace(jobReport))
            {
                parts.Add("PawnDiary.Ctx.Doing".Translate(jobReport));
            }
            else if (pawn.CurJobDef != null)
            {
                parts.Add("PawnDiary.Ctx.Doing".Translate(ExternalText(pawn.CurJobDef.LabelCap.Resolve())));
            }

            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Builds deterministic visible surroundings for a captured map cell. Containment uses this
        /// before ejection because a held pawn is not <c>Spawned</c>, and therefore cannot use the normal
        /// pawn overload. This deliberately performs no weather roll and includes no exact coordinates.
        /// </summary>
        public static string BuildSurroundingsSummaryAt(Map map, IntVec3 position)
        {
            if (map == null || !position.InBounds(map)) return string.Empty;

            List<string> parts = new List<string>();
            Room room = position.GetRoom(map);
            bool outdoors = room == null || room.PsychologicallyOutdoors;
            string roomRole = TryReadRoomRoleLabel(room);
            if (!string.IsNullOrWhiteSpace(roomRole)) parts.Add(roomRole);
            parts.Add((outdoors ? "PawnDiary.Ctx.Outdoors" : "PawnDiary.Ctx.Indoors").Translate());

            if (outdoors)
            {
                WeatherDef weather = map.weatherManager?.CurWeatherPerceived;
                if (weather != null) parts.Add(ExternalText(weather.label));
                if (map.Biome != null) parts.Add(ExternalText(map.Biome.label));
            }

            float temperature = GenTemperature.GetTemperatureForCell(position, map);
            if (temperature <= DiaryTuning.Current.coldBelowC)
                parts.Add("PawnDiary.Ctx.Cold".Translate());
            else if (temperature >= DiaryTuning.Current.hotAboveC)
                parts.Add("PawnDiary.Ctx.Hot".Translate());

            float beauty = BeautyUtility.AverageBeautyPerceptible(position, map);
            if (beauty >= DiaryTuning.Current.beautyPleasant
                || beauty <= -DiaryTuning.Current.beautyPleasant)
            {
                parts.Add("PawnDiary.Ctx.Surroundings".Translate(DiaryBuckets.BeautyBucket(beauty)));
            }

            string activeConditions = BuildActiveMapConditionsSummary(map);
            if (!string.IsNullOrWhiteSpace(activeConditions))
            {
                parts.Add(DiaryContextReactions.Format(
                    DiaryContextReactions.ActiveMapConditions, activeConditions));
            }

            return string.Join(", ", parts.ToArray());
        }

        // GetRoomRoleLabel looks like a simple label getter, but RimWorld can lazily recalculate every
        // room stat and the room role inside it. A transiently inconsistent room graph (especially while
        // performance/room-role mods are also patching that recalculation) can therefore throw. The role
        // is optional prompt flavor, so omit only that fragment instead of aborting the diary event or the
        // component tick. Do not log here: the same broken room can be sampled by many events in one tick.
        private static string TryReadRoomRoleLabel(Room room)
        {
            if (room == null)
            {
                return string.Empty;
            }

            try
            {
                return ExternalText(room.GetRoomRoleLabel());
            }
            catch (Exception)
            {
                return string.Empty;
            }
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

                string label = ExternalText(condition.Label);
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = ExternalText(condition.def.LabelCap.Resolve());
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
                return ExternalText(archivable.ArchivedLabel);
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

            PawnSummaryFacts facts = CollectPawnSummaryFacts(pawn);
            List<string> parts = new List<string>
            {
                "sex=" + facts.sex
            };

            if (!string.IsNullOrWhiteSpace(facts.lifeStage))
            {
                parts.Add("life_stage=" + facts.lifeStage);
            }

            // DLC identity labels are structured prompt schema (like sex=/life_stage=), so they stay
            // English per the localization carve-out. Each accessor returns empty without its DLC, so
            // a no-DLC game simply omits these lines — see DlcContext and AGENTS.md ("DLC-safety").
            if (!string.IsNullOrWhiteSpace(facts.xenotype))
            {
                parts.Add("xenotype=" + facts.xenotype);
            }

            if (!string.IsNullOrWhiteSpace(facts.royalTitle))
            {
                parts.Add("title=" + facts.royalTitle);
            }

            if (!string.IsNullOrWhiteSpace(facts.faith))
            {
                parts.Add("faith=" + facts.faith);
            }

            // The public context-provider hook lets adapter/personality mods add compact identity context such as
            // "personality=blunt, curious". Providers run in the impure snapshot phase (inside
            // CollectPawnSummaryFacts), and only cleaned strings continue into the prompt pipeline.
            if (facts.providerLines != null && facts.providerLines.Count > 0)
            {
                parts.Add(string.Join("; ", facts.providerLines.ToArray()));
            }

            if (!string.IsNullOrWhiteSpace(facts.mood))
            {
                parts.Add("mood=" + facts.mood);
            }

            string health = FormatHealthSummary(facts.health);
            if (!string.IsNullOrWhiteSpace(health))
            {
                parts.Add("health=" + health);
            }

            string lowCapacities = DiaryListText.JoinComma(facts.lowCapacities);
            if (!string.IsNullOrWhiteSpace(lowCapacities))
            {
                parts.Add("low_capacities=" + lowCapacities);
            }

            string topThoughts = DiaryListText.JoinComma(facts.topThoughts);
            if (!string.IsNullOrWhiteSpace(topThoughts))
            {
                parts.Add("thoughts=" + topThoughts);
            }

            return string.Join("; ", parts.ToArray());
        }

        // Builds the SAME pawn-summary context BuildPawnSummary feeds into a prompt, but as a
        // structured public DTO instead of a `key=value` blob. This is
        // the "machinery as a service" read: a chat/context mod can read our understanding of the
        // pawn without us driving another model. Side-effect free — never creates a diary record.
        //
        // KEEP IN SYNC with BuildPawnSummary above: both share CollectPawnSummaryFacts so the only
        // difference is formatting (string join vs DTO fields), never which facts are gathered. A
        // change to one is a change to the other.
        public static DiaryPawnSummarySnapshot BuildPawnSummarySnapshot(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            PawnSummaryFacts facts = CollectPawnSummaryFacts(pawn);
            return new DiaryPawnSummarySnapshot
            {
                sex = facts.sex,
                lifeStage = facts.lifeStage,
                xenotype = facts.xenotype,
                royalTitle = facts.royalTitle,
                faith = facts.faith,
                mood = facts.mood,
                health = new DiaryHealthSummarySnapshot
                {
                    downed = facts.health.downed,
                    painShock = facts.health.painShock,
                    pain = facts.health.pain,
                    bleeding = facts.health.bleeding,
                    conditions = DiaryListText.CopyNonNull(facts.health.conditions)
                },
                lowCapacities = DiaryListText.CopyNonNull(facts.lowCapacities),
                topThoughts = DiaryListText.CopyNonNull(facts.topThoughts),
                providerLines = DiaryListText.CopyNonNull(facts.providerLines)
            };
        }

        // The single impure gather point for pawn-summary facts. Both BuildPawnSummary (string for
        // the prompt) and BuildPawnSummarySnapshot (DTO for the public API) format from this, so the
        // facts themselves can never drift between the prompt path and the exported snapshot. Any new
        // field belongs here, with formatters in both consumers.
        private static PawnSummaryFacts CollectPawnSummaryFacts(Pawn pawn)
        {
            PawnSummaryFacts facts = new PawnSummaryFacts
            {
                sex = pawn.gender.ToString().ToLowerInvariant()
            };

            // Snapshot the biological age once; the band selection itself lives in DiaryBuckets and
            // takes a plain int so it has no Pawn dependency. A null ageTracker yields no band, as
            // before.
            facts.lifeStage = pawn.ageTracker != null
                ? DiaryBuckets.AgeBucket(pawn.ageTracker.AgeBiologicalYears)
                : string.Empty;

            facts.xenotype = DlcContext.Xenotype(pawn);
            facts.royalTitle = DlcContext.RoyalTitle(pawn);
            facts.faith = DlcContext.Ideoligion(pawn);

            // Provider lines are collected once as a list; BuildPawnSummary joins them with "; " for
            // the prompt blob, BuildPawnSummarySnapshot hands the list straight through so each
            // provider's contribution is its own DTO entry.
            //
            // Providers are third-party adapter code. The gather path itself is deterministic, but a
            // provider may legitimately call Verse Rand (e.g. to pick a flavor line). When this runs
            // as a public API read (GetPawnSummary / the context bundle) rather than during real
            // generation, that Rand consumption must not perturb the game's deterministic RNG stream,
            // so snapshot and restore UnityEngine.Random around the provider invocation. Mirrors the
            // prompt-preview path.
            UnityEngine.Random.State providerRandomState = UnityEngine.Random.state;
            try
            {
                facts.providerLines = PawnContextProviders.BuildContextLineList(pawn);
            }
            finally
            {
                UnityEngine.Random.state = providerRandomState;
            }

            facts.mood = BuildMoodSummary(pawn);
            facts.health = CollectHealthFacts(pawn);
            facts.lowCapacities = BuildLowCapacitiesSummary(pawn);
            facts.topThoughts = BuildTopThoughtsSummary(pawn);
            return facts;
        }

        // Intermediate gathered facts for one pawn. Pure data — no RimWorld references — so the two
        // formatting paths (prompt string, public DTO) format from the same source.
        private struct PawnSummaryFacts
        {
            public string sex;
            public string lifeStage;
            public string xenotype;
            public string royalTitle;
            public string faith;
            public string mood;
            public HealthFacts health;
            public List<string> lowCapacities;
            public List<string> topThoughts;
            public List<string> providerLines;
        }

        // Health is the one composite field (downed + pain shock + pain bucket + bleeding bucket +
        // up to two condition labels). Gather once into this struct so the prompt string and the DTO
        // stay in lockstep.
        private struct HealthFacts
        {
            public bool downed;
            public bool painShock;
            public string pain;
            public string bleeding;
            public List<string> conditions;
        }

        private static HealthFacts CollectHealthFacts(Pawn pawn)
        {
            HealthFacts facts = new HealthFacts { conditions = new List<string>() };
            if (pawn?.health == null)
            {
                return facts;
            }

            facts.downed = pawn.Downed;
            facts.painShock = pawn.health.InPainShock;

            float pain = pawn.health.hediffSet?.PainTotal ?? 0f;
            if (pain > DiaryTuning.Current.painVisibleAbove)
            {
                facts.pain = DiaryBuckets.PainBucket(pain);
            }

            float bleedRate = pawn.health.hediffSet?.BleedRateTotal ?? 0f;
            if (bleedRate > DiaryTuning.Current.bleedVisibleAbove)
            {
                facts.bleeding = DiaryBuckets.BleedingBucket(bleedRate);
            }

            facts.conditions = BuildNotableHediffsSummary(pawn);

            return facts;
        }

        // Formats gathered health facts into the same prompt string BuildHealthSummary produced
        // before this was factored out (downed, pain shock, pain=, bleeding=, conditions=, joined
        // with ", "). The prompt path uses this; the DTO path reads the struct fields directly.
        private static string FormatHealthSummary(HealthFacts facts)
        {
            List<string> parts = new List<string>();
            if (facts.downed)
            {
                parts.Add("PawnDiary.Ctx.Downed".Translate());
            }

            if (facts.painShock)
            {
                parts.Add("PawnDiary.Ctx.PainShock".Translate());
            }

            if (!string.IsNullOrWhiteSpace(facts.pain))
            {
                parts.Add("pain=" + facts.pain);
            }

            if (!string.IsNullOrWhiteSpace(facts.bleeding))
            {
                parts.Add("bleeding=" + facts.bleeding);
            }

            if (facts.conditions != null && facts.conditions.Count > 0)
            {
                parts.Add("conditions=" + DiaryListText.JoinComma(facts.conditions));
            }

            // Empty when healthy, so the prompt omits health entirely.
            return parts.Count == 0 ? string.Empty : string.Join(", ", parts.ToArray());
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
                return ExternalText(primary.LabelNoCount);
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
                    return ExternalText(chosen.LabelNoCount);
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
            return DiaryBuckets.MoodBucket(moodPercent);
        }

        private static List<string> BuildNotableHediffsSummary(Pawn pawn)
        {
            List<string> conditions = new List<string>();
            if (pawn.health?.hediffSet?.hediffs == null)
            {
                return conditions;
            }

            foreach (string label in pawn.health.hediffSet.hediffs
                .Where(hediff => hediff != null && hediff.Visible && (hediff.IsCurrentlyLifeThreatening || hediff.Bleeding || hediff.PainOffset > 0f || hediff.SummaryHealthPercentImpact < -0.05f))
                .OrderByDescending(hediff => hediff.IsCurrentlyLifeThreatening ? 100f : hediff.BleedRate + hediff.PainOffset - hediff.SummaryHealthPercentImpact)
                .Select(hediff => ExternalText(hediff.Label))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(2))
            {
                conditions.Add(label);
            }

            return conditions;
        }

        private static List<string> BuildLowCapacitiesSummary(Pawn pawn)
        {
            List<string> parts = new List<string>();
            if (pawn.health?.capacities == null)
            {
                return parts;
            }

            PawnCapacityDef[] relevantCapacities =
            {
                PawnCapacityDefOf.Moving,
                PawnCapacityDefOf.Talking,
                PawnCapacityDefOf.Sight,
                PawnCapacityDefOf.Hearing
            };

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

            return parts;
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

        private static List<string> BuildTopThoughtsSummary(Pawn pawn)
        {
            List<string> parts = new List<string>();
            if (pawn.needs?.mood?.thoughts == null)
            {
                return parts;
            }

            List<Thought> thoughts = new List<Thought>();
            try
            {
                // GetAllMoodThoughts itself calls MoodOffset() on every thought, so a modded thought
                // whose MoodOffset throws escapes the per-thought guard below. Losing the whole
                // summary beats losing the diary entry being built. Do not log: the broken thought
                // rethrows on every entry while it persists.
                pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            }
            catch (Exception)
            {
                return parts;
            }

            // Snapshot each thought's mood offset exactly once, skipping any thought whose getters
            // throw. Vanilla relation thoughts are fragile here — Thought_OpinionOfMyLover.MoodOffset
            // NREs when the lover relation is already gone by the time we read it — and other mods
            // postfix MoodOffset too. One stale thought must cost only itself, not the whole pawn
            // summary (and with it the diary entry being built).
            List<WeightedThought> visible = new List<WeightedThought>();
            for (int i = 0; i < thoughts.Count; i++)
            {
                Thought thought = thoughts[i];
                if (thought == null)
                {
                    continue;
                }

                try
                {
                    if (!thought.VisibleInNeedsTab)
                    {
                        continue;
                    }

                    float offset = thought.MoodOffset();
                    if (Mathf.Abs(offset) >= 1f)
                    {
                        visible.Add(new WeightedThought(thought, offset));
                    }
                }
                catch (Exception e)
                {
                    // Stale relation thoughts are routine pawn churn, so this stays quiet on the common
                    // path. But a genuinely broken (e.g. mod-patched) thought getter should be findable:
                    // WarningOnce keyed by the thought def surfaces each such def at most once per session.
                    Log.WarningOnce(
                        "[Pawn Diary] Skipped a mood thought whose getter threw while summarizing (stale "
                        + "relation, or another mod's MoodOffset patch?): " + e,
                        ("PawnDiary.ThoughtSummarySkip." + (thought.def?.defName ?? "unknown")).GetHashCode());
                }
            }

            List<WeightedThought> positive = visible.Where(t => t.Offset > 0f).ToList();
            List<WeightedThought> negative = visible.Where(t => t.Offset < 0f).ToList();

            string posStr = PickWeightedThought(positive);
            string negStr = PickWeightedThought(negative);

            if (!string.IsNullOrWhiteSpace(posStr))
            {
                parts.Add(posStr);
            }

            if (!string.IsNullOrWhiteSpace(negStr))
            {
                parts.Add(negStr);
            }

            return parts;
        }

        // Picks one thought from the list using weighted random: weight = |moodOffset|,
        // so thoughts with stronger effect are more likely to be chosen. Works on the offsets
        // snapshotted by BuildTopThoughtsSummary — it never calls Thought.MoodOffset() again,
        // because that getter can throw for stale relation thoughts (see the snapshot loop).
        private static string PickWeightedThought(List<WeightedThought> thoughts)
        {
            if (thoughts == null || thoughts.Count == 0)
            {
                return string.Empty;
            }

            float totalWeight = 0f;
            for (int i = 0; i < thoughts.Count; i++)
            {
                totalWeight += Mathf.Abs(thoughts[i].Offset);
            }

            if (totalWeight <= 0f)
            {
                return string.Empty;
            }

            float roll = UnityEngine.Random.value * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < thoughts.Count; i++)
            {
                cumulative += Mathf.Abs(thoughts[i].Offset);
                if (roll <= cumulative)
                {
                    return ThoughtSummaryText(thoughts[i]);
                }
            }

            // Fallback: return the last one (shouldn't reach here normally)
            return ThoughtSummaryText(thoughts[thoughts.Count - 1]);
        }

        // Formats one picked thought as "<label> (<effect bucket>)". LabelCap resolves through the
        // same relation lookups that make MoodOffset fragile (e.g. the lover's name), so a thought
        // that went stale after the snapshot degrades to an empty line instead of throwing.
        private static string ThoughtSummaryText(WeightedThought pick)
        {
            try
            {
                return ExternalText(pick.Thought.LabelCap) + " (" + DiaryBuckets.EffectBucket(pick.Offset) + ")";
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // Pairs a mood thought with the offset captured when it was snapshotted, so the fragile
        // Thought.MoodOffset() getter runs exactly once per thought. A plain value pair, no behavior.
        // New to C#? A readonly struct is like a frozen two-field object, allocated inline in the
        // list rather than on the heap.
        private readonly struct WeightedThought
        {
            public readonly Thought Thought;
            public readonly float Offset;

            public WeightedThought(Thought thought, float offset)
            {
                Thought = thought;
                Offset = offset;
            }
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
                return "PawnDiary.Ctx.Corpse".Translate(ExternalText(corpse.InnerPawn.LabelShortCap));
            }

            return ExternalText(thing.LabelNoCount);
        }

        private static string ExternalText(string value)
        {
            return PromptTextSanitizer.LocalizedPromptText(value);
        }
    }
}
