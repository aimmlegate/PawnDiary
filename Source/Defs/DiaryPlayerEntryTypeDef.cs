// XML-backed catalog for the friendly entry types offered by the normal-play composer. Runtime Defs
// are copied into plain snapshots before they reach pure policy or UI code, and every missing/malformed
// catalog falls back to one conservative Personal row.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One player-facing entry category and its frozen prompt/display policy.</summary>
    public class DiaryPlayerEntryTypeDef : Def
    {
        public string entryTypeKey;
        public string eventPromptKey;
        public string defaultTemplateKey;
        public int displayOrder;
        public bool important;
        public bool combat;
        public bool reflection;
        public string colorCue;
        public string domain;
    }

    /// <summary>Impure Def lookup and localization boundary for player entry types.</summary>
    internal static class DiaryPlayerEntryTypes
    {
        public const string PersonalDefName = "PawnDiary_PlayerEntryType_Personal";
        public const string PersonalEventPromptKey = "PlayerPersonal";

        /// <summary>Returns a detached, deterministically ordered catalog for the composer UI.</summary>
        public static List<PlayerEntryTypeSnapshot> ForUi()
        {
            List<PlayerEntryTypeSnapshot> result = new List<PlayerEntryTypeSnapshot>();
            List<DiaryPlayerEntryTypeDef> defs = DefDatabase<DiaryPlayerEntryTypeDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    PlayerEntryTypeSnapshot snapshot = Snapshot(defs[i]);
                    if (IsUsable(snapshot) && !Contains(result, snapshot.entryTypeKey)) result.Add(snapshot);
                }
            }

            result.Sort(Compare);
            if (!Contains(result, PlayerEntryComposerPolicy.PersonalEntryTypeKey))
            {
                result.Insert(0, PersonalFallback());
            }
            return result;
        }

        /// <summary>Resolves an exact selectable key. Blank and unknown keys are rejected.</summary>
        public static bool TryResolve(string entryTypeKey, out PlayerEntryTypeSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(entryTypeKey)) return false;
            string key = entryTypeKey.Trim();
            List<PlayerEntryTypeSnapshot> catalog = ForUi();
            for (int i = 0; i < catalog.Count; i++)
            {
                if (string.Equals(catalog[i].entryTypeKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    snapshot = catalog[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>Resolves saved display metadata, using Personal for blank/unknown new-style keys.</summary>
        public static PlayerEntryTypeSnapshot ResolveOrPersonal(string entryTypeKey)
        {
            PlayerEntryTypeSnapshot result;
            return TryResolve(entryTypeKey, out result) ? result : PersonalFallbackFromDefs();
        }

        private static PlayerEntryTypeSnapshot Snapshot(DiaryPlayerEntryTypeDef source)
        {
            if (source == null) return null;
            return new PlayerEntryTypeSnapshot
            {
                entryTypeKey = Clean(source.entryTypeKey),
                eventPromptKey = Clean(source.eventPromptKey),
                defaultTemplateKey = Clean(source.defaultTemplateKey),
                displayOrder = source.displayOrder,
                important = source.important,
                combat = source.combat,
                reflection = source.reflection,
                colorCue = Clean(source.colorCue),
                domain = Clean(source.domain),
                label = source.LabelCap.Resolve(),
                description = source.description ?? string.Empty
            };
        }

        private static PlayerEntryTypeSnapshot PersonalFallbackFromDefs()
        {
            DiaryPlayerEntryTypeDef source =
                DefDatabase<DiaryPlayerEntryTypeDef>.GetNamedSilentFail(PersonalDefName);
            PlayerEntryTypeSnapshot snapshot = Snapshot(source);
            return IsUsable(snapshot) ? snapshot : PersonalFallback();
        }

        private static PlayerEntryTypeSnapshot PersonalFallback()
        {
            return new PlayerEntryTypeSnapshot
            {
                entryTypeKey = PlayerEntryComposerPolicy.PersonalEntryTypeKey,
                eventPromptKey = PersonalEventPromptKey,
                defaultTemplateKey = DiaryPipelineTemplates.SoloDefault,
                displayOrder = 0,
                important = false,
                combat = false,
                reflection = false,
                colorCue = DiaryEvent.QuietColorCue,
                domain = DiaryEventDomainClassifier.PlayerEntry,
                label = "PawnDiary.EntryComposer.EntryType.Personal".Translate().Resolve(),
                description = "PawnDiary.EntryComposer.EntryType.Personal.Description".Translate().Resolve()
            };
        }

        private static bool IsUsable(PlayerEntryTypeSnapshot row)
        {
            return row != null
                && !string.IsNullOrWhiteSpace(row.entryTypeKey)
                && !string.IsNullOrWhiteSpace(row.eventPromptKey)
                && !string.IsNullOrWhiteSpace(row.defaultTemplateKey);
        }

        private static bool Contains(List<PlayerEntryTypeSnapshot> rows, string key)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i]?.entryTypeKey, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int Compare(PlayerEntryTypeSnapshot left, PlayerEntryTypeSnapshot right)
        {
            int order = (left?.displayOrder ?? 0).CompareTo(right?.displayOrder ?? 0);
            return order != 0 ? order : string.Compare(left?.entryTypeKey, right?.entryTypeKey,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Impure localized projection of only the templates explicitly opted into the composer.</summary>
    internal static class DiaryPlayerPromptTemplates
    {
        /// <summary>Returns detached selectable templates in XML-owned player order.</summary>
        public static List<PlayerEntryTemplateSnapshot> ForUi()
        {
            List<PlayerEntryTemplateSnapshot> result = new List<PlayerEntryTemplateSnapshot>();
            List<DiaryPromptTemplateDef> defs = FirstSelectableDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                DiaryPromptTemplateDef source = defs[i];
                string key = source.templateKey.Trim();
                result.Add(new PlayerEntryTemplateSnapshot
                {
                    templateKey = key,
                    displayOrder = source.playerOrder,
                    label = source.LabelCap.Resolve(),
                    description = source.description ?? string.Empty
                });
            }
            result.Sort(Compare);
            return result;
        }

        /// <summary>
        /// Returns the exact first selectable Def for each template key. Both the UI and the prompt-policy
        /// adapter consume this same winner list so a duplicate key or defName alias cannot rebind later.
        /// </summary>
        internal static List<DiaryPromptTemplateDef> FirstSelectableDefs()
        {
            List<DiaryPromptTemplateDef> result = new List<DiaryPromptTemplateDef>();
            List<DiaryPromptTemplateDef> defs =
                DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;
            if (defs == null) return result;

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryPromptTemplateDef source = defs[i];
                if (source == null || !source.playerSelectable
                    || string.IsNullOrWhiteSpace(source.templateKey)) continue;
                string key = source.templateKey.Trim();
                bool duplicate = false;
                for (int existing = 0; existing < result.Count; existing++)
                {
                    if (string.Equals(
                        result[existing]?.templateKey?.Trim(), key,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) result.Add(source);
            }
            return result;
        }

        private static int Compare(PlayerEntryTemplateSnapshot left, PlayerEntryTemplateSnapshot right)
        {
            int order = (left?.displayOrder ?? 0).CompareTo(right?.displayOrder ?? 0);
            return order != 0 ? order : string.Compare(left?.templateKey, right?.templateKey,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
