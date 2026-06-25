// Writing-style (persona) preset store, extracted from PawnDiarySettings so the save DTO stops
// owning catalog mutation. Holds the saved list of XML override rows plus user-created custom
// styles, and all CRUD/normalization around them. PawnDiarySettings constructs one instance and
// calls its ExposeData from its own; the Scribe key "personaPresets" is unchanged so existing
// saves keep loading unchanged. See AGENTS.md ("IExposable") for the save model primer.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One editable writing-style preset row persisted in settings. Rows are either:
    /// - an override of an XML style Def (custom = false, defName matches the Def), or
    /// - a fully custom style created in settings (custom = true).
    /// The type name keeps "Persona" for save compatibility with older settings.
    /// Implements <see cref="IExposable"/> so RimWorld can save/load it.
    /// </summary>
    public class PersonaPresetConfig : IExposable
    {
        // Stable key used everywhere styles are referenced (per-pawn record, prompt context, picker).
        public string defName = string.Empty;
        // Human-readable picker label shown in UI.
        public string label = string.Empty;
        // Writing-style rule appended to first-person prompts.
        public string rule = string.Empty;
        // Internal theme tags used for weighted first-roll style selection.
        public List<string> themes = new List<string>();
        // True when this row is a user-created style (not an override of an XML Def).
        public bool custom;

        public PersonaPresetConfig()
        {
        }

        public PersonaPresetConfig(string defName, string label, string rule, IEnumerable<string> themes, bool custom)
        {
            this.defName = defName ?? string.Empty;
            this.label = label ?? string.Empty;
            this.rule = rule ?? string.Empty;
            this.themes = PersonaPresetStore.NormalizeThemes(themes);
            this.custom = custom;
        }

        // Reads/writes the row fields on save and load (Scribe is RimWorld's serializer).
        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", string.Empty);
            Scribe_Values.Look(ref label, "label", string.Empty);
            Scribe_Values.Look(ref rule, "rule", string.Empty);
            Scribe_Collections.Look(ref themes, "themes", LookMode.Value);
            Scribe_Values.Look(ref custom, "custom", false);
        }
    }

    /// <summary>
    /// Saved catalog of writing-style edits: XML override rows plus user-created custom styles.
    /// Implements <see cref="IExposable"/> so RimWorld can save/load it.
    /// </summary>
    public class PersonaPresetStore : IExposable
    {
        // The saved rows: XML override rows (custom=false) and user-created styles (custom=true).
        public List<PersonaPresetConfig> presets = new List<PersonaPresetConfig>();

        /// <summary>Guarantees the preset list is non-null (defensive against deserialization gaps).</summary>
        public void EnsureList()
        {
            if (presets == null)
            {
                presets = new List<PersonaPresetConfig>();
            }
        }

        /// <summary>Finds an override row for an XML writing-style Def by defName.</summary>
        public PersonaPresetConfig OverrideFor(string defName)
        {
            EnsureList();
            return presets.FirstOrDefault(preset =>
                preset != null
                && !preset.custom
                && preset.defName == defName);
        }

        /// <summary>Finds a custom writing-style row by defName.</summary>
        public PersonaPresetConfig CustomFor(string defName)
        {
            EnsureList();
            return presets.FirstOrDefault(preset =>
                preset != null
                && preset.custom
                && preset.defName == defName);
        }

        /// <summary>Returns only user-created writing styles (custom=true) for catalog merging and UI.</summary>
        public List<PersonaPresetConfig> Customs()
        {
            EnsureList();
            return presets
                .Where(preset => preset != null && preset.custom)
                .ToList();
        }

        /// <summary>Upserts an override row for an XML writing-style Def.</summary>
        public void SetOverride(string defName, string label, string rule, IEnumerable<string> themes)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return;
            }

            EnsureList();
            PersonaPresetConfig existing = OverrideFor(defName);
            if (existing == null)
            {
                existing = new PersonaPresetConfig(defName, label, rule, themes, false);
                presets.Add(existing);
            }
            else
            {
                existing.label = label ?? string.Empty;
                existing.rule = rule ?? string.Empty;
                existing.themes = NormalizeThemes(themes);
                existing.custom = false;
            }

            DiaryPersonas.InvalidateCache();
        }

        /// <summary>Removes an override row for an XML writing-style Def, restoring XML defaults.</summary>
        public void ResetOverride(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || presets == null)
            {
                return;
            }

            if (presets.RemoveAll(preset => preset != null && !preset.custom && preset.defName == defName) > 0)
            {
                DiaryPersonas.InvalidateCache();
            }
        }

        /// <summary>Adds a new custom writing-style row and returns its generated defName.</summary>
        public string AddCustom()
        {
            EnsureList();
            string defName = NextCustomDefName();
            // .Translate() is main-thread only; this is only ever called from the settings UI thread.
            presets.Add(new PersonaPresetConfig(
                defName,
                "PawnDiary.Settings.NewPersonaLabel".Translate().Resolve(),
                string.Empty,
                new[] { DiaryPersonas.PredefinedThemeTags[0] },
                true));
            DiaryPersonas.InvalidateCache();
            return defName;
        }

        /// <summary>Deletes one user-created writing-style row.</summary>
        public void RemoveCustom(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || presets == null)
            {
                return;
            }

            if (presets.RemoveAll(preset => preset != null && preset.custom && preset.defName == defName) > 0)
            {
                DiaryPersonas.InvalidateCache();
            }
        }

        /// <summary>Removes all style overrides and custom styles, restoring pure XML style defs.</summary>
        public void ResetAll()
        {
            EnsureList();
            presets.Clear();
            DiaryPersonas.InvalidateCache();
        }

        /// <summary>
        /// Normalizes the saved rows after load: strips invalid/unknown theme tags, guarantees
        /// custom styles keep at least one predefined tag, and drops malformed rows and duplicate
        /// keys so the catalog stays safe and deterministic.
        /// </summary>
        public void Normalize()
        {
            EnsureList();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<PersonaPresetConfig> normalized = new List<PersonaPresetConfig>();
            for (int i = 0; i < presets.Count; i++)
            {
                PersonaPresetConfig preset = presets[i];
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

                if (preset.themes == null)
                {
                    preset.themes = new List<string>();
                }

                preset.themes = NormalizeThemes(preset.themes);

                if (preset.custom && preset.themes.Count == 0)
                {
                    preset.themes.Add(DiaryPersonas.PredefinedThemeTags[0]);
                }

                normalized.Add(preset);
            }

            presets = normalized;
        }

        // Reads/writes the preset list on save and load. This is called from
        // PawnDiarySettings.ExposeData while Scribe's cur parent node is still the settings node,
        // so <personaPresets> is written at the same level as before extraction — existing saves
        // keep loading unchanged.
        public void ExposeData()
        {
            EnsureList();
            Scribe_Collections.Look(ref presets, "personaPresets", LookMode.Deep);
        }

        // Generates deterministic custom style keys so they are stable across saves and merges.
        private string NextCustomDefName()
        {
            const string prefix = "DiaryPersona_Custom_";
            int next = 1;
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);

            List<DiaryPersonaDef> defs = DefDatabase<DiaryPersonaDef>.AllDefsListForReading;
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

        /// <summary>
        /// Filters a theme list to the predefined vocabulary, lowercased and de-duplicated. Shared
        /// by the row constructor and the upsert/normalize paths so theme policy has one home.
        /// </summary>
        internal static List<string> NormalizeThemes(IEnumerable<string> themes)
        {
            HashSet<string> allowedTags = new HashSet<string>(DiaryPersonas.PredefinedThemeTags, StringComparer.Ordinal);
            return themes == null
                ? new List<string>()
                : themes
                    .Where(theme => !string.IsNullOrWhiteSpace(theme))
                    .Select(theme => theme.Trim().ToLowerInvariant())
                    .Where(theme => allowedTags.Contains(theme))
                    .Distinct()
                    .ToList();
        }
    }
}
