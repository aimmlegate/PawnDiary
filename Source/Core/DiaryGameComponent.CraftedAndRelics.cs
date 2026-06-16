// Crafted-quality and relic-install — two notable events vanilla does NOT route through
// TaleRecorder, bridged here into the same solo diary-event flow as tales. RecordCraftedQuality
// fires from QualityUtility.SendCraftNotification (masterwork/legendary items, minus art, which the
// CraftedArt tale already owns); RecordRelicInstalled fires from the install-relic job's completion.
// Both emit synthetic "tale=" game-context entries (TaleQuality / TaleRelic synthetic groups), so the
// UI classifies them into the Tale domain alongside genuine TaleRecorder events.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records a masterwork or legendary item created by a colonist. Vanilla sends letters for
        /// these through QualityUtility rather than TaleRecorder, so this bridges that notification
        /// into the same solo diary-event flow used by Tale events.
        /// </summary>
        public void RecordCraftedQuality(Thing craftedThing, Pawn worker)
        {
            if (!IsDiaryEligible(worker) || craftedThing == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            QualityCategory quality;
            if (!QualityUtility.TryGetQuality(craftedThing, out quality)
                || (quality != QualityCategory.Masterwork && quality != QualityCategory.Legendary))
            {
                return;
            }

            // Art already flows through vanilla's CraftedArt tale (RecordTale), so recording the
            // quality letter for it too would produce two diary entries for one sculpture. Let the
            // tale own art; this hook covers only non-art masterwork/legendary items.
            if (craftedThing.TryGetComp<CompArt>() != null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGroupEnabled(TaleQualityGroupKey))
            {
                return;
            }

            string defName = quality == QualityCategory.Legendary ? "CraftedLegendary" : "CraftedMasterwork";
            string key = "crafted_quality|" + worker.GetUniqueLoadID() + "|" + defName + "|" + craftedThing.ThingID;
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string qualityLabel = ("QualityCategory_" + quality).Translate().Resolve();
            string itemLabel = DiaryContextBuilder.CleanLine(craftedThing.LabelShortCap);
            string label = "PawnDiary.Event.CraftedQualityLabel".Translate(qualityLabel).Resolve();
            string text = "PawnDiary.Event.CraftedQuality".Translate(worker.LabelShortCap, qualityLabel, itemLabel).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(InteractionGroups.ByKey(TaleQualityGroupKey));
            string gameContext = "tale=" + defName
                + "; source=QualityUtility.SendCraftNotification"
                + "; quality=" + quality
                + "; item=" + itemLabel
                + "; itemDef=" + (craftedThing.def?.defName ?? string.Empty);

            DiaryEvent qualityEvent = AddSoloEvent(worker, null, defName, label, text, instruction, gameContext);
            QueueLlmRewrite(qualityEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records the pawn who installs a venerated ideology relic in a reliquary. The relic
        /// container knows the item but not the worker, so the Harmony patch calls this from the
        /// install job's completion action where both are available.
        /// </summary>
        public void RecordRelicInstalled(Pawn pawn, Thing relic)
        {
            if (!IsDiaryEligible(pawn) || relic == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGroupEnabled(TaleRelicGroupKey))
            {
                return;
            }

            string key = "relic_installed|" + pawn.GetUniqueLoadID() + "|" + relic.ThingID;
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string relicLabel = DiaryContextBuilder.CleanLine(relic.LabelShortCap);
            string label = "PawnDiary.Event.RelicInstalledLabel".Translate().Resolve();
            string text = "PawnDiary.Event.RelicInstalled".Translate(pawn.LabelShortCap, relicLabel).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(InteractionGroups.ByKey(TaleRelicGroupKey));
            string gameContext = "tale=RelicInstalled"
                + "; source=JobDriver_InstallRelic"
                + "; relic=" + relicLabel
                + "; relicDef=" + (relic.def?.defName ?? string.Empty);

            DiaryEvent relicEvent = AddSoloEvent(pawn, null, "RelicInstalled", label, text, instruction, gameContext);
            QueueLlmRewrite(relicEvent, DiaryEvent.InitiatorRole);
        }
    }
}
