// XML-backed PSYCHOTYPE Defs and lookup helpers. The psychotype is a pawn's outlook/temperament lens
// (what they notice, value, and fear), the second per-pawn voice layer alongside the writing style
// (DiaryPersonaDef). See design/PSYCHOTYPE_PLAN.md and AGENTS.md.
//
// This file is the impure Def/DefDatabase boundary; the pure roll/resolution logic lives in
// Source/Pipeline/PsychotypeRollPolicy.cs + PsychotypeResolutionPolicy.cs.
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One skill's stage-2 nudge for a psychotype: a skill defName and how many points a passion in it
    /// adds to this psychotype's roll weight. Authored on the Def so the affinities stay data-owned.
    /// </summary>
    public class DiaryPsychotypeSkillAffinity
    {
        public string skill;
        public int points = 1;
    }

    /// <summary>
    /// XML-backed psychotype for a pawn. Unlike <see cref="DiaryPersonaDef"/> (mechanics), the
    /// <see cref="rule"/> here is 1-2 semantic sentences describing how the pawn judges events. The
    /// label is picker text only and is deliberately NOT injected into the prompt (see
    /// <see cref="DiaryPsychotypes.RuleFor"/>).
    /// </summary>
    public class DiaryPsychotypeDef : Def
    {
        // The interiority rule folded into the LLM system prompt (without any label prefix).
        public string rule;

        // Adult family bucket: "grounded" / "inward" / "intense" / "anxious". Ignored for child defs.
        // Matched against PsychotypeRollPolicy's family constants; unknown values simply never win a roll.
        public string family = PsychotypeRollPolicy.FamilyGrounded;

        // Which catalog band this def belongs to: "adult" (default) or "child". Children re-roll onto
        // the adult catalog when they crystallize (cross psychotypeCrystallizationAgeYears).
        public string lifeStage = PsychotypeRollPolicy.StageAdult;

        // Model-facing stage-2 nudge data: which skill passions steer the roll toward this psychotype.
        // Initialized so old/partial defs that omit <skillAffinities> never NullReference.
        public List<DiaryPsychotypeSkillAffinity> skillAffinities = new List<DiaryPsychotypeSkillAffinity>();

        // Runtime-only marker set by the settings merge (DiaryPsychotypes.MergeWithSettings) for
        // player-created custom psychotypes; it is NEVER authored in XML, so built-in defs default false.
        // A custom row is excluded from the auto-roll (RollCandidates) but kept in the manual per-pawn
        // picker (PickerDefsFor) — that is the whole "manual-only customs" contract.
        public bool custom;
    }

    /// <summary>
    /// Central lookup/fallback helper for the psychotype catalog. Mirrors <see cref="DiaryPersonas"/>:
    /// XML defs are merged with settings-backed edits from the psychotype studio (built-in overrides +
    /// player customs) and cached. Custom rows are manual-only — flagged so <see cref="RollCandidates"/>
    /// skips them, while <see cref="PickerDefsFor"/> keeps them for hand assignment.
    /// </summary>
    internal static class DiaryPsychotypes
    {
        // The empty-rule "Neutral" psychotype defName. Pre-existing saves with generated entries adopt
        // this so established voices do not shift, and turning psychotypes off resolves to it.
        public const string NeutralDefName = "DiaryPsychotype_Neutral";

        // Hardcoded fallback used when no XML Defs are loaded at all (early startup / missing files).
        // Neutral by design: an empty rule contributes no prompt text.
        private static readonly DiaryPsychotypeDef Fallback = new DiaryPsychotypeDef
        {
            defName = NeutralDefName,
            label = "neutral",
            rule = string.Empty,
            lifeStage = PsychotypeRollPolicy.StageAdult
        };

        private static readonly List<DiaryPsychotypeDef> FallbackList = new List<DiaryPsychotypeDef> { Fallback };

        // Merged-catalog cache, invalidated on preset edits/load. Same shape as DiaryPersonas' cache:
        // reference-compare the base list + settings so the merge only reruns when something changed.
        private static IReadOnlyList<DiaryPsychotypeDef> cachedAll;
        private static IReadOnlyList<DiaryPsychotypeDef> cachedBaseList;
        private static int cachedBaseCount = -1;
        private static PawnDiarySettings cachedSettings;

        /// <summary>
        /// All psychotype defs the game should see: XML defs merged with settings-backed edits (built-in
        /// overrides + player customs), or the hardcoded Neutral fallback if none exist in XML. Cached and
        /// invalidated on preset edits/load, exactly like <see cref="DiaryPersonas.All"/>.
        /// </summary>
        public static IReadOnlyList<DiaryPsychotypeDef> All
        {
            get
            {
                List<DiaryPsychotypeDef> defs = DefDatabase<DiaryPsychotypeDef>.AllDefsListForReading;
                IReadOnlyList<DiaryPsychotypeDef> baseList = defs != null && defs.Count > 0
                    ? (IReadOnlyList<DiaryPsychotypeDef>)defs
                    : FallbackList;
                PawnDiarySettings settings = PawnDiaryMod.Settings;
                if (cachedAll != null
                    && ReferenceEquals(cachedBaseList, baseList)
                    && cachedBaseCount == baseList.Count
                    && ReferenceEquals(cachedSettings, settings))
                {
                    return cachedAll;
                }

                cachedBaseList = baseList;
                cachedBaseCount = baseList.Count;
                cachedSettings = settings;
                cachedAll = MergeWithSettings(baseList, settings);
                return cachedAll;
            }
        }

        /// <summary>Clears the merged psychotype catalog after settings-backed preset edits or load-time cleanup.</summary>
        public static void InvalidateCache()
        {
            cachedAll = null;
            cachedBaseList = null;
            cachedBaseCount = -1;
            cachedSettings = null;
        }

        /// <summary>The empty-rule Neutral psychotype, falling back to the hardcoded one if XML is absent.</summary>
        public static DiaryPsychotypeDef Neutral
        {
            get { return ForDefName(NeutralDefName) ?? Fallback; }
        }

        /// <summary>Looks up a psychotype by defName, or null if not found / the name is blank.</summary>
        public static DiaryPsychotypeDef ForDefName(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return null;
            }

            return All.FirstOrDefault(type => type.defName == defName);
        }

        /// <summary>Resolves a defName to its psychotype, falling back to Neutral if missing/unknown.</summary>
        public static DiaryPsychotypeDef Resolve(string defName)
        {
            return ForDefName(defName) ?? Neutral;
        }

        /// <summary>
        /// Returns the prompt-facing rule for a psychotype defName, WITHOUT any label prefix. This is a
        /// deliberate divergence from <see cref="DiaryPersonas.RuleFor"/>: psychotype labels are picker
        /// text, never prompt text (the pawn's outlook shows through the entry, it is never named).
        /// </summary>
        public static string RuleFor(string defName)
        {
            DiaryPsychotypeDef type = Resolve(defName);
            return type == null ? string.Empty : (type.rule ?? string.Empty);
        }

        /// <summary>
        /// The psychotype defs available in the picker for a stage band ("adult"/"child"), Neutral
        /// first. Used by the per-pawn UI so a child never sees adult options and vice versa.
        /// </summary>
        public static List<DiaryPsychotypeDef> PickerDefsFor(string stageBand)
        {
            string wanted = NormalizeStage(stageBand);
            List<DiaryPsychotypeDef> picker = new List<DiaryPsychotypeDef>();
            DiaryPsychotypeDef neutral = Neutral;
            if (neutral != null)
            {
                picker.Add(neutral);
            }

            IReadOnlyList<DiaryPsychotypeDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryPsychotypeDef type = all[i];
                if (type == null || type.defName == NeutralDefName)
                {
                    continue;
                }

                if (string.Equals(NormalizeStage(type.lifeStage), wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    picker.Add(type);
                }
            }

            return picker;
        }

        /// <summary>
        /// Projects every non-Neutral psychotype def into a pure <see cref="PsychotypeCandidate"/> for
        /// the roll. Both stage bands are included; the pure roll filters to the pawn's band itself.
        /// </summary>
        public static List<PsychotypeCandidate> RollCandidates()
        {
            List<PsychotypeCandidate> candidates = new List<PsychotypeCandidate>();
            IReadOnlyList<DiaryPsychotypeDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryPsychotypeDef type = all[i];
                if (type == null || string.IsNullOrWhiteSpace(type.defName) || type.defName == NeutralDefName)
                {
                    continue;
                }

                // Manual-only customs never enter the auto-roll; they are hand-picked from the per-pawn
                // editor. Built-in overrides keep custom=false, so they still roll with their edited family.
                if (type.custom)
                {
                    continue;
                }

                Dictionary<string, int> affinities = new Dictionary<string, int>();
                if (type.skillAffinities != null)
                {
                    for (int a = 0; a < type.skillAffinities.Count; a++)
                    {
                        DiaryPsychotypeSkillAffinity affinity = type.skillAffinities[a];
                        if (affinity != null && !string.IsNullOrWhiteSpace(affinity.skill))
                        {
                            affinities[affinity.skill] = affinity.points;
                        }
                    }
                }

                candidates.Add(new PsychotypeCandidate
                {
                    defName = type.defName,
                    family = string.IsNullOrWhiteSpace(type.family) ? PsychotypeRollPolicy.FamilyGrounded : type.family,
                    stage = NormalizeStage(type.lifeStage),
                    skillAffinities = affinities
                });
            }

            return candidates;
        }

        // Builds the effective runtime psychotype catalog from XML defs plus settings-based edits/customs.
        // Mirrors DiaryPersonas.MergeWithSettings. Overrides keep the built-in defName (custom=false) so
        // they still roll; appended customs are flagged custom=true so RollCandidates skips them.
        private static IReadOnlyList<DiaryPsychotypeDef> MergeWithSettings(IReadOnlyList<DiaryPsychotypeDef> baseList,
            PawnDiarySettings settings)
        {
            if (settings == null)
            {
                return baseList;
            }

            settings.psychotypePresets.EnsureList();
            if (settings.psychotypePresets.presets == null || settings.psychotypePresets.presets.Count == 0)
            {
                return baseList;
            }

            List<DiaryPsychotypeDef> merged = new List<DiaryPsychotypeDef>();
            for (int i = 0; i < baseList.Count; i++)
            {
                DiaryPsychotypeDef source = baseList[i];
                if (source == null || string.IsNullOrWhiteSpace(source.defName))
                {
                    continue;
                }

                PsychotypePresetConfig overridePreset = settings.psychotypePresets.OverrideFor(source.defName);
                merged.Add(BuildPsychotype(source, overridePreset));
            }

            List<PsychotypePresetConfig> custom = settings.psychotypePresets.Customs();
            for (int i = 0; i < custom.Count; i++)
            {
                PsychotypePresetConfig customPreset = custom[i];
                if (customPreset == null || string.IsNullOrWhiteSpace(customPreset.defName))
                {
                    continue;
                }

                merged.Add(BuildPsychotype(null, customPreset));
            }

            return merged.Count > 0 ? merged : FallbackList;
        }

        // Projects one XML source def and/or one settings preset into an effective psychotype. For an
        // override (source + preset), edited label/rule/family win but the source's lifeStage and
        // skillAffinities (its roll identity) are preserved. For a custom (source == null), the row is a
        // brand-new adult psychotype flagged custom=true with no skill affinities.
        private static DiaryPsychotypeDef BuildPsychotype(DiaryPsychotypeDef source, PsychotypePresetConfig preset)
        {
            DiaryPsychotypeDef type = new DiaryPsychotypeDef();
            type.defName = preset?.defName ?? source?.defName ?? string.Empty;
            type.label = preset?.label ?? source?.label ?? string.Empty;
            type.rule = preset?.rule ?? source?.rule ?? string.Empty;
            type.family = PsychotypeRollPolicy.NormalizeFamily(preset?.family ?? source?.family);
            // Custom rows are adult (the editor never creates child customs); overrides keep the XML band.
            type.lifeStage = NormalizeStage(source?.lifeStage);
            type.custom = source == null;

            // Preserve the source's skill-passion nudges so an overridden built-in keeps its roll behavior.
            // Customs never roll, so they carry none.
            type.skillAffinities = new List<DiaryPsychotypeSkillAffinity>();
            if (source?.skillAffinities != null)
            {
                for (int i = 0; i < source.skillAffinities.Count; i++)
                {
                    DiaryPsychotypeSkillAffinity affinity = source.skillAffinities[i];
                    if (affinity != null && !string.IsNullOrWhiteSpace(affinity.skill))
                    {
                        type.skillAffinities.Add(new DiaryPsychotypeSkillAffinity
                        {
                            skill = affinity.skill,
                            points = affinity.points
                        });
                    }
                }
            }

            return type;
        }

        // Blank/unknown lifeStage counts as adult (the common case), keeping old/partial defs safe.
        private static string NormalizeStage(string lifeStage)
        {
            return string.Equals(lifeStage, PsychotypeRollPolicy.StageChild, System.StringComparison.OrdinalIgnoreCase)
                ? PsychotypeRollPolicy.StageChild
                : PsychotypeRollPolicy.StageAdult;
        }
    }
}
