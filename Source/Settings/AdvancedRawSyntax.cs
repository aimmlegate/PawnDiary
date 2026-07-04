// Pure syntax checks for compact raw tables shown in the Advanced settings tab. The UI uses these
// checks to keep advanced, XML-shaped override boxes editable while still giving immediate feedback
// before a malformed table is written into the live Def override store.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>Validation issue kinds for raw Advanced settings table syntax.</summary>
    public enum AdvancedRawSyntaxIssue
    {
        None,
        ExpectedThoughtProgressionColumns,
        MissingCategory,
        MissingThoughtDef,
        MissingStages,
        BadStagePair,
        BadStageIndex,
        BadSeverity,
        ExpectedPair,
        MissingKey,
        BadFloat,
        ExpectedPromptFieldColumns,
        BadBool,
        ExpectedSeverityTierColumns,
        BadInt
    }

    /// <summary>One parsed <c>stageIndex:severity</c> token in a thought progression rule.</summary>
    public class AdvancedRawSyntaxStage
    {
        public string stageIndex;
        public string severity;
    }

    /// <summary>One parsed raw-table line, kept as strings so the UI can display exactly what was typed.</summary>
    public class AdvancedRawSyntaxLine
    {
        public int lineNumber;
        public string rawText;
        public string categoryKey;
        public string thoughtDefName;
        public readonly List<AdvancedRawSyntaxStage> stages = new List<AdvancedRawSyntaxStage>();
        public readonly List<string> columns = new List<string>();
    }

    /// <summary>The first validation error found in an Advanced raw table.</summary>
    public class AdvancedRawSyntaxError
    {
        public int lineNumber;
        public string rawText;
        public string token;
        public AdvancedRawSyntaxIssue issue;
    }

    /// <summary>Parsed syntax state for an Advanced raw table.</summary>
    public class AdvancedRawSyntaxCheck
    {
        public string schemaName;
        public bool valid = true;
        public bool empty;
        public bool nullSentinel;
        public readonly List<AdvancedRawSyntaxLine> lines = new List<AdvancedRawSyntaxLine>();
        public AdvancedRawSyntaxError firstError;
    }

    /// <summary>
    /// Knows which raw Advanced fields have structured syntax and validates them without RimWorld or
    /// Unity objects. New to C#/RimWorld? See AGENTS.md ("Defs") for why these strings mirror XML.
    /// </summary>
    public static class AdvancedRawSyntax
    {
        public const string WeatherMentionChancesField = "weatherMentionChances";
        public const string RitualQualityBandsField = "ritualQualityBands";
        public const string PromptFieldsField = "fields";
        public const string SeverityTiersField = "hediffSeverityTiers";
        public const string ThoughtProgressionRulesField = "thoughtProgressionRules";
        public const string ProgressionSkillMilestonesField = "progressionSkillMilestones";

        public const string CategoryKeyField = "categoryKey";
        public const string ThoughtDefNameField = "thoughtDefName";
        public const string StagesField = "stages";
        public const string StageIndexField = "stageIndex";
        public const string SeverityField = "severity";

        /// <summary>True when the field should draw the syntax preview/check strip under its text box.</summary>
        public static bool HasPreview(string fieldName)
        {
            return string.Equals(fieldName, WeatherMentionChancesField, StringComparison.Ordinal)
                || string.Equals(fieldName, RitualQualityBandsField, StringComparison.Ordinal)
                || string.Equals(fieldName, PromptFieldsField, StringComparison.Ordinal)
                || string.Equals(fieldName, SeverityTiersField, StringComparison.Ordinal)
                || string.Equals(fieldName, ThoughtProgressionRulesField, StringComparison.Ordinal)
                || string.Equals(fieldName, ProgressionSkillMilestonesField, StringComparison.Ordinal);
        }

        /// <summary>Runs the field-specific syntax check, or returns null for fields with no raw syntax.</summary>
        public static AdvancedRawSyntaxCheck Check(string fieldName, string text)
        {
            if (string.Equals(fieldName, WeatherMentionChancesField, StringComparison.Ordinal))
            {
                return CheckWeatherMentionRules(text);
            }

            if (string.Equals(fieldName, RitualQualityBandsField, StringComparison.Ordinal))
            {
                return CheckRitualQualityBands(text);
            }

            if (string.Equals(fieldName, PromptFieldsField, StringComparison.Ordinal))
            {
                return CheckPromptFields(text);
            }

            if (string.Equals(fieldName, SeverityTiersField, StringComparison.Ordinal))
            {
                return CheckSeverityTiers(text);
            }

            if (string.Equals(fieldName, ThoughtProgressionRulesField, StringComparison.Ordinal))
            {
                return CheckThoughtProgressionRules(text);
            }

            if (string.Equals(fieldName, ProgressionSkillMilestonesField, StringComparison.Ordinal))
            {
                return CheckIntList(text, "List<int>");
            }

            return null;
        }

        /// <summary>Validates <c>WeatherDef=chance</c>, mirroring WeatherMentionRule.weather/chance.</summary>
        public static AdvancedRawSyntaxCheck CheckWeatherMentionRules(string text)
        {
            AdvancedRawSyntaxCheck check = NewCheck("WeatherMentionRule");
            if (PrepareLines(check, text))
            {
                return check;
            }

            foreach (AdvancedRawSyntaxLine line in NonEmptyLines(text))
            {
                check.lines.Add(line);
                string[] pair = SplitPair(line.rawText);
                if (pair == null)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.ExpectedPair, line.rawText);
                    continue;
                }

                AddColumns(line, pair[0], pair[1]);
                if (pair[0].Length == 0)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.MissingKey, line.rawText);
                }

                if (!IsFloat(pair[1]))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadFloat, pair[1]);
                }
            }

            MarkEmptyIfNoLines(check);
            return check;
        }

        /// <summary>Validates <c>maxExclusive=label</c>, mirroring RitualQualityBand.maxExclusive/label.</summary>
        public static AdvancedRawSyntaxCheck CheckRitualQualityBands(string text)
        {
            AdvancedRawSyntaxCheck check = NewCheck("RitualQualityBand");
            if (PrepareLines(check, text))
            {
                return check;
            }

            foreach (AdvancedRawSyntaxLine line in NonEmptyLines(text))
            {
                check.lines.Add(line);
                string[] pair = SplitPair(line.rawText);
                if (pair == null)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.ExpectedPair, line.rawText);
                    continue;
                }

                AddColumns(line, pair[0], pair[1]);
                if (!IsFloat(pair[0]))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadFloat, pair[0]);
                }
            }

            MarkEmptyIfNoLines(check);
            return check;
        }

        /// <summary>
        /// Validates prompt-template field rows:
        /// <c>enabled|label|source|contextKey</c>. <c>contextKey</c> may be omitted because the Def
        /// field defaults to an empty string when absent.
        /// </summary>
        public static AdvancedRawSyntaxCheck CheckPromptFields(string text)
        {
            AdvancedRawSyntaxCheck check = NewCheck("DiaryPromptFieldDef");
            if (PrepareLines(check, text))
            {
                return check;
            }

            foreach (AdvancedRawSyntaxLine line in NonEmptyLines(text))
            {
                check.lines.Add(line);
                string[] parts = line.rawText.Split('|');
                if (parts.Length < 3 || parts.Length > 4)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.ExpectedPromptFieldColumns, line.rawText);
                    continue;
                }

                string enabled = parts[0].Trim();
                string label = parts[1].Trim();
                string source = parts[2].Trim();
                string contextKey = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                AddColumns(line, enabled, label, source, contextKey);

                if (!IsBoolLiteral(enabled))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadBool, enabled);
                }
            }

            MarkEmptyIfNoLines(check);
            return check;
        }

        /// <summary>
        /// Validates prompt-enchantment severity tier rows:
        /// <c>level|chance|frequency|weight|severity</c>. Numeric columns may be omitted to inherit
        /// their Def defaults, matching PromptEnchantmentSeverityTier's -1 fallback fields.
        /// </summary>
        public static AdvancedRawSyntaxCheck CheckSeverityTiers(string text)
        {
            AdvancedRawSyntaxCheck check = NewCheck("PromptEnchantmentSeverityTier");
            if (PrepareLines(check, text))
            {
                return check;
            }

            foreach (AdvancedRawSyntaxLine line in NonEmptyLines(text))
            {
                check.lines.Add(line);
                string[] parts = line.rawText.Split('|');
                if (parts.Length < 1 || parts.Length > 5 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.ExpectedSeverityTierColumns, line.rawText);
                    continue;
                }

                for (int i = 0; i < parts.Length; i++)
                {
                    line.columns.Add(parts[i].Trim());
                }

                for (int i = 1; i < parts.Length; i++)
                {
                    string value = parts[i].Trim();
                    if (value.Length > 0 && !IsFloat(value))
                    {
                        MarkError(check, line, AdvancedRawSyntaxIssue.BadFloat, value);
                    }
                }
            }

            MarkEmptyIfNoLines(check);
            return check;
        }

        /// <summary>
        /// Validates the documented thought progression format:
        /// <c>categoryKey|thoughtDefName|stageIndex:severity,...</c>.
        /// The compact row mirrors the current XML Def schema:
        /// <c>categoryKey</c>, <c>thoughtDefName</c>, and <c>stages/li/stageIndex + severity</c>.
        /// </summary>
        public static AdvancedRawSyntaxCheck CheckThoughtProgressionRules(string text)
        {
            AdvancedRawSyntaxCheck check = NewCheck("ThoughtProgressionRule");
            if (PrepareLines(check, text))
            {
                return check;
            }

            foreach (AdvancedRawSyntaxLine line in NonEmptyLines(text))
            {
                check.lines.Add(line);
                string[] parts = line.rawText.Split('|');
                if (parts.Length != 3)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.ExpectedThoughtProgressionColumns, line.rawText);
                    continue;
                }

                line.categoryKey = parts[0].Trim();
                line.thoughtDefName = parts[1].Trim();
                string stageList = parts[2].Trim();
                AddColumns(line, line.categoryKey, line.thoughtDefName, stageList);

                if (line.categoryKey.Length == 0)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.MissingCategory, line.rawText);
                }

                if (line.thoughtDefName.Length == 0)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.MissingThoughtDef, line.rawText);
                }

                if (stageList.Length == 0)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.MissingStages, line.rawText);
                    continue;
                }

                ValidateStages(check, line, stageList);
            }

            MarkEmptyIfNoLines(check);
            return check;
        }

        /// <summary>Validates one-integer-per-line XML list fields such as progression skill milestones.</summary>
        public static AdvancedRawSyntaxCheck CheckIntList(string text, string schemaName)
        {
            AdvancedRawSyntaxCheck check = NewCheck(schemaName);
            if (PrepareLines(check, text))
            {
                return check;
            }

            foreach (AdvancedRawSyntaxLine line in NonEmptyLines(text))
            {
                check.lines.Add(line);
                line.columns.Add(line.rawText);
                if (!IsInt(line.rawText))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadInt, line.rawText);
                }
            }

            MarkEmptyIfNoLines(check);
            return check;
        }

        private static void ValidateStages(AdvancedRawSyntaxCheck check, AdvancedRawSyntaxLine line, string stageList)
        {
            string[] stageParts = stageList.Split(',');
            for (int i = 0; i < stageParts.Length; i++)
            {
                string stageText = stageParts[i].Trim();
                if (stageText.Length == 0)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadStagePair, stageText);
                    continue;
                }

                string[] pair = stageText.Split(':');
                if (pair.Length != 2)
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadStagePair, stageText);
                    continue;
                }

                string stageIndexText = pair[0].Trim();
                if (!IsInt(stageIndexText))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadStageIndex, stageText);
                    continue;
                }

                string severityText = pair[1].Trim();
                if (!IsInt(severityText))
                {
                    MarkError(check, line, AdvancedRawSyntaxIssue.BadSeverity, stageText);
                    continue;
                }

                line.stages.Add(new AdvancedRawSyntaxStage
                {
                    stageIndex = stageIndexText,
                    severity = severityText
                });
            }
        }

        private static AdvancedRawSyntaxCheck NewCheck(string schemaName)
        {
            return new AdvancedRawSyntaxCheck { schemaName = schemaName ?? string.Empty };
        }

        // Returns true when the caller can stop: blank text and <null> are complete valid values.
        private static bool PrepareLines(AdvancedRawSyntaxCheck check, string text)
        {
            string value = text ?? string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                check.empty = true;
                return true;
            }

            if (IsNullSentinel(trimmed))
            {
                check.empty = true;
                check.nullSentinel = true;
                return true;
            }

            return false;
        }

        private static IEnumerable<AdvancedRawSyntaxLine> NonEmptyLines(string text)
        {
            string value = text ?? string.Empty;
            string[] rawLines = value.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
            {
                string raw = rawLines[i].Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                yield return new AdvancedRawSyntaxLine
                {
                    lineNumber = i + 1,
                    rawText = raw
                };
            }
        }

        private static void AddColumns(AdvancedRawSyntaxLine line, params string[] columns)
        {
            if (line == null || columns == null)
            {
                return;
            }

            for (int i = 0; i < columns.Length; i++)
            {
                line.columns.Add(columns[i] ?? string.Empty);
            }
        }

        private static string[] SplitPair(string line)
        {
            if (line == null)
            {
                return null;
            }

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

        private static void MarkEmptyIfNoLines(AdvancedRawSyntaxCheck check)
        {
            if (check.lines.Count == 0)
            {
                check.empty = true;
            }
        }

        private static void MarkError(
            AdvancedRawSyntaxCheck check,
            AdvancedRawSyntaxLine line,
            AdvancedRawSyntaxIssue issue,
            string token)
        {
            check.valid = false;
            if (check.firstError != null)
            {
                return;
            }

            check.firstError = new AdvancedRawSyntaxError
            {
                lineNumber = line != null ? line.lineNumber : 0,
                rawText = line != null ? line.rawText : string.Empty,
                issue = issue,
                token = token ?? string.Empty
            };
        }

        private static bool IsBoolLiteral(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool IsFloat(string value)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool IsNullSentinel(string text)
        {
            return string.Equals(text, "<null>", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
        }
    }
}
