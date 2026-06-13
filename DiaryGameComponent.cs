using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public class DiaryGameComponent : GameComponent
    {
        private const int SocialFightDedupTicks = 300;
        private const int MentalBreakDedupTicks = 2500;

        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        private List<DiaryEvent> diaryEvents = new List<DiaryEvent>();

        // Transient (not saved) guard against duplicate mental-state events, e.g. the two
        // mirrored SocialFighting starts, or a break that re-triggers quickly.
        private readonly Dictionary<string, int> recentMentalEvents = new Dictionary<string, int>();

        public DiaryGameComponent(Game game)
        {
            LlmClient.BeginSession();
        }

        public static DiaryGameComponent Current
        {
            get
            {
                return Verse.Current.Game?.GetComponent<DiaryGameComponent>();
            }
        }

        public override void StartedNewGame()
        {
            LlmClient.BeginSession();
        }

        public override void LoadedGame()
        {
            LlmClient.BeginSession();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref diaries, "diaries", LookMode.Deep);
            Scribe_Collections.Look(ref diaryEvents, "diaryEvents", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (diaries == null)
                {
                    diaries = new List<PawnDiaryRecord>();
                }

                if (diaryEvents == null)
                {
                    diaryEvents = new List<DiaryEvent>();
                }
            }
        }

        public override void GameComponentTick()
        {
            LlmGenerationResult result;
            while (LlmClient.TryDequeueCompleted(out result))
            {
                ApplyLlmResult(result);
            }
        }

        public IReadOnlyList<DiaryEntryView> EntriesFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return EmptyEntries.List;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return EmptyEntries.List;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<DiaryEntryView> views = new List<DiaryEntryView>();

            if (diary.eventIds != null)
            {
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                    string povRole = diaryEvent?.RoleForPawn(pawnId);
                    EnsureGenerationQueued(diaryEvent, povRole);
                    DiaryEntryView view = diaryEvent?.ToViewFor(pawnId);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            if (diary.entries != null)
            {
                for (int i = 0; i < diary.entries.Count; i++)
                {
                    DiaryEntryView view = DiaryEntryView.FromLegacy(diary.entries[i]);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            return views;
        }

        private void EnsureGenerationQueued(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            // In dual mode, a request from either pawn's diary covers both POVs at once.
            // Fall through to the single-POV path only for a partially-generated (legacy) event.
            if (DualPovEnabled && DiaryEvent.RoleIsInitiatorOrRecipient(povRole) && diaryEvent.CanQueueDual())
            {
                QueueDualRewrite(diaryEvent);
                return;
            }

            if (diaryEvent.CanQueueGeneration(povRole))
            {
                QueueLlmRewrite(diaryEvent, povRole);
            }
        }

        private static bool DualPovEnabled
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.dualPovGeneration;
            }
        }

        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            string fallbackText = $"{initiator.LabelShortCap} had a {interactionDef.LabelCap.Resolve()} interaction with {recipient.LabelShortCap}.";
            RecordInteraction(initiator, recipient, interactionDef, fallbackText);
        }

        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string gameText)
        {
            RecordInteraction(initiator, recipient, interactionDef, gameText, gameText);
        }

        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string initiatorGameText, string recipientGameText)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!IsInteractionSignificant(interactionDef))
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = CleanLine(initiatorGameText);
            string recipientText = CleanLine(recipientGameText);
            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = $"{initiator.LabelShortCap} had a {interactionLabel} interaction with {recipient.LabelShortCap}.";
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionInstruction(interactionDef),
                BuildGameContextSummary(interactionDef, interactionLabel));
            QueuePairwiseGeneration(diaryEvent);
        }

        // Social fights and mental breaks. Social fights are pairwise (both pawns fight);
        // everything else is recorded as a solo break from the breaking pawn's point of view.
        public void RecordMentalState(Pawn pawn, MentalStateDef stateDef, Pawn otherPawn, string reason)
        {
            if (!IsHumanlike(pawn) || stateDef == null)
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.IsMentalStateEnabled(stateDef))
            {
                return;
            }

            string label = stateDef.LabelCap.Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForMentalState(stateDef);
            bool isSocialFight = string.Equals(stateDef.defName, "SocialFighting", StringComparison.OrdinalIgnoreCase)
                && IsHumanlike(otherPawn) && otherPawn != pawn;

            if (isSocialFight)
            {
                // SocialFighting fires once per participant; collapse the mirrored call.
                if (RecentlyRecorded("fight|" + PairKey(pawn, otherPawn), SocialFightDedupTicks))
                {
                    return;
                }

                string text = pawn.LabelShortCap + " and " + otherPawn.LabelShortCap + " lost their tempers and came to blows.";
                string gameContext = "mental_state=" + stateDef.defName + "; label=" + CleanLine(label) + ReasonSuffix(reason);
                DiaryEvent fightEvent = AddPairwiseEvent(pawn, otherPawn, stateDef.defName, label,
                    text, text, instruction, gameContext);
                QueuePairwiseGeneration(fightEvent);
                return;
            }

            if (RecentlyRecorded("break|" + pawn.GetUniqueLoadID() + "|" + stateDef.defName, MentalBreakDedupTicks))
            {
                return;
            }

            string breakText = BuildMentalBreakText(pawn, label, otherPawn, reason);
            string breakContext = "mental_state=" + stateDef.defName + "; label=" + CleanLine(label)
                + (otherPawn != null ? "; target=" + CleanLine(otherPawn.LabelShortCap) : string.Empty)
                + ReasonSuffix(reason);
            DiaryEvent breakEvent = AddSoloEvent(pawn, otherPawn, stateDef.defName, label, breakText, instruction, breakContext);
            QueueLlmRewrite(breakEvent, DiaryEvent.InitiatorRole);
        }

        private DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext)
        {
            string neutralText = string.Equals(initiatorText, recipientText, StringComparison.OrdinalIgnoreCase)
                ? initiatorText
                : initiator.LabelShortCap + ": " + initiatorText + " / " + recipient.LabelShortCap + ": " + recipientText;

            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = Find.TickManager.TicksGame,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = initiator.GetUniqueLoadID(),
                recipientPawnId = recipient.GetUniqueLoadID(),
                initiatorName = initiator.LabelShortCap,
                recipientName = recipient.LabelShortCap,
                initiatorText = initiatorText,
                recipientText = recipientText,
                neutralText = neutralText,
                sequenceText = neutralText,
                gameContext = gameContext,
                instruction = instruction,
                initiatorPawnSummary = BuildPawnSummary(initiator),
                recipientPawnSummary = BuildPawnSummary(recipient),
                initiatorSurroundings = BuildSurroundingsSummary(initiator),
                recipientSurroundings = BuildSurroundingsSummary(recipient),
                opinionsSummary = BuildOpinionsSummary(initiator, recipient),
                initiatorContinuity = BuildContinuitySummary(initiator, recipient),
                recipientContinuity = BuildContinuitySummary(recipient, initiator),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId);
            AddEventRef(recipient, diaryEvent.eventId);
            return diaryEvent;
        }

        private DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext)
        {
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = Find.TickManager.TicksGame,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = pawn.GetUniqueLoadID(),
                recipientPawnId = string.Empty,
                initiatorName = pawn.LabelShortCap,
                recipientName = string.Empty,
                initiatorText = text,
                recipientText = string.Empty,
                neutralText = text,
                sequenceText = text,
                gameContext = gameContext,
                instruction = instruction,
                initiatorPawnSummary = BuildPawnSummary(pawn),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = BuildSurroundingsSummary(pawn),
                recipientSurroundings = "n/a",
                opinionsSummary = otherPawn != null ? BuildOpinionsSummary(pawn, otherPawn) : "n/a",
                initiatorContinuity = BuildContinuitySummary(pawn, otherPawn),
                recipientContinuity = "none",
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId);
            return diaryEvent;
        }

        private void QueuePairwiseGeneration(DiaryEvent diaryEvent)
        {
            if (DualPovEnabled)
            {
                QueueDualRewrite(diaryEvent);
            }
            else
            {
                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            }
        }

        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = pawn.LabelShortCap + " had a mental break (" + CleanLine(label) + ")";
            if (otherPawn != null)
            {
                text += ", directed at " + CleanLine(otherPawn.LabelShortCap);
            }

            string cleanReason = CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += ". Reason: " + cleanReason;
            }

            return text + ".";
        }

        private static string ReasonSuffix(string reason)
        {
            string cleanReason = CleanLine(reason);
            return string.IsNullOrWhiteSpace(cleanReason) ? string.Empty : "; reason=" + cleanReason;
        }

        private bool RecentlyRecorded(string key, int windowTicks)
        {
            int now = Find.TickManager.TicksGame;
            int last;
            if (recentMentalEvents.TryGetValue(key, out last) && now - last < windowTicks)
            {
                return true;
            }

            recentMentalEvents[key] = now;
            return false;
        }

        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }

        private static string PairKey(Pawn first, Pawn second)
        {
            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }

        private void AddEventRef(Pawn pawn, string eventId)
        {
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            diary.pawnName = pawn.LabelShortCap;
            if (diary.eventIds == null)
            {
                diary.eventIds = new List<string>();
            }

            if (!diary.eventIds.Contains(eventId))
            {
                diary.eventIds.Add(eventId);
            }
        }

        private void QueueLlmRewrite(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            string rawText = BuildInteractionPrompt(diaryEvent, povRole);
            diaryEvent.SetPrompt(povRole, rawText);

            if (PawnDiaryMod.Settings == null)
            {
                diaryEvent.MarkFailed(povRole, "LLM settings are not available.");
                return;
            }

            diaryEvent.llmEndpoint = EndpointUtility.BuildChatCompletionsUrl(PawnDiaryMod.Settings.endpointUrl);
            diaryEvent.llmModel = PawnDiaryMod.Settings.modelName;
            diaryEvent.MarkQueued(povRole);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                systemPrompt = PawnDiaryMod.Settings.systemPrompt,
                rawText = rawText,
                endpointUrl = PawnDiaryMod.Settings.endpointUrl,
                modelName = PawnDiaryMod.Settings.modelName,
                apiKey = PawnDiaryMod.Settings.apiKey,
                timeoutSeconds = PawnDiaryMod.Settings.timeoutSeconds,
                maxTokens = PawnDiaryMod.Settings.maxTokens,
                temperature = PawnDiaryMod.Settings.temperature
            });
        }

        private void QueueDualRewrite(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || !diaryEvent.CanQueueDual())
            {
                return;
            }

            string rawText = BuildDualInteractionPrompt(diaryEvent);
            diaryEvent.SetDualPrompt(rawText);

            if (PawnDiaryMod.Settings == null)
            {
                diaryEvent.MarkDualFailed("LLM settings are not available.");
                return;
            }

            diaryEvent.llmEndpoint = EndpointUtility.BuildChatCompletionsUrl(PawnDiaryMod.Settings.endpointUrl);
            diaryEvent.llmModel = PawnDiaryMod.Settings.modelName;
            diaryEvent.MarkDualQueued();

            // Two entries in one response need roughly twice the budget of a single POV.
            int dualMaxTokens = Mathf.Clamp(PawnDiaryMod.Settings.maxTokens * 2, 64, 4096);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = DiaryEvent.DualRole,
                systemPrompt = PawnDiaryMod.Settings.systemPrompt,
                rawText = rawText,
                endpointUrl = PawnDiaryMod.Settings.endpointUrl,
                modelName = PawnDiaryMod.Settings.modelName,
                apiKey = PawnDiaryMod.Settings.apiKey,
                timeoutSeconds = PawnDiaryMod.Settings.timeoutSeconds,
                maxTokens = dualMaxTokens,
                temperature = PawnDiaryMod.Settings.temperature
            });
        }

        // Prompts intentionally omit any field that is empty or "normal" (see AppendField),
        // so the model only ever sees signal — no "health: healthy", no weather indoors, etc.
        private static string BuildDualInteractionPrompt(DiaryEvent diaryEvent)
        {
            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) + " (two viewpoints)" };

            AppendField(lines, "initiator", diaryEvent.initiatorName);
            AppendField(lines, "recipient", diaryEvent.recipientName);
            if (string.Equals(diaryEvent.initiatorText, diaryEvent.recipientText, StringComparison.OrdinalIgnoreCase))
            {
                AppendField(lines, "what happened", diaryEvent.initiatorText);
            }
            else
            {
                AppendField(lines, "initiator saw", diaryEvent.initiatorText);
                AppendField(lines, "recipient saw", diaryEvent.recipientText);
            }

            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "initiator profile", diaryEvent.initiatorPawnSummary);
            AppendField(lines, "recipient profile", diaryEvent.recipientPawnSummary);
            if (string.Equals(diaryEvent.initiatorSurroundings, diaryEvent.recipientSurroundings, StringComparison.OrdinalIgnoreCase))
            {
                AppendField(lines, "setting", diaryEvent.initiatorSurroundings);
            }
            else
            {
                AppendField(lines, "initiator setting", diaryEvent.initiatorSurroundings);
                AppendField(lines, "recipient setting", diaryEvent.recipientSurroundings);
            }

            AppendField(lines, "initiator relationship", diaryEvent.initiatorContinuity);
            AppendField(lines, "recipient relationship", diaryEvent.recipientContinuity);

            return string.Join("\n", lines.ToArray())
                + "\n\nWrite two short first-person diary entries, one from each pawn's point of view, following the instruction."
                + " Each pawn only knows what they could perceive. Output EXACTLY in this format and nothing else:"
                + "\n[INITIATOR]"
                + "\n<" + diaryEvent.initiatorName + "'s diary entry>"
                + "\n[RECIPIENT]"
                + "\n<" + diaryEvent.recipientName + "'s diary entry>";
        }

        private static string BuildInteractionPrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent);
            }

            bool isInitiator = string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase);
            string otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string povText = diaryEvent.TextForRole(povRole);
            string otherText = isInitiator ? diaryEvent.recipientText : diaryEvent.initiatorText;
            string povSummary = isInitiator ? diaryEvent.initiatorPawnSummary : diaryEvent.recipientPawnSummary;

            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.NameForRole(povRole));
            AppendField(lines, "with", otherName);
            AppendField(lines, "what happened", povText);
            if (!string.Equals(povText, otherText, StringComparison.OrdinalIgnoreCase))
            {
                AppendField(lines, "their view", otherText);
            }

            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "you", povSummary);
            AppendField(lines, "setting", diaryEvent.SurroundingsForRole(povRole));
            AppendField(lines, "relationship", diaryEvent.ContinuityForRole(povRole));

            return string.Join("\n", lines.ToArray());
        }

        private static string BuildSoloPrompt(DiaryEvent diaryEvent)
        {
            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.initiatorName);
            AppendField(lines, "what happened", diaryEvent.initiatorText);
            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "you", diaryEvent.initiatorPawnSummary);
            AppendField(lines, "setting", diaryEvent.initiatorSurroundings);
            AppendField(lines, "relationship", diaryEvent.initiatorContinuity);

            return string.Join("\n", lines.ToArray());
        }

        private static string EventNoun(DiaryEvent diaryEvent)
        {
            string label = CleanLine(diaryEvent.interactionLabel);
            return string.IsNullOrWhiteSpace(label) ? "social event" : label.ToLowerInvariant();
        }

        // Adds "key: value" only when the value carries real signal. Empty strings and
        // placeholder values ("none", "n/a", "unknown") are skipped so they cost no tokens.
        private static void AppendField(List<string> lines, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (trimmed == "none" || trimmed == "n/a" || trimmed == "unknown")
            {
                return;
            }

            lines.Add(key + ": " + trimmed);
        }

        private static string BuildGameContextSummary(InteractionDef interactionDef, string interactionLabel)
        {
            if (interactionDef == null)
            {
                return "unknown";
            }

            List<string> parts = new List<string>
            {
                "def=" + interactionDef.defName,
                "label=" + CleanLine(interactionLabel)
            };

            string worker = interactionDef.Worker?.GetType().Name;
            if (!string.IsNullOrWhiteSpace(worker))
            {
                parts.Add("worker=" + worker);
            }

            if (interactionDef.initiatorThought != null)
            {
                parts.Add("initiatorThought=" + CleanLine(interactionDef.initiatorThought.LabelCap.Resolve()));
            }

            if (interactionDef.recipientThought != null)
            {
                parts.Add("recipientThought=" + CleanLine(interactionDef.recipientThought.LabelCap.Resolve()));
            }

            return string.Join("; ", parts.ToArray());
        }

        // The pov pawn's standing toward the other pawn: relation kind, opinion, the social
        // memories driving that opinion, and the last diary line they wrote about them. The
        // diary line acts as a lightweight memory layer feeding continuity back to the model.
        private string BuildContinuitySummary(Pawn povPawn, Pawn otherPawn)
        {
            if (povPawn == null || otherPawn == null)
            {
                return "none";
            }

            List<string> parts = new List<string>();

            PawnRelationDef relation = PawnRelationUtility.GetMostImportantRelation(povPawn, otherPawn);
            if (relation != null)
            {
                string relationLabel = CleanLine(relation.GetGenderSpecificLabelCap(otherPawn));
                if (!string.IsNullOrWhiteSpace(relationLabel))
                {
                    parts.Add(relationLabel);
                }
            }

            int opinion = povPawn.relations?.OpinionOf(otherPawn) ?? 0;
            parts.Add("opinion " + FormatOpinion(opinion));

            string reasons = BuildSocialThoughtsSummary(povPawn, otherPawn);
            if (!string.IsNullOrWhiteSpace(reasons))
            {
                parts.Add("because " + reasons);
            }

            string latest = LatestDiaryLineAbout(povPawn.GetUniqueLoadID(), otherPawn.GetUniqueLoadID());
            if (!string.IsNullOrWhiteSpace(latest))
            {
                parts.Add("last wrote: \"" + latest + "\"");
            }

            return parts.Count == 0 ? "none" : string.Join("; ", parts.ToArray());
        }

        // The standing social memories that drive the opinion, aggregated by kind and signed
        // (e.g. "shared kind words +12, slept in a poor bedroom -4").
        private static string BuildSocialThoughtsSummary(Pawn povPawn, Pawn otherPawn)
        {
            if (povPawn.needs?.mood?.thoughts == null)
            {
                return string.Empty;
            }

            List<ISocialThought> social = new List<ISocialThought>();
            povPawn.needs.mood.thoughts.GetSocialThoughts(otherPawn, social);
            if (social.Count == 0)
            {
                return string.Empty;
            }

            Dictionary<string, float> byLabel = new Dictionary<string, float>();
            for (int i = 0; i < social.Count; i++)
            {
                Thought thought = social[i] as Thought;
                if (thought == null)
                {
                    continue;
                }

                string label = CleanLine(thought.LabelCap);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                float offset = social[i].OpinionOffset();
                byLabel[label] = (byLabel.TryGetValue(label, out float existing) ? existing : 0f) + offset;
            }

            return string.Join(", ", byLabel
                .Where(pair => Mathf.Abs(pair.Value) >= 1f)
                .OrderByDescending(pair => Mathf.Abs(pair.Value))
                .Take(3)
                .Select(pair => pair.Key + " " + FormatSignedNumber(Mathf.RoundToInt(pair.Value)))
                .ToArray());
        }

        private string LatestDiaryLineAbout(string pawnId, string otherPawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(otherPawnId) || diaryEvents == null)
            {
                return string.Empty;
            }

            for (int i = diaryEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent == null || !diaryEvent.Involves(pawnId, otherPawnId))
                {
                    continue;
                }

                string role = diaryEvent.RoleForPawn(pawnId);
                string line = diaryEvent.DisplayTextForRole(role);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    line = CleanLine(line);
                    return line.Length <= 160 ? line : line.Substring(0, 160) + "...";
                }
            }

            return string.Empty;
        }

        private static string BuildOpinionsSummary(Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null)
            {
                return "unknown";
            }

            int initiatorOpinion = initiator.relations?.OpinionOf(recipient) ?? 0;
            int recipientOpinion = recipient.relations?.OpinionOf(initiator) ?? 0;
            return initiator.LabelShortCap + "->" + recipient.LabelShortCap + " "
                + FormatOpinion(initiatorOpinion) + "; " + recipient.LabelShortCap + "->" + initiator.LabelShortCap + " "
                + FormatOpinion(recipientOpinion);
        }

        private static string BuildSurroundingsSummary(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();

            Room room = pawn.Position.GetRoom(pawn.Map);
            bool outdoors = room == null || room.PsychologicallyOutdoors;

            if (room != null)
            {
                string roomRole = CleanLine(room.GetRoomRoleLabel());
                if (!string.IsNullOrWhiteSpace(roomRole))
                {
                    parts.Add(roomRole);
                }
            }

            parts.Add(outdoors ? "outdoors" : "indoors");

            // Weather and biome only matter when the pawn is exposed to them.
            if (outdoors)
            {
                if (pawn.Map.weatherManager?.CurWeatherPerceived != null)
                {
                    parts.Add(CleanLine(pawn.Map.weatherManager.CurWeatherPerceived.label));
                }

                if (pawn.Map.Biome != null)
                {
                    parts.Add(CleanLine(pawn.Map.Biome.label));
                }
            }

            // Temperature only when it's actually uncomfortable.
            float temperature = pawn.AmbientTemperature;
            if (temperature <= 0f)
            {
                parts.Add("cold (" + temperature.ToString("0") + "C)");
            }
            else if (temperature >= 32f)
            {
                parts.Add("hot (" + temperature.ToString("0") + "C)");
            }

            // Beauty only when it's notably nice or grim.
            float beauty = BeautyUtility.AverageBeautyPerceptible(pawn.Position, pawn.Map);
            if (beauty >= 0.3f || beauty <= -0.3f)
            {
                parts.Add(BeautyBucket(beauty) + " surroundings");
            }

            string nearby = BuildNearbyThingsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(nearby))
            {
                parts.Add("near " + nearby);
            }

            string jobReport = CleanLine(pawn.GetJobReport());
            if (!string.IsNullOrWhiteSpace(jobReport))
            {
                parts.Add("doing: " + jobReport);
            }
            else if (pawn.CurJobDef != null)
            {
                parts.Add("doing: " + CleanLine(pawn.CurJobDef.LabelCap.Resolve()));
            }

            return string.Join(", ", parts.ToArray());
        }

        private static string BuildPawnSummary(Pawn pawn)
        {
            if (pawn == null)
            {
                return "unknown";
            }

            List<string> parts = new List<string>
            {
                "sex=" + pawn.gender.ToString().ToLowerInvariant()
            };

            if (pawn.ageTracker != null)
            {
                parts.Add("age=" + pawn.ageTracker.AgeBiologicalYears);
            }

            string traits = BuildTraitsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(traits))
            {
                parts.Add("traits=" + traits);
            }

            string mood = BuildMoodSummary(pawn);
            if (!string.IsNullOrWhiteSpace(mood))
            {
                parts.Add("mood=" + mood);
            }

            string health = BuildHealthSummary(pawn);
            if (!string.IsNullOrWhiteSpace(health))
            {
                parts.Add("health=" + health);
            }

            string capacities = BuildLowCapacitiesSummary(pawn);
            if (!string.IsNullOrWhiteSpace(capacities))
            {
                parts.Add("low_capacities=" + capacities);
            }

            string thoughts = BuildTopThoughtsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(thoughts))
            {
                parts.Add("top_thoughts=" + thoughts);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string BuildTraitsSummary(Pawn pawn)
        {
            if (pawn.story?.traits?.TraitsSorted == null)
            {
                return string.Empty;
            }

            return string.Join(", ", pawn.story.traits.TraitsSorted
                .Where(trait => trait != null && !trait.Suppressed)
                .Select(trait => CleanLine(trait.LabelCap))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(3)
                .ToArray());
        }

        private static string BuildMoodSummary(Pawn pawn)
        {
            if (pawn.needs?.mood == null)
            {
                return string.Empty;
            }

            int moodPercent = Mathf.RoundToInt(pawn.needs.mood.CurLevelPercentage * 100f);
            return MoodBucket(moodPercent) + " " + moodPercent + "%";
        }

        private static string BuildHealthSummary(Pawn pawn)
        {
            if (pawn.health == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();

            if (pawn.Downed)
            {
                parts.Add("downed");
            }

            if (pawn.health.InPainShock)
            {
                parts.Add("pain shock");
            }

            float pain = pawn.health.hediffSet?.PainTotal ?? 0f;
            if (pain > 0.03f)
            {
                parts.Add("pain=" + PainBucket(pain) + " " + Mathf.RoundToInt(pain * 100f) + "%");
            }

            float bleedRate = pawn.health.hediffSet?.BleedRateTotal ?? 0f;
            if (bleedRate > 0.01f)
            {
                parts.Add("bleeding=" + bleedRate.ToString("0.##") + "/day");
            }

            string notableHediffs = BuildNotableHediffsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(notableHediffs))
            {
                parts.Add("conditions=" + notableHediffs);
            }

            // Empty when healthy, so the prompt omits health entirely.
            return parts.Count == 0 ? string.Empty : string.Join(", ", parts.ToArray());
        }

        private static string BuildNotableHediffsSummary(Pawn pawn)
        {
            if (pawn.health?.hediffSet?.hediffs == null)
            {
                return string.Empty;
            }

            return string.Join(", ", pawn.health.hediffSet.hediffs
                .Where(hediff => hediff != null && hediff.Visible && (hediff.IsCurrentlyLifeThreatening || hediff.Bleeding || hediff.PainOffset > 0f || hediff.SummaryHealthPercentImpact < -0.05f))
                .OrderByDescending(hediff => hediff.IsCurrentlyLifeThreatening ? 100f : hediff.BleedRate + hediff.PainOffset - hediff.SummaryHealthPercentImpact)
                .Select(hediff => CleanLine(hediff.Label))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(2)
                .ToArray());
        }

        private static string BuildLowCapacitiesSummary(Pawn pawn)
        {
            if (pawn.health?.capacities == null)
            {
                return string.Empty;
            }

            PawnCapacityDef[] relevantCapacities =
            {
                PawnCapacityDefOf.Consciousness,
                PawnCapacityDefOf.Talking,
                PawnCapacityDefOf.Hearing,
                PawnCapacityDefOf.Sight,
                PawnCapacityDefOf.Manipulation,
                PawnCapacityDefOf.Moving
            };

            List<string> parts = new List<string>();
            for (int i = 0; i < relevantCapacities.Length; i++)
            {
                PawnCapacityDef capacity = relevantCapacities[i];
                if (capacity == null)
                {
                    continue;
                }

                float level = pawn.health.capacities.GetLevel(capacity);
                if (level < 0.85f)
                {
                    parts.Add(CleanLine(capacity.GetLabelFor(pawn)) + "=" + Mathf.RoundToInt(level * 100f) + "%");
                }
            }

            return string.Join(", ", parts.ToArray());
        }

        private static string BuildTopThoughtsSummary(Pawn pawn)
        {
            if (pawn.needs?.mood?.thoughts == null)
            {
                return string.Empty;
            }

            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            return string.Join(", ", thoughts
                .Where(thought => thought != null && thought.VisibleInNeedsTab && Mathf.Abs(thought.MoodOffset()) >= 1f)
                .OrderByDescending(thought => Mathf.Abs(thought.MoodOffset()))
                .Select(thought => CleanLine(thought.LabelCap) + " " + FormatSignedNumber(Mathf.RoundToInt(thought.MoodOffset())))
                .Take(3)
                .ToArray());
        }

        private static string BuildNearbyThingsSummary(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return string.Empty;
            }

            List<string> labels = new List<string>();

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 5f, true))
            {
                if (thing == null || thing == pawn || thing is Pawn || !ShouldIncludeNearbyThing(thing))
                {
                    continue;
                }

                string label = NearbyThingLabel(thing);
                if (string.IsNullOrWhiteSpace(label) || labels.Contains(label))
                {
                    continue;
                }

                labels.Add(label);
                if (labels.Count >= 6)
                {
                    break;
                }
            }

            return string.Join(", ", labels.ToArray());
        }

        private static bool ShouldIncludeNearbyThing(Thing thing)
        {
            if (thing is Corpse || thing is Fire)
            {
                return true;
            }

            ThingDef def = thing.def;
            if (def == null)
            {
                return false;
            }

            return def.category == ThingCategory.Building
                || def.category == ThingCategory.Item
                || def.category == ThingCategory.Plant;
        }

        private static string NearbyThingLabel(Thing thing)
        {
            if (thing is Fire)
            {
                return "fire";
            }

            Corpse corpse = thing as Corpse;
            if (corpse?.InnerPawn != null)
            {
                return "corpse of " + CleanLine(corpse.InnerPawn.LabelShortCap);
            }

            return CleanLine(thing.LabelNoCount);
        }

        private static string BuildXmlSummary(InteractionDef interactionDef, string interactionLabel)
        {
            List<string> parts = new List<string>
            {
                "def=" + interactionDef.defName,
                "label=" + CleanLine(interactionLabel)
            };

            string worker = interactionDef.Worker?.GetType().Name;
            if (!string.IsNullOrWhiteSpace(worker))
            {
                parts.Add("worker=" + worker);
            }

            if (interactionDef.initiatorThought != null)
            {
                parts.Add("initiatorThought=" + CleanLine(interactionDef.initiatorThought.LabelCap.Resolve()));
            }

            if (interactionDef.recipientThought != null)
            {
                parts.Add("recipientThought=" + CleanLine(interactionDef.recipientThought.LabelCap.Resolve()));
            }

            if (!string.IsNullOrWhiteSpace(interactionDef.description))
            {
                parts.Add("description=" + CleanLine(interactionDef.description));
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return PawnDiaryMod.Settings?.InstructionFor(interactionDef) ?? string.Empty;
        }

        // An interaction is recorded only if its group (see InteractionGroups) is enabled
        // in settings.
        private static bool IsInteractionSignificant(InteractionDef interactionDef)
        {
            return interactionDef != null
                && !string.IsNullOrWhiteSpace(interactionDef.defName)
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsInteractionEnabled(interactionDef);
        }

        private static string CleanLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Replace("\r", " ").Replace("\n", " ");
            cleaned = Regex.Replace(cleaned, "<.*?>", string.Empty);
            return cleaned.Trim();
        }

        private static string FormatOpinion(int opinion)
        {
            string sign = opinion > 0 ? "+" : string.Empty;
            return sign + opinion + " (" + OpinionBucket(opinion) + ")";
        }

        private static string FormatSignedNumber(int value)
        {
            return (value > 0 ? "+" : string.Empty) + value;
        }

        private static string BeautyBucket(float beauty)
        {
            if (beauty >= 2f)
            {
                return "beautiful";
            }

            if (beauty >= 0.3f)
            {
                return "pleasant";
            }

            if (beauty > -0.3f)
            {
                return "neutral";
            }

            if (beauty > -2f)
            {
                return "ugly";
            }

            return "hideous";
        }

        private static string MoodBucket(int moodPercent)
        {
            if (moodPercent >= 75)
            {
                return "happy";
            }

            if (moodPercent >= 50)
            {
                return "stable";
            }

            if (moodPercent >= 30)
            {
                return "stressed";
            }

            return "miserable";
        }

        private static string PainBucket(float pain)
        {
            if (pain >= 0.4f)
            {
                return "severe";
            }

            if (pain >= 0.18f)
            {
                return "moderate";
            }

            return "minor";
        }

        private static string OpinionBucket(int opinion)
        {
            if (opinion >= 60)
            {
                return "devoted";
            }

            if (opinion >= 25)
            {
                return "friendly";
            }

            if (opinion > -10)
            {
                return "neutral";
            }

            if (opinion > -40)
            {
                return "strained";
            }

            return "hostile";
        }

        private void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.eventId))
            {
                return;
            }

            DiaryEvent diaryEvent = FindEvent(result.eventId);
            diaryEvent?.ApplyLlmResult(result);
        }

        private DiaryEvent FindEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || diaryEvents == null)
            {
                return null;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i].eventId == eventId)
                {
                    return diaryEvents[i];
                }
            }

            return null;
        }

        private PawnDiaryRecord FindDiary(Pawn pawn, bool createIfMissing)
        {
            if (pawn == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < diaries.Count; i++)
            {
                if (diaries[i].pawnId == pawnId)
                {
                    return diaries[i];
                }
            }

            if (!createIfMissing)
            {
                return null;
            }

            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawn.LabelShortCap
            };
            diaries.Add(diary);
            return diary;
        }

        private static class EmptyEntries
        {
            public static readonly IReadOnlyList<DiaryEntryView> List = new List<DiaryEntryView>();
        }
    }

    public class PawnDiaryRecord : IExposable
    {
        public string pawnId;
        public string pawnName;
        public List<string> eventIds = new List<string>();
        public List<DiaryEntry> entries = new List<DiaryEntry>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (eventIds == null)
                {
                    eventIds = new List<string>();
                }

                if (entries == null)
                {
                    entries = new List<DiaryEntry>();
                }
            }
        }
    }

    public class DiaryEvent : IExposable
    {
        public const string InitiatorRole = "initiator";
        public const string RecipientRole = "recipient";
        public const string NeutralRole = "neutral";
        public const string DualRole = "dual";
        public const string NotGeneratedStatus = "not_generated";
        public const string PendingStatus = "pending";
        public const string CompleteStatus = "complete";
        public const string FailedStatus = "failed";

        public string eventId;
        public int tick;
        public string date;
        public string interactionDefName;
        public string interactionLabel;
        public string initiatorPawnId;
        public string recipientPawnId;
        public string initiatorName;
        public string recipientName;
        public string initiatorText;
        public string recipientText;
        public string neutralText;
        public string sequenceText;
        public string gameContext;
        public string instruction;
        public string initiatorPawnSummary;
        public string recipientPawnSummary;
        public string initiatorSurroundings;
        public string recipientSurroundings;
        public string opinionsSummary;
        public string initiatorContinuity;
        public string recipientContinuity;
        public string initiatorPrompt;
        public string recipientPrompt;
        public string neutralPrompt;
        public string initiatorGeneratedText;
        public string recipientGeneratedText;
        public string neutralGeneratedText;
        public string initiatorStatus;
        public string recipientStatus;
        public string neutralStatus;
        public string initiatorError;
        public string recipientError;
        public string neutralError;
        public string llmEndpoint;
        public string llmModel;
        public bool solo;

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Values.Look(ref solo, "solo", false);
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref date, "date");
            Scribe_Values.Look(ref interactionDefName, "interactionDefName");
            Scribe_Values.Look(ref interactionLabel, "interactionLabel");
            Scribe_Values.Look(ref initiatorPawnId, "initiatorPawnId");
            Scribe_Values.Look(ref recipientPawnId, "recipientPawnId");
            Scribe_Values.Look(ref initiatorName, "initiatorName");
            Scribe_Values.Look(ref recipientName, "recipientName");
            Scribe_Values.Look(ref initiatorText, "initiatorText");
            Scribe_Values.Look(ref recipientText, "recipientText");
            Scribe_Values.Look(ref neutralText, "neutralText");
            Scribe_Values.Look(ref sequenceText, "sequenceText");
            Scribe_Values.Look(ref gameContext, "gameContext");
            Scribe_Values.Look(ref instruction, "instruction");
            Scribe_Values.Look(ref initiatorPawnSummary, "initiatorPawnSummary");
            Scribe_Values.Look(ref recipientPawnSummary, "recipientPawnSummary");
            Scribe_Values.Look(ref initiatorSurroundings, "initiatorSurroundings");
            Scribe_Values.Look(ref recipientSurroundings, "recipientSurroundings");
            Scribe_Values.Look(ref opinionsSummary, "opinionsSummary");
            Scribe_Values.Look(ref initiatorContinuity, "initiatorContinuity");
            Scribe_Values.Look(ref recipientContinuity, "recipientContinuity");
            Scribe_Values.Look(ref initiatorGeneratedText, "initiatorGeneratedText");
            Scribe_Values.Look(ref recipientGeneratedText, "recipientGeneratedText");
            Scribe_Values.Look(ref neutralGeneratedText, "neutralGeneratedText");
            Scribe_Values.Look(ref initiatorStatus, "initiatorStatus");
            Scribe_Values.Look(ref recipientStatus, "recipientStatus");
            Scribe_Values.Look(ref neutralStatus, "neutralStatus");
            Scribe_Values.Look(ref initiatorError, "initiatorError");
            Scribe_Values.Look(ref recipientError, "recipientError");
            Scribe_Values.Look(ref neutralError, "neutralError");
            Scribe_Values.Look(ref llmEndpoint, "llmEndpoint");
            Scribe_Values.Look(ref llmModel, "llmModel");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    eventId = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(initiatorStatus))
                {
                    initiatorStatus = NotGeneratedStatus;
                }

                if (string.IsNullOrWhiteSpace(recipientStatus))
                {
                    recipientStatus = NotGeneratedStatus;
                }

                if (string.IsNullOrWhiteSpace(neutralText))
                {
                    neutralText = string.Equals(initiatorText, recipientText, StringComparison.OrdinalIgnoreCase)
                        ? initiatorText
                        : initiatorName + ": " + initiatorText + "\n" + recipientName + ": " + recipientText;
                }

                if (string.IsNullOrWhiteSpace(sequenceText))
                {
                    sequenceText = neutralText;
                }

                if (string.IsNullOrWhiteSpace(gameContext))
                {
                    gameContext = "def=" + interactionDefName + "; label=" + interactionLabel;
                }

                if (string.IsNullOrWhiteSpace(instruction))
                {
                    instruction = interactionLabel;
                }

                if (string.IsNullOrWhiteSpace(initiatorPawnSummary))
                {
                    initiatorPawnSummary = "unknown";
                }

                if (string.IsNullOrWhiteSpace(recipientPawnSummary))
                {
                    recipientPawnSummary = "unknown";
                }

                if (string.IsNullOrWhiteSpace(initiatorSurroundings))
                {
                    initiatorSurroundings = "unknown";
                }

                if (string.IsNullOrWhiteSpace(recipientSurroundings))
                {
                    recipientSurroundings = initiatorSurroundings;
                }

                if (string.IsNullOrWhiteSpace(opinionsSummary))
                {
                    opinionsSummary = "unknown";
                }

                if (string.IsNullOrWhiteSpace(initiatorContinuity))
                {
                    initiatorContinuity = "none";
                }

                if (string.IsNullOrWhiteSpace(recipientContinuity))
                {
                    recipientContinuity = "none";
                }

                if (neutralStatus == null)
                {
                    neutralStatus = NotGeneratedStatus;
                }

                initiatorStatus = NormalizeLoadedStatus(initiatorStatus, initiatorGeneratedText);
                recipientStatus = NormalizeLoadedStatus(recipientStatus, recipientGeneratedText);
                neutralStatus = NormalizeLoadedStatus(neutralStatus, neutralGeneratedText);
            }
        }

        public void SetPrompt(string povRole, string prompt)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorPrompt = prompt;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientPrompt = prompt;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralPrompt = prompt;
            }
        }

        public void SetDualPrompt(string prompt)
        {
            initiatorPrompt = prompt;
            recipientPrompt = prompt;
        }

        public bool CanQueueGeneration(string povRole)
        {
            string status = StatusFor(povRole);
            return string.IsNullOrWhiteSpace(status) || RoleEquals(status, NotGeneratedStatus);
        }

        public bool CanQueueDual()
        {
            return !solo && CanQueueGeneration(InitiatorRole) && CanQueueGeneration(RecipientRole);
        }

        public static bool RoleIsInitiatorOrRecipient(string povRole)
        {
            return RoleEquals(povRole, InitiatorRole) || RoleEquals(povRole, RecipientRole);
        }

        public void MarkQueued(string povRole)
        {
            SetStatus(povRole, PendingStatus);
            SetError(povRole, null);
        }

        public void MarkFailed(string povRole, string error)
        {
            SetStatus(povRole, FailedStatus);
            SetError(povRole, error);
        }

        public void MarkDualQueued()
        {
            MarkQueued(InitiatorRole);
            MarkQueued(RecipientRole);
        }

        public void MarkDualFailed(string error)
        {
            MarkFailed(InitiatorRole, error);
            MarkFailed(RecipientRole, error);
        }

        public void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null)
            {
                return;
            }

            if (RoleEquals(result.povRole, InitiatorRole))
            {
                ApplyLlmResultToInitiator(result);
                return;
            }

            if (RoleEquals(result.povRole, RecipientRole))
            {
                ApplyLlmResultToRecipient(result);
                return;
            }

            if (RoleEquals(result.povRole, NeutralRole))
            {
                ApplyLlmResultToNeutral(result);
                return;
            }

            if (RoleEquals(result.povRole, DualRole))
            {
                ApplyDualResult(result);
            }
        }

        public DiaryEntryView ToViewFor(string pawnId)
        {
            string povRole = RoleForPawn(pawnId);
            if (string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            return new DiaryEntryView(
                tick,
                date,
                TextFor(povRole),
                GeneratedTextFor(povRole),
                StatusFor(povRole),
                ErrorFor(povRole),
                llmEndpoint,
                llmModel,
                PromptFor(povRole),
                eventId,
                povRole);
        }

        public string RoleForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            if (pawnId == initiatorPawnId)
            {
                return InitiatorRole;
            }

            if (pawnId == recipientPawnId)
            {
                return RecipientRole;
            }

            return null;
        }

        public bool Involves(string firstPawnId, string secondPawnId)
        {
            return (firstPawnId == initiatorPawnId && secondPawnId == recipientPawnId)
                || (firstPawnId == recipientPawnId && secondPawnId == initiatorPawnId);
        }

        public string TextForRole(string povRole)
        {
            return TextFor(povRole);
        }

        public string DisplayTextForRole(string povRole)
        {
            string generated = GeneratedTextFor(povRole);
            return string.IsNullOrWhiteSpace(generated) ? TextFor(povRole) : generated;
        }

        public string NameForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorName;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientName;
            }

            return "colony";
        }

        public string SurroundingsForRole(string povRole)
        {
            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientSurroundings;
            }

            return initiatorSurroundings;
        }

        public string ContinuityForRole(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorContinuity;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientContinuity;
            }

            return "none";
        }

        private string TextFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorText;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientText;
            }

            return neutralText;
        }

        private string GeneratedTextFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorGeneratedText;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientGeneratedText;
            }

            return neutralGeneratedText;
        }

        private string StatusFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorStatus;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientStatus;
            }

            return neutralStatus;
        }

        private string ErrorFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorError;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientError;
            }

            return neutralError;
        }

        private string PromptFor(string povRole)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                return initiatorPrompt;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                return recipientPrompt;
            }

            return neutralPrompt;
        }

        private void ApplyLlmResultToInitiator(LlmGenerationResult result)
        {
            if (result.success)
            {
                initiatorGeneratedText = result.generatedText;
                initiatorStatus = CompleteStatus;
                initiatorError = null;
            }
            else
            {
                initiatorStatus = FailedStatus;
                initiatorError = result.error;
            }
        }

        private void ApplyLlmResultToRecipient(LlmGenerationResult result)
        {
            if (result.success)
            {
                recipientGeneratedText = result.generatedText;
                recipientStatus = CompleteStatus;
                recipientError = null;
            }
            else
            {
                recipientStatus = FailedStatus;
                recipientError = result.error;
            }
        }

        private void ApplyDualResult(LlmGenerationResult result)
        {
            if (!result.success)
            {
                MarkDualFailed(result.error);
                return;
            }

            string initiatorEntry;
            string recipientEntry;
            ParseDualResponse(result.generatedText, out initiatorEntry, out recipientEntry);

            initiatorGeneratedText = initiatorEntry;
            initiatorStatus = CompleteStatus;
            initiatorError = null;

            recipientGeneratedText = recipientEntry;
            recipientStatus = CompleteStatus;
            recipientError = null;
        }

        private static void ParseDualResponse(string text, out string initiatorEntry, out string recipientEntry)
        {
            initiatorEntry = string.Empty;
            recipientEntry = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            const string initiatorMarker = "[INITIATOR]";
            const string recipientMarker = "[RECIPIENT]";
            int initiatorIndex = text.IndexOf(initiatorMarker, StringComparison.OrdinalIgnoreCase);
            int recipientIndex = text.IndexOf(recipientMarker, StringComparison.OrdinalIgnoreCase);

            if (initiatorIndex >= 0 && recipientIndex >= 0)
            {
                if (initiatorIndex < recipientIndex)
                {
                    int start = initiatorIndex + initiatorMarker.Length;
                    initiatorEntry = text.Substring(start, recipientIndex - start);
                    recipientEntry = text.Substring(recipientIndex + recipientMarker.Length);
                }
                else
                {
                    int start = recipientIndex + recipientMarker.Length;
                    recipientEntry = text.Substring(start, initiatorIndex - start);
                    initiatorEntry = text.Substring(initiatorIndex + initiatorMarker.Length);
                }
            }
            else if (recipientIndex >= 0)
            {
                initiatorEntry = text.Substring(0, recipientIndex);
                recipientEntry = text.Substring(recipientIndex + recipientMarker.Length);
            }
            else if (initiatorIndex >= 0)
            {
                initiatorEntry = text.Substring(initiatorIndex + initiatorMarker.Length);
                recipientEntry = text;
            }
            else
            {
                // No markers returned; fall back to the same text for both POVs.
                initiatorEntry = text;
                recipientEntry = text;
            }

            initiatorEntry = CleanDualEntry(initiatorEntry);
            recipientEntry = CleanDualEntry(recipientEntry);

            if (string.IsNullOrWhiteSpace(initiatorEntry))
            {
                initiatorEntry = CleanDualEntry(text);
            }

            if (string.IsNullOrWhiteSpace(recipientEntry))
            {
                recipientEntry = CleanDualEntry(text);
            }
        }

        private static string CleanDualEntry(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Replace("[INITIATOR]", string.Empty).Replace("[RECIPIENT]", string.Empty);
            return value.Trim();
        }

        private void ApplyLlmResultToNeutral(LlmGenerationResult result)
        {
            if (result.success)
            {
                neutralGeneratedText = result.generatedText;
                neutralStatus = CompleteStatus;
                neutralError = null;
            }
            else
            {
                neutralStatus = FailedStatus;
                neutralError = result.error;
            }
        }

        private void SetStatus(string povRole, string status)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorStatus = status;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientStatus = status;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralStatus = status;
            }
        }

        private void SetError(string povRole, string error)
        {
            if (RoleEquals(povRole, InitiatorRole))
            {
                initiatorError = error;
                return;
            }

            if (RoleEquals(povRole, RecipientRole))
            {
                recipientError = error;
                return;
            }

            if (RoleEquals(povRole, NeutralRole))
            {
                neutralError = error;
            }
        }

        private static string NormalizeLoadedStatus(string status, string generatedText)
        {
            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                return CompleteStatus;
            }

            if (RoleEquals(status, PendingStatus))
            {
                return NotGeneratedStatus;
            }

            return string.IsNullOrWhiteSpace(status) ? NotGeneratedStatus : status;
        }

        private static bool RoleEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
