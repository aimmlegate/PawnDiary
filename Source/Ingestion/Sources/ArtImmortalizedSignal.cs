// Impure Quality Wave H6 adapter: turns one already-decided art-immortalization into a localized solo
// page. The writer was chosen by the pure ArtImmortalizationPolicy and the deed was confirmed
// unclaimed by the patch before this signal was built, so Emit only localizes and registers.
//
// Registration IS the claim: the moment the DiaryEvent exists, the hot/archive ownership query in
// DiaryArtPatches sees it, and every later sculpture about the same deed goes quiet. Generation can
// fail afterwards without releasing the deed — the page is already the owner and ordinary orphan
// recovery retries it. New to C#/RimWorld? See AGENTS.md.
using System;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one quiet solo page about an artwork that immortalized a colony deed.</summary>
    internal sealed class ArtImmortalizedSignal : DiarySignal
    {
        private readonly Pawn writer;
        private readonly string taleSubjectName;
        private readonly DiaryInteractionGroupDef group;
        private readonly ArtImmortalizedEventData payload;

        /// <summary>The durable event created by Emit, or null when creation did not finish.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        public ArtImmortalizedSignal(
            Pawn writer,
            string artDefName,
            string artThingId,
            string artTitle,
            string sculptorName,
            string taleDefName,
            string taleIdentity,
            string taleSubjectName,
            DiaryInteractionGroupDef group,
            int tick)
        {
            this.writer = writer;
            this.taleSubjectName = taleSubjectName ?? string.Empty;
            this.group = group;
            payload = new ArtImmortalizedEventData
            {
                PawnId = writer?.GetUniqueLoadID() ?? string.Empty,
                Tick = Math.Max(0, tick),
                ArtDefName = artDefName ?? string.Empty,
                ArtThingId = artThingId ?? string.Empty,
                ArtTitle = artTitle ?? string.Empty,
                SculptorName = sculptorName ?? string.Empty,
                TaleDefName = taleDefName ?? string.Empty,
                TaleIdentity = taleIdentity ?? string.Empty
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool groupEnabled = group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: writer != null && DiaryGameComponent.IsDiaryEligible(writer),
                userEnabled: groupEnabled,
                signalEnabled: DiaryTuning.Current.artImmortalizationEnabled,
                ambientSignalEnabled: true);
        }

        /// <summary>
        /// The exact deed identity. This only guards two sculptures finished in the same moment; the
        /// durable once-per-deed guarantee is the hot/archive ownership query in DiaryArtPatches,
        /// because the dedup store is a short rolling window, not a permanent ledger.
        /// </summary>
        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, DiaryTuning.Current.taleDedupTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || writer == null || group == null || decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string title = string.IsNullOrWhiteSpace(payload.ArtTitle)
                ? "PawnDiary.Event.ArtImmortalizedFallbackTitle".Translate().Resolve()
                : payload.ArtTitle;
            string subject = string.IsNullOrWhiteSpace(taleSubjectName)
                ? writer.LabelShortCap
                : taleSubjectName;

            string label = "PawnDiary.Event.ArtImmortalizedLabel".Translate().Resolve();
            string text = "PawnDiary.Event.ArtImmortalized".Translate(subject, title).Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);
            string context = ArtImmortalizedEventData.BuildGameContext(
                payload.ArtDefName,
                payload.ArtThingId,
                title,
                payload.SculptorName,
                payload.TaleDefName,
                payload.TaleIdentity);

            CreatedEvent = CreateSoloEvent(
                sink, writer, null, ArtImmortalizedEventData.ArtImmortalizedDefName,
                label, text, instruction, context);
            if (CreatedEvent == null)
            {
                return;
            }

            try
            {
                sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
            }
            catch (Exception exception)
            {
                // The durable event already owns the deed; ordinary orphan recovery can retry it.
                Log.ErrorOnce(
                    "[Pawn Diary] Art-immortalization page was created but its initial generation "
                    + "queue failed; normal recovery may retry it: " + exception,
                    "PawnDiary.ArtImmortalized.Queue".GetHashCode());
            }
        }
    }
}
