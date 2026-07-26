// Developer-only cultural-lore section for Dialog_PawnWritingStyle.
//
// The pawn saves only culture identity/provenance; the actual lore clauses and topic triggers stay
// XML-owned. This UI combines a detached copy of both so developers can answer three questions
// without opening Player.log: which profile is active, what text/structured facts select a topic,
// and whether the last prompt actually annotated a field. Nothing in this file mutates save state.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_PawnWritingStyle
    {
        private string selectedLoreTopicKey = string.Empty;
        private Vector2 loreTextScroll;

        /// <summary>
        /// Draws the developer-only culture provenance, topic picker, clauses, triggers, and most
        /// recent annotation result.
        /// </summary>
        private void DrawLoreMemorySection(
            float x,
            float width,
            ref float y,
            LoreMemorySnapshotForDev lore)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            string title = FormatLoreFrame(
                "PawnDiary.Dev.Lore.SectionTitle",
                lore?.topics?.Count ?? 0);
            float titleHeight = SmallLabelHeight(title, width);
            Widgets.Label(new Rect(x, y, width, titleHeight), title);
            y += titleHeight + FieldGap;

            if (lore == null)
            {
                string unavailable = "PawnDiary.Dev.Lore.Unavailable".Translate();
                float unavailableHeight = SmallLabelHeight(unavailable, width);
                Widgets.Label(new Rect(x, y, width, unavailableHeight), unavailable);
                y += unavailableHeight + FieldGap;
                return;
            }

            string state = LoreStateText(lore);
            float stateHeight = SmallLabelHeight(state, width);
            Widgets.Label(new Rect(x, y, width, stateHeight), state);
            y += stateHeight + FieldGap;

            LoreMemoryTopicForDev selected = EnsureLoreTopicSelection(lore.topics);
            if (selected == null)
            {
                string empty = "PawnDiary.Dev.Lore.NoTopics".Translate();
                float emptyHeight = SmallLabelHeight(empty, width);
                Widgets.Label(new Rect(x, y, width, emptyHeight), empty);
                y += emptyHeight + FieldGap;
            }
            else
            {
                int selectedIndex = FindLoreTopicIndex(lore.topics, selected.topicKey);
                DrawLoreTopicNavigation(
                    new Rect(x, y, width, ButtonHeight),
                    lore.topics,
                    selected,
                    selectedIndex);
                y += ButtonHeight + FieldGap;

                y += DrawLabeledScrollText(
                    new Rect(x, y, width, PromptAreaHeight),
                    "PawnDiary.Dev.Lore.TopicDetails".Translate(),
                    LoreTopicDetails(lore, selected),
                    ref loreTextScroll,
                    PromptAreaHeight) + FieldGap;
            }

            string lastMatch = LoreLastMatchText(lore);
            float lastMatchHeight = SmallLabelHeight(lastMatch, width);
            Widgets.Label(new Rect(x, y, width, lastMatchHeight), lastMatch);
            y += lastMatchHeight + FieldGap;
        }

        /// <summary>
        /// Mirrors <see cref="DrawLoreMemorySection"/> so the parent scroll view reserves exactly
        /// the height used by the developer lore controls.
        /// </summary>
        private float LoreMemorySectionHeight(float width, LoreMemorySnapshotForDev lore)
        {
            float height = SectionGap + FieldGap;
            height += SmallLabelHeight(
                FormatLoreFrame(
                    "PawnDiary.Dev.Lore.SectionTitle",
                    lore?.topics?.Count ?? 0),
                width) + FieldGap;

            if (lore == null)
            {
                return height
                    + SmallLabelHeight(
                        "PawnDiary.Dev.Lore.Unavailable".Translate(),
                        width)
                    + FieldGap;
            }

            height += SmallLabelHeight(LoreStateText(lore), width) + FieldGap;
            if (SelectedLoreTopicOrFirst(lore.topics) == null)
            {
                height += SmallLabelHeight(
                    "PawnDiary.Dev.Lore.NoTopics".Translate(),
                    width) + FieldGap;
            }
            else
            {
                height += ButtonHeight + FieldGap;
                height += LabeledScrollTextHeight(
                    "PawnDiary.Dev.Lore.TopicDetails".Translate(),
                    width,
                    PromptAreaHeight) + FieldGap;
            }

            height += SmallLabelHeight(LoreLastMatchText(lore), width) + FieldGap;
            return height;
        }

        private void DrawLoreTopicNavigation(
            Rect rect,
            IReadOnlyList<LoreMemoryTopicForDev> topics,
            LoreMemoryTopicForDev selected,
            int selectedIndex)
        {
            float unit = Mathf.Max(1f, (rect.width - FieldGap * 2f) / 4f);
            Rect previousRect = new Rect(rect.x, rect.y, unit, rect.height);
            Rect pickerRect = new Rect(
                previousRect.xMax + FieldGap,
                rect.y,
                unit * 2f,
                rect.height);
            Rect nextRect = new Rect(
                pickerRect.xMax + FieldGap,
                rect.y,
                unit,
                rect.height);

            if (Widgets.ButtonText(
                previousRect,
                "PawnDiary.Dev.Lore.Previous".Translate(),
                true,
                true,
                selectedIndex > 0))
            {
                SelectLoreTopic(topics[selectedIndex - 1]);
            }

            string selector = FormatLoreFrame(
                "PawnDiary.Dev.Lore.Selector",
                selected.topicKey,
                selectedIndex + 1,
                topics?.Count ?? 0);
            if (Widgets.ButtonText(pickerRect, selector))
            {
                OpenLoreTopicPicker(topics);
            }

            if (Widgets.ButtonText(
                nextRect,
                "PawnDiary.Dev.Lore.Next".Translate(),
                true,
                true,
                topics != null && selectedIndex >= 0 && selectedIndex + 1 < topics.Count))
            {
                SelectLoreTopic(topics[selectedIndex + 1]);
            }
        }

        private void OpenLoreTopicPicker(IReadOnlyList<LoreMemoryTopicForDev> topics)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (topics != null)
            {
                for (int i = 0; i < topics.Count; i++)
                {
                    LoreMemoryTopicForDev topic = topics[i];
                    if (topic == null || string.IsNullOrWhiteSpace(topic.topicKey))
                    {
                        continue;
                    }

                    LoreMemoryTopicForDev option = topic;
                    options.Add(new FloatMenuOption(
                        LoreTopicLabel(option),
                        delegate
                        {
                            SelectLoreTopic(option);
                        }));
                }
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private LoreMemoryTopicForDev EnsureLoreTopicSelection(
            IReadOnlyList<LoreMemoryTopicForDev> topics)
        {
            LoreMemoryTopicForDev selected = SelectedLoreTopicOrFirst(topics);
            if (selected == null)
            {
                selectedLoreTopicKey = string.Empty;
                loreTextScroll = Vector2.zero;
                return null;
            }

            if (!string.Equals(
                selectedLoreTopicKey,
                selected.topicKey,
                StringComparison.Ordinal))
            {
                SelectLoreTopic(selected);
            }

            return selected;
        }

        private LoreMemoryTopicForDev SelectedLoreTopicOrFirst(
            IReadOnlyList<LoreMemoryTopicForDev> topics)
        {
            int selectedIndex = FindLoreTopicIndex(topics, selectedLoreTopicKey);
            if (selectedIndex >= 0)
            {
                return topics[selectedIndex];
            }

            if (topics == null)
            {
                return null;
            }

            for (int i = 0; i < topics.Count; i++)
            {
                if (topics[i] != null && !string.IsNullOrWhiteSpace(topics[i].topicKey))
                {
                    return topics[i];
                }
            }

            return null;
        }

        private void SelectLoreTopic(LoreMemoryTopicForDev topic)
        {
            if (topic == null)
            {
                return;
            }

            selectedLoreTopicKey = topic.topicKey ?? string.Empty;
            loreTextScroll = Vector2.zero;
        }

        private static int FindLoreTopicIndex(
            IReadOnlyList<LoreMemoryTopicForDev> topics,
            string topicKey)
        {
            if (topics == null || string.IsNullOrWhiteSpace(topicKey))
            {
                return -1;
            }

            for (int i = 0; i < topics.Count; i++)
            {
                LoreMemoryTopicForDev topic = topics[i];
                if (topic != null
                    && string.Equals(topic.topicKey, topicKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string LoreStateText(LoreMemorySnapshotForDev lore)
        {
            if (lore == null)
            {
                return string.Empty;
            }

            string state = lore.hasKnowledgeState
                ? "PawnDiary.Dev.Lore.StatePresent".Translate().ToString()
                : "PawnDiary.Dev.Lore.StateMissing".Translate().ToString();
            string injection = lore.injectionEnabled
                ? "PawnDiary.Dev.Lore.InjectionEnabled".Translate().ToString()
                : "PawnDiary.Dev.Lore.InjectionDisabled".Translate().ToString();
            return FormatLoreFrame(
                "PawnDiary.Dev.Lore.State",
                state,
                injection,
                LoreProfileText(lore.originProfile, lore.originCultureSource),
                LoreProfileText(lore.adoptedProfile, null));
        }

        private static string LoreProfileText(
            LoreMemoryProfileForDev profile,
            string provenance)
        {
            string requested = string.IsNullOrWhiteSpace(profile?.requestedCultureDefName)
                ? "PawnDiary.Dev.Lore.None".Translate().ToString()
                : profile.requestedCultureDefName;
            string status;
            if (profile == null || string.IsNullOrWhiteSpace(profile.requestedCultureDefName))
            {
                status = "PawnDiary.Dev.Lore.ProfileNotApplicable".Translate();
            }
            else if (profile.authored)
            {
                status = FormatLoreFrame(
                    "PawnDiary.Dev.Lore.ProfileFound",
                    profile.resolvedCultureDefName);
            }
            else if (!string.IsNullOrWhiteSpace(profile.resolvedCultureDefName))
            {
                status = FormatLoreFrame(
                    "PawnDiary.Dev.Lore.ProfileFallback",
                    profile.resolvedCultureDefName);
            }
            else
            {
                status = "PawnDiary.Dev.Lore.ProfileMissing".Translate();
            }

            if (provenance == null)
            {
                return FormatLoreFrame(
                    "PawnDiary.Dev.Lore.ProfileWithoutSource",
                    requested,
                    status);
            }

            string source = string.IsNullOrWhiteSpace(provenance)
                ? "PawnDiary.Dev.Lore.None".Translate().ToString()
                : provenance;
            return FormatLoreFrame("PawnDiary.Dev.Lore.Profile", requested, source, status);
        }

        private static string LoreTopicDetails(
            LoreMemorySnapshotForDev lore,
            LoreMemoryTopicForDev topic)
        {
            StringBuilder builder = new StringBuilder(512);
            AppendLoreDetail(
                builder,
                "PawnDiary.Dev.Lore.OriginClause",
                LoreClauseText(topic?.originClause));
            if (!string.IsNullOrWhiteSpace(
                lore?.adoptedProfile?.requestedCultureDefName))
            {
                AppendLoreDetail(
                    builder,
                    "PawnDiary.Dev.Lore.AdoptedClause",
                    LoreClauseText(topic?.adoptedClause));
            }

            AppendLoreDetail(
                builder,
                "PawnDiary.Dev.Lore.TextTriggers",
                LoreListText(topic?.triggerTextTerms));

            List<string> structured = new List<string>();
            AddLoreTriggerGroup(structured, "key", topic?.triggerContextKeys);
            AddLoreTriggerGroup(structured, "pair", topic?.triggerContextPairs);
            AddLoreTriggerGroup(structured, "marker", topic?.triggerValueMarkers);
            AddLoreTriggerGroup(structured, "def", topic?.triggerDefNames);
            AppendLoreDetail(
                builder,
                "PawnDiary.Dev.Lore.StructuredTriggers",
                LoreListText(structured));
            return builder.ToString().TrimEnd();
        }

        private static void AddLoreTriggerGroup(
            List<string> destination,
            string prefix,
            List<string> values)
        {
            if (destination == null || values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    destination.Add(prefix + ":" + values[i]);
                }
            }
        }

        private static void AppendLoreDetail(
            StringBuilder builder,
            string key,
            string value)
        {
            if (builder == null)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(FormatLoreFrame(key, value));
        }

        private static string LoreLastMatchText(LoreMemorySnapshotForDev lore)
        {
            if (lore == null || !lore.hasLastPromptReport)
            {
                return "PawnDiary.Dev.Lore.LastMatchNone".Translate();
            }

            return FormatLoreFrame(
                "PawnDiary.Dev.Lore.LastMatch",
                LoreListText(lore.matchedCultureTopics),
                LoreListText(lore.annotatedFieldSources));
        }

        private static string LoreTopicLabel(LoreMemoryTopicForDev topic)
        {
            if (topic == null)
            {
                return string.Empty;
            }

            string label = string.IsNullOrWhiteSpace(topic.label)
                ? topic.topicKey
                : topic.label;
            return string.Equals(label, topic.topicKey, StringComparison.OrdinalIgnoreCase)
                ? label
                : label + " [" + topic.topicKey + "]";
        }

        private static string LoreClauseText(string clause)
        {
            return string.IsNullOrWhiteSpace(clause)
                ? "PawnDiary.Dev.Lore.NoClause".Translate().ToString()
                : clause;
        }

        private static string LoreListText(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "PawnDiary.Dev.Lore.None".Translate();
            }

            return string.Join(", ", ToLoreArray(values));
        }

        private static string[] ToLoreArray(IReadOnlyList<string> values)
        {
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i] ?? string.Empty;
            }

            return result;
        }

        private static string FormatLoreFrame(string key, params object[] values)
        {
            string frame = key.Translate().Resolve();
            try
            {
                // Avoid Verse's argument formatter changing lowercase clauses after ':'.
                return string.Format(frame, values);
            }
            catch (FormatException)
            {
                return frame;
            }
        }
    }
}
