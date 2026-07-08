// Psychotype (outlook) preset store, the sibling of PersonaPresetStore for the second per-pawn voice
// layer. Holds the saved list of XML override rows plus user-created custom psychotypes, and all
// CRUD/normalization around them. PawnDiarySettings constructs one instance and calls its ExposeData
// from its own under the Scribe key "psychotypePresets" (a NEW key, so older saves simply have none).
// See AGENTS.md ("IExposable") for the save model primer.
//
// One deliberate divergence from writing-style presets: CUSTOM psychotypes are edited here but never
// auto-rolled onto new pawns. The runtime merge (DiaryPsychotypes.MergeWithSettings) flags custom rows
// so they stay OUT of the roll while remaining in the per-pawn picker for manual assignment. Built-in
// OVERRIDES do apply everywhere, the auto-roll included.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One editable psychotype preset row persisted in settings. Rows are either:
    /// - an override of an XML psychotype Def (custom = false, defName matches the Def), or
    /// - a fully custom psychotype created in settings (custom = true).
    /// Implements <see cref="IExposable"/> so RimWorld can save/load it. Mirrors <see cref="PersonaPresetConfig"/>.
    /// </summary>
    public class PsychotypePresetConfig : IExposable
    {
        // Stable key used everywhere psychotypes are referenced (per-pawn record, prompt context, picker).
        public string defName = string.Empty;
        // Human-readable picker label. Never injected into the prompt (see DiaryPsychotypes.RuleFor).
        public string label = string.Empty;
        // The 1-2 semantic sentences folded into the system prompt as the pawn's outlook lens.
        public string rule = string.Empty;
        // Adult roll bucket (grounded/inward/intense/anxious). Steers the auto-roll for OVERRIDES of a
        // built-in; on custom rows it only groups the row, since customs are never auto-rolled.
        public string family = PsychotypeRollPolicy.FamilyGrounded;
        // True when this row is a user-created psychotype (not an override of an XML Def).
        public bool custom;

        public PsychotypePresetConfig()
        {
        }

        public PsychotypePresetConfig(string defName, string label, string rule, string family, bool custom)
        {
            this.defName = defName ?? string.Empty;
            this.label = label ?? string.Empty;
            this.rule = rule ?? string.Empty;
            this.family = PsychotypeRollPolicy.NormalizeFamily(family);
            this.custom = custom;
        }

        // Reads/writes the row fields on save and load (Scribe is RimWorld's serializer).
        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", string.Empty);
            Scribe_Values.Look(ref label, "label", string.Empty);
            Scribe_Values.Look(ref rule, "rule", string.Empty);
            Scribe_Values.Look(ref family, "family", PsychotypeRollPolicy.FamilyGrounded);
            Scribe_Values.Look(ref custom, "custom", false);
        }
    }

    /// <summary>
    /// Saved catalog of psychotype edits: XML override rows plus user-created custom psychotypes.
    /// Implements <see cref="IExposable"/> so RimWorld can save/load it. Mirrors <see cref="PersonaPresetStore"/>.
    /// </summary>
    public class PsychotypePresetStore : IExposable
    {
        // The saved rows: XML override rows (custom=false) and user-created psychotypes (custom=true).
        public List<PsychotypePresetConfig> presets = new List<PsychotypePresetConfig>();

        /// <summary>Guarantees the preset list is non-null (defensive against deserialization gaps).</summary>
        public void EnsureList()
        {
            if (presets == null)
            {
                presets = new List<PsychotypePresetConfig>();
            }
        }

        /// <summary>Finds an override row for an XML psychotype Def by defName.</summary>
        public PsychotypePresetConfig OverrideFor(string defName)
        {
            EnsureList();
            return presets.FirstOrDefault(preset =>
                preset != null
                && !preset.custom
                && preset.defName == defName);
        }

        /// <summary>Finds a custom psychotype row by defName.</summary>
        public PsychotypePresetConfig CustomFor(string defName)
        {
            EnsureList();
            return presets.FirstOrDefault(preset =>
                preset != null
                && preset.custom
                && preset.defName == defName);
        }

        /// <summary>Returns only user-created psychotypes (custom=true) for catalog merging and UI.</summary>
        public List<PsychotypePresetConfig> Customs()
        {
            EnsureList();
            return presets
                .Where(preset => preset != null && preset.custom)
                .ToList();
        }

        /// <summary>Upserts an override row for an XML psychotype Def.</summary>
        public void SetOverride(string defName, string label, string rule, string family)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            EnsureList();
            PsychotypePresetConfig existing = OverrideFor(defName);
            if (existing == null)
            {
                existing = new PsychotypePresetConfig(defName, label, rule, family, false);
                presets.Add(existing);
            }
            else
            {
                existing.label = label ?? string.Empty;
                existing.rule = rule ?? string.Empty;
                existing.family = PsychotypeRollPolicy.NormalizeFamily(family);
                existing.custom = false;
            }

            DiaryPsychotypes.InvalidateCache();
        }

        /// <summary>Removes an override row for an XML psychotype Def, restoring XML defaults.</summary>
        public void ResetOverride(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || presets == null)
            {
                return;
            }

            if (presets.RemoveAll(preset => preset != null && !preset.custom && preset.defName == defName) > 0)
            {
                DiaryPsychotypes.InvalidateCache();
            }
        }

        /// <summary>Adds a new custom psychotype row and returns its generated defName.</summary>
        public string AddCustom()
        {
            EnsureList();
            string defName = NextCustomDefName();
            // .Translate() is main-thread only; this is only ever called from the settings UI thread.
            presets.Add(new PsychotypePresetConfig(
                defName,
                "PawnDiary.Settings.NewPsychotypeLabel".Translate().Resolve(),
                string.Empty,
                PsychotypeRollPolicy.FamilyGrounded,
                true));
            DiaryPsychotypes.InvalidateCache();
            return defName;
        }

        /// <summary>Deletes one user-created psychotype row.</summary>
        public void RemoveCustom(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || presets == null)
            {
                return;
            }

            if (presets.RemoveAll(preset => preset != null && preset.custom && preset.defName == defName) > 0)
            {
                DiaryPsychotypes.InvalidateCache();
            }
        }

        /// <summary>Removes all overrides and custom psychotypes, restoring pure XML psychotype defs.</summary>
        public void ResetAll()
        {
            EnsureList();
            presets.Clear();
            DiaryPsychotypes.InvalidateCache();
        }

        /// <summary>
        /// Normalizes the saved rows after load: clamps each family to a valid roll bucket and drops
        /// malformed rows and duplicate keys so the catalog stays safe and deterministic.
        /// </summary>
        public void Normalize()
        {
            EnsureList();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<PsychotypePresetConfig> normalized = new List<PsychotypePresetConfig>();
            for (int i = 0; i < presets.Count; i++)
            {
                PsychotypePresetConfig preset = presets[i];
                if (preset == null || string.IsNullOrWhiteSpace(preset.defName))
                {
                    continue;
                }

                if (!seen.Add(preset.defName))
                {
                    continue;
                }

                preset.label = preset.label ?? string.Empty;
                preset.rule = preset.rule ?? string.Empty;
                preset.family = PsychotypeRollPolicy.NormalizeFamily(preset.family);

                normalized.Add(preset);
            }

            presets = normalized;
        }

        // Reads/writes the preset list on save and load. Called from PawnDiarySettings.ExposeData while
        // Scribe's cur parent node is the settings node, so <psychotypePresets> sits beside <personaPresets>.
        public void ExposeData()
        {
            EnsureList();
            Scribe_Collections.Look(ref presets, "psychotypePresets", LookMode.Deep);
        }

        // Generates deterministic custom psychotype keys so they are stable across saves and merges.
        private string NextCustomDefName()
        {
            const string prefix = "DiaryPsychotype_Custom_";
            int next = 1;
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);

            List<DiaryPsychotypeDef> defs = DefDatabase<DiaryPsychotypeDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(defs[i]?.defName))
                    {
                        used.Add(defs[i].defName);
                    }
                }
            }

            for (int i = 0; i < presets.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(presets[i]?.defName))
                {
                    used.Add(presets[i].defName);
                }
            }

            while (used.Contains(prefix + next))
            {
                next++;
            }

            return prefix + next;
        }
    }
}
