using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // XML-backed writing style for a pawn. The class/field names keep "Persona" for save and Def
    // compatibility with older Pawn Diary versions, but the player-facing feature is writing styles.
    public class DiaryPersonaDef : Def
    {
        // The writing-style rule injected into the LLM system prompt.
        public string rule;

        // Coarse internal keyword tags (e.g. "grim", "warm", "anxious", "void") used only to bias the
        // *initial* style roll toward prose that fits the pawn's traits/backstory. They are
        // matched against PersonaAffinity's pawn -> theme logic; "void" also gets a creepjoiner-only
        // boost. Tags are never shown to the player, so they are NOT localized. Untagged styles
        // simply ride the base weight.
        // Initialized so old/partial defs that omit <themes> never NullReference.
        public List<string> themes = new List<string>();
    }

    // Central lookup/fallback helper for the writing-style catalog. RimWorld loads Defs from
    // 1.6/Defs/DiaryPersonaDefs.xml; the hardcoded fallback keeps saves usable if XML is missing.
    public static class DiaryPersonas
    {
        // Fixed vocabulary used by PersonaAffinity and the writing-style settings editor. Players can
        // assign only these tags to custom styles, which keeps weighting behavior predictable.
        public static readonly string[] PredefinedThemeTags =
        {
            "grim",
            "warm",
            "hostile",
            "anxious",
            "analytical",
            "dramatic",
            "social",
            "whimsical",
            "noble",
            "void"
        };

        // Hardcoded fallback used when no XML Defs are loaded at all (e.g. during early startup or missing mod files).
        private static readonly DiaryPersonaDef Fallback = new DiaryPersonaDef
        {
            defName = "DiaryPersona_StoicSurvivor",
            label = "three-line-note",
            rule = "This pawn tends to write exactly three short diary sentences: fact, problem, next action. No flourish. For example: \"Wall patched. Rain still gets through. I will fix the gap tomorrow.\""
        };

        // Wrapped in a list so All can return a non-null IReadOnlyList even with zero XML defs.
        private static readonly List<DiaryPersonaDef> FallbackList = new List<DiaryPersonaDef> { Fallback };
        private static IReadOnlyList<DiaryPersonaDef> cachedAll;
        private static IReadOnlyList<DiaryPersonaDef> cachedBaseList;
        private static int cachedBaseCount = -1;
        private static PawnDiarySettings cachedSettings;

        /// <summary>
        /// All loaded writing-style defs, or the hardcoded fallback list if none exist in XML.
        /// </summary>
        public static IReadOnlyList<DiaryPersonaDef> All
        {
            get
            {
                List<DiaryPersonaDef> defs = DefDatabase<DiaryPersonaDef>.AllDefsListForReading;
                IReadOnlyList<DiaryPersonaDef> baseList = defs != null && defs.Count > 0 ? defs : FallbackList;
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

        /// <summary>
        /// Clears the merged style catalog after settings-backed preset edits or load-time cleanup.
        /// </summary>
        public static void InvalidateCache()
        {
            cachedAll = null;
            cachedBaseList = null;
            cachedBaseCount = -1;
            cachedSettings = null;
        }

        /// <summary>
        /// The default writing style, sourced from DiaryPromptDef.xml's defaultPersonaDefName,
        /// with cascading fallbacks to the first available def then the hardcoded Fallback.
        /// </summary>
        public static DiaryPersonaDef Default
        {
            get
            {
                // The default style is itself configurable in DiaryPromptDef.xml.
                return ForDefName(DiaryPrompts.Current.defaultPersonaDefName) ?? All.FirstOrDefault() ?? Fallback;
            }
        }

        /// <summary>
        /// Picks the initial style for a brand-new pawn diary record. Existing records keep
        /// their saved style; this is only used the first time a pawn enters the diary system.
        /// </summary>
        public static DiaryPersonaDef RandomStartingPersona()
        {
            IReadOnlyList<DiaryPersonaDef> personas = All;
            if (personas == null || personas.Count == 0)
            {
                return Default ?? Fallback;
            }

            return personas[Rand.Range(0, personas.Count)] ?? Default ?? Fallback;
        }

        // Base weight every style gets so the catalog never starves; the theme bonus is layered
        // on top. Multiplied per duplicate already in use, so a style another colonist holds is
        // ~quarter as likely each time (soft penalty, never fully excluded). Floor keeps weights
        // positive so weighted selection cannot divide by zero. Tunable.
        private const float BaseWeight = 1f;
        private const float DuplicatePenalty = 0.25f;
        private const float WeightFloor = 0.0001f;

        /// <summary>
        /// Picks the initial style for a NEW pawn, biased toward styles whose <c>themes</c>
        /// fit the pawn's traits/backstory (see <see cref="PersonaAffinity"/>) and softly penalized
        /// for styles already used by other colonists (<paramref name="usedCounts"/> maps a
        /// style defName to how many current colonists already write in it). Falls back to a flat
        /// random pick if weights are unusable. Existing records keep their saved style — this
        /// only runs the first time a pawn enters the diary system.
        /// </summary>
        public static DiaryPersonaDef WeightedStartingPersona(Pawn pawn, IDictionary<string, int> usedCounts)
        {
            IReadOnlyList<DiaryPersonaDef> personas = All;
            if (personas == null || personas.Count == 0)
            {
                return Default ?? Fallback;
            }

            // Sum of all weights; each persona's chance is its weight / this total.
            float total = 0f;
            float[] weights = new float[personas.Count];
            for (int i = 0; i < personas.Count; i++)
            {
                DiaryPersonaDef persona = personas[i];
                if (persona == null)
                {
                    continue;
                }

                float weight = BaseWeight + PersonaAffinity.ThemeBonusFor(persona, pawn);

                // Apply the soft duplicate penalty once per colonist already using this style.
                int used = 0;
                if (usedCounts != null && persona.defName != null && usedCounts.TryGetValue(persona.defName, out used) && used > 0)
                {
                    weight *= Mathf.Pow(DuplicatePenalty, used);
                }

                weight = Mathf.Max(weight, WeightFloor);
                weights[i] = weight;
                total += weight;
            }

            if (total <= 0f)
            {
                return RandomStartingPersona();
            }

            // Standard weighted pick: walk the cumulative weights until we pass the roll.
            float roll = Rand.Range(0f, total);
            float cumulative = 0f;
            for (int i = 0; i < personas.Count; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative && personas[i] != null)
                {
                    return personas[i];
                }
            }

            return RandomStartingPersona();
        }

        /// <summary>
        /// Looks up a writing style by defName, returning null if not found or the name is blank.
        /// </summary>
        public static DiaryPersonaDef ForDefName(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return null;
            }

            return All.FirstOrDefault(persona => persona.defName == defName);
        }

        /// <summary>
        /// Resolves a defName to its style, falling back to Default if the name is missing or unknown.
        /// </summary>
        public static DiaryPersonaDef Resolve(string defName)
        {
            return ForDefName(defName) ?? Default;
        }

        // Include the label in the prompt so debug output clearly shows which style was used.
        public static string RuleFor(string defName)
        {
            DiaryPersonaDef persona = Resolve(defName);
            if (persona == null)
            {
                return string.Empty;
            }

            string rule = persona.rule ?? string.Empty;
            if (string.IsNullOrWhiteSpace(persona.label))
            {
                return rule;
            }

            return persona.label + ": " + rule;
        }

        // Builds the effective runtime style catalog from XML defs plus settings-based edits/custom rows.
        private static IReadOnlyList<DiaryPersonaDef> MergeWithSettings(IReadOnlyList<DiaryPersonaDef> baseList,
            PawnDiarySettings settings)
        {
            if (settings == null)
            {
                return baseList;
            }

            settings.personaPresets.EnsureList();
            if (settings.personaPresets.presets == null || settings.personaPresets.presets.Count == 0)
            {
                return baseList;
            }

            List<DiaryPersonaDef> merged = new List<DiaryPersonaDef>();
            for (int i = 0; i < baseList.Count; i++)
            {
                DiaryPersonaDef source = baseList[i];
                if (source == null || string.IsNullOrWhiteSpace(source.defName))
                {
                    continue;
                }

                PersonaPresetConfig overridePreset = settings.personaPresets.OverrideFor(source.defName);
                merged.Add(BuildPersona(source, overridePreset));
            }

            List<PersonaPresetConfig> custom = settings.personaPresets.Customs();
            for (int i = 0; i < custom.Count; i++)
            {
                PersonaPresetConfig customPreset = custom[i];
                if (customPreset == null || string.IsNullOrWhiteSpace(customPreset.defName))
                {
                    continue;
                }

                merged.Add(BuildPersona(null, customPreset));
            }

            return merged.Count > 0 ? merged : FallbackList;
        }

        private static DiaryPersonaDef BuildPersona(DiaryPersonaDef source, PersonaPresetConfig settingsPreset)
        {
            DiaryPersonaDef persona = new DiaryPersonaDef();
            persona.defName = settingsPreset?.defName ?? source?.defName ?? string.Empty;
            persona.label = settingsPreset?.label ?? source?.label ?? string.Empty;
            persona.rule = settingsPreset?.rule ?? source?.rule ?? string.Empty;
            persona.themes = new List<string>();

            List<string> themes = settingsPreset?.themes ?? source?.themes;
            if (themes != null)
            {
                for (int i = 0; i < themes.Count; i++)
                {
                    string theme = themes[i];
                    if (!string.IsNullOrWhiteSpace(theme))
                    {
                        persona.themes.Add(theme);
                    }
                }
            }

            return persona;
        }
    }
}
