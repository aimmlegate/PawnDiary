// Tale ingestion signal — the impure capture+emit half of the "vanilla Tale recorded" source
// (TaleRecorder.RecordTale). Replaces the old DiaryGameComponent.RecordTale. Tales are vanilla's
// broad notable-history events (deaths, wounds, surgeries, births, recruitment, research, disasters).
// This is the most branchy source: one Tale can route to Drop / RouteBatch / GeneratePair /
// GenerateSolo / a neutral death-description (pair or solo). The pure route choice lives in
// TaleEventData.Decide (tested); this class snapshots the live RimWorld facts and performs the
// requested side effect.
//
// Pure decision, the CoveredElsewhere skip-list, the dedup key, and the base game-context format live
// in Source/Capture/Events/TaleEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one TaleRecorder event and emits it via the route the catalog chose. Built by
    /// <see cref="TaleRecorderPatch"/> and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class TaleSignal : DiarySignal
    {
        private readonly Tale tale;
        private readonly TaleDef taleDef;
        private readonly Pawn firstPawn;
        private readonly Pawn secondPawn;
        private readonly bool firstEligible;
        private readonly bool secondEligible;
        private readonly Pawn deathVictim;
        private readonly bool deathDescription;
        private readonly DiaryInteractionGroupDef batchGroup;
        private readonly TaleEventData payload;

        public TaleSignal(Tale tale, TaleDef taleDef)
        {
            taleDef = taleDef ?? tale?.def;
            this.tale = tale;
            this.taleDef = taleDef;

            if (!DiaryGameComponent.GamePlaying || tale == null || taleDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            ExtractTalePawns(tale, out firstPawn, out secondPawn);

            deathVictim = DeathVictimForTale(taleDef, firstPawn, secondPawn);
            deathDescription = DiaryGameComponent.IsDeathDescriptionEligible(deathVictim);

            firstEligible = DiaryGameComponent.IsDiaryEligible(firstPawn) || (deathDescription && firstPawn == deathVictim);
            secondEligible = DiaryGameComponent.IsDiaryEligible(secondPawn) || (deathDescription && secondPawn == deathVictim);
            batchGroup = deathDescription ? null : DiaryGameComponent.TaleBatchGroupFor(taleDef);

            payload = new TaleEventData
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
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: firstEligible || secondEligible,
                userEnabled: PawnDiaryMod.Settings.IsTaleEnabled(taleDef),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload != null ? payload.DedupKey() : string.Empty;

        public override int DedupWindowTicks => DiaryTuning.Current.taleDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            string label = CleanTaleLabel(taleDef);
            Def attachedDef = AttachedDefFor(tale);
            string instruction = InteractionGroups.InstructionForTale(taleDef);
            string gameContext = BuildTaleGameContext(tale, taleDef, label, attachedDef);
            if (deathDescription)
            {
                gameContext = AppendDeathDescriptionContext(gameContext, deathVictim, firstPawn, secondPawn);
            }

            if (decision == CaptureDecision.RouteBatch)
            {
                if (batchGroup != null)
                {
                    sink.RecordBatchedTale(batchGroup, firstPawn, secondPawn, firstEligible, secondEligible,
                        taleDef, label, attachedDef, instruction);
                }
                return;
            }

            if (decision == CaptureDecision.GeneratePair
                || decision == CaptureDecision.GeneratePairDeathDescription)
            {
                string text = BuildTalePairText(firstPawn, secondPawn, label, attachedDef);
                DiaryEvent pairEvent = sink.AddPairwiseEvent(firstPawn, secondPawn, taleDef.defName, label,
                    text, text, instruction, gameContext);
                if (decision == CaptureDecision.GeneratePairDeathDescription)
                {
                    sink.AddDeathEventRef(deathVictim, pairEvent.eventId);
                    sink.QueueDeathDescriptionFor(pairEvent);
                    return;
                }

                sink.QueuePair(pairEvent);
                return;
            }

            if (decision != CaptureDecision.GenerateSolo
                && decision != CaptureDecision.GenerateSoloDeathDescription)
            {
                return;
            }

            Pawn povPawn = firstEligible ? firstPawn : secondPawn;
            Pawn otherPawn = firstEligible ? secondPawn : firstPawn;
            string soloText = DiaryGameComponent.BuildTaleSoloText(povPawn, label, otherPawn, attachedDef);
            DiaryEvent soloEvent = sink.AddSoloEvent(povPawn, otherPawn, taleDef.defName, label, soloText, instruction, gameContext);
            if (decision == CaptureDecision.GenerateSoloDeathDescription)
            {
                sink.AddDeathEventRef(deathVictim, soloEvent.eventId);
                sink.QueueDeathDescriptionFor(soloEvent);
                return;
            }

            sink.QueueSolo(soloEvent, DiaryEvent.InitiatorRole);
        }

        // ── Tale-specific helpers moved verbatim from the old DiaryGameComponent.Tales.cs ──

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

        private static string CleanTaleLabel(TaleDef taleDef)
        {
            if (taleDef == null)
            {
                return "unknown";
            }

            string label = DiaryLineCleaner.CleanLine(taleDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label) ? taleDef.defName : label;
        }

        private static string BuildTalePairText(Pawn firstPawn, Pawn secondPawn, string label, Def attachedDef)
        {
            string text = "PawnDiary.Event.TalePair".Translate(firstPawn.LabelShortCap, secondPawn.LabelShortCap, label).Resolve();
            return DiaryGameComponent.AppendAttachedDefText(text, attachedDef);
        }

        private static string BuildTaleGameContext(Tale tale, TaleDef taleDef, string label, Def attachedDef)
        {
            List<string> parts = new List<string>
            {
                "tale=" + taleDef.defName,
                "label=" + DiaryLineCleaner.CleanLine(label),
                "taleClass=" + tale.GetType().Name
            };

            if (attachedDef != null)
            {
                parts.Add("attachedDef=" + attachedDef.defName);
                parts.Add("attachedLabel=" + DiaryLineCleaner.CleanLine(attachedDef.LabelCap.Resolve()));
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string AppendDeathDescriptionContext(string gameContext, Pawn deathVictim, Pawn firstPawn, Pawn secondPawn)
        {
            List<string> parts = new List<string>
            {
                gameContext,
                "death_description=true",
                "death_victim=" + DiaryLineCleaner.CleanLine(deathVictim.LabelShortCap),
                "death_victim_id=" + deathVictim.GetUniqueLoadID(),
                "death_victim_role=" + DeathVictimRole(deathVictim, firstPawn, secondPawn)
            };

            Pawn otherPawn = deathVictim == firstPawn ? secondPawn : firstPawn;
            if (otherPawn != null)
            {
                parts.Add("other_pawn=" + DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap));
            }

            string deathFacts = DeathContextCache.ConsumeOrBuild(deathVictim);
            if (!string.IsNullOrWhiteSpace(deathFacts))
            {
                parts.Add(deathFacts);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static Pawn DeathVictimForTale(TaleDef taleDef, Pawn firstPawn, Pawn secondPawn)
        {
            if (taleDef == null || string.IsNullOrWhiteSpace(taleDef.defName))
            {
                return null;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyTale(taleDef);
            string victimRole = group?.DeathVictimRoleFor(taleDef.defName);
            if (string.Equals(victimRole, DiaryEvent.InitiatorRole, System.StringComparison.OrdinalIgnoreCase))
            {
                return firstPawn;
            }

            if (string.Equals(victimRole, DiaryEvent.RecipientRole, System.StringComparison.OrdinalIgnoreCase))
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
    }
}
