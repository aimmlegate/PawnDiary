// Catalog payload for one exact Royalty persona-weapon lifecycle page. The guarded runtime adapter
// owns live weapon/Pawn observation and persistence; this detached value owns only the final page
// gate and stable once-per-bond-epoch identity.
namespace PawnDiary.Capture
{
    /// <summary>Primitive facts for formation, meaningful separation/recovery, or a standalone ending.</summary>
    internal sealed class PersonaWeaponEventData : DiaryEventData
    {
        public const string BondFormedDefName = "PersonaWeaponBondFormed";
        public const string BondSeparatedDefName = "PersonaWeaponBondSeparated";
        public const string BondRecoveredDefName = "PersonaWeaponBondRecovered";
        public const string BondEndedDefName = "PersonaWeaponBondEnded";

        public override DiaryEventType EventType => DiaryEventType.PersonaWeapon;

        public string DefName = string.Empty;
        public string WeaponThingId = string.Empty;
        public int BondEpoch;
        public string NarrativePhase = string.Empty;
        public bool PawnEligible;
        public bool HasExactLifecycle;
        public bool AlreadyRecorded;

        /// <summary>Applies the final pure truth, identity, and user-setting gates.</summary>
        public static CaptureDecision Decide(PersonaWeaponEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || data.AlreadyRecorded || !data.PawnEligible || !data.HasExactLifecycle
                || string.IsNullOrWhiteSpace(data.PawnId)
                || !SafeId(data.WeaponThingId) || data.BondEpoch < 1
                || DefNameForPhase(data.NarrativePhase) != data.DefName)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>Returns the once-per-weapon, epoch, and lifecycle-edge key.</summary>
        public string DedupKey()
        {
            return SafeId(WeaponThingId) && BondEpoch > 0 && DefNameForPhase(NarrativePhase) == DefName
                ? "persona-weapon|" + WeaponThingId.Trim() + "|" + BondEpoch + "|" + NarrativePhase
                : string.Empty;
        }

        /// <summary>Maps one frozen lifecycle phase to its stable synthetic event Def name.</summary>
        public static string DefNameForPhase(string phase)
        {
            if (phase == PersonaNarrativePhaseTokens.BondFormed) return BondFormedDefName;
            if (phase == PersonaNarrativePhaseTokens.BondSeparated) return BondSeparatedDefName;
            if (phase == PersonaNarrativePhaseTokens.BondRecovered) return BondRecoveredDefName;
            if (phase == PersonaNarrativePhaseTokens.BondEnded) return BondEndedDefName;
            return string.Empty;
        }

        /// <summary>Maps one lifecycle phase to the structural trait-selection category.</summary>
        public static string TraitEventTokenForPhase(string phase)
        {
            if (phase == PersonaNarrativePhaseTokens.BondFormed) return PersonaTraitEventTokens.Formation;
            if (phase == PersonaNarrativePhaseTokens.BondSeparated) return PersonaTraitEventTokens.Separation;
            if (phase == PersonaNarrativePhaseTokens.BondRecovered) return PersonaTraitEventTokens.Recovery;
            if (phase == PersonaNarrativePhaseTokens.BondEnded) return PersonaTraitEventTokens.Ending;
            return string.Empty;
        }

        private static bool SafeId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0;
        }
    }
}
