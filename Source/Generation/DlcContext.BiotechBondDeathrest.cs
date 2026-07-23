// Guarded Biotech Phase-8 live projections. Psychic-bond and deathrest hooks pass their Verse
// objects only into this boundary; pure policy, saved state, and prompt adapters receive detached
// primitive snapshots.
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static partial class DlcContext
    {
        // Vanilla stores the authoritative partner in a private field. RemoveBond can be entered
        // after one PsychicBond hediff has already been removed, and PostRemove runs after the
        // owning gene has left its tracker, so the public hediff projection alone is not enough
        // to freeze the pre-mutation pair at either exact boundary.
        private static readonly System.Reflection.FieldInfo PsychicBondedPawnField =
            AccessTools.Field(typeof(Gene_PsychicBonding), "bondedPawn");

        /// <summary>Returns the private vanilla partner used by an exact bond mutation boundary.</summary>
        internal static Pawn PsychicBondMutationPartner(Gene_PsychicBonding gene)
        {
            if (!ModsConfig.BiotechActive || gene == null || PsychicBondedPawnField == null)
                return null;
            return PsychicBondedPawnField.GetValue(gene) as Pawn;
        }

        /// <summary>Returns the exact live partner targeted by this pawn's PsychicBond hediff.</summary>
        internal static Pawn PsychicBondPartner(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.health?.hediffSet?.hediffs == null) return null;
            for (int i = 0; i < pawn.health.hediffSet.hediffs.Count; i++)
            {
                Hediff_PsychicBond bond = pawn.health.hediffSet.hediffs[i] as Hediff_PsychicBond;
                Pawn partner = bond?.target as Pawn;
                if (partner != null) return partner;
            }
            return null;
        }

        /// <summary>Proves that each pawn's live PsychicBond hediff targets the other pawn.</summary>
        internal static bool AreMutuallyPsychicBonded(Pawn first, Pawn second)
        {
            if (!ModsConfig.BiotechActive || first == null || second == null
                || first.health?.hediffSet?.hediffs == null
                || second.health?.hediffSet?.hediffs == null) return false;
            return ReferenceEquals(PsychicBondPartner(first), second)
                && ReferenceEquals(PsychicBondPartner(second), first);
        }

        /// <summary>Copies one sorted pair lifecycle snapshot on the main thread.</summary>
        internal static PsychicBondMutationSnapshot CapturePsychicBondMutation(
            Pawn first,
            Pawn second,
            int epoch,
            string phase,
            string cause,
            bool mutuallyBondedBefore,
            bool mutuallyBondedAfter,
            int tick)
        {
            if (!ModsConfig.BiotechActive || first == null || second == null) return null;
            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            PsychicBondPair pair = PsychicBondPairPolicy.Create(firstId, secondId);
            if (pair == null) return null;
            bool firstIsSortedFirst = pair.firstPawnId == firstId;
            Pawn sortedFirst = firstIsSortedFirst ? first : second;
            Pawn sortedSecond = firstIsSortedFirst ? second : first;
            return new PsychicBondMutationSnapshot
            {
                ownerPawnId = firstId,
                partnerPawnId = secondId,
                firstPawnId = pair.firstPawnId,
                firstPawnName = DiaryLineCleaner.CleanLine(sortedFirst.LabelShortCap),
                secondPawnId = pair.secondPawnId,
                secondPawnName = DiaryLineCleaner.CleanLine(sortedSecond.LabelShortCap),
                bondEpoch = epoch,
                phase = phase ?? string.Empty,
                cause = cause ?? PsychicBondCauseTokens.Unknown,
                mutuallyBondedBefore = mutuallyBondedBefore,
                mutuallyBondedAfter = mutuallyBondedAfter,
                firstPawnEligible = DiaryGameComponent.IsDiaryEligible(sortedFirst),
                secondPawnEligible = DiaryGameComponent.IsDiaryEligible(sortedSecond),
                observedTick = tick < 0 ? 0 : tick
            };
        }

        /// <summary>Captures pre-Wake deathrest truth without exposing the live gene to pure policy.</summary>
        internal static bool TryCaptureDeathrest(
            Gene_Deathrest gene,
            int tick,
            out Pawn pawn,
            out DeathrestMutationSnapshot snapshot)
        {
            pawn = null;
            snapshot = null;
            if (!ModsConfig.BiotechActive || gene?.pawn == null
                || gene.pawn.genes == null || gene.pawn.health?.hediffSet?.hediffs == null)
                return false;
            pawn = gene.pawn;
            snapshot = new DeathrestMutationSnapshot
            {
                pawnId = pawn.GetUniqueLoadID() ?? string.Empty,
                pawnName = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                activeDeathrestBefore = HasHediffDefName(pawn, "Deathrest"),
                deathrestPercentBefore = gene.DeathrestPercent,
                pawnEligible = DiaryGameComponent.IsDiaryEligible(pawn),
                observedTick = tick < 0 ? 0 : tick
            };
            return !string.IsNullOrWhiteSpace(snapshot.pawnId);
        }

        /// <summary>Confirms the exact Biotech interruption hediff without requiring its Def reference.</summary>
        internal static bool HasInterruptedDeathrest(Pawn pawn)
        {
            return ModsConfig.BiotechActive && HasHediffDefName(pawn, "InterruptedDeathrest");
        }

        private static bool HasHediffDefName(Pawn pawn, string defName)
        {
            if (pawn?.health?.hediffSet?.hediffs == null || string.IsNullOrWhiteSpace(defName))
                return false;
            for (int i = 0; i < pawn.health.hediffSet.hediffs.Count; i++)
            {
                if (string.Equals(
                    pawn.health.hediffSet.hediffs[i]?.def?.defName,
                    defName,
                    System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
