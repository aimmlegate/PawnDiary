// Verse boundary for the Add API provider catalog. RimWorld loads the mutable Def rows and applies
// DefInjected localization; this adapter immediately copies their connection defaults into the pure
// ApiProviderPresetSnapshot contract used by validation and the settings UI.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// XML-owned defaults for one provider preset. The inherited label/description are localized via
    /// DefInjected and explain the same protocol mode when an existing lane switches modes.
    /// </summary>
    public class DiaryApiProviderPresetDef : Def
    {
        public int displayOrder;
        public ApiCompatibilityMode apiMode;
        public string baseUrl;
        public ApiAuthMode authMode;
        public string customAuthHeaderName;
    }

    /// <summary>Localized UI text paired with one detached, validated preset snapshot.</summary>
    internal sealed class DiaryApiProviderPresetView
    {
        public ApiProviderPresetSnapshot preset;
        public string label;
        public string description;
    }

    /// <summary>Loads the five provider Defs and supplies localized fallbacks if XML is unavailable.</summary>
    internal static class DiaryApiProviderPresets
    {
        private static List<DiaryApiProviderPresetView> cachedViews;
        private static LoadedLanguage cachedLanguage;

        /// <summary>
        /// Returns a deterministic five-row catalog. A malformed connection row falls back to pure safe
        /// defaults while retaining its localized Def text; a missing Def uses Keyed fallback copy. The
        /// five localized view rows are cached only for the active language, then rebuilt on a switch.
        /// </summary>
        public static List<DiaryApiProviderPresetView> ForUi()
        {
            if (cachedViews != null && cachedLanguage == LanguageDatabase.activeLanguage)
            {
                return cachedViews;
            }

            List<ApiProviderPresetSnapshot> fallbacks = ApiProviderPresetPolicy.BuildCatalog(null);
            List<ApiProviderPresetSnapshot> candidates = new List<ApiProviderPresetSnapshot>();
            Dictionary<string, DiaryApiProviderPresetDef> loadedByKey =
                new Dictionary<string, DiaryApiProviderPresetDef>();

            for (int i = 0; i < fallbacks.Count; i++)
            {
                string key = fallbacks[i].presetKey;
                DiaryApiProviderPresetDef source =
                    DefDatabase<DiaryApiProviderPresetDef>.GetNamedSilentFail(key);
                if (source == null)
                {
                    continue;
                }

                loadedByKey[key] = source;
                candidates.Add(Snapshot(source));
            }

            List<ApiProviderPresetSnapshot> catalog =
                ApiProviderPresetPolicy.BuildCatalog(candidates);
            List<DiaryApiProviderPresetView> views =
                new List<DiaryApiProviderPresetView>(catalog.Count);
            for (int i = 0; i < catalog.Count; i++)
            {
                ApiProviderPresetSnapshot preset = catalog[i];
                loadedByKey.TryGetValue(preset.presetKey, out DiaryApiProviderPresetDef source);
                views.Add(new DiaryApiProviderPresetView
                {
                    preset = preset,
                    label = LocalizedLabel(source, preset.presetKey),
                    description = LocalizedDescription(source, preset.presetKey)
                });
            }

            // Do not cache an early missing-Def fallback: content may still be loading. Once all five
            // source Defs exist, retain the tiny catalog for this language and rebuild on a switch.
            if (loadedByKey.Count == fallbacks.Count)
            {
                cachedViews = views;
                cachedLanguage = LanguageDatabase.activeLanguage;
            }

            return views;
        }

        /// <summary>Returns the localized explanation for one live protocol mode.</summary>
        public static string DescriptionForMode(ApiCompatibilityMode mode)
        {
            string key = ApiProviderPresetPolicy.PresetKeyForMode(mode);
            List<DiaryApiProviderPresetView> views = ForUi();
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].preset != null && views[i].preset.presetKey == key)
                {
                    return views[i].description ?? string.Empty;
                }
            }

            return FallbackDescriptionKey(key).Translate().ToString();
        }

        private static ApiProviderPresetSnapshot Snapshot(DiaryApiProviderPresetDef source)
        {
            return new ApiProviderPresetSnapshot
            {
                presetKey = source.defName,
                displayOrder = source.displayOrder,
                apiMode = source.apiMode,
                baseUrl = source.baseUrl,
                authMode = source.authMode,
                customAuthHeaderName = source.customAuthHeaderName
            };
        }

        private static string LocalizedLabel(DiaryApiProviderPresetDef source, string key)
        {
            string value = source?.LabelCap.ToString();
            return string.IsNullOrWhiteSpace(value)
                ? FallbackLabelKey(key).Translate().ToString()
                : value;
        }

        private static string LocalizedDescription(DiaryApiProviderPresetDef source, string key)
        {
            string value = source?.description;
            return string.IsNullOrWhiteSpace(value)
                ? FallbackDescriptionKey(key).Translate().ToString()
                : value;
        }

        private static string FallbackLabelKey(string presetKey)
        {
            switch (presetKey)
            {
                case ApiProviderPresetPolicy.OpenAiResponsesPresetKey:
                    return "PawnDiary.Settings.ApiPreset.OpenAiResponses";
                case ApiProviderPresetPolicy.AnthropicPresetKey:
                    return "PawnDiary.Settings.ApiPreset.Anthropic";
                case ApiProviderPresetPolicy.GeminiPresetKey:
                    return "PawnDiary.Settings.ApiPreset.Gemini";
                case ApiProviderPresetPolicy.OllamaPresetKey:
                    return "PawnDiary.Settings.ApiPreset.Ollama";
                default:
                    return "PawnDiary.Settings.ApiPreset.CustomOpenAi";
            }
        }

        private static string FallbackDescriptionKey(string presetKey)
        {
            return FallbackLabelKey(presetKey) + ".Description";
        }
    }
}
