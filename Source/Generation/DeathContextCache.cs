// Captures colonist death facts at the exact Pawn.Kill call, then hands those facts to the
// TaleRecorder path. Tales tell us that a death was notable, but they do not retain the killing
// blow, damaged part, or culprit hediff, so this small transient cache bridges the two hooks.
// New to C#/RimWorld? See AGENTS.md ("Harmony patches").
using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Stores one short-lived, unsaved summary of how a colonist died so the later Tale event can
    /// generate a persona-independent death description grounded in damage, organ, illness, and
    /// nearby context.
    /// </summary>
    internal static class DeathContextCache
    {
        private static readonly Dictionary<string, string> CachedByPawnId = new Dictionary<string, string>();
        private static readonly Queue<string> CachedPawnOrder = new Queue<string>();

        // Death facts are consumed within the same Pawn.Kill call (the death Tale fires
        // synchronously). A leftover entry only happens when no death tale is recorded for the pawn
        // (e.g. the Tale group is disabled in settings), so any lingering entries are already stale.
        // Cap the cache so those can't accumulate over a long game.
        private const int MaxCachedEntries = 64;

        /// <summary>
        /// Clears stale, unsaved death facts when RimWorld starts or loads a different Game.
        /// </summary>
        public static void Clear()
        {
            CachedByPawnId.Clear();
            CachedPawnOrder.Clear();
        }

        /// <summary>
        /// Records the killing blow and visible fatal health context before RimWorld finishes
        /// killing the pawn. This runs on the main thread from the Pawn.Kill Harmony prefix.
        /// </summary>
        public static void Capture(Pawn pawn, DamageInfo? damageInfo, Hediff exactCulprit)
        {
            if (pawn == null || !IsHumanlikePlayerPawn(pawn))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            List<string> parts = new List<string>();

            if (damageInfo.HasValue)
            {
                DamageInfo info = damageInfo.Value;
                AppendDamageInfo(parts, info);
            }

            AppendCulprit(parts, exactCulprit);
            AppendMissingParts(parts, pawn);
            AppendLifeThreateningHediffs(parts, pawn, exactCulprit);

            string surroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(surroundings) && surroundings != "unknown" && surroundings != "none")
            {
                parts.Add("death_surroundings=" + surroundings);
            }

            if (parts.Count == 0)
            {
                parts.Add("death_facts=unknown");
            }

            Store(pawnId, string.Join("; ", parts.ToArray()));
        }

        /// <summary>
        /// Returns and removes the cached death facts for a pawn. If no exact killing-blow cache is
        /// available, falls back to the pawn's current visible health/surroundings.
        /// </summary>
        public static string ConsumeOrBuild(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            string pawnId = pawn.GetUniqueLoadID();
            string cached;
            if (!string.IsNullOrWhiteSpace(pawnId) && CachedByPawnId.TryGetValue(pawnId, out cached))
            {
                CachedByPawnId.Remove(pawnId);
                CompactOrderIfNeeded();
                return cached;
            }

            List<string> parts = new List<string>();
            AppendMissingParts(parts, pawn);
            AppendLifeThreateningHediffs(parts, pawn, null);

            string surroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(surroundings) && surroundings != "unknown" && surroundings != "none")
            {
                parts.Add("death_surroundings=" + surroundings);
            }

            return parts.Count == 0 ? string.Empty : string.Join("; ", parts.ToArray());
        }

        private static void Store(string pawnId, string context)
        {
            if (!CachedByPawnId.ContainsKey(pawnId))
            {
                CachedPawnOrder.Enqueue(pawnId);
            }

            CachedByPawnId[pawnId] = context;
            PruneOldestEntries();
            CompactOrderIfNeeded();
        }

        private static void PruneOldestEntries()
        {
            while (CachedByPawnId.Count > MaxCachedEntries && CachedPawnOrder.Count > 0)
            {
                string oldestPawnId = CachedPawnOrder.Dequeue();
                CachedByPawnId.Remove(oldestPawnId);
            }
        }

        private static void CompactOrderIfNeeded()
        {
            if (CachedPawnOrder.Count <= MaxCachedEntries * 2)
            {
                return;
            }

            CachedPawnOrder.Clear();
            foreach (string pawnId in CachedByPawnId.Keys)
            {
                CachedPawnOrder.Enqueue(pawnId);
            }
        }

        private static void AppendDamageInfo(List<string> parts, DamageInfo info)
        {
            DamageDef damageDef = info.Def;
            if (damageDef != null)
            {
                parts.Add("damage=" + DiaryLineCleaner.CleanLine(damageDef.LabelCap.Resolve()));
                parts.Add("damageDef=" + damageDef.defName);
            }

            if (info.Amount > 0f)
            {
                parts.Add("damageAmount=" + info.Amount.ToString("0.#", CultureInfo.InvariantCulture));
            }

            BodyPartRecord hitPart = info.HitPart;
            if (hitPart != null)
            {
                parts.Add("hitPart=" + DiaryLineCleaner.CleanLine(hitPart.LabelCap));
            }

            Thing instigator = info.Instigator;
            if (instigator != null)
            {
                parts.Add("instigator=" + DiaryLineCleaner.CleanLine(instigator.LabelShortCap));
            }

            ThingDef weapon = info.Weapon;
            if (weapon != null)
            {
                parts.Add("weapon=" + DiaryLineCleaner.CleanLine(weapon.LabelCap.Resolve()));
                parts.Add("weaponDef=" + weapon.defName);
            }

            Tool tool = info.Tool;
            if (tool != null && !string.IsNullOrWhiteSpace(tool.label))
            {
                parts.Add("tool=" + DiaryLineCleaner.CleanLine(tool.label));
            }
        }

        private static void AppendCulprit(List<string> parts, Hediff exactCulprit)
        {
            if (exactCulprit == null)
            {
                return;
            }

            parts.Add("culprit=" + DiaryLineCleaner.CleanLine(exactCulprit.LabelCap));
            if (exactCulprit.def != null)
            {
                parts.Add("culpritDef=" + exactCulprit.def.defName);
            }

            BodyPartRecord part = exactCulprit.Part;
            if (part != null)
            {
                parts.Add("culpritPart=" + DiaryLineCleaner.CleanLine(part.LabelCap));
            }

            if (exactCulprit.Severity > 0f)
            {
                parts.Add("culpritSeverity=" + exactCulprit.Severity.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        private static void AppendMissingParts(List<string> parts, Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            List<string> missing = new List<string>();
            for (int i = 0; i < pawn.health.hediffSet.hediffs.Count; i++)
            {
                Hediff_MissingPart missingPart = pawn.health.hediffSet.hediffs[i] as Hediff_MissingPart;
                if (missingPart == null || missingPart.Part == null)
                {
                    continue;
                }

                string label = "part=" + DiaryLineCleaner.CleanLine(missingPart.Part.LabelCap);
                if (missingPart.lastInjury != null)
                {
                    label += " cause=" + DiaryLineCleaner.CleanLine(missingPart.lastInjury.LabelCap);
                }

                missing.Add(label);
            }

            if (missing.Count > 0)
            {
                parts.Add("destroyed_or_missing_parts=" + string.Join(", ", missing.ToArray()));
            }
        }

        private static void AppendLifeThreateningHediffs(List<string> parts, Pawn pawn, Hediff exactCulprit)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            List<string> hediffs = new List<string>();
            for (int i = 0; i < pawn.health.hediffSet.hediffs.Count; i++)
            {
                Hediff hediff = pawn.health.hediffSet.hediffs[i];
                if (hediff == null || hediff == exactCulprit || !hediff.Visible)
                {
                    continue;
                }

                if (!hediff.IsCurrentlyLifeThreatening && !hediff.IsLethal && !hediff.Bleeding)
                {
                    continue;
                }

                string label = DiaryLineCleaner.CleanLine(hediff.LabelCap);
                if (hediff.Part != null)
                {
                    label += " (" + DiaryLineCleaner.CleanLine(hediff.Part.LabelCap) + ")";
                }

                hediffs.Add(label);
            }

            if (hediffs.Count > 0)
            {
                parts.Add("other_lethal_conditions=" + string.Join(", ", hediffs.ToArray()));
            }
        }

        private static bool IsHumanlikePlayerPawn(Pawn pawn)
        {
            return pawn != null
                && pawn.RaceProps != null
                && pawn.RaceProps.Humanlike
                && pawn.IsColonist;
        }
    }
}
