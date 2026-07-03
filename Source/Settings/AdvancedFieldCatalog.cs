// Declarative catalog of every XML Def field the Advanced settings tab can edit. The catalog
// is authored once (Build()) and drives both the runtime "apply overrides to Defs" seam and the UI:
// the tab walks the catalog by group and draws one widget per field type, so adding a field means
// one line in Build() and nothing else.
//
// Architecture seam (see AGENTS.md rule #2): instead of wrapping every Def read in a helper, the
// catalog writes overrides straight into the live Def instance fields via cached reflection. Every
// existing reader (`DiaryTuning.Current.someField`, `DiarySignalPolicies.ForKey(...).someField`,
// `DiaryContextReactions.ForKey(...).someField`, prompt-policy Def readers, etc.) then sees the new
// value with no call-site changes.
// The pristine XML value is snapshotted once, before the first override is applied, so Reset can
// restore it. Lists and small tables are edited as line-based text areas so XML-owned prompt/weight
// policy can be tuned without hand-editing the XML file.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace PawnDiary
{
    /// <summary>Scalar field kinds the Advanced tab can render and persist.</summary>
    public enum AdvancedFieldType
    {
        Bool,
        Int,
        Float,
        String,
        StringList,
        IntList
    }

    /// <summary>Which top-level tab a group belongs to: numeric tuning vs prompt text.</summary>
    public enum AdvancedFieldCategory
    {
        Tuning,
        Prompts
    }

    /// <summary>
    /// One editable field on a loaded Def. Carries its group, label/tooltip localization keys,
    /// value range (for sliders/clamping), and a cached reflection accessor bound to a resolver that
    /// returns the live Def instance. Knows how to read/write/format/parse its own value.
    /// </summary>
    public class AdvancedFieldDescriptor
    {
        public string key;              // "defName.fieldName"
        public string groupKey;         // localization key for the group header
        public string labelKey;         // optional keyed label; empty => humanized field name
        public string tooltipKey;       // optional; empty => no tooltip
        public AdvancedFieldType fieldType;
        public float min;
        public float max;
        public bool hasRange;           // true => clamp + (for float/int) eligible for slider
        public bool useSlider;          // force slider rendering for float/int with a range
        public bool isLongText;         // string rendered as a multi-line TextArea (prompt text)
        public string fieldName;        // C# field name on the resolved Def type

        private readonly Type defType;
        private readonly Func<Def> resolveDef;
        private FieldInfo fieldInfo;
        public Func<object> customReader;
        public Action<object> customWriter;
        // Most descriptors parse by fieldType. Complex prompt/weight tables (weather chances,
        // prompt field rows, severity tiers) plug in a tiny line-based parser/formatter here.
        public Func<object, string> customFormatter;
        public Func<string, object> customParser;
        // Optional display-only value for the rare row that intentionally mirrors inherited text.
        // Keyed XML prompt text does not use this hook: those rows show only the literal override
        // field, keeping translation keys and their resolved defaults out of node settings.
        public Func<string> effectiveReader;
        private const string NullListSentinel = "<null>";

        // Pristine XML value captured before the first override was applied. Used by Reset.
        public string defaultSnapshot;

        /// <summary>Vertical space the Advanced body should reserve for this field's row.</summary>
        public float RowHeight
        {
            get
            {
                if (isLongText || fieldType == AdvancedFieldType.StringList || fieldType == AdvancedFieldType.IntList)
                {
                    return AdvancedLabelLineHeight + AdvancedLongTextHeight + AdvancedRowGap;
                }

                if (fieldType == AdvancedFieldType.Bool)
                {
                    return AdvancedBoolRowHeight;
                }

                return AdvancedLabelLineHeight + AdvancedControlLineHeight + AdvancedRowGap;
            }
        }

        // Row-metric constants live on the descriptor so the catalog and the UI agree on layout.
        public const float AdvancedBoolRowHeight = 30f;
        public const float AdvancedLabelLineHeight = 20f;
        public const float AdvancedControlLineHeight = 28f;
        public const float AdvancedLongTextHeight = 140f;
        public const float AdvancedRowGap = 4f;

        public AdvancedFieldDescriptor(Type defType, Func<Def> resolveDef, string defName, string fieldName)
        {
            this.defType = defType;
            this.resolveDef = resolveDef;
            this.fieldName = fieldName;
            key = defName + "." + fieldName;
        }

        private FieldInfo Field
        {
            get
            {
                if (fieldInfo == null && defType != null)
                {
                    fieldInfo = defType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                }

                return fieldInfo;
            }
        }

        /// <summary>The current live value of this field on its Def (null if the Def/field is missing).</summary>
        public object ReadDefValue()
        {
            if (customReader != null)
            {
                return customReader();
            }

            Def def = resolveDef != null ? resolveDef() : null;
            FieldInfo info = Field;
            if (def == null || info == null)
            {
                return null;
            }

            return info.GetValue(def);
        }

        /// <summary>Writes a runtime-typed value back into the live Def field (the override seam).</summary>
        public void WriteDefValue(object value)
        {
            if (customWriter != null)
            {
                customWriter(value);
                return;
            }

            Def def = resolveDef != null ? resolveDef() : null;
            FieldInfo info = Field;
            if (def == null || info == null)
            {
                return;
            }

            try
            {
                info.SetValue(def, value);
            }
            catch
            {
                // A stale field name or type mismatch should never crash the settings window.
            }
        }

        /// <summary>Current live value rendered as an invariant-culture string (for the store/snapshot).</summary>
        public string ReadDefValueString()
        {
            return Format(ReadDefValue());
        }

        /// <summary>
        /// Value shown in the UI. Usually this is the raw Def field; a descriptor can opt into a
        /// display-only inherited value without changing where edits are written.
        /// </summary>
        public string ReadDisplayValueString()
        {
            if (effectiveReader != null)
            {
                return effectiveReader() ?? string.Empty;
            }

            return ReadDefValueString();
        }

        /// <summary>Parses a stored/snapshot string into the runtime value, applying clamp + NaN guards.</summary>
        public object Parse(string invariantString)
        {
            if (customParser != null)
            {
                return customParser(invariantString ?? string.Empty);
            }

            object value;
            return TryParse(invariantString, out value) ? value : DefaultValue();
        }

        /// <summary>
        /// Parses without clamping. Used only to restore a snapshotted pristine value on Reset, so a
        /// sentinel like -1 (signal/context "inherit tuning") is written back verbatim instead of being
        /// clamped up to the catalog min. NaN/infinity floats still fall back to the default.
        /// </summary>
        public object ParseUnclamped(string invariantString)
        {
            if (customParser != null)
            {
                return customParser(invariantString ?? string.Empty);
            }

            if (string.IsNullOrEmpty(invariantString))
            {
                return DefaultValue();
            }

            try
            {
                if (fieldType == AdvancedFieldType.Bool)
                {
                    return string.Equals(invariantString, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (fieldType == AdvancedFieldType.Int)
                {
                    return int.Parse(invariantString, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }

                if (fieldType == AdvancedFieldType.Float)
                {
                    float value = float.Parse(invariantString, NumberStyles.Float, CultureInfo.InvariantCulture);
                    return float.IsNaN(value) || float.IsInfinity(value) ? DefaultValue() : value;
                }

                if (fieldType == AdvancedFieldType.StringList)
                {
                    return ParseStringList(invariantString);
                }

                if (fieldType == AdvancedFieldType.IntList)
                {
                    return ParseIntList(invariantString, false);
                }

                return invariantString;
            }
            catch
            {
                return DefaultValue();
            }
        }

        /// <summary>
        /// Strict parse used by the text-field UI: returns false (no value) when the input does not
        /// yet form a complete number/string, so a half-typed value like "12." never commits as 0.
        /// On success the clamped runtime value is returned via <paramref name="value"/>.
        /// </summary>
        public bool TryParse(string invariantString, out object value)
        {
            value = DefaultValue();
            if (customParser != null)
            {
                try
                {
                    value = customParser(invariantString ?? string.Empty);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(invariantString)
                && (fieldType == AdvancedFieldType.Int || fieldType == AdvancedFieldType.Float))
            {
                return false;
            }

            try
            {
                if (fieldType == AdvancedFieldType.Bool)
                {
                    value = string.Equals(invariantString, "true", StringComparison.OrdinalIgnoreCase);
                    return true;
                }

                if (fieldType == AdvancedFieldType.Int)
                {
                    value = ClampInt(int.Parse(invariantString, NumberStyles.Integer, CultureInfo.InvariantCulture));
                    return true;
                }

                if (fieldType == AdvancedFieldType.Float)
                {
                    value = ClampFloat(float.Parse(invariantString, NumberStyles.Float, CultureInfo.InvariantCulture));
                    return true;
                }

                if (fieldType == AdvancedFieldType.StringList)
                {
                    value = ParseStringList(invariantString);
                    return true;
                }

                if (fieldType == AdvancedFieldType.IntList)
                {
                    value = ParseIntList(invariantString, true);
                    return true;
                }

                value = invariantString;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Formats a runtime value as the invariant-culture string used by the store.</summary>
        public string Format(object value)
        {
            if (customFormatter != null)
            {
                return customFormatter(value);
            }

            if (value == null)
            {
                return string.Empty;
            }

            if (fieldType == AdvancedFieldType.Bool)
            {
                return (bool)value ? "true" : "false";
            }

            if (fieldType == AdvancedFieldType.Int)
            {
                return ((int)value).ToString(CultureInfo.InvariantCulture);
            }

            if (fieldType == AdvancedFieldType.Float)
            {
                return ((float)value).ToString(CultureInfo.InvariantCulture);
            }

            if (fieldType == AdvancedFieldType.StringList)
            {
                return FormatStringList(value as List<string>);
            }

            if (fieldType == AdvancedFieldType.IntList)
            {
                return FormatIntList(value as List<int>);
            }

            return (string)value ?? string.Empty;
        }

        /// <summary>Human-friendly label, localized when a keyed override exists, else the humanized field name.</summary>
        public string DisplayLabel()
        {
            string text = TranslateOrEmpty(labelKey);
            return string.IsNullOrEmpty(text) ? Humanize(fieldName) : text;
        }

        /// <summary>Localized tooltip, or empty when no tooltip key was authored.</summary>
        public string DisplayTooltip()
        {
            return TranslateOrEmpty(tooltipKey);
        }

        /// <summary>Returns a sane default of the runtime type (used when parsing fails on load).</summary>
        public object DefaultValue()
        {
            switch (fieldType)
            {
                case AdvancedFieldType.Bool: return false;
                case AdvancedFieldType.Int: return 0;
                case AdvancedFieldType.Float: return 0f;
                case AdvancedFieldType.StringList: return new List<string>();
                case AdvancedFieldType.IntList: return new List<int>();
                default: return string.Empty;
            }
        }

        private static string FormatStringList(List<string> values)
        {
            if (values == null)
            {
                return NullListSentinel;
            }

            if (values.Count == 0)
            {
                // The override store treats empty strings as "clear override". A lone newline is an
                // invisible, non-empty sentinel that round-trips as an intentionally empty list.
                return "\n";
            }

            return string.Join("\n", values.Where(value => value != null).ToArray());
        }

        private static List<string> ParseStringList(string text)
        {
            if (IsNullListSentinel(text))
            {
                return null;
            }

            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string value = lines[i].Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static string FormatIntList(List<int> values)
        {
            if (values == null)
            {
                return NullListSentinel;
            }

            if (values.Count == 0)
            {
                return "\n";
            }

            string[] lines = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                lines[i] = values[i].ToString(CultureInfo.InvariantCulture);
            }

            return string.Join("\n", lines);
        }

        private static List<int> ParseIntList(string text, bool clamp)
        {
            if (IsNullListSentinel(text))
            {
                return null;
            }

            List<int> result = new List<int>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string value = lines[i].Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                int parsed = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                result.Add(clamp ? parsed : parsed);
            }

            return result;
        }

        private static bool IsNullListSentinel(string text)
        {
            string trimmed = text == null ? string.Empty : text.Trim();
            return string.Equals(trimmed, NullListSentinel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase);
        }

        private int ClampInt(int value)
        {
            if (!hasRange)
            {
                return value;
            }

            if (value < (int)min)
            {
                return (int)min;
            }

            if (value > (int)max)
            {
                return (int)max;
            }

            return value;
        }

        private float ClampFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return hasRange ? min : 0f;
            }

            if (!hasRange)
            {
                return value;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static string TranslateOrEmpty(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (!key.CanTranslate())
            {
                return string.Empty;
            }

            TaggedString tagged = key.Translate();
            string resolved = tagged == null ? string.Empty : ((string)tagged).StripTags();
            return string.IsNullOrEmpty(resolved) ? string.Empty : resolved;
        }

        // Turns "socialFightDedupTicks" into "Social fight dedup ticks" so the Advanced tab reads
        // reasonably without authoring one label string per field.
        private static string Humanize(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(fieldName.Length + 8);
            for (int i = 0; i < fieldName.Length; i++)
            {
                char c = fieldName[i];
                if (char.IsUpper(c))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0)
            {
                sb[0] = char.ToUpperInvariant(sb[0]);
            }

            return sb.ToString();
        }
    }

    /// <summary>A named group of Advanced fields (one section in the left rail of the Advanced tab).</summary>
    public class AdvancedFieldGroup
    {
        public string groupKey;
        public string displayName;
        public AdvancedFieldCategory category = AdvancedFieldCategory.Tuning;
        public readonly List<AdvancedFieldDescriptor> fields = new List<AdvancedFieldDescriptor>();

        /// <summary>Localized group header, falling back to the authored display name when no key exists.</summary>
        public string DisplayTitle()
        {
            if (!string.IsNullOrEmpty(groupKey))
            {
                if (groupKey.CanTranslate())
                {
                    TaggedString tagged = groupKey.Translate();
                    string resolved = tagged == null ? string.Empty : ((string)tagged).StripTags();
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        return resolved;
                    }
                }
            }

            return displayName ?? string.Empty;
        }
    }

    /// <summary>
    /// Static catalog of all editable Advanced fields, grouped for the left rail. Also owns the
    /// one-time snapshot of pristine XML defaults and the apply-override-to-Def logic.
    /// </summary>
    public static class AdvancedFieldCatalog
    {
        private static readonly List<AdvancedFieldGroup> groups = new List<AdvancedFieldGroup>();
        private static readonly List<AdvancedFieldDescriptor> all = new List<AdvancedFieldDescriptor>();
        private const string NullTableSentinel = "<null>";
        private static bool built;
        private static bool dynamicPromptGroupsBuilt;
        private static bool snapshotted;

        public static IReadOnlyList<AdvancedFieldGroup> Groups
        {
            get { EnsureBuilt(); EnsureDynamicPromptGroups(); return groups; }
        }

        public static IReadOnlyList<AdvancedFieldDescriptor> All
        {
            get { EnsureBuilt(); EnsureDynamicPromptGroups(); return all; }
        }

        /// <summary>True once the pristine XML snapshot has been captured (i.e. overrides may be applied).</summary>
        public static bool IsSnapshotted
        {
            get { return snapshotted; }
        }

        /// <summary>
        /// Snapshots pristine XML defaults (once) then re-applies every stored override to its live
        /// Def field. Safe to call repeatedly: the snapshot runs only the first time, and re-applying
        /// the same values is idempotent. Called on settings load and at the top of the Advanced tab.
        /// </summary>
        public static void EnsureApplied(TuningOverrideStore store)
        {
            EnsureBuilt();
            EnsureDynamicPromptGroups();
            if (!snapshotted)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    all[i].defaultSnapshot = all[i].ReadDefValueString();
                }

                snapshotted = true;
            }

            if (store == null)
            {
                return;
            }

            for (int i = 0; i < all.Count; i++)
            {
                AdvancedFieldDescriptor descriptor = all[i];
                string stored;
                if (store.TryGet(descriptor.key, out stored) && !string.IsNullOrEmpty(stored))
                {
                    object value;
                    // Skip corrupt/unparseable saved values instead of forcing a default 0/false into
                    // the live Def — a hand-edited save must not silently change behavior.
                    if (descriptor.TryParse(stored, out value))
                    {
                        descriptor.WriteDefValue(value);
                    }
                }
            }
        }

        /// <summary>Restores one field's pristine XML value into the live Def (used by per-field Reset).</summary>
        public static void ResetField(TuningOverrideStore store, AdvancedFieldDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            if (store != null)
            {
                store.Clear(descriptor.key);
            }

            // Restore the snapshot WITHOUT clamping: signal/context fields use -1 as the "inherit
            // tuning" sentinel, and clamping it to the catalog min would turn the sentinel into a real
            // 0 and silently change behavior. The snapshot came from a valid Def value, so it is safe
            // to write back verbatim.
            descriptor.WriteDefValue(descriptor.ParseUnclamped(descriptor.defaultSnapshot));
        }

        /// <summary>Restores every field in a group to its pristine XML value.</summary>
        public static void ResetGroup(TuningOverrideStore store, AdvancedFieldGroup group)
        {
            if (group == null)
            {
                return;
            }

            for (int i = 0; i < group.fields.Count; i++)
            {
                ResetField(store, group.fields[i]);
            }
        }

        /// <summary>Restores every catalogued field to its pristine XML value and clears the store.</summary>
        public static void ResetAll(TuningOverrideStore store)
        {
            for (int i = 0; i < all.Count; i++)
            {
                ResetField(store, all[i]);
            }

            if (store != null)
            {
                store.ClearAll();
            }
        }

        private static void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;
            Build();
        }

        private static void EnsureDynamicPromptGroups()
        {
            EnsureBuilt();
            if (dynamicPromptGroupsBuilt || !PromptWeightDefsReady())
            {
                return;
            }

            RegisterPromptWeightDefs(new Builder());
            dynamicPromptGroupsBuilt = true;
        }

        private static bool PromptWeightDefsReady()
        {
            // Every type below ships with at least one XML row in this mod. Waiting for all of them
            // avoids the original bug in a different form: registering after only interaction groups
            // loaded would still cache an empty humor-cue section for the session.
            return LoadedDefs<DiaryPromptEnchantmentDef>().Count > 0
                && LoadedDefs<DiaryHumorCueDef>().Count > 0
                && LoadedDefs<DiaryEventWindowDef>().Count > 0
                && LoadedDefs<DiaryObservedConditionDef>().Count > 0
                && LoadedDefs<DiaryInteractionGroupDef>().Count > 0
                && LoadedDefs<DiaryHediffPersonaOverrideDef>().Count > 0;
        }

        private static List<TDef> LoadedDefs<TDef>() where TDef : Def
        {
            List<TDef> defs = DefDatabase<TDef>.AllDefsListForReading;
            return defs == null ? new List<TDef>() : defs.Where(def => def != null).OrderBy(def => def.defName).ToList();
        }

        private static string DefDisplay(Def def, string prefixKey)
        {
            string prefix = string.Empty;
            if (!string.IsNullOrEmpty(prefixKey) && prefixKey.CanTranslate())
            {
                TaggedString tagged = prefixKey.Translate();
                prefix = tagged == null ? string.Empty : ((string)tagged).StripTags();
            }

            string name = def == null ? string.Empty : def.LabelCap.ToString();
            if (string.IsNullOrWhiteSpace(name) || (def != null && string.Equals(name, def.defName, StringComparison.Ordinal)))
            {
                name = def?.defName ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(prefix) ? name : prefix + ": " + name;
        }

        private static string DynamicGroupKey(string prefix, Def def)
        {
            return "PawnDiary.Settings.Adv.Dynamic." + prefix + "." + (def?.defName ?? "unknown");
        }

        private static string FormatWeatherMentionRules(object value)
        {
            List<WeatherMentionRule> rules = value as List<WeatherMentionRule>;
            if (rules == null)
            {
                return NullTableSentinel;
            }

            if (rules.Count == 0)
            {
                return "\n";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < rules.Count; i++)
            {
                WeatherMentionRule rule = rules[i];
                if (rule != null)
                {
                    lines.Add((rule.weather ?? string.Empty) + "=" + rule.chance.ToString(CultureInfo.InvariantCulture));
                }
            }

            return lines.Count == 0 ? "\n" : string.Join("\n", lines.ToArray());
        }

        private static object ParseWeatherMentionRules(string text)
        {
            if (IsNullTableSentinel(text))
            {
                return null;
            }

            List<WeatherMentionRule> rules = new List<WeatherMentionRule>();
            foreach (string line in Lines(text))
            {
                string[] parts = SplitPair(line);
                if (parts == null)
                {
                    continue;
                }

                rules.Add(new WeatherMentionRule
                {
                    weather = parts[0],
                    chance = float.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture)
                });
            }

            return rules;
        }

        private static string FormatRitualQualityBands(object value)
        {
            List<PawnDiary.Capture.RitualQualityBand> bands = value as List<PawnDiary.Capture.RitualQualityBand>;
            if (bands == null)
            {
                return NullTableSentinel;
            }

            if (bands.Count == 0)
            {
                return "\n";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < bands.Count; i++)
            {
                PawnDiary.Capture.RitualQualityBand band = bands[i];
                if (band != null)
                {
                    lines.Add(band.maxExclusive.ToString(CultureInfo.InvariantCulture) + "=" + (band.label ?? string.Empty));
                }
            }

            return lines.Count == 0 ? "\n" : string.Join("\n", lines.ToArray());
        }

        private static object ParseRitualQualityBands(string text)
        {
            if (IsNullTableSentinel(text))
            {
                return null;
            }

            List<PawnDiary.Capture.RitualQualityBand> bands = new List<PawnDiary.Capture.RitualQualityBand>();
            foreach (string line in Lines(text))
            {
                string[] parts = SplitPair(line);
                if (parts == null)
                {
                    continue;
                }

                bands.Add(new PawnDiary.Capture.RitualQualityBand
                {
                    maxExclusive = float.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
                    label = parts[1]
                });
            }

            return bands;
        }

        private static string FormatPromptFields(object value)
        {
            List<DiaryPromptFieldDef> fields = value as List<DiaryPromptFieldDef>;
            if (fields == null)
            {
                return NullTableSentinel;
            }

            if (fields.Count == 0)
            {
                return "\n";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldDef field = fields[i];
                if (field != null)
                {
                    lines.Add((field.enabled ? "true" : "false")
                        + "|" + (field.label ?? string.Empty)
                        + "|" + (field.source ?? string.Empty)
                        + "|" + (field.contextKey ?? string.Empty));
                }
            }

            return lines.Count == 0 ? "\n" : string.Join("\n", lines.ToArray());
        }

        private static object ParsePromptFields(string text)
        {
            if (IsNullTableSentinel(text))
            {
                return null;
            }

            List<DiaryPromptFieldDef> fields = new List<DiaryPromptFieldDef>();
            foreach (string line in Lines(text))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 3)
                {
                    continue;
                }

                bool enabled = !string.Equals(parts[0].Trim(), "false", StringComparison.OrdinalIgnoreCase);
                fields.Add(new DiaryPromptFieldDef
                {
                    enabled = enabled,
                    label = parts[1].Trim(),
                    source = parts[2].Trim(),
                    contextKey = parts.Length > 3 ? parts[3].Trim() : string.Empty
                });
            }

            return fields;
        }

        private static string FormatSeverityTiers(object value)
        {
            List<PromptEnchantmentSeverityTier> tiers = value as List<PromptEnchantmentSeverityTier>;
            if (tiers == null)
            {
                return NullTableSentinel;
            }

            if (tiers.Count == 0)
            {
                return "\n";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < tiers.Count; i++)
            {
                PromptEnchantmentSeverityTier tier = tiers[i];
                if (tier != null)
                {
                    lines.Add((tier.level ?? string.Empty)
                        + "|" + tier.chance.ToString(CultureInfo.InvariantCulture)
                        + "|" + tier.frequency.ToString(CultureInfo.InvariantCulture)
                        + "|" + tier.weight.ToString(CultureInfo.InvariantCulture)
                        + "|" + tier.severity.ToString(CultureInfo.InvariantCulture));
                }
            }

            return lines.Count == 0 ? "\n" : string.Join("\n", lines.ToArray());
        }

        private static object ParseSeverityTiers(string text)
        {
            if (IsNullTableSentinel(text))
            {
                return null;
            }

            List<PromptEnchantmentSeverityTier> tiers = new List<PromptEnchantmentSeverityTier>();
            foreach (string line in Lines(text))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    continue;
                }

                tiers.Add(new PromptEnchantmentSeverityTier
                {
                    level = parts[0].Trim(),
                    chance = ParseFloatPart(parts, 1, -1f),
                    frequency = ParseFloatPart(parts, 2, -1f),
                    weight = ParseFloatPart(parts, 3, -1f),
                    severity = ParseFloatPart(parts, 4, -1f)
                });
            }

            return tiers;
        }

        private static string FormatThoughtProgressionRules(object value)
        {
            List<ThoughtProgressionRule> rules = value as List<ThoughtProgressionRule>;
            if (rules == null)
            {
                return NullTableSentinel;
            }

            if (rules.Count == 0)
            {
                return "\n";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < rules.Count; i++)
            {
                ThoughtProgressionRule rule = rules[i];
                if (rule == null)
                {
                    continue;
                }

                List<string> stages = new List<string>();
                if (rule.stages != null)
                {
                    for (int stageIndex = 0; stageIndex < rule.stages.Count; stageIndex++)
                    {
                        ThoughtProgressionStage stage = rule.stages[stageIndex];
                        if (stage != null)
                        {
                            stages.Add(stage.stageIndex.ToString(CultureInfo.InvariantCulture)
                                + ":" + stage.severity.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                lines.Add((rule.categoryKey ?? string.Empty)
                    + "|" + (rule.thoughtDefName ?? string.Empty)
                    + "|" + string.Join(",", stages.ToArray()));
            }

            return lines.Count == 0 ? "\n" : string.Join("\n", lines.ToArray());
        }

        private static object ParseThoughtProgressionRules(string text)
        {
            if (IsNullTableSentinel(text))
            {
                return null;
            }

            List<ThoughtProgressionRule> rules = new List<ThoughtProgressionRule>();
            foreach (string line in Lines(text))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 2)
                {
                    continue;
                }

                ThoughtProgressionRule rule = new ThoughtProgressionRule
                {
                    categoryKey = parts[0].Trim(),
                    thoughtDefName = parts[1].Trim(),
                    stages = new List<ThoughtProgressionStage>()
                };

                if (parts.Length > 2)
                {
                    string[] stageParts = parts[2].Split(',');
                    for (int i = 0; i < stageParts.Length; i++)
                    {
                        string stageText = stageParts[i].Trim();
                        if (stageText.Length == 0)
                        {
                            continue;
                        }

                        string[] pair = stageText.Split(':');
                        if (pair.Length != 2)
                        {
                            continue;
                        }

                        rule.stages.Add(new ThoughtProgressionStage
                        {
                            stageIndex = int.Parse(pair[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture),
                            severity = int.Parse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture)
                        });
                    }
                }

                rules.Add(rule);
            }

            return rules;
        }

        private static IEnumerable<string> Lines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    yield return line;
                }
            }
        }

        private static string[] SplitPair(string line)
        {
            int index = line.IndexOf('=');
            if (index < 0)
            {
                index = line.IndexOf(':');
            }

            if (index <= 0)
            {
                return null;
            }

            return new[] { line.Substring(0, index).Trim(), line.Substring(index + 1).Trim() };
        }

        private static bool IsNullTableSentinel(string text)
        {
            string trimmed = text == null ? string.Empty : text.Trim();
            return string.Equals(trimmed, NullTableSentinel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static float ParseFloatPart(string[] parts, int index, float fallback)
        {
            if (parts == null || index < 0 || index >= parts.Length || string.IsNullOrWhiteSpace(parts[index]))
            {
                return fallback;
            }

            return float.Parse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---- Catalog authoring ----
        // One fluent builder, one group at a time. OnTuning targets DiaryTuningDef (the single
        // Diary_Tuning instance); OnSignal/OnContext target the named defName via the existing
        // accessor that already returns a live Def (with a safe fallback when the def is absent).
        private static void Build()
        {
            Builder b = new Builder();

            b.Tuning("Dedup", "PawnDiary.Settings.Adv.Group.Dedup")
                .Int("socialFightDedupTicks", 0, 100000)
                .Int("mentalBreakDedupTicks", 0, 100000)
                .Int("taleDedupTicks", 0, 100000)
                .Int("moodEventDedupTicks", 0, 100000)
                .Int("thoughtDedupTicks", 0, 100000)
                .Int("romanceDedupTicks", 0, 100000)
                .Int("raidDedupTicks", 0, 100000)
                .Int("raidGenerationDelayTicks", 0, 100000)
                .Int("questDedupTicks", 0, 100000)
                .Int("ritualDedupTicks", 0, 100000)
                .Int("abilityDedupTicks", 0, 100000);

            b.Tuning("Ability sampling", "PawnDiary.Settings.Adv.Group.Ability")
                .Float("abilityUseMinChance", 0f, 1f, true)
                .Float("abilityUseMaxChance", 0f, 1f, true)
                .Int("abilityUseReferenceCooldownTicks", 0, 600000);

            b.Tuning("Ritual quality labels", "PawnDiary.Settings.Adv.Group.RitualQuality")
                .CustomLongText("ritualQualityBands", FormatRitualQualityBands, ParseRitualQualityBands);

            b.Tuning("Surroundings scan", "PawnDiary.Settings.Adv.Group.Surroundings")
                .Float("nearbyRadius", 0f, 50f, true)
                .Int("maxNearbyThings", 0, 50)
                .Float("coldBelowC", -100f, 100f, false)
                .Float("hotAboveC", -100f, 100f, false);

            b.Tuning("Weather mention fallbacks", "PawnDiary.Settings.Adv.Group.Weather")
                .CustomLongText("weatherMentionChances", FormatWeatherMentionRules, ParseWeatherMentionRules)
                .Float("weatherChanceVeryBad", 0f, 1f, true)
                .Float("weatherChanceBad", 0f, 1f, true)
                .Float("weatherChanceNeutral", 0f, 1f, true)
                .Float("weatherChanceDefault", 0f, 1f, true);

            b.Tuning("Health thresholds", "PawnDiary.Settings.Adv.Group.Health")
                .Float("painVisibleAbove", 0f, 1f, true)
                .Float("bleedVisibleAbove", 0f, 1f, true)
                .Float("lowCapacityThreshold", 0f, 1f, true);

            b.Tuning("Prompt enchantment tuning", "PawnDiary.Settings.Adv.Group.Enchantment")
                .Float("promptEnchantmentMinorHediffSeverity", 0f, 1f, true)
                .Float("promptEnchantmentModerateHediffSeverity", 0f, 1f, true)
                .Float("promptEnchantmentMajorHediffSeverity", 0f, 1f, true)
                .Float("promptEnchantmentCriticalHediffSeverity", 0f, 1f, true)
                .Float("promptEnchantmentCloudedConsciousnessBelow", 0f, 1f, true)
                .Float("promptEnchantmentFadingConsciousnessBelow", 0f, 1f, true)
                .Float("promptEnchantmentBarelyConsciousBelow", 0f, 1f, true)
                .Int("promptEnchantmentMaxImpactCues", 0, 20);

            b.Tuning("Consciousness gates", "PawnDiary.Settings.Adv.Group.Consciousness")
                .Float("minimumConsciousnessForFirstPersonGeneration", 0f, 1f, true)
                .Float("staggeredConsciousnessIntensity4Below", 0f, 1f, true)
                .Float("staggeredConsciousnessIntensity3Below", 0f, 1f, true)
                .Float("staggeredConsciousnessIntensity2Below", 0f, 1f, true)
                .Float("staggeredConsciousnessIntensity1Below", 0f, 1f, true)
                .Float("intoxicationSeverityIntensity4At", 0f, 2f, true)
                .Float("intoxicationSeverityIntensity3At", 0f, 2f, true)
                .Float("intoxicationSeverityIntensity2At", 0f, 2f, true)
                .Float("intoxicationSeverityIntensity1At", 0f, 2f, true);

            b.Tuning("Beauty / mood / pain / opinion buckets", "PawnDiary.Settings.Adv.Group.Buckets")
                .Float("beautyBeautiful", -10f, 10f, false)
                .Float("beautyPleasant", -10f, 10f, false)
                .Float("beautyUgly", -10f, 10f, false)
                .Int("moodHappy", 0, 100)
                .Int("moodStable", 0, 100)
                .Int("moodStressed", 0, 100)
                .Float("painSevere", 0f, 1f, true)
                .Float("painModerate", 0f, 1f, true)
                .Int("opinionDevoted", -100, 100)
                .Int("opinionFriendly", -100, 100)
                .Int("opinionNeutralAbove", -100, 100)
                .Int("opinionStrainedAbove", -100, 100)
                .StringList("positiveMoodConditionDefNames")
                .StringList("negativeMoodConditionDefNames");

            b.Tuning("Thought thresholds", "PawnDiary.Settings.Adv.Group.Thought")
                .Float("thoughtMinMoodOffset", 0f, 100f, false)
                .Float("thoughtEatingMinMoodOffset", 0f, 100f, false)
                .StringList("thoughtIgnoreTokens")
                .StringList("thoughtBypassThresholdTokens")
                .StringList("thoughtEatingTokens")
                .StringList("thoughtAmbientTokens")
                .Int("thoughtAmbientWindowTicks", 0, 600000)
                .Int("thoughtAmbientMinEventsToWrite", 0, 50)
                .Int("thoughtAmbientMaxSampleLines", 0, 50);

            b.Tuning("Scanner intervals", "PawnDiary.Settings.Adv.Group.Scanners")
                .Int("thoughtProgressionScanIntervalTicks", 0, 600000)
                .Int("thoughtProgressionDedupTicks", 0, 100000)
                .CustomLongText("thoughtProgressionRules", FormatThoughtProgressionRules, ParseThoughtProgressionRules)
                .Int("hediffProgressionScanIntervalTicks", 0, 600000)
                .Int("progressionScanIntervalTicks", 0, 600000)
                .IntList("progressionSkillMilestones")
                .StringList("psylinkHediffDefNames");

            b.Tuning("Work sampling", "PawnDiary.Settings.Adv.Group.Work")
                .Int("workScanIntervalTicks", 0, 600000)
                .Float("workBaseChance", 0f, 1f, true)
                .Int("workSameTypeCooldownTicks", 0, 600000)
                .Float("workRecentDifferentTypeMultiplier", 0f, 5f, true)
                .Float("workPassionChanceMultiplier", 0f, 5f, true)
                .Float("workNegativeChanceMultiplier", 0f, 5f, true)
                .Float("workDarkStudyChanceMultiplier", 0f, 5f, true)
                .Int("workLowSkillThreshold", 0, 20);

            b.Tuning("Day reflection", "PawnDiary.Settings.Adv.Group.DayReflection")
                .Bool("daySummaryEnabled")
                .Int("daySummaryMaxHighlights", 0, 20)
                .Int("daySummaryOpinionDeltaThreshold", 0, 100)
                .Float("daySummaryWeightCriticalEvent", 0f, 5f, true)
                .Float("daySummaryWeightMajorEvent", 0f, 5f, true)
                .Float("daySummaryWeightHediff", 0f, 5f, true)
                .Float("daySummaryWeightOpinionShift", 0f, 5f, true)
                .Float("daySummaryWeightFiller", 0f, 5f, true)
                .StringList("daySummaryImportantSignalKinds");

            b.Tuning("Quadrum reflection", "PawnDiary.Settings.Adv.Group.Quadrum")
                .Bool("quadrumReflectionEnabled")
                .Int("quadrumReflectionTimingWindowDays", 1, 15)
                .Int("quadrumReflectionMinImportantEntries", 1, 50)
                .Int("quadrumReflectionMaxPromptEvents", 1, 50)
                .Int("quadrumReflectionMaxTokens", 1, 4000);

            b.Tuning("Arc reflection", "PawnDiary.Settings.Adv.Group.Arc")
                .Bool("arcReflectionEnabled")
                .Bool("arcReflectionAllowSecondMajorEntry")
                .Int("arcReflectionMaxEntriesPerYear", 1, 10)
                .Int("arcReflectionSecondEntryMinGapDays", 0, 120)
                .Int("arcReflectionMajorSeverityThreshold", 0, 100)
                .Int("arcReflectionForceAfterYearDay", 0, 100)
                .Int("arcReflectionMemoryShortfallRetryTicks", 0, 600000)
                .Int("arcReflectionMinMemoriesPreferred", 0, 50)
                .Int("arcReflectionMinMemoriesForced", 0, 50)
                .Int("arcReflectionMaxMemories", 0, 50)
                .Int("arcReflectionRecentlyUsedMemoryCap", 0, 200)
                .Int("arcReflectionMemorySnippetMaxChars", 0, 2000)
                .Int("arcReflectionMaxTokens", 0, 4000)
                .StringList("arcReflectionMajorXenotypeDefNames")
                .StringList("arcReflectionHighStakesDefNameTokens");

            b.Tuning("Misc tuning", "PawnDiary.Settings.Adv.Group.Misc")
                .Int("diaryLineMaxChars", 0, 1000)
                .Int("previousEntryEndingSentenceCount", 1, 6)
                .Int("previousEntryEndingMaxChars", 40, 2000)
                .Int("activeScanEventWindow", 1, 100000)
                .Int("archivedFallbackTitleWords", 1, 50)
                .Int("archivedFallbackTextMaxChars", 1, 2000)
                .Int("uiHistoryScanMaxEventsPerFrame", 1, 1000)
                .Float("uiHistoryScanFrameBudgetSeconds", 0f, 0.01f, true)
                .Int("minimumFirstPersonAgeYears", 0, 100)
                .Float("humorChance", 0f, 1f, true);

            // ---- Signal policies (DiarySignalPolicyDef). The accessor returns a fallback def when a
            // signal def is absent, so editing never crashes; values use -1 as the "inherit tuning"
            // sentinel, which the snapshot preserves across Reset.
            b.Signal("DiarySignalPolicy_Thought", "Thought", "PawnDiary.Settings.Adv.Group.SignalThought")
                .Bool("enabled")
                .Int("dedupTicks", 0, 100000)
                .Float("minMoodOffset", 0f, 100f, false)
                .Float("eatingMinMoodOffset", 0f, 100f, false)
                .StringList("ignoreTokens")
                .StringList("bypassThresholdTokens")
                .StringList("eatingTokens")
                .StringList("ambientTokens");

            b.Signal("DiarySignalPolicy_AmbientThought", "AmbientThought", "PawnDiary.Settings.Adv.Group.SignalAmbient")
                .Bool("enabled")
                .Int("ambientWindowTicks", 0, 600000)
                .Int("ambientMinEventsToWrite", 0, 50)
                .Int("ambientMaxSampleLines", 0, 50);

            b.Signal("DiarySignalPolicy_ThoughtProgression", "ThoughtProgression", "PawnDiary.Settings.Adv.Group.SignalProgression")
                .Bool("enabled")
                .Int("scanIntervalTicks", 0, 600000)
                .Int("dedupTicks", 0, 100000)
                .CustomLongText("thoughtProgressionRules", FormatThoughtProgressionRules, ParseThoughtProgressionRules);

            b.Signal("DiarySignalPolicy_Work", "Work", "PawnDiary.Settings.Adv.Group.SignalWork")
                .Bool("enabled")
                .Int("scanIntervalTicks", 0, 600000)
                .Float("baseChance", 0f, 1f, true)
                .Int("sameTypeCooldownTicks", 0, 600000)
                .Float("recentDifferentTypeMultiplier", 0f, 5f, true)
                .Float("passionChanceMultiplier", 0f, 5f, true)
                .Float("negativeChanceMultiplier", 0f, 5f, true)
                .Float("darkStudyChanceMultiplier", 0f, 5f, true)
                .Int("lowSkillThreshold", 0, 20);

            b.Signal("DiarySignalPolicy_Progression", "Progression", "PawnDiary.Settings.Adv.Group.SignalPawn")
                .Bool("enabled")
                .Int("scanIntervalTicks", 0, 600000);

            // ---- Context reactions (DiaryContextReactionDef). Scalar knobs plus prompt text keys and
            // letter-def lists that affect prompt context.
            b.Context("DiaryContextReaction_ActiveMapConditions", "ActiveMapConditions", "PawnDiary.Settings.Adv.Group.CtxConditions")
                .Bool("enabled")
                .Int("maxItems", 0, 20)
                .Bool("displayOnUiOnly")
                .LongText("text");

            b.Context("DiaryContextReaction_RecentThreatLetter", "RecentThreatLetter", "PawnDiary.Settings.Adv.Group.CtxThreat")
                .Bool("enabled")
                .Int("scanBack", 0, 500)
                .Int("timeoutTicks", 0, 600000)
                .Bool("requireHomeMap")
                .Bool("requireDanger")
                .LongText("text")
                .StringList("letterDefNames");

            // ---- Shared prompt default instructions (DiaryPromptDef). Prompt Studio already edits the
            // shared system prompts; these are the remaining prompt instructions and style default.
            b.PromptDefaults("PawnDiary.Settings.Adv.Group.PromptDefaults")
                .LongText("singlePovInstruction")
                .LongText("recipientFollowupInstruction")
                .LongText("deathDescriptionInstruction")
                .LongText("arrivalDescriptionInstruction")
                .LongText("titleUserInstruction")
                .Text("defaultPersonaDefName");

            // ---- Prompt templates (DiaryPromptTemplateDef). Text fields, prompt switches, token caps,
            // and field lists are editable; missing XML falls back to the shared system prompts / final
            // instructions, and Reset restores the snapshot (possibly empty) so the fallback returns.
            b.Template(DiaryPromptTemplates.PairDefault, "PawnDiary.Settings.Adv.Group.TmplPairDefault")
                .LongText("systemPrompt").LongText("finalInstruction").LongText("recipientFinalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.PairImportant, "PawnDiary.Settings.Adv.Group.TmplPairImportant")
                .LongText("systemPrompt").LongText("finalInstruction").LongText("recipientFinalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.PairCombat, "PawnDiary.Settings.Adv.Group.TmplPairCombat")
                .LongText("systemPrompt").LongText("finalInstruction").LongText("recipientFinalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.PairBatched, "PawnDiary.Settings.Adv.Group.TmplPairBatched")
                .LongText("systemPrompt").LongText("finalInstruction").LongText("recipientFinalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloDefault, "PawnDiary.Settings.Adv.Group.TmplSoloDefault")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloImportant, "PawnDiary.Settings.Adv.Group.TmplSoloImportant")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloInternalState, "PawnDiary.Settings.Adv.Group.TmplSoloInternal")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloBatched, "PawnDiary.Settings.Adv.Group.TmplSoloBatched")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloDayReflection, "PawnDiary.Settings.Adv.Group.TmplDayReflection")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloQuadrumReflection, "PawnDiary.Settings.Adv.Group.TmplQuadrumReflection")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.SoloArcReflection, "PawnDiary.Settings.Adv.Group.TmplArcReflection")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.DeathDescription, "PawnDiary.Settings.Adv.Group.TmplDeath")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.ArrivalDescription, "PawnDiary.Settings.Adv.Group.TmplArrival")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);
            b.Template(DiaryPromptTemplates.Title, "PawnDiary.Settings.Adv.Group.TmplTitle")
                .LongText("systemPrompt").LongText("finalInstruction")
                .Bool("includePromptEnchantment").Bool("includePersona").Bool("appendDirectSpeechInstruction")
                .Int("maxTokens", 0, 4000).CustomLongText("fields", FormatPromptFields, ParsePromptFields);

            // Prompt-policy Def groups are registered lazily after DefDatabase is populated. Settings
            // load can run early; a one-shot build here would cache empty lists and hide groups such as
            // humor cues for the whole session.
        }

        private static void RegisterPromptWeightDefs(Builder b)
        {
            RegisterPromptEnchantments(b);
            RegisterHumorCues(b);
            RegisterEventWindows(b);
            RegisterObservedConditions(b);
            RegisterInteractionGroups(b);
            RegisterHediffPersonaOverrides(b);
        }

        private static void RegisterPromptEnchantments(Builder b)
        {
            foreach (DiaryPromptEnchantmentDef def in LoadedDefs<DiaryPromptEnchantmentDef>())
            {
                b.Def(def, DefDisplay(def, "PawnDiary.Settings.Adv.DynamicPrefix.PromptEnchantment"), DynamicGroupKey("PromptEnchantment", def), AdvancedFieldCategory.Prompts)
                    .Text("source")
                    .Text("capacityDefName")
                    .Float("chance", 0f, 1f, true)
                    .Float("weight", 0f, 100f, false)
                    .Float("severity", 0f, 100f, false)
                    .Bool("visibleOnly")
                    .Float("minHediffSeverity", 0f, 100f, false)
                    .Float("minCapacity", -1f, 1f, false)
                    .Float("maxCapacity", -1f, 1f, false)
                    .StringList("hediffDefNames")
                    .CustomLongText("hediffSeverityTiers", FormatSeverityTiers, ParseSeverityTiers)
                    .Text("conditionLabel")
                    .Text("intensityText")
                    .Text("priorityText")
                    .LongText("descriptionOverrideText")
                    .StringList("cueTexts");
            }
        }

        private static void RegisterHumorCues(Builder b)
        {
            foreach (DiaryHumorCueDef def in LoadedDefs<DiaryHumorCueDef>())
            {
                b.Def(def, DefDisplay(def, "PawnDiary.Settings.Adv.DynamicPrefix.HumorCue"), DynamicGroupKey("HumorCue", def), AdvancedFieldCategory.Prompts)
                    .LongText("rule")
                    .Text("tier")
                    .Float("weight", 0f, 100f, false);
            }
        }

        private static void RegisterEventWindows(Builder b)
        {
            foreach (DiaryEventWindowDef def in LoadedDefs<DiaryEventWindowDef>())
            {
                b.Def(def, DefDisplay(def, "PawnDiary.Settings.Adv.DynamicPrefix.EventWindow"), DynamicGroupKey("EventWindow", def), AdvancedFieldCategory.Prompts)
                    .Bool("enabled")
                    .LongText("instruction")
                    .LongText("startText")
                    .LongText("endText")
                    .LongText("timeoutText")
                    .Text("colorCue")
                    .Bool("promptEnabled")
                    .Float("promptWeight", 0f, 100f, false)
                    .Float("normalPromptWeightMultiplier", 0f, 10f, false)
                    .Int("promptDecayTicks", 0, 600000)
                    .Float("promptDecayMinMultiplier", 0f, 1f, false)
                    .Text("promptPriorityText")
                    .Text("promptConditionText")
                    .LongText("promptDescriptionText")
                    .StringList("promptCueTexts");
            }
        }

        private static void RegisterObservedConditions(Builder b)
        {
            foreach (DiaryObservedConditionDef def in LoadedDefs<DiaryObservedConditionDef>())
            {
                b.Def(def, DefDisplay(def, "PawnDiary.Settings.Adv.DynamicPrefix.ObservedCondition"), DynamicGroupKey("ObservedCondition", def), AdvancedFieldCategory.Prompts)
                    .Bool("enabled")
                    .LongText("instruction")
                    .LongText("startText")
                    .LongText("endText")
                    .Text("colorCue")
                    .Bool("promptEnabled")
                    .Float("promptWeight", 0f, 100f, false)
                    .Float("normalPromptWeightMultiplier", 0f, 10f, false)
                    .Int("promptDecayTicks", 0, 600000)
                    .Float("promptDecayMinMultiplier", 0f, 1f, false)
                    .Int("maxActiveTicks", 0, 600000)
                    .Int("restartCooldownTicks", 0, 600000)
                    .Int("maxEvidenceLabels", 0, 20)
                    .Int("maxEvidenceChars", 0, 2000)
                    .Int("maxEvidenceCount", 0, 9999)
                    .Text("promptPriorityText")
                    .Text("promptConditionText")
                    .LongText("promptDescriptionText")
                    .StringList("promptCueTexts")
                    .StringList("matchDefNames")
                    .StringList("matchDefNameContains")
                    .StringList("matchLabels")
                    .StringList("suppressWhenThingDefNames");
            }
        }

        private static void RegisterInteractionGroups(Builder b)
        {
            foreach (DiaryInteractionGroupDef def in LoadedDefs<DiaryInteractionGroupDef>())
            {
                b.Def(def, DefDisplay(def, "PawnDiary.Settings.Adv.DynamicPrefix.InteractionGroup"), DynamicGroupKey("InteractionGroup", def), AdvancedFieldCategory.Prompts)
                    .Bool("defaultEnabled")
                    .Bool("important")
                    .Bool("combat")
                    .Bool("captureRenderedGameText")
                    .Text("colorCue")
                    .LongText("instruction")
                    .StringList("instructions")
                    .Text("tone")
                    .StringList("tones")
                    .StringList("matchDefNames")
                    .StringList("matchTokens")
                    .StringList("matchPrefixes")
                    .StringList("matchSuffixes")
                    .StringList("matchSegments")
                    .StringList("matchPackageIds")
                    .StringList("disableWhenPackageIdsLoaded")
                    .StringList("deathVictimInitiatorDefNames")
                    .StringList("deathVictimRecipientDefNames");

                RegisterInteractionBatch(b, def);
                RegisterInteractionPromotion(b, def);
                RegisterInteractionHediffPolicy(b, def);
            }
        }

        private static void RegisterInteractionBatch(Builder b, DiaryInteractionGroupDef def)
        {
            InteractionBatchPolicy batch = def.batch;
            if (batch == null)
            {
                return;
            }

            b.CustomBool("batch.enabled", () => batch.enabled, value => batch.enabled = (bool)value)
                .CustomText("batch.mode", () => batch.mode.ToString(), value => batch.mode = ParseEnum((string)value, batch.mode))
                .CustomInt("batch.windowTicks", () => batch.windowTicks, value => batch.windowTicks = (int)value, 0, 600000)
                .CustomInt("batch.maxEvents", () => batch.maxEvents, value => batch.maxEvents = (int)value, 1, 100)
                .CustomText("batch.scope", () => batch.scope.ToString(), value => batch.scope = ParseEnum((string)value, batch.scope))
                .CustomText("batch.syntheticDefName", () => batch.syntheticDefName, value => batch.syntheticDefName = (string)value)
                .CustomText("batch.labelText", () => batch.labelText, value => batch.labelText = (string)value)
                .CustomLongText("batch.briefText", () => batch.briefText, value => batch.briefText = (string)value)
                .CustomLongText("batch.headerText", () => batch.headerText, value => batch.headerText = (string)value)
                .CustomLongText("batch.fallbackText", () => batch.fallbackText, value => batch.fallbackText = (string)value)
                .CustomLongText("batch.instructionText", () => batch.instructionText, value => batch.instructionText = (string)value)
                .CustomBool("batch.includeInteractionLabel", () => batch.includeInteractionLabel, value => batch.includeInteractionLabel = (bool)value)
                .CustomInt("batch.minEventsToWrite", () => batch.minEventsToWrite, value => batch.minEventsToWrite = (int)value, 0, 100)
                .CustomInt("batch.maxSampleLines", () => batch.maxSampleLines, value => batch.maxSampleLines = (int)value, 0, 100);
        }

        private static void RegisterInteractionPromotion(Builder b, DiaryInteractionGroupDef def)
        {
            InteractionPromotionPolicy promotion = def.promotion;
            if (promotion == null)
            {
                return;
            }

            b.CustomBool("promotion.enabled", () => promotion.enabled, value => promotion.enabled = (bool)value)
                .CustomFloat("promotion.baseChance", () => promotion.baseChance, value => promotion.baseChance = (float)value, 0f, 1f, true)
                .CustomFloat("promotion.maxChance", () => promotion.maxChance, value => promotion.maxChance = (float)value, 0f, 1f, true)
                .CustomFloat("promotion.opinionStrongBonus", () => promotion.opinionStrongBonus, value => promotion.opinionStrongBonus = (float)value, 0f, 1f, true)
                .CustomInt("promotion.opinionStrongThreshold", () => promotion.opinionStrongThreshold, value => promotion.opinionStrongThreshold = (int)value, -100, 100)
                .CustomFloat("promotion.opinionAsymmetryBonus", () => promotion.opinionAsymmetryBonus, value => promotion.opinionAsymmetryBonus = (float)value, 0f, 1f, true)
                .CustomInt("promotion.opinionAsymmetryThreshold", () => promotion.opinionAsymmetryThreshold, value => promotion.opinionAsymmetryThreshold = (int)value, 0, 100)
                .CustomFloat("promotion.needLowBonus", () => promotion.needLowBonus, value => promotion.needLowBonus = (float)value, 0f, 1f, true)
                .CustomFloat("promotion.needLowThreshold", () => promotion.needLowThreshold, value => promotion.needLowThreshold = (float)value, 0f, 1f, true)
                .CustomFloat("promotion.moodExtremeBonus", () => promotion.moodExtremeBonus, value => promotion.moodExtremeBonus = (float)value, 0f, 1f, true)
                .CustomFloat("promotion.moodLowThreshold", () => promotion.moodLowThreshold, value => promotion.moodLowThreshold = (float)value, 0f, 1f, true);
        }

        private static void RegisterInteractionHediffPolicy(Builder b, DiaryInteractionGroupDef def)
        {
            HediffSignalPolicy hediff = def.hediff;
            if (hediff == null)
            {
                return;
            }

            b.CustomBool("hediff.enabled", () => hediff.enabled, value => hediff.enabled = (bool)value)
                .CustomText("hediff.mode", () => hediff.mode.ToString(), value => hediff.mode = ParseEnum((string)value, hediff.mode))
                .CustomBool("hediff.visibleOnly", () => hediff.visibleOnly, value => hediff.visibleOnly = (bool)value)
                .CustomBool("hediff.badOnly", () => hediff.badOnly, value => hediff.badOnly = (bool)value)
                .CustomBool("hediff.excludeInjuries", () => hediff.excludeInjuries, value => hediff.excludeInjuries = (bool)value)
                .CustomFloat("hediff.minSeverity", () => hediff.minSeverity, value => hediff.minSeverity = (float)value, 0f, 100f, false)
                .CustomBool("hediff.chronicAlways", () => hediff.chronicAlways, value => hediff.chronicAlways = (bool)value)
                .CustomBool("hediff.sickThoughtAlways", () => hediff.sickThoughtAlways, value => hediff.sickThoughtAlways = (bool)value)
                .CustomBool("hediff.addictionAlways", () => hediff.addictionAlways, value => hediff.addictionAlways = (bool)value)
                .CustomBool("hediff.missingPartAlways", () => hediff.missingPartAlways, value => hediff.missingPartAlways = (bool)value)
                .CustomBool("hediff.recordOnAdd", () => hediff.recordOnAdd, value => hediff.recordOnAdd = (bool)value)
                .CustomBool("hediff.recordOnSeverityIncrease", () => hediff.recordOnSeverityIncrease, value => hediff.recordOnSeverityIncrease = (bool)value)
                .CustomFloat("hediff.severityStep", () => hediff.severityStep, value => hediff.severityStep = (float)value, 0f, 100f, false)
                .CustomInt("hediff.dedupTicks", () => hediff.dedupTicks, value => hediff.dedupTicks = (int)value, 0, 600000)
                .CustomFloat("hediff.dayReflectionWeight", () => hediff.dayReflectionWeight, value => hediff.dayReflectionWeight = (float)value, 0f, 100f, false)
                .CustomLongText("hediff.appearedText", () => hediff.appearedText, value => hediff.appearedText = (string)value)
                .CustomLongText("hediff.progressedText", () => hediff.progressedText, value => hediff.progressedText = (string)value);
        }

        private static void RegisterHediffPersonaOverrides(Builder b)
        {
            foreach (DiaryHediffPersonaOverrideDef def in LoadedDefs<DiaryHediffPersonaOverrideDef>())
            {
                b.Def(def, DefDisplay(def, "PawnDiary.Settings.Adv.DynamicPrefix.HediffPersona"), DynamicGroupKey("HediffPersona", def), AdvancedFieldCategory.Prompts)
                    .Int("priority", 0, 100000)
                    .Text("personaDefName")
                    .Bool("visibleOnly")
                    .Float("minSeverity", -1f, 100f, false)
                    .StringList("hediffDefNames")
                    .StringList("hediffDefNameContains")
                    .StringList("hediffLabelContains");
            }
        }

        private static TEnum ParseEnum<TEnum>(string text, TEnum fallback) where TEnum : struct
        {
            try
            {
                return (TEnum)Enum.Parse(typeof(TEnum), text ?? string.Empty, true);
            }
            catch
            {
                return fallback;
            }
        }

        // Fluent builder that targets the current group. Each method returns the builder so one group
        // reads as a vertical list of fields. Adding a field anywhere = one line. The active "target"
        // (Def type + live-instance resolver + defName) is set by Tuning/Signal/Context and reused by
        // every Bool/Int/Float in that group.
        private class Builder
        {
            private AdvancedFieldGroup current;
            private Type targetDefType;
            private Func<Def> targetResolve;
            private string targetDefName;
            private string targetTemplateKey;

            public Builder PromptDefaults(string groupKey)
            {
                BeginGroup("Shared prompt instructions", groupKey, AdvancedFieldCategory.Prompts);
                targetDefType = typeof(DiaryPromptDef);
                targetResolve = () => DiaryPrompts.Current;
                targetDefName = "Diary_Prompts";
                targetTemplateKey = null;
                return this;
            }

            public Builder Tuning(string display, string groupKey)
            {
                BeginGroup(display, groupKey, AdvancedFieldCategory.Tuning);
                targetDefType = typeof(DiaryTuningDef);
                targetResolve = () => DiaryTuning.Current;
                targetDefName = "Diary_Tuning";
                targetTemplateKey = null;
                return this;
            }

            public Builder Signal(string defName, string signalKey, string groupKey)
            {
                // Capture the key once so the resolver closure does not reuse a loop variable.
                string key = signalKey;
                BeginGroup("Signal: " + signalKey, groupKey, AdvancedFieldCategory.Tuning);
                targetDefType = typeof(DiarySignalPolicyDef);
                targetResolve = () => DiarySignalPolicies.ForKey(key);
                targetDefName = defName;
                targetTemplateKey = null;
                return this;
            }

            public Builder Context(string defName, string reactionKey, string groupKey)
            {
                string key = reactionKey;
                BeginGroup("Context: " + reactionKey, groupKey, AdvancedFieldCategory.Tuning);
                targetDefType = typeof(DiaryContextReactionDef);
                targetResolve = () => DiaryContextReactions.ForKey(key);
                targetDefName = defName;
                targetTemplateKey = null;
                return this;
            }

            public Builder Template(string templateKey, string groupKey)
            {
                // Each prompt template becomes its own rail group so the player picks a template, then
                // edits its system prompt / final instruction / recipient follow-up. The resolver uses
                // the stable templateKey; the defName here is only a namespace for the override key.
                string key = templateKey;
                BeginGroup("Template: " + templateKey, groupKey, AdvancedFieldCategory.Prompts);
                targetDefType = typeof(DiaryPromptTemplateDef);
                targetResolve = () => DiaryPromptTemplates.ForKey(key);
                targetDefName = "tmpl." + templateKey;
                targetTemplateKey = key;
                return this;
            }

            public Builder Def<TDef>(TDef def, string display, string groupKey, AdvancedFieldCategory category)
                where TDef : Def
            {
                string defName = def == null || string.IsNullOrWhiteSpace(def.defName) ? display : def.defName;
                TDef captured = def;
                BeginGroup(display, groupKey, category);
                targetDefType = typeof(TDef);
                targetResolve = () => DefDatabase<TDef>.GetNamedSilentFail(defName) ?? captured;
                targetDefName = defName;
                targetTemplateKey = null;
                return this;
            }

            public Builder Bool(string fieldName)
            {
                return Add(fieldName, AdvancedFieldType.Bool, 0f, 0f, false, false, false, null);
            }

            public Builder Int(string fieldName, int min, int max)
            {
                return Add(fieldName, AdvancedFieldType.Int, min, max, true, false, false, null);
            }

            public Builder Float(string fieldName, float min, float max, bool slider)
            {
                return Add(fieldName, AdvancedFieldType.Float, min, max, true, slider, false, null);
            }

            public Builder Text(string fieldName)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, false, null);
            }

            public Builder Text(string fieldName, Func<string> effectiveReader)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, false, effectiveReader);
            }

            public Builder StringList(string fieldName)
            {
                return Add(fieldName, AdvancedFieldType.StringList, 0f, 0f, false, false, true, null);
            }

            public Builder StringList(string fieldName, Func<string> effectiveReader)
            {
                return Add(fieldName, AdvancedFieldType.StringList, 0f, 0f, false, false, true, effectiveReader);
            }

            public Builder IntList(string fieldName)
            {
                return Add(fieldName, AdvancedFieldType.IntList, 0f, 0f, false, false, true, null);
            }

            // A long multi-line string (prompt text). Rendered as a TextArea, not a one-line field.
            public Builder LongText(string fieldName)
            {
                Func<string> effectiveReader = null;

                // Template prompt fields (systemPrompt/finalInstruction/recipientFinalInstruction) are
                // raw per-template overrides. By policy the inherited shared prompt text is shown only on
                // the Shared/event prompts subpage, so Prompt policy leaves these blank (blank = inherit)
                // and the two subpages never edit the same setting twice. The policy is the single,
                // testable decision point: only when it opts a field back in do we mirror the inherited
                // shared text as the greyed effective value.
                if (!string.IsNullOrEmpty(targetTemplateKey)
                    && PromptSettingsMenuPolicy.IsTemplateTextOverrideField(fieldName)
                    && PromptSettingsMenuPolicy.TemplateFieldShouldShowInheritedFallback(fieldName))
                {
                    string templateKey = targetTemplateKey;
                    if (string.Equals(fieldName, "systemPrompt", StringComparison.Ordinal))
                    {
                        effectiveReader = () => DiaryPromptTemplates.SystemPromptFor(templateKey);
                    }
                    else if (string.Equals(fieldName, "finalInstruction", StringComparison.Ordinal))
                    {
                        effectiveReader = () => DiaryPromptTemplates.FinalInstructionFor(templateKey);
                    }
                    else if (string.Equals(fieldName, "recipientFinalInstruction", StringComparison.Ordinal))
                    {
                        effectiveReader = () => DiaryPromptTemplates.RecipientFinalInstruction(templateKey);
                    }
                }

                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, true, effectiveReader);
            }

            public Builder LongText(string fieldName, Func<string> effectiveReader)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, true, effectiveReader);
            }

            public Builder CustomLongText(string fieldName, Func<object, string> formatter, Func<string, object> parser)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, true, null, formatter, parser);
            }

            public Builder CustomFloat(string fieldName, Func<object> reader, Action<object> writer, float min, float max, bool slider)
            {
                return Add(fieldName, AdvancedFieldType.Float, min, max, true, slider, false, null, null, null, reader, writer);
            }

            public Builder CustomInt(string fieldName, Func<object> reader, Action<object> writer, int min, int max)
            {
                return Add(fieldName, AdvancedFieldType.Int, min, max, true, false, false, null, null, null, reader, writer);
            }

            public Builder CustomBool(string fieldName, Func<object> reader, Action<object> writer)
            {
                return Add(fieldName, AdvancedFieldType.Bool, 0f, 0f, false, false, false, null, null, null, reader, writer);
            }

            public Builder CustomText(string fieldName, Func<object> reader, Action<object> writer)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, false, null, null, null, reader, writer);
            }

            public Builder CustomText(string fieldName, Func<object> reader, Action<object> writer,
                Func<string> effectiveReader)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, false, effectiveReader, null, null, reader, writer);
            }

            public Builder CustomLongText(string fieldName, Func<object> reader, Action<object> writer)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, true, null, null, null, reader, writer);
            }

            public Builder CustomLongText(string fieldName, Func<object> reader, Action<object> writer,
                Func<string> effectiveReader)
            {
                return Add(fieldName, AdvancedFieldType.String, 0f, 0f, false, false, true, effectiveReader, null, null, reader, writer);
            }

            public Builder CustomStringList(string fieldName, Func<object> reader, Action<object> writer)
            {
                return Add(fieldName, AdvancedFieldType.StringList, 0f, 0f, false, false, true, null, null, null, reader, writer);
            }

            public Builder CustomStringList(string fieldName, Func<object> reader, Action<object> writer,
                Func<string> effectiveReader)
            {
                return Add(fieldName, AdvancedFieldType.StringList, 0f, 0f, false, false, true, effectiveReader, null, null, reader, writer);
            }

            private Builder Add(
                string fieldName,
                AdvancedFieldType type,
                float min,
                float max,
                bool hasRange,
                bool slider,
                bool longText,
                Func<string> effectiveReader,
                Func<object, string> customFormatter = null,
                Func<string, object> customParser = null,
                Func<object> customReader = null,
                Action<object> customWriter = null)
            {
                if (TuningOverrideMigration.IsRemovedFieldName(fieldName))
                {
                    // Keep raw XML translation lookup fields out of node settings. Players can edit
                    // the paired literal override fields instead (for example startText, not startTextKey).
                    return this;
                }

                AdvancedFieldDescriptor descriptor = new AdvancedFieldDescriptor(targetDefType, targetResolve, targetDefName, fieldName)
                {
                    fieldType = type,
                    min = min,
                    max = max,
                    hasRange = hasRange,
                    useSlider = slider,
                    isLongText = longText,
                    effectiveReader = effectiveReader,
                    customFormatter = customFormatter,
                    customParser = customParser,
                    customReader = customReader,
                    customWriter = customWriter,
                    // Auto-keyed label and help so a translator can override one field without code
                    // changes; missing keys fall back to the humanized field name / no help line.
                    labelKey = "PawnDiary.Settings.Adv.Field." + fieldName,
                    tooltipKey = "PawnDiary.Settings.Adv.Help." + fieldName
                };
                current.fields.Add(descriptor);
                all.Add(descriptor);
                if (snapshotted)
                {
                    descriptor.defaultSnapshot = descriptor.ReadDefValueString();
                }

                return this;
            }

            private void BeginGroup(string display, string groupKey, AdvancedFieldCategory category)
            {
                current = new AdvancedFieldGroup { groupKey = groupKey, displayName = display, category = category };
                groups.Add(current);
            }
        }
    }
}
