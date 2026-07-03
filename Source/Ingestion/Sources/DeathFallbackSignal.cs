// Death-fallback ingestion signal — the impure capture+emit half of the "pawn died with no death
// Tale" source (the Pawn.Kill postfix). Replaces the old DiaryGameComponent.RecordDeathFallback.
// Most deaths carry a vanilla death Tale (killer/weapon context) and flow through TaleSignal's
// death-description routes; this fallback only fires for condition/need deaths that emit no Tale, and
// no-ops if a final death page already exists. The neutral death-description prompt (not a
// first-person rewrite) is queued, same as the Tale death routes.
//
// The death cause facts are captured separately by the Pawn.Kill PREFIX into DeathContextCache before
// vanilla mutates the pawn; this signal consumes them at emit time. Pure decision + the fallback
// game-context format live in Source/Capture/Events/DeathEventData.cs. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures a colonist death that produced no vanilla Tale and emits a neutral death-description
    /// page. Built by <see cref="PawnKillPatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class DeathFallbackSignal : DiarySignal
    {
        // Synthetic defName for the neutral final death page (stable save/classification token).
        internal const string DeathFallbackDefName = "PawnDiary_DeathFallback";

        private readonly Pawn pawn;
        private readonly DiaryInteractionGroupDef group;
        private readonly DeathEventData payload;

        public DeathFallbackSignal(Pawn pawn)
        {
            this.pawn = pawn;

            if (!DiaryGameComponent.GamePlaying || pawn == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            group = InteractionGroups.ClassifyDefName(GroupDomain.Tale, DeathFallbackDefName);
            string pawnId = pawn.GetUniqueLoadID();
            payload = new DeathEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = DeathFallbackDefName,
                PawnLabel = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                PawnLoadId = pawnId,
                // Needs the live event store; read it through the current component (non-null while a
                // pawn is being killed in-game). Matches the old RecordDeathFallback capture order.
                HasExistingDeathDescription = DiaryGameComponent.Current?.HasDeathDescriptionFor(pawn) ?? false,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDeathDescriptionEligible(pawn),
                userEnabled: group != null && PawnDiaryMod.Settings.IsGroupEnabled(group.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string EventTypeDedupKey(DiaryEventData payload, CaptureDecision decision)
        {
            return GenericEventTypeDedup.DeathDescriptionKey(payload?.PawnId);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSoloDeathDescription)
            {
                return;
            }

            string label = "PawnDiary.Event.DeathFallbackLabel".Translate().Resolve();
            string text = "PawnDiary.Event.DeathFallback".Translate(pawn.LabelShortCap).Resolve();
            string gameContext = DeathEventData.BuildFallbackGameContext(
                DeathFallbackDefName,
                DiaryLineCleaner.CleanLine(label),
                payload.PawnLabel,
                payload.PawnLoadId,
                DiaryEvent.InitiatorRole,
                DeathContextCache.ConsumeOrBuild(pawn));
            DiaryEvent deathEvent = sink.AddSoloEvent(pawn, null, DeathFallbackDefName, label, text,
                InteractionGroups.InstructionForGroup(group), gameContext);
            sink.AddDeathEventRef(pawn, deathEvent.eventId);
            sink.QueueDeathDescriptionFor(deathEvent);
        }
    }
}
