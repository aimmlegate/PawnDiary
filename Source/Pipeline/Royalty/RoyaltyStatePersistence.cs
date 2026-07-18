// Pure normalization and silent-baseline helpers for Royalty save state. The RimWorld-facing models
// adapt these detached values to Scribe; this file stays usable by the standalone Royalty tests.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Repairs Royalty state deterministically without reading live game objects.</summary>
    internal static class RoyaltyStatePersistence
    {
        internal const int CurrentObservationVersion = 1;
        internal const int HardMaximumPersonaStates = 2048;
        internal const int HardMaximumTitleObservations = 32;

        /// <summary>Builds the conservative no-catch-up baseline for an existing coded weapon.</summary>
        public static PersonaBondStateSnapshot BaselinePersona(
            PersonaWeaponSnapshot weapon,
            int tick,
            int maximumTraits)
        {
            if (!SafeId(weapon?.weaponThingId) || !SafeId(weapon.codedPawnId)) return null;
            return NormalizePersona(new PersonaBondStateSnapshot
            {
                weaponThingId = weapon.weaponThingId,
                weaponDefName = weapon.weaponDefName,
                lastDisplayName = weapon.displayName,
                bondEpoch = 1,
                currentPawnId = weapon.codedPawnId,
                currentPawnName = weapon.codedPawnName,
                phaseToken = PersonaBondPhaseTokens.Active,
                bondStartedTick = Math.Max(0, tick),
                pendingSeparationTick = -1,
                firstConsequentialKillObserved = true,
                lastPrimaryObservedTick = weapon.isCurrentlyPrimary ? Math.Max(0, tick) : -1,
                endedTick = -1,
                endCauseToken = PersonaEndCauseTokens.None,
                traits = weapon.traits
            }, maximumTraits);
        }

        /// <summary>Normalizes one bond row while preserving an explicit legitimate empty ledger.</summary>
        public static PersonaBondStateSnapshot NormalizePersona(
            PersonaBondStateSnapshot source,
            int maximumTraits)
        {
            if (source == null || !SafeId(source.weaponThingId)) return null;
            string phase = Clean(source.phaseToken);
            if (!PersonaBondPhaseTokens.IsKnown(phase)) phase = PersonaBondPhaseTokens.Untracked;
            int epoch = Math.Max(0, source.bondEpoch);
            if (phase != PersonaBondPhaseTokens.Untracked && epoch < 1) epoch = 1;
            string endCause = Clean(source.endCauseToken);
            if (!KnownEndCause(endCause)) endCause = PersonaEndCauseTokens.None;

            return new PersonaBondStateSnapshot
            {
                weaponThingId = Clean(source.weaponThingId),
                weaponDefName = SafeText(source.weaponDefName),
                lastDisplayName = SafeText(source.lastDisplayName),
                bondEpoch = epoch,
                currentPawnId = SafeId(source.currentPawnId) ? Clean(source.currentPawnId) : string.Empty,
                currentPawnName = SafeText(source.currentPawnName),
                previousPawnId = SafeId(source.previousPawnId) ? Clean(source.previousPawnId) : string.Empty,
                phaseToken = phase,
                bondStartedTick = Math.Max(-1, source.bondStartedTick),
                pendingSeparationTick = phase == PersonaBondPhaseTokens.SeparationPending
                    ? Math.Max(-1, source.pendingSeparationTick)
                    : -1,
                separationEmitted = source.separationEmitted,
                // Recorded is a stronger historical fact than observed. Repair legacy/corrupt rows
                // that somehow persisted the flags in the impossible inverse combination.
                firstConsequentialKillObserved = source.firstConsequentialKillObserved
                    || source.firstConsequentialKillEventRecorded,
                firstConsequentialKillEventRecorded = source.firstConsequentialKillEventRecorded,
                lastPrimaryObservedTick = Math.Max(-1, source.lastPrimaryObservedTick),
                endedTick = phase == PersonaBondPhaseTokens.Ended ? Math.Max(-1, source.endedTick) : -1,
                endCauseToken = phase == PersonaBondPhaseTokens.Ended ? endCause : PersonaEndCauseTokens.None,
                traits = NormalizeTraits(source.traits, maximumTraits)
            };
        }

        /// <summary>De-duplicates weapon IDs, keeping the newest row at a fixed corruption ceiling.</summary>
        public static List<PersonaBondStateSnapshot> NormalizePersonas(
            IList<PersonaBondStateSnapshot> source,
            int maximumTraits,
            int maximumRows)
        {
            List<PersonaBondStateSnapshot> reversed = new List<PersonaBondStateSnapshot>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int cap = Math.Max(0, Math.Min(HardMaximumPersonaStates, maximumRows));
            for (int i = (source?.Count ?? 0) - 1; i >= 0 && reversed.Count < cap; i--)
            {
                PersonaBondStateSnapshot row = NormalizePersona(source[i], maximumTraits);
                if (row != null && seen.Add(row.weaponThingId)) reversed.Add(row);
            }
            reversed.Reverse();
            return reversed;
        }

        /// <summary>
        /// Returns whether a saved live bond is also visible as the exact pawn's current coded weapon.
        /// Saved lifecycle state alone is insufficient for current-context enrichment: map removal and
        /// ambiguous UnCode deliberately preserve that state so a later return can reconcile silently.
        /// </summary>
        public static bool IsCurrentVisiblePersonaBond(
            PersonaBondStateSnapshot state,
            string pawnId,
            IList<PersonaWeaponSnapshot> visibleWeapons)
        {
            if (state == null || !PersonaBondPhaseTokens.IsLive(state.phaseToken)
                || !SafeId(pawnId)
                || !string.Equals(state.currentPawnId, Clean(pawnId), StringComparison.Ordinal)
                || visibleWeapons == null)
            {
                return false;
            }

            for (int i = 0; i < visibleWeapons.Count; i++)
            {
                PersonaWeaponSnapshot weapon = visibleWeapons[i];
                if (weapon != null && !weapon.isDestroyed
                    && string.Equals(weapon.weaponThingId, state.weaponThingId, StringComparison.Ordinal)
                    && string.Equals(weapon.codedPawnId, state.currentPawnId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Copies current faction titles into a silent, deterministically ordered baseline.</summary>
        public static List<RoyalTitleObservationSnapshot> BaselineTitles(
            IList<RoyalTitleSnapshot> titles,
            int tick)
        {
            List<RoyalTitleObservationSnapshot> rows = new List<RoyalTitleObservationSnapshot>();
            if (titles != null)
            {
                for (int i = 0; i < titles.Count; i++)
                {
                    RoyalTitleSnapshot title = titles[i];
                    if (title == null) continue;
                    rows.Add(new RoyalTitleObservationSnapshot
                    {
                        factionId = title.factionId,
                        factionName = title.factionName,
                        titleDefName = title.titleDefName,
                        titleLabel = title.titleLabel,
                        seniority = title.seniority,
                        lastObservedTick = Math.Max(0, tick)
                    });
                }
            }
            return NormalizeTitleObservations(rows, HardMaximumTitleObservations);
        }

        /// <summary>Repairs faction rows, keeps the highest title per faction, and sorts by identity.</summary>
        public static List<RoyalTitleObservationSnapshot> NormalizeTitleObservations(
            IList<RoyalTitleObservationSnapshot> source,
            int maximumRows)
        {
            Dictionary<string, RoyalTitleObservationSnapshot> byFaction =
                new Dictionary<string, RoyalTitleObservationSnapshot>(StringComparer.Ordinal);
            for (int i = 0; i < (source?.Count ?? 0); i++)
            {
                RoyalTitleObservationSnapshot row = source[i];
                if (row == null || !SafeId(row.factionId) || !SafeId(row.titleDefName)) continue;
                RoyalTitleObservationSnapshot normalized = new RoyalTitleObservationSnapshot
                {
                    factionId = Clean(row.factionId),
                    factionName = SafeText(row.factionName),
                    titleDefName = Clean(row.titleDefName),
                    titleLabel = SafeText(row.titleLabel),
                    seniority = Math.Max(0, row.seniority),
                    lastObservedTick = Math.Max(-1, row.lastObservedTick)
                };
                RoyalTitleObservationSnapshot existing;
                if (!byFaction.TryGetValue(normalized.factionId, out existing)
                    || normalized.seniority > existing.seniority
                    || (normalized.seniority == existing.seniority
                        && normalized.lastObservedTick >= existing.lastObservedTick))
                    byFaction[normalized.factionId] = normalized;
            }

            List<RoyalTitleObservationSnapshot> result = new List<RoyalTitleObservationSnapshot>(byFaction.Values);
            result.Sort((left, right) => string.CompareOrdinal(left.factionId, right.factionId));
            int cap = Math.Max(0, Math.Min(HardMaximumTitleObservations, maximumRows));
            if (result.Count > cap) result.RemoveRange(cap, result.Count - cap);
            return result;
        }

        private static List<PersonaTraitFact> NormalizeTraits(IList<PersonaTraitFact> source, int maximumTraits)
        {
            List<PersonaTraitFact> result = new List<PersonaTraitFact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int cap = Math.Max(0, Math.Min(128, maximumTraits));
            for (int i = 0; i < (source?.Count ?? 0) && result.Count < cap; i++)
            {
                PersonaTraitFact row = source[i];
                string id = SafeId(row?.traitDefName) ? Clean(row.traitDefName) : string.Empty;
                if (id.Length == 0 || !seen.Add(id)) continue;
                result.Add(new PersonaTraitFact
                {
                    traitDefName = id,
                    label = SafeText(row.label),
                    description = SafeText(row.description),
                    workerTypeToken = SafeText(row.workerTypeToken),
                    hasKillThought = row.hasKillThought,
                    hasBondedThought = row.hasBondedThought,
                    hasBondedHediff = row.hasBondedHediff,
                    hasEquippedHediff = row.hasEquippedHediff
                });
            }
            return result;
        }

        private static bool KnownEndCause(string value)
        {
            return value == PersonaEndCauseTokens.None || value == PersonaEndCauseTokens.PawnDeath
                || value == PersonaEndCauseTokens.WeaponDestroyed || value == PersonaEndCauseTokens.Transfer
                || value == PersonaEndCauseTokens.UnknownUncode || value == PersonaEndCauseTokens.MapRemoval;
        }

        private static bool SafeId(string value)
        {
            string cleaned = Clean(value);
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0;
        }

        private static string SafeText(string value)
        {
            return (value ?? string.Empty).Trim().Replace(";", ",");
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
