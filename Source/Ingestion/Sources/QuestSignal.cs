// Quest ingestion signal — the impure capture+emit half of the "quest lifecycle" source (accepted /
// completed / failed). Replaces the shared DiaryGameComponent.RecordQuestSignal fan-out core. The
// root-first XML group chooses either the legacy all-eligible fan-out or one stable map witness, while
// a single quest+signal dedup window prevents competing pages for the same lifecycle edge.
//
// The thin RecordQuestAccepted/RecordQuestEnded methods stay on the component (they update the
// accept-scanner baseline and map outcomes), and submit this fan-out. The quest lifecycle ALSO emits a
// generic event-window signal — that is separate XML policy, so it fires once at capture (before the
// diary group gate) regardless of whether quest diary entries are enabled. Pure decision +
// game-context + display-label cleanup live in Source/Capture/Events/QuestEventData.cs.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Quest lifecycle fan-out with XML-owned audience scope. Built by
    /// <see cref="DiaryGameComponent.RecordQuestAccepted"/> /
    /// <see cref="DiaryGameComponent.RecordQuestEnded"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>.
    /// </summary>
    internal sealed class QuestFanoutSignal : DiaryFanoutSignal
    {
        // Defensive caps for the prose fields embedded in the prompt (stable parser/length caps, not
        // tunable feature policy — AGENTS.md section 12). Moved from DiaryGameComponent.Quests.cs.
        private const int QuestDescriptionCap = 600;
        private const int QuestRewardsCap = 300;

        private readonly bool valid;
        private readonly string colonyDedupKey;

        internal string Signal { get; }
        internal string QuestDefName { get; }
        internal string CleanedLabel { get; }
        internal string FactionDefName { get; }
        internal string Rewards { get; }
        internal string Description { get; }
        internal string Instruction { get; }
        internal string TextKey { get; }
        internal DiaryInteractionGroupDef Group { get; }
        internal RoyalAscentLifecycleDecision AscentDecision { get; }

        public QuestFanoutSignal(Quest quest, string signal, string textKey)
        {
            if (!DiaryGameComponent.GamePlaying || quest == null || string.IsNullOrEmpty(signal) || PawnDiaryMod.Settings == null)
            {
                return;
            }

            Signal = signal;
            TextKey = textKey;
            QuestDefName = string.IsNullOrEmpty(quest.root?.defName) ? "unknown" : quest.root.defName;
            CleanedLabel = BuildQuestLabel(quest);
            RoyaltyPolicySnapshot royaltyPolicy = DiaryRoyaltyPolicy.Snapshot();
            AscentDecision = RoyalAscentPolicy.Evaluate(
                new RoyalAscentLifecycleFacts
                {
                    questRootDefName = QuestDefName,
                    lifecycleSignal = signal,
                    correlationId = quest.GetUniqueLoadID(),
                    tick = Find.TickManager.TicksGame
                },
                royaltyPolicy,
                ModsConfig.RoyaltyActive);
            bool exactAscentRoot = RoyalAscentPolicy.IsExactQuestRoot(QuestDefName, royaltyPolicy);

            // The exact DLC route is all-or-nothing at this adapter boundary. Without Royalty, or
            // when the XML master policy is disabled, it must not leak into the package-gated window
            // or group. Ordinary Quest roots continue through their mature generic path below.
            if (exactAscentRoot && (!AscentDecision.recognized
                || (!AscentDecision.opensWindow && !AscentDecision.closesWindow)))
            {
                return;
            }

            // Event windows are generic XML policy, not the Quest-domain diary group. Emit the lifecycle
            // signal before group gating so XML can watch a quest without forcing all quest diary
            // entries on. Isolate it so an event-window failure cannot skip the quest capture.
            try
            {
                DiaryGameComponent.Instance?.RecordEventWindowSignal(
                    DiaryGameComponent.EventWindowSourceQuest,
                    QuestDefName,
                    signal,
                    CleanedLabel,
                    null,
                    null,
                    AscentDecision?.correlationId,
                    AscentDecision?.arcKey);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Quest event-window signal failed and was skipped: " + e,
                    "QuestFanoutSignal.EventWindow".GetHashCode());
            }

            // The start window already owns the one accepted page. QuestEventData.Decide also drops
            // accepted signals, but stop here explicitly so this adapter cannot waste work or drift into
            // a second page if generic Quest capture policy changes later. Terminal phases continue to
            // the exact one-witness Quest page below.
            if (exactAscentRoot && !AscentDecision.emitsTerminalPage)
            {
                return;
            }

            // Resolve once, root first. The same exact group owns settings, instruction, and fanout;
            // reclassifying by signal here would silently restore the generic Quest policy.
            Group = InteractionGroups.ClassifyQuest(QuestDefName, signal);
            if (Group == null || !PawnDiaryMod.Settings.IsGroupEnabled(Group.defName))
            {
                return;
            }

            FactionDefName = BuildQuestFactionDefName(quest);
            Rewards = BuildQuestRewardsSummary(quest);

            // Resolve the description once and reject malformed quests (blank or RimWorld's generated
            // "ERR:" placeholder text) so they never produce a diary page. The event-window signal
            // above still fired, but no per-pawn diary entries are generated for these quests.
            Description = BuildQuestDescription(quest);
            if (string.IsNullOrEmpty(Description))
            {
                return;
            }

            Instruction = InteractionGroups.InstructionForGroup(Group);
            colonyDedupKey = "quest|" + quest.id + "|" + signal;
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiaryTuning.Current.questDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            if (Group != null && Group.questFanoutScope == QuestFanoutScope.MapWitness)
            {
                Pawn witness = DiaryGameComponent.StableLoadedMapWitness();
                if (witness != null)
                {
                    yield return new QuestPawnSignal(this, witness, witness.GetUniqueLoadID());
                }
                yield break;
            }

            HashSet<string> seen = new HashSet<string>();
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map?.mapPawns == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn pawn = colonists[j];
                    if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    string pawnId = pawn.GetUniqueLoadID();
                    if (!seen.Add(pawnId))
                    {
                        continue;
                    }

                    yield return new QuestPawnSignal(this, pawn, pawnId);
                }
            }
        }

        // ── Helpers moved verbatim from the old DiaryGameComponent.Quests.cs ──

        private static string BuildQuestLabel(Quest quest)
        {
            return QuestEventData.BuildDisplayLabel(quest.name, quest.root?.label, quest.root?.defName);
        }

        private static string BuildQuestFactionDefName(Quest quest)
        {
            try
            {
                foreach (Faction faction in quest.InvolvedFactions)
                {
                    if (faction == null || faction == Faction.OfPlayer)
                    {
                        continue;
                    }

                    string defName = faction.def?.defName;
                    if (!string.IsNullOrEmpty(defName))
                    {
                        return defName;
                    }
                }
            }
            catch
            {
                // InvolvedFactions enumeration is vanilla-side and should not throw, but the faction
                // field is best-effort context — never fail the diary entry over it.
            }

            return QuestEventData.FactionUnknown;
        }

        private static string BuildQuestRewardsSummary(Quest quest)
        {
            try
            {
                List<QuestPart> parts = quest.PartsListForReading;
                if (parts == null || parts.Count == 0)
                {
                    return QuestEventData.RewardsNone;
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < parts.Count; i++)
                {
                    QuestPart_DropPods dropPods = parts[i] as QuestPart_DropPods;
                    if (dropPods?.thingDefs == null || dropPods.thingDefs.Count == 0)
                    {
                        continue;
                    }

                    for (int j = 0; j < dropPods.thingDefs.Count; j++)
                    {
                        ThingDefCountClass reward = dropPods.thingDefs[j];
                        ThingDef thingDef = reward?.thingDef;
                        if (thingDef == null)
                        {
                            continue;
                        }

                        string thingLabel = !string.IsNullOrWhiteSpace(thingDef.label)
                            ? thingDef.label
                            : thingDef.defName;

                        if (sb.Length > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(thingLabel).Append(" x").Append(reward.count);

                        if (sb.Length > QuestRewardsCap)
                        {
                            break;
                        }
                    }

                    if (sb.Length > QuestRewardsCap)
                    {
                        break;
                    }
                }

                if (sb.Length == 0)
                {
                    return QuestEventData.RewardsNone;
                }

                if (sb.Length > QuestRewardsCap)
                {
                    return sb.ToString(0, QuestRewardsCap) + "...";
                }

                return sb.ToString();
            }
            catch
            {
                return QuestEventData.RewardsNone;
            }
        }

        private static string BuildQuestDescription(Quest quest)
        {
            try
            {
                string description = quest.description.Resolve();
                // Reject blank and RimWorld's generated/placeholder "ERR:" descriptions before they
                // reach a diary prompt. The QuestFanoutSignal constructor drops quests whose
                // description resolves to empty here.
                if (QuestEventData.IsMalformedResolvedQuestDescription(description))
                {
                    return string.Empty;
                }

                description = PromptTextSanitizer.LocalizedPromptText(description);
                if (description.Length > QuestDescriptionCap)
                {
                    return TextTruncation.SafePrefix(description, QuestDescriptionCap) + "...";
                }

                return description;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>One eligible colonist's solo quest entry.</summary>
    internal sealed class QuestPawnSignal : DiarySignal
    {
        private readonly QuestFanoutSignal source;
        private readonly Pawn pawn;
        private readonly QuestEventData payload;

        public QuestPawnSignal(QuestFanoutSignal source, Pawn pawn, string pawnId)
        {
            this.source = source;
            this.pawn = pawn;
            payload = new QuestEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                Signal = source.Signal,
                DefName = source.QuestDefName,
                Label = source.CleanedLabel,
                FactionDefName = source.FactionDefName,
                Rewards = source.Rewards,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true, userEnabled: true, signalEnabled: true, ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string perPawnContext = QuestEventData.BuildGameContext(
                source.QuestDefName, source.Signal, source.CleanedLabel, source.FactionDefName, source.Rewards);

            // The description is prose, so it lives in the localized event text (fed to the LLM as
            // "what happened") rather than the structured game-context marker.
            string text = source.TextKey.Translate(pawn.LabelShortCap, source.CleanedLabel, source.Description).Resolve();

            DiaryEvent questEvent = sink.AddSoloEvent(pawn, null, source.QuestDefName, source.CleanedLabel,
                text, source.Instruction, perPawnContext);
            if (questEvent == null)
            {
                return;
            }

            ApplyRoyalAscentNarrativeEvidence(sink, questEvent);

            sink.QueueSolo(questEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Attaches terminal journey evidence to the already-authorized Quest page. Failure here can
        /// never cancel or duplicate the canonical page.
        /// </summary>
        private void ApplyRoyalAscentNarrativeEvidence(DiaryGameComponent sink, DiaryEvent diaryEvent)
        {
            if (source.AscentDecision == null || !source.AscentDecision.emitsTerminalPage
                || sink == null || diaryEvent == null || pawn == null)
            {
                return;
            }

            try
            {
                string pawnId = pawn.GetUniqueLoadID();
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = pawnId,
                        povRole = DiaryEvent.InitiatorRole,
                        royalty = sink.RoyaltyNarrativeSnapshotFor(pawn, diaryEvent.tick),
                        recentSelectedCandidateKeys = sink.RecentNarrativeSelectedCandidateKeys(pawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full),
                        evidence = RoyalAscentPolicy.JourneyEvidence(
                            diaryEvent.eventId,
                            diaryEvent.tick,
                            pawnId,
                            DiaryEvent.InitiatorRole,
                            source.AscentDecision,
                            "quest",
                            source.QuestDefName)
                    });
                if (result.evidence.Count > 0)
                {
                    diaryEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, result);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Royal Ascent terminal continuity failed; the canonical Quest page remains: "
                    + exception,
                    "PawnDiary.RoyalAscent.TerminalNarrative".GetHashCode());
            }
        }
    }
}
