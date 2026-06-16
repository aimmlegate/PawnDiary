// The DiaryEvent factory and its supporting text builders. AddPairwiseEvent/AddSoloEvent are the
// two constructors every Record* hook funnels through: they stamp a new DiaryEvent with an id,
// date, the fallback game text, and all the per-POV context summaries (pawn, surroundings,
// opinions, continuity, atmosphere, …), register it, and cross-reference the involved pawns'
// records. The rest are small pure helpers that assemble the factual fallback text and the
// structured game-context strings for mental breaks and Tale/death events.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Creates a DiaryEvent involving two pawns (initiator + recipient), enriches it with context
        /// summaries, registers it, and cross-references both pawns' diary records.
        /// </summary>
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
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(initiator),
                recipientPawnSummary = DiaryContextBuilder.BuildPawnSummary(recipient),
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(initiator),
                recipientSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(recipient),
                opinionsSummary = DiaryContextBuilder.BuildOpinionsSummary(initiator, recipient),
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(initiator, recipient, diaryEvents),
                recipientContinuity = DiaryContextBuilder.BuildContinuitySummary(recipient, initiator, diaryEvents),
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(initiator.GetUniqueLoadID(), diaryEvents),
                recipientLastOpener = DiaryContextBuilder.LatestDiaryOpener(recipient.GetUniqueLoadID(), diaryEvents),
                initiatorBurningPassion = DiaryContextBuilder.RandomBurningPassion(initiator),
                recipientBurningPassion = DiaryContextBuilder.RandomBurningPassion(recipient),
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(initiator),
                recipientWeapon = DiaryContextBuilder.EquippedWeapon(recipient),
                initiatorAtmosphere = DiaryContextBuilder.BuildAtmosphere(initiator, recipient, instruction),
                recipientAtmosphere = DiaryContextBuilder.BuildAtmosphere(recipient, initiator, instruction),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId);
            AddEventRef(recipient, diaryEvent.eventId);
            return diaryEvent;
        }

        /// <summary>
        /// Creates a solo DiaryEvent (single-POV, e.g. a mental break) with no recipient role.
        /// </summary>
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
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(pawn),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn),
                recipientSurroundings = "n/a",
                opinionsSummary = otherPawn != null ? DiaryContextBuilder.BuildOpinionsSummary(pawn, otherPawn) : "n/a",
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(pawn, otherPawn, diaryEvents),
                recipientContinuity = "none",
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(pawn.GetUniqueLoadID(), diaryEvents),
                recipientLastOpener = string.Empty,
                initiatorBurningPassion = DiaryContextBuilder.RandomBurningPassion(pawn),
                recipientBurningPassion = string.Empty,
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(pawn),
                recipientWeapon = string.Empty,
                initiatorAtmosphere = DiaryContextBuilder.BuildAtmosphere(pawn, otherPawn, instruction),
                recipientAtmosphere = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId);
            return diaryEvent;
        }

        /// <summary>
        /// Assembles a human-readable fallback description for a mental break, including target and reason if available.
        /// </summary>
        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = "PawnDiary.Event.MentalBreak".Translate(pawn.LabelShortCap, DiaryContextBuilder.CleanLine(label));
            if (otherPawn != null)
            {
                text += "PawnDiary.Event.DirectedAt".Translate(DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap)).Resolve();
            }

            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }

        /// <summary>
        /// Returns a cleaned "reason=…" suffix for appending to gameContext strings, or empty if no reason.
        /// </summary>
        private static string ReasonSuffix(string reason)
        {
            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            return string.IsNullOrWhiteSpace(cleanReason) ? string.Empty : "; reason=" + cleanReason;
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
            if (taleDef == null || string.IsNullOrWhiteSpace(taleDef.defName) || !DeathTaleDefs.Contains(taleDef.defName))
            {
                return null;
            }

            // KilledBy is firstPawn=VICTIM, secondPawn=KILLER. Most other kill tales are
            // firstPawn=KILLER, secondPawn=VICTIM/COLONIST/CHILD.
            if (string.Equals(taleDef.defName, "KilledBy", StringComparison.OrdinalIgnoreCase))
            {
                return firstPawn;
            }

            return secondPawn;
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
                && (pawn.IsColonist || pawn.Faction == Faction.OfPlayer);
        }
    }
}
