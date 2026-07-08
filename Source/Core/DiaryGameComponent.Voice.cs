// The two-layer per-pawn "voice": writing style (mechanics) + psychotype (outlook). This partial owns
// age-band resolution, the lazy crystallization/backfill check (EnsureVoiceStage), the psychotype roll
// helpers, and the psychotype rule resolution used by both generation and the UI. It is the main-thread
// boundary that mutates the saved record; the pure roll/resolution logic lives in Source/Pipeline.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Whether the psychotype layer is on. Off => the block is omitted from prompts and rolls defer.
        private static bool PsychotypesEnabled
        {
            get { return PawnDiaryMod.Settings?.enablePsychotypes ?? true; }
        }

        /// <summary>
        /// The catalog band a pawn currently belongs to: "child" below the crystallization age, else
        /// "adult". Null/odd age data (off-map or unusual saves) is treated as adult so nothing strands.
        /// </summary>
        private static string BandForPawn(Pawn pawn)
        {
            int crystAge = Math.Max(0, DiaryTuning.Current.psychotypeCrystallizationAgeYears);
            if (crystAge <= 0 || pawn?.ageTracker == null)
            {
                return DiaryPersonas.StageAdult;
            }

            return pawn.ageTracker.AgeBiologicalYears < crystAge
                ? DiaryPersonas.StageChild
                : DiaryPersonas.StageAdult;
        }

        /// <summary>
        /// Lazy per-pawn voice maintenance, run on the main thread before a pawn's style/psychotype is
        /// resolved for an entry. It (a) backfills pre-feature legacy records — established voices freeze
        /// to Neutral, entry-less ones roll fresh — and (b) crystallizes a child into the adult catalogs
        /// when they cross the crystallization age. Pinned (player-chosen) layers are never auto-re-rolled.
        /// Idempotent and cheap on the common path (band already stamped and matching).
        /// </summary>
        internal void EnsureVoiceStage(Pawn pawn, PawnDiaryRecord diary)
        {
            if (pawn == null || diary == null)
            {
                return;
            }

            string targetBand = BandForPawn(pawn);
            bool bandStamped = !string.IsNullOrEmpty(diary.voiceStageBand);
            bool bandMatches = bandStamped
                && string.Equals(diary.voiceStageBand, targetBand, StringComparison.OrdinalIgnoreCase);
            bool psychotypeSet = !string.IsNullOrEmpty(diary.psychotypeDefName);
            bool styleSet = !string.IsNullOrWhiteSpace(diary.personaDefName)
                && DiaryPersonas.ForDefName(diary.personaDefName) != null;
            // When the layer is off the psychotype is intentionally left unset (deferred), so it does not
            // keep the fast path from engaging.
            bool psychotypeOk = !PsychotypesEnabled || psychotypeSet;
            if (bandMatches && psychotypeOk && styleSet)
            {
                return;
            }

            bool legacyUnstamped = !bandStamped;
            bool crystallizing = string.Equals(diary.voiceStageBand, DiaryPersonas.StageChild, StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetBand, DiaryPersonas.StageAdult, StringComparison.OrdinalIgnoreCase);
            bool bandChanged = bandStamped && !bandMatches;

            // ---- Psychotype layer (only managed while the feature is enabled; otherwise deferred) ----
            if (PsychotypesEnabled && !diary.psychotypePinned)
            {
                // Freeze an established PRE-FEATURE voice (legacy record whose band was never stamped and
                // which already has generated prose): Neutral contributes no prompt text, so the diary the
                // player has already been reading does not suddenly shift. RecordHasGeneratedEntries walks
                // the archive/events, so it is checked lazily here (a legacy record) rather than up front.
                if (legacyUnstamped && RecordHasGeneratedEntries(diary))
                {
                    diary.psychotypeDefName = DiaryPsychotypes.NeutralDefName;
                }
                else if (!psychotypeSet || crystallizing || bandChanged)
                {
                    string childCarry = crystallizing ? diary.psychotypeDefName : null;
                    diary.psychotypeDefName = RollPsychotypeFor(pawn, targetBand, childCarry, diary.pawnId);
                }

                if (string.IsNullOrEmpty(diary.psychotypeDefName))
                {
                    diary.psychotypeDefName = DiaryPsychotypes.NeutralDefName;
                }
            }

            // ---- Writing style layer ----
            if (!diary.writingStylePinned)
            {
                // Legacy records already carry a style rolled by the old logic; keep it (only stamp the
                // band). Genuine crystallization / band change / a missing style re-rolls onto the band.
                if (!legacyUnstamped && (crystallizing || bandChanged || !styleSet))
                {
                    diary.personaDefName = RollStyleFor(pawn, targetBand, diary.pawnId);
                }
            }

            if (string.IsNullOrWhiteSpace(diary.personaDefName) || DiaryPersonas.ForDefName(diary.personaDefName) == null)
            {
                diary.personaDefName = DiaryPersonas.Default.defName;
            }

            // Stamp the band — but keep a still-unstamped LEGACY record unstamped while the psychotype
            // layer is OFF. New records always carry a band from creation, so this only defers stamping for
            // genuine pre-feature records; it preserves the "pre-feature" signal so enabling the layer
            // later still freezes an established voice to Neutral instead of re-rolling it. Stamping
            // unconditionally would mask that and defeat the freeze across a disable->enable sequence.
            if (PsychotypesEnabled || bandStamped)
            {
                diary.voiceStageBand = targetBand;
            }
        }

        private string RollPsychotypeFor(Pawn pawn, string band, string childCarryDefName, string excludePawnId)
        {
            return PsychotypeRolls.Roll(pawn, band, BuildUsedPsychotypeCounts(excludePawnId, band), childCarryDefName);
        }

        private string RollStyleFor(Pawn pawn, string band, string excludePawnId)
        {
            return DiaryPersonas.WeightedStartingPersona(pawn, BuildUsedPersonaCounts(excludePawnId, band), band).defName;
        }

        /// <summary>
        /// True when a pawn has any generated diary prose (live or archived). Used to decide whether a
        /// pre-feature legacy record should freeze to Neutral (established voice) or roll fresh.
        /// </summary>
        private bool RecordHasGeneratedEntries(PawnDiaryRecord diary)
        {
            if (diary == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(diary.pawnId) && archive.CountForPawn(diary.pawnId) > 0)
            {
                return true;
            }

            if (diary.eventIds == null)
            {
                return false;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.HasGeneratedTextForRole(DiaryEvent.InitiatorRole)
                    || diaryEvent.HasGeneratedTextForRole(DiaryEvent.RecipientRole)
                    || diaryEvent.HasGeneratedTextForRole(DiaryEvent.NeutralRole))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves the effective psychotype for a pawn: External API override &gt; pawn custom rule &gt;
        /// base type. Ensures the pawn's voice stage first (so a crystallization/backfill lands before we
        /// read the record), and returns an empty rule when the layer is disabled. Used by generation
        /// (<see cref="EnsureVoiceStage"/> already ran) and the per-pawn editor UI.
        /// </summary>
        internal PsychotypeResolution ResolvePsychotypeFor(Pawn pawn)
        {
            PawnDiaryRecord diary = pawn == null ? null : FindDiaryByPawnId(pawn.GetUniqueLoadID());
            if (pawn != null && diary != null)
            {
                EnsureVoiceStage(pawn, diary);
            }

            return BuildPsychotypeResolution(diary);
        }

        /// <summary>
        /// Read-only psychotype resolution for repeated UI draws (tab tooltip, dialog repaint). Unlike
        /// <see cref="ResolvePsychotypeFor"/> it does NOT run <see cref="EnsureVoiceStage"/>, so it never
        /// rolls (consumes Rand) or mutates the record during an OnGUI pass. An unset legacy record simply
        /// reads as Neutral here until generation or the editor backfills it.
        /// </summary>
        internal PsychotypeResolution ResolvePsychotypeForDisplay(Pawn pawn)
        {
            PawnDiaryRecord diary = pawn == null ? null : FindDiaryByPawnId(pawn.GetUniqueLoadID());
            return BuildPsychotypeResolution(diary);
        }

        // ---- Per-pawn editor surface (Dialog_PawnWritingStyle) ----------------------------------------

        /// <summary>Whether the psychotype layer is enabled, for the editor's disabled hint.</summary>
        internal bool PsychotypeLayerEnabled
        {
            get { return PsychotypesEnabled; }
        }

        /// <summary>The pawn's current voice band ("child"/"adult"), so the editor's pickers show only
        /// stage-appropriate options.</summary>
        internal string VoiceBandFor(Pawn pawn)
        {
            return BandForPawn(pawn);
        }

        /// <summary>This pawn's saved custom psychotype rule (player-authored), or empty.</summary>
        internal string CustomPsychotypeRuleFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return string.Empty;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            return diary == null ? string.Empty : diary.customPsychotypeRule ?? string.Empty;
        }

        /// <summary>True when the player has pinned this pawn's psychotype (never auto-re-rolled).</summary>
        internal bool PsychotypePinnedFor(Pawn pawn)
        {
            PawnDiaryRecord diary = IsDiaryEligible(pawn) ? FindDiary(pawn, false) : null;
            return diary != null && diary.psychotypePinned;
        }

        /// <summary>True when the player has pinned this pawn's writing style (never auto-re-rolled).</summary>
        internal bool WritingStylePinnedFor(Pawn pawn)
        {
            PawnDiaryRecord diary = IsDiaryEligible(pawn) ? FindDiary(pawn, false) : null;
            return diary != null && diary.writingStylePinned;
        }

        /// <summary>Sets this pawn's base psychotype Def (player pick from the editor). Does not pin by
        /// itself — the editor writes the pin state separately via <see cref="SetPsychotypePinned"/>.</summary>
        internal bool SetPsychotype(Pawn pawn, string psychotypeDefName)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            DiaryPsychotypeDef type = DiaryPsychotypes.Resolve(psychotypeDefName);
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return false;
            }

            diary.psychotypeDefName = type.defName;
            if (string.IsNullOrEmpty(diary.voiceStageBand))
            {
                diary.voiceStageBand = BandForPawn(pawn);
            }

            return true;
        }

        /// <summary>Saves a player-authored custom psychotype rule; empty clears it. Line breaks kept.</summary>
        internal bool SetCustomPsychotypeRule(Pawn pawn, string rule)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            string cleaned = PsychotypeText.CleanRule(rule);
            PawnDiaryRecord diary = FindDiary(pawn, !string.IsNullOrEmpty(cleaned));
            if (diary == null)
            {
                return string.IsNullOrEmpty(cleaned);
            }

            diary.customPsychotypeRule = cleaned;
            return true;
        }

        /// <summary>Rolls a fresh psychotype for the pawn's current band without committing — used by the
        /// editor's Re-roll button to preview a new pick before Save.</summary>
        internal string RollPsychotypePreview(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return DiaryPsychotypes.NeutralDefName;
            }

            string band = BandForPawn(pawn);
            return RollPsychotypeFor(pawn, band, null, pawn.GetUniqueLoadID());
        }

        /// <summary>Sets whether this pawn's psychotype is pinned (player-chosen, never auto-re-rolled).</summary>
        internal bool SetPsychotypePinned(Pawn pawn, bool pinned)
        {
            return SetVoicePin(pawn, pinned, (diary, value) => diary.psychotypePinned = value);
        }

        /// <summary>Sets whether this pawn's writing style is pinned (player-chosen, never auto-re-rolled).</summary>
        internal bool SetWritingStylePinned(Pawn pawn, bool pinned)
        {
            return SetVoicePin(pawn, pinned, (diary, value) => diary.writingStylePinned = value);
        }

        // Shared pin setter: only materializes a record when pinning (unpinning a pawn with no record is
        // already the desired state).
        private bool SetVoicePin(Pawn pawn, bool pinned, Action<PawnDiaryRecord, bool> apply)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, pinned);
            if (diary == null)
            {
                return !pinned;
            }

            apply(diary, pinned);
            return true;
        }

        /// <summary>
        /// Read-only snapshot of a pawn's effective psychotype for the public integration API. Returns
        /// null for an ineligible pawn; the rule is empty when the layer is disabled or Neutral. Does not
        /// run EnsureVoiceStage (no roll/mutation from a read).
        /// </summary>
        internal Integration.DiaryPsychotypeSnapshot PsychotypeSnapshotFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return null;
            }

            PsychotypeResolution resolution = ResolvePsychotypeForDisplay(pawn);
            return new Integration.DiaryPsychotypeSnapshot
            {
                psychotypeDefName = resolution.baseTypeDefName ?? string.Empty,
                label = resolution.baseTypeLabel ?? string.Empty,
                rule = resolution.rule ?? string.Empty
            };
        }

        // ---- External integration psychotype override (mirrors the writing-style override pair) --------

        /// <summary>
        /// Saves a source-owned external psychotype override above the pawn's base/custom rule. Validated
        /// by <see cref="Integration.PawnDiaryApi"/> before it reaches here.
        /// </summary>
        internal bool SetExternalPsychotypeOverride(Pawn pawn, string sourceId, string rule)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            string cleanedSource = PsychotypeText.CleanSourceId(sourceId);
            string cleanedRule = PsychotypeText.CleanExternalRule(rule);
            if (string.IsNullOrWhiteSpace(cleanedSource) || string.IsNullOrWhiteSpace(cleanedRule))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return false;
            }

            diary.externalPsychotypeOverrideSourceId = cleanedSource;
            diary.externalPsychotypeOverrideRule = cleanedRule;
            return true;
        }

        /// <summary>
        /// Clears an external psychotype override when no override exists or the same source owns it. A
        /// different source cannot silently remove another adapter's active override.
        /// </summary>
        internal bool ResetExternalPsychotypeOverride(Pawn pawn, string sourceId)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            string cleanedSource = PsychotypeText.CleanSourceId(sourceId);
            if (string.IsNullOrWhiteSpace(cleanedSource))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            string existing = PsychotypeText.CleanExternalRule(diary?.externalPsychotypeOverrideRule);
            if (string.IsNullOrWhiteSpace(existing))
            {
                return true;
            }

            string owner = PsychotypeText.CleanSourceId(diary.externalPsychotypeOverrideSourceId);
            if (!string.Equals(owner, cleanedSource, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            diary.externalPsychotypeOverrideRule = string.Empty;
            diary.externalPsychotypeOverrideSourceId = string.Empty;
            return true;
        }

        // Builds the resolution from the already-sanitized record fields. When the layer is off, the
        // metadata is still filled (so the editor can show the saved type) but the effective rule is
        // blanked so no psychotype block reaches the prompt.
        private PsychotypeResolution BuildPsychotypeResolution(PawnDiaryRecord diary)
        {
            string baseDefName = string.IsNullOrEmpty(diary?.psychotypeDefName)
                ? DiaryPsychotypes.NeutralDefName
                : diary.psychotypeDefName;
            DiaryPsychotypeDef type = DiaryPsychotypes.Resolve(baseDefName);
            string baseRule = DiaryPsychotypes.RuleFor(type.defName);
            string customRule = PsychotypeText.CleanRule(diary?.customPsychotypeRule);
            string externalRule = PsychotypeText.CleanExternalRule(diary?.externalPsychotypeOverrideRule);
            string externalSource = string.IsNullOrWhiteSpace(externalRule)
                ? string.Empty
                : PsychotypeText.CleanSourceId(diary?.externalPsychotypeOverrideSourceId);

            PsychotypeResolution resolution = PsychotypeResolutionPolicy.Resolve(
                baseRule, customRule, externalSource, externalRule);
            resolution.baseTypeDefName = type.defName;
            resolution.baseTypeLabel = type.label ?? string.Empty;

            // Disabled => omit the automatic outlook (pending rolls stay deferred elsewhere). An explicit
            // EXTERNAL integration override (e.g. the RimTalk bridge's persona-led voice) is an opt-in
            // signal from another mod, so it still applies — otherwise that integration would silently do
            // nothing whenever the player has the automatic psychotype layer switched off.
            if (!PsychotypesEnabled && resolution.source != PsychotypeRuleSource.ExternalApiOverride)
            {
                resolution.rule = string.Empty;
            }

            return resolution;
        }
    }
}
