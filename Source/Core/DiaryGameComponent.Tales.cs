// Tales — the TaleRecorder.RecordTale hook's diary flow. Tales are vanilla's broad notable-history
// events (deaths, wounds, surgeries, births, recruitment, research, disasters, ...). RecordTale
// snapshots the live RimWorld facts the pure catalog cannot read; TaleEventData.Decide chooses the
// final route (batch / death-description / pair / solo); this file performs that side effect.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string DeathFallbackDefName = "PawnDiary_DeathFallback";

        /// <summary>
        /// Records one RimWorld TaleRecorder event by snapshotting live facts, asking the pure
        /// catalog for a route, then executing the requested side effect.
        /// </summary>
        public void RecordTale(Tale tale, TaleDef taleDef)
        {
            taleDef = taleDef ?? tale?.def;
            if (!CanRecordGameplayEventNow() || tale == null || taleDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            Pawn firstPawn;
            Pawn secondPawn;
            ExtractTalePawns(tale, out firstPawn, out secondPawn);

            Pawn deathVictim = DeathVictimForTale(taleDef, firstPawn, secondPawn);
            bool deathDescription = IsDeathDescriptionEligible(deathVictim);

            bool firstEligible = IsDiaryEligible(firstPawn) || (deathDescription && firstPawn == deathVictim);
            bool secondEligible = IsDiaryEligible(secondPawn) || (deathDescription && secondPawn == deathVictim);
            DiaryInteractionGroupDef batchGroup = deathDescription ? null : TaleBatchGroupFor(taleDef);

            // Snapshot live facts for the pure catalog decision. The caller pre-computes the impure
            // classifications (covered-elsewhere set membership, GameConditionDef duplicate,
            // pawn eligibility with death override, and XML batch policy) so Decide stays pure.
            TaleEventData data = new TaleEventData
            {
                PawnId = firstPawn?.GetUniqueLoadID() ?? string.Empty,
                Tick = Find.TickManager.TicksGame,
                DefName = taleDef.defName,
                FirstPawnId = firstPawn?.GetUniqueLoadID(),
                SecondPawnId = secondPawn?.GetUniqueLoadID(),
                FirstEligible = firstEligible,
                SecondEligible = secondEligible,
                IsCoveredElsewhere = TaleEventData.CoveredElsewhere.Contains(taleDef.defName),
                IsGameConditionDuplicate = DefDatabase<GameConditionDef>.GetNamedSilentFail(taleDef.defName) != null,
                IsBatched = batchGroup != null,
                IsDeathDescription = deathDescription,
            };

            CaptureContext ctx = BuildCaptureContext(
                eligible: firstEligible || secondEligible,
                userEnabled: PawnDiaryMod.Settings.IsTaleEnabled(taleDef),
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Tale);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision == CaptureDecision.Drop)
            {
                return;
            }

            string key = "tale|" + taleDef.defName + "|" + (firstPawn?.GetUniqueLoadID() ?? string.Empty)
                + "|" + (secondPawn?.GetUniqueLoadID() ?? string.Empty);
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string label = CleanTaleLabel(taleDef);
            Def attachedDef = AttachedDefFor(tale);
            string instruction = PawnDiaryMod.Settings.InstructionForTale(taleDef);
            string gameContext = BuildTaleGameContext(tale, taleDef, label, attachedDef);
            if (deathDescription)
            {
                gameContext = AppendDeathDescriptionContext(gameContext, deathVictim, firstPawn, secondPawn);
            }

            if (decision == CaptureDecision.RouteBatch)
            {
                if (batchGroup != null)
                {
                    RecordBatchedTale(batchGroup, firstPawn, secondPawn, firstEligible, secondEligible,
                        taleDef, label, attachedDef, instruction);
                }
                return;
            }

            if (decision == CaptureDecision.GeneratePair
                || decision == CaptureDecision.GeneratePairDeathDescription)
            {
                string text = BuildTalePairText(firstPawn, secondPawn, label, attachedDef);
                DiaryEvent pairEvent = AddPairwiseEvent(firstPawn, secondPawn, taleDef.defName, label,
                    text, text, instruction, gameContext);
                if (decision == CaptureDecision.GeneratePairDeathDescription)
                {
                    AddDeathEventRef(deathVictim, pairEvent.eventId);
                    QueueDeathDescription(pairEvent);
                    return;
                }

                QueuePairwiseGeneration(pairEvent);
                return;
            }

            if (decision != CaptureDecision.GenerateSolo
                && decision != CaptureDecision.GenerateSoloDeathDescription)
            {
                return;
            }

            Pawn povPawn = firstEligible ? firstPawn : secondPawn;
            Pawn otherPawn = firstEligible ? secondPawn : firstPawn;
            string soloText = BuildTaleSoloText(povPawn, label, otherPawn, attachedDef);
            DiaryEvent soloEvent = AddSoloEvent(povPawn, otherPawn, taleDef.defName, label, soloText, instruction, gameContext);
            if (decision == CaptureDecision.GenerateSoloDeathDescription)
            {
                AddDeathEventRef(deathVictim, soloEvent.eventId);
                QueueDeathDescription(soloEvent);
                return;
            }

            QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records a neutral final death entry for kill paths that do not produce a vanilla death
        /// Tale. The Tale path is preferred when present because it carries killer/weapon context;
        /// this fallback only runs after Pawn.Kill and no-ops if a final death entry already exists.
        /// </summary>
        public void RecordDeathFallback(Pawn pawn)
        {
            if (!CanRecordGameplayEventNow()
                || pawn == null
                || PawnDiaryMod.Settings == null)
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(GroupDomain.Tale, DeathFallbackDefName);
            string pawnId = pawn.GetUniqueLoadID();
            DeathEventData data = new DeathEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = DeathFallbackDefName,
                PawnLabel = DiaryContextBuilder.CleanLine(pawn.LabelShortCap),
                PawnLoadId = pawnId,
                HasExistingDeathDescription = HasDeathDescriptionFor(pawn),
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: IsDeathDescriptionEligible(pawn),
                userEnabled: group != null && PawnDiaryMod.Settings.IsGroupEnabled(group.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Death);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision != CaptureDecision.GenerateSoloDeathDescription)
            {
                return;
            }

            string label = "PawnDiary.Event.DeathFallbackLabel".Translate().Resolve();
            string text = "PawnDiary.Event.DeathFallback".Translate(pawn.LabelShortCap).Resolve();
            string gameContext = DeathEventData.BuildFallbackGameContext(
                DeathFallbackDefName,
                DiaryContextBuilder.CleanLine(label),
                data.PawnLabel,
                data.PawnLoadId,
                DiaryEvent.InitiatorRole,
                DeathContextCache.ConsumeOrBuild(pawn));
            DiaryEvent deathEvent = AddSoloEvent(pawn, null, DeathFallbackDefName, label, text,
                PawnDiaryMod.Settings.InstructionForGroup(group), gameContext);
            AddDeathEventRef(pawn, deathEvent.eventId);
            QueueDeathDescription(deathEvent);
        }

        /// <summary>
        /// Pulls the live pawn references out of vanilla Tale subclasses. TaleData also keeps
        /// historical snapshots, but the live Pawn is what we need for diary ownership/context.
        /// </summary>
        private static void ExtractTalePawns(Tale tale, out Pawn firstPawn, out Pawn secondPawn)
        {
            firstPawn = null;
            secondPawn = null;

            Tale_DoublePawn doublePawnTale = tale as Tale_DoublePawn;
            if (doublePawnTale != null)
            {
                firstPawn = doublePawnTale.firstPawnData?.pawn;
                secondPawn = doublePawnTale.secondPawnData?.pawn;
                return;
            }

            Tale_SinglePawn singlePawnTale = tale as Tale_SinglePawn;
            if (singlePawnTale != null)
            {
                firstPawn = singlePawnTale.pawnData?.pawn;
            }
        }

        /// <summary>
        /// Returns the extra Def attached to some tales (research project, skill, damage type,
        /// crafted object kind, etc.), or null for plain pawn-only tales.
        /// </summary>
        private static Def AttachedDefFor(Tale tale)
        {
            Tale_DoublePawnAndDef doublePawnAndDef = tale as Tale_DoublePawnAndDef;
            if (doublePawnAndDef != null)
            {
                return doublePawnAndDef.defData?.def;
            }

            Tale_SinglePawnAndDef singlePawnAndDef = tale as Tale_SinglePawnAndDef;
            return singlePawnAndDef?.defData?.def;
        }

        /// <summary>
        /// Resolves a user-facing TaleDef label, falling back to defName if the label is blank.
        /// </summary>
        private static string CleanTaleLabel(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return "unknown";
            }

            string label = DiaryContextBuilder.CleanLine(taleDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? taleDef.defName : label;
        }

        /// <summary>
        /// Builds the raw game text for a single-pawn tale, localized because it is shown in
        /// the Diary tab and also passed to the model as "what happened".
        /// </summary>
        private static string BuildTaleSoloText(Pawn povPawn, string label, Pawn otherPawn, Def attachedDef)
        {
            string text = otherPawn != null
                ? "PawnDiary.Event.TaleSoloWithOther".Translate(povPawn.LabelShortCap, label, otherPawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.TaleSolo".Translate(povPawn.LabelShortCap, label).Resolve();

            return AppendAttachedDefText(text, attachedDef);
        }

        /// <summary>
        /// Builds the raw game text for a two-pawn tale. Both pawns receive the same factual
        /// description; the LLM prompt adds separate pawn summaries and continuity for each POV.
        /// </summary>
        private static string BuildTalePairText(Pawn firstPawn, Pawn secondPawn, string label, Def attachedDef)
        {
            string text = "PawnDiary.Event.TalePair".Translate(firstPawn.LabelShortCap, secondPawn.LabelShortCap, label).Resolve();
            return AppendAttachedDefText(text, attachedDef);
        }

        /// <summary>
        /// Appends the TaleData_Def label when vanilla supplied one, such as a research project,
        /// skill, damage type, or crafted object kind.
        /// </summary>
        private static string AppendAttachedDefText(string text, Def attachedDef)
        {
            if (attachedDef == null)
            {
                return text;
            }

            string label = DiaryContextBuilder.CleanLine(attachedDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label)
                ? text
                : text + "PawnDiary.Event.TaleAttachedDef".Translate(label).Resolve();
        }

        /// <summary>
        /// Creates a compact metadata string for Tale-sourced diary events. The leading tale=
        /// marker is also how saved events are later classified back into the Tale domain.
        /// </summary>
        private static string BuildTaleGameContext(Tale tale, TaleDef taleDef, string label, Def attachedDef)
        {
            List<string> parts = new List<string>
            {
                "tale=" + taleDef.defName,
                "label=" + DiaryContextBuilder.CleanLine(label),
                "taleClass=" + tale.GetType().Name
            };

            if (attachedDef != null)
            {
                parts.Add("attachedDef=" + attachedDef.defName);
                parts.Add("attachedLabel=" + DiaryContextBuilder.CleanLine(attachedDef.LabelCap.Resolve()));
            }

            return string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Appends colonist-death metadata to the Tale context. The neutral death prompt reads this
        /// instead of using a pawn persona, so it can describe cause, damaged body part, illness, and
        /// nearby context without pretending the dead pawn wrote a diary entry.
        /// </summary>
        private static string AppendDeathDescriptionContext(string gameContext, Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            List<string> parts = new List<string>
            {
                gameContext,
                "death_description=true",
                "death_victim=" + DiaryContextBuilder.CleanLine(deathVictim.LabelShortCap),
                "death_victim_id=" + deathVictim.GetUniqueLoadID(),
                "death_victim_role=" + DeathVictimRole(deathVictim, firstPawn, secondPawn)
            };

            Pawn otherPawn = deathVictim == firstPawn ? secondPawn : firstPawn;
            if (otherPawn != null)
            {
                parts.Add("other_pawn=" + DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap));
            }

            string deathFacts = DeathContextCache.ConsumeOrBuild(deathVictim);
            if (!string.IsNullOrWhiteSpace(deathFacts))
            {
                parts.Add(deathFacts);
            }

            return string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Returns the pawn who died for TaleDefs that represent deaths. Vanilla's TaleDefs do not
        /// use one consistent pawn order, so this method keeps that mapping in one place.
        /// </summary>
        private static Pawn DeathVictimForTale(TaleDef taleDef, Pawn firstPawn, Pawn secondPawn)
        {
            if (taleDef == null || string.IsNullOrWhiteSpace(taleDef.defName))
            {
                return null;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyTale(taleDef);
            string victimRole = group?.DeathVictimRoleFor(taleDef.defName);
            if (string.Equals(victimRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return firstPawn;
            }

            if (string.Equals(victimRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return secondPawn;
            }

            return null;
        }

        private static string DeathVictimRole(Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            if (deathVictim == firstPawn)
            {
                return DiaryEvent.InitiatorRole;
            }

            if (deathVictim == secondPawn)
            {
                return DiaryEvent.RecipientRole;
            }

            return DiaryEvent.NeutralRole;
        }

        private static bool IsDeathDescriptionEligible(Pawn pawn)
        {
            return pawn != null
                && IsHumanlike(pawn)
                && pawn.IsColonist;
        }

        private bool HasDeathDescriptionFor(Pawn pawn)
        {
            string pawnId = pawn?.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary?.eventIds == null)
            {
                return false;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent != null && diaryEvent.IsDeathDescriptionFor(pawnId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
