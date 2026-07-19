// Pawn progression scanner. This watches slow-changing pawn state that does not always have a clean
// one-shot vanilla hook: passion skill milestones, psylink levels, xenotype/gene changes, and royal
// titles. It stores only scanner baselines/highest values on PawnDiaryRecord; existing diary pages
// remain the history layer used by reflections. Phase 5 gene observation advances even when the
// Progression page source is disabled, so re-enabling it cannot create a catch-up identity page.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Periodically scans eligible colonists for future progression changes. First scan for a pawn
        /// baselines current state and emits nothing, preventing old-save catch-up bursts.
        /// </summary>
        private void ScanPawnProgressionsForDiaryEvents()
        {
            // Pending growth ownership must age out even when the ordinary Progression page source is
            // disabled. Settings control page creation; they never freeze saved observation state.
            MaintainPendingBiotechGrowthMoments();
            MaintainBiotechFamilyArcs();
            bool progressionEnabled = PawnDiaryMod.Settings != null
                && DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression);
            bool observeBiotech = ModsConfig.BiotechActive;
            bool observeRoyalty = ModsConfig.RoyaltyActive;
            if (!observeRoyalty) MarkRoyaltyObservationUnavailable();
            if (!progressionEnabled && !observeBiotech && !observeRoyalty)
            {
                return;
            }

            List<Pawn> colonists = SnapshotFreeColonists();
            MaintainRoyaltyTransientProgression(colonists);
            BaselineRoyaltyStateIfNeeded(colonists);
            for (int i = 0; i < colonists.Count; i++)
            {
                ScanPawnProgressionForDiaryEvents(
                    colonists[i],
                    progressionEnabled,
                    observeBiotech);
            }
        }

        /// <summary>
        /// Runs one pawn through the same progression bookkeeping used by the colony scanner. Keeping
        /// this seam internal lets the in-game suite prove disabled-output advancement without touching
        /// unrelated player colonists.
        /// </summary>
        internal void ScanPawnProgressionForDiaryEvents(
            Pawn pawn,
            bool progressionEnabled,
            bool observeBiotechGenes)
        {
            if (!IsDiaryEligible(pawn)) return;
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null) return;

            PawnProgressionState state = diary.EnsureProgressionState();
            bool baseline = state.baselineProgressionOnNextScan;
            if (observeBiotechGenes)
            {
                // Phase 6 tenure/baseline state advances even when Progression pages are disabled.
                // This prevents an old save or later re-enable from inventing a "first" mech.
                ObserveMechanitorState(pawn, state);
                // The first/disabled pass is bookkeeping only. Enabled later passes may emit one
                // significant fallback, but every path advances before returning.
                ObserveGeneIdentity(pawn, state, progressionEnabled && !baseline);
            }
            if (ModsConfig.RoyaltyActive)
            {
                // Royalty Phase 4 owns its own versioned baseline. Observe even while Progression
                // output is disabled so title/psylink truth cannot bank a later catch-up page.
                ObserveRoyaltyProgression(pawn, state, progressionEnabled);
            }
            if (!progressionEnabled) return;

            ScanPassionSkillMilestones(pawn, state, baseline);
            ScanTraitGain(pawn, state);
            if (baseline) state.baselineProgressionOnNextScan = false;
        }

        /// <summary>
        /// Removes comparison state captured while Biotech was installed. A save made with the DLC
        /// disabled therefore cannot report a stale catch-up mutation if the DLC is enabled later.
        /// </summary>
        private void InvalidateBiotechGeneObservationsWithoutDlc()
        {
            if (ModsConfig.BiotechActive || diaries == null) return;
            for (int i = 0; i < diaries.Count; i++)
            {
                diaries[i]?.progressionState?.biotechProgressionState?
                    .geneIdentityObservation?.Invalidate();
            }
        }

        /// <summary>
        /// Captures and advances the bounded current gene baseline. First/disabled passes are silent;
        /// enabled later passes may emit one XML-significant fallback identity transition.
        /// </summary>
        private void ObserveGeneIdentity(
            Pawn pawn,
            PawnProgressionState state,
            bool allowFallbackEmission)
        {
            GeneIdentitySnapshot identity;
            if (state == null || !DlcContext.TryCaptureGeneIdentity(pawn, out identity))
            {
                return;
            }

            GeneIdentityObservationState observation = state.EnsureBiotechState()
                .EnsureGeneIdentityObservation();
            bool firstCurrentBaseline = !observation.HasCurrentBaseline();
            GeneSaliencePolicySnapshot policy = DiaryBiotechPolicy.Snapshot().geneSalience;
            GeneIdentitySnapshot before = GeneIdentityTransitionPolicy.FromObservation(
                observation.CaptureSnapshot());
            GeneIdentityTransitionDecision decision = firstCurrentBaseline
                ? null
                : GeneIdentityTransitionPolicy.Evaluate(before, identity, policy);
            observation.Observe(
                identity,
                policy.maximumObservedGeneDefNames,
                policy.labelCharacterLimit);
            // Frozen legacy keys remain current for old-save/downgrade compatibility, but the nested
            // versioned observation is authoritative for Phase 5 diffing.
            state.lastObservedXenotypeDefName = identity.xenotypeDefName ?? string.Empty;
            state.lastObservedXenotypeLabel = identity.xenotypeLabel ?? string.Empty;
            if (!allowFallbackEmission || firstCurrentBaseline
                || !GeneIdentityTransitionPolicy.ShouldEmitFallback(
                    decision,
                    policy.minimumFallbackGeneChanges)) return;

            EmitGeneIdentityTransition(
                pawn,
                before,
                identity,
                decision,
                GeneChangeCauseTokens.ObservedChange,
                null);
        }

        /// <summary>Captures exact recipient before-state for one verified vanilla xenogerm call.</summary>
        internal BiotechGeneMutationCallState BeginBiotechGeneMutation(
            Pawn recipient,
            Pawn otherPawn,
            string causeToken)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || !IsDiaryEligible(recipient)) return null;
            GeneIdentitySnapshot before;
            if (!DlcContext.TryCaptureGeneIdentity(recipient, out before)) return null;
            return new BiotechGeneMutationCallState
            {
                recipient = recipient,
                otherPawn = otherPawn,
                causeToken = causeToken ?? string.Empty,
                before = before
            };
        }

        /// <summary>
        /// Completes an exact xenogerm mutation, advances observation immediately, and emits at most one
        /// canonical recipient page. Returns true only when that page committed.
        /// </summary>
        internal bool CompleteBiotechGeneMutation(BiotechGeneMutationCallState call)
        {
            GeneIdentitySnapshot after;
            if (call?.recipient == null || call.before == null
                || !ModsConfig.BiotechActive
                || !DlcContext.TryCaptureGeneIdentity(call.recipient, out after)) return false;

            PawnDiaryRecord diary = FindDiary(call.recipient, true);
            if (diary == null) return false;
            PawnProgressionState state = diary.EnsureProgressionState();
            GeneIdentityObservationState observation = state.EnsureBiotechState()
                .EnsureGeneIdentityObservation();
            GeneSaliencePolicySnapshot policy = DiaryBiotechPolicy.Snapshot().geneSalience;
            GeneIdentityTransitionDecision decision = GeneIdentityTransitionPolicy.Evaluate(
                call.before,
                after,
                policy);

            // Advance even for an empty/disabled result so the slow observer cannot replay this call.
            observation.Observe(after, policy.maximumObservedGeneDefNames, policy.labelCharacterLimit);
            state.lastObservedXenotypeDefName = after.xenotypeDefName ?? string.Empty;
            state.lastObservedXenotypeLabel = after.xenotypeLabel ?? string.Empty;
            if (!decision.HasAnyChange) return false;

            bool emitted = EmitGeneIdentityTransition(
                call.recipient,
                call.before,
                after,
                decision,
                call.causeToken,
                call.otherPawn);
            if (emitted && string.Equals(
                call.causeToken,
                GeneChangeCauseTokens.XenogermReimplant,
                StringComparison.Ordinal))
            {
                BiotechGeneMutationCorrelation.ClaimCurrentAbility(call.recipient);
            }
            return emitted;
        }

        private bool EmitGeneIdentityTransition(
            Pawn pawn,
            GeneIdentitySnapshot before,
            GeneIdentitySnapshot after,
            GeneIdentityTransitionDecision decision,
            string causeToken,
            Pawn otherPawn)
        {
            if (pawn == null || decision == null || !decision.HasAnyChange) return false;
            BiotechPolicySnapshot biotechPolicy = DiaryBiotechPolicy.Snapshot();
            GeneSaliencePolicySnapshot policy = biotechPolicy.geneSalience;
            bool major = IsMajorArcXenotype(after?.xenotypeDefName);
            string context = GeneIdentityContextFormatter.Format(
                before,
                after,
                decision,
                causeToken,
                otherPawn?.LabelShortCap,
                otherPawn?.GetUniqueLoadID(),
                policy.labelCharacterLimit);
            context += "; major_xenotype=" + (major ? "true" : "false");
            string previousLabel = GeneIdentityContextFormatter.CleanField(
                before?.xenotypeLabel,
                policy.labelCharacterLimit);
            string currentLabel = GeneIdentityContextFormatter.CleanField(
                after?.xenotypeLabel,
                policy.labelCharacterLimit);
            ProgressionEventData data = ProgressionData(
                pawn,
                ProgressionEventData.GeneIdentityChangedDefName,
                "gene_identity",
                currentLabel,
                previousLabel,
                currentLabel,
                context);
            string label = "PawnDiary.Event.Biotech.GeneIdentity.Label".Translate().Resolve();
            string text = "PawnDiary.Event.Biotech.GeneIdentity.Text"
                .Translate(pawn.LabelShortCap).Resolve();
            string dedupKey = GeneIdentityEventKeys.DedupKey(
                pawn.GetUniqueLoadID(),
                causeToken);
            BiotechNarrativeSnapshot narrativeSnapshot = BuildGeneIdentityNarrativeSnapshot(
                pawn,
                after,
                decision,
                biotechPolicy,
                data.Tick);
            List<NarrativeEvidence> narrativeEvidence = new List<NarrativeEvidence>
            {
                new NarrativeEvidence
                {
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = causeToken ?? string.Empty,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = pawn.GetUniqueLoadID(),
                    subjectLabel = pawn.LabelShortCap,
                    beliefTopics = new List<string> { "identity", "gene" },
                    salience = major ? NarrativeSalienceTokens.Major : NarrativeSalienceTokens.Meaningful,
                    pawnCanKnow = true,
                    sourceDomain = "biotech_gene",
                    sourceDefName = ProgressionEventData.GeneIdentityChangedDefName
                }
            };
            return DispatchProgression(
                pawn,
                data,
                label,
                text,
                major,
                dedupKey,
                DiaryTuning.Current.genericEventTypeDedupTicks,
                narrativeEvidence,
                narrativeSnapshot);
        }

        /// <summary>
        /// Converts the already-selected leading gene theme into one exact-subject identity lens. It
        /// never enumerates genes here: the Phase-5 selector already bounded and sanitized the decision.
        /// A xenotype-only change retains the existing N2 visible-xenotype fallback.
        /// </summary>
        private static BiotechNarrativeSnapshot BuildGeneIdentityNarrativeSnapshot(
            Pawn pawn,
            GeneIdentitySnapshot after,
            GeneIdentityTransitionDecision decision,
            BiotechPolicySnapshot policy,
            int sourceTick)
        {
            if (!ModsConfig.BiotechActive || pawn == null || after == null || decision == null)
            {
                return null;
            }

            GeneTheme leadingTheme = null;
            if (decision.themes != null)
            {
                for (int i = 0; i < decision.themes.Count; i++)
                {
                    GeneTheme candidate = decision.themes[i];
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.defName)
                        && !string.IsNullOrWhiteSpace(candidate.label))
                    {
                        leadingTheme = candidate;
                        break;
                    }
                }
            }

            string pawnName = DiaryLineCleaner.CleanLine(pawn.LabelShortCap);
            string stableKey;
            string identityText;
            List<string> topics = new List<string> { "identity" };
            if (leadingTheme != null)
            {
                stableKey = "gene|" + leadingTheme.defName.Trim();
                identityText = FormatGeneIdentityNarrative(
                    policy?.geneIdentityNarrativeFormat,
                    pawnName,
                    leadingTheme.label);
                topics.Add("gene");
            }
            else
            {
                stableKey = (after.xenotypeDefName ?? string.Empty).Trim();
                identityText = FormatGeneIdentityNarrative(
                    policy?.identityNarrativeFormat,
                    pawnName,
                    after.xenotypeLabel);
                topics.Add("xenotype");
            }

            return new BiotechNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = pawn.GetUniqueLoadID(),
                childId = pawn.GetUniqueLoadID(),
                xenotypeDefName = after.xenotypeDefName ?? string.Empty,
                identityStableKey = stableKey,
                identityText = identityText,
                identityTopicTokens = topics,
                sourceTick = sourceTick,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
        }

        private static string FormatGeneIdentityNarrative(
            string format,
            string pawnName,
            string visibleTheme)
        {
            if (string.IsNullOrWhiteSpace(format) || string.IsNullOrWhiteSpace(pawnName)
                || string.IsNullOrWhiteSpace(visibleTheme))
            {
                return string.Empty;
            }

            try
            {
                return DiaryLineCleaner.CleanLine(string.Format(format, pawnName, visibleTheme));
            }
            catch (FormatException)
            {
                // Malformed custom DefInjected prose disables only this optional continuity lens.
                return string.Empty;
            }
        }

        private void ScanPassionSkillMilestones(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            if (pawn?.skills == null)
            {
                return;
            }

            List<SkillDef> skillDefs = DefDatabase<SkillDef>.AllDefsListForReading;
            for (int i = 0; i < skillDefs.Count; i++)
            {
                SkillDef skillDef = skillDefs[i];
                SkillRecord skill = pawn.skills.GetSkill(skillDef);
                if (skill == null)
                {
                    continue;
                }

                bool hasPassion = skill.passion != Passion.None;
                string skillDefName = skillDef?.defName ?? string.Empty;
                int previous = state.HighestSkillMilestone(skillDefName);
                ProgressionMilestoneDecision decision = ProgressionMilestonePolicy.EvaluateSkillMilestone(
                    skill.Level,
                    hasPassion,
                    DiaryTuning.Current.progressionSkillMilestones,
                    previous,
                    baseline);

                if (decision.newHighestMilestone != previous)
                {
                    state.SetSkillMilestone(skillDefName, decision.newHighestMilestone);
                }

                if (!decision.shouldEmit)
                {
                    continue;
                }

                string skillLabel = CleanLabel(skillDef?.label, skillDefName);
                string passionToken = skill.passion == Passion.Major ? "major" : "minor";
                string passionLabel = (skill.passion == Passion.Major
                    ? "PawnDiary.Event.ProgressionPassionMajor"
                    : "PawnDiary.Event.ProgressionPassionMinor").Translate().Resolve();
                string extraContext = "skill=" + skillLabel
                    + "; skill_level=" + decision.milestoneToEmit
                    + "; previous_skill_milestone=" + previous
                    + "; passion=" + passionToken;
                ProgressionEventData data = ProgressionData(
                    pawn,
                    ProgressionEventData.SkillMilestoneDefName,
                    "skill",
                    skillLabel,
                    previous.ToString(),
                    decision.milestoneToEmit.ToString(),
                    extraContext);
                string label = "PawnDiary.Event.ProgressionSkillLabel"
                    .Translate(skillLabel, decision.milestoneToEmit).Resolve();
                string text = "PawnDiary.Event.ProgressionSkillText"
                    .Translate(pawn.LabelShortCap, skillLabel, decision.milestoneToEmit, passionLabel).Resolve();
                DispatchProgression(pawn, data, label, text, majorArcCandidate: false);
            }
        }

        private void ScanXenotypeChange(Pawn pawn, PawnProgressionState state, bool baseline)
        {
            // Retained as a narrow reflection-compatible wrapper for the existing RimTest fixture.
            // The top-level scanner calls ObserveGeneIdentity directly so disabled output still advances.
            ObserveGeneIdentity(pawn, state, !baseline);
        }

        // Watches the pawn's trait set for newly gained traits. Traits rarely change through a clean
        // one-shot vanilla hook (TraitSet.GainTrait also fires all through pawn generation), so like the
        // xenotype scanner this compares a saved snapshot against the live set and baselines silently on
        // the first scan — that keeps a pawn's starting traits out of the diary and only records traits
        // acquired during play. It uses its OWN baseline flag rather than the shared one so a save made
        // before this feature (empty snapshot, already-false shared flag) baselines instead of spamming.
        private void ScanTraitGain(Pawn pawn, PawnProgressionState state)
        {
            // story/traits is null for some pawn kinds; guard and treat it as "no traits".
            List<Trait> traits = pawn?.story?.traits?.allTraits;
            List<string> currentKeys = new List<string>();
            Dictionary<string, Trait> traitByKey = new Dictionary<string, Trait>(StringComparer.OrdinalIgnoreCase);
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];
                    string defName = trait?.def?.defName;
                    string key = TraitProgressionPolicy.BuildTraitKey(defName, trait?.Degree ?? 0);
                    if (string.IsNullOrEmpty(key) || traitByKey.ContainsKey(key))
                    {
                        continue;
                    }

                    currentKeys.Add(key);
                    traitByKey[key] = trait;
                }
            }

            if (state.baselineTraitGainOnNextScan)
            {
                state.knownTraitKeys = currentKeys;
                state.baselineTraitGainOnNextScan = false;
                return;
            }

            List<string> newKeys = TraitProgressionPolicy.NewlyGainedTraitKeys(state.knownTraitKeys, currentKeys);
            // Advance the snapshot unconditionally, mirroring the scalar progression scanners above: a
            // gain the user has filtered off is intentionally missed rather than retried every scan.
            state.knownTraitKeys = currentKeys;

            for (int i = 0; i < newKeys.Count; i++)
            {
                Trait trait;
                if (traitByKey.TryGetValue(newKeys[i], out trait) && trait != null)
                {
                    EmitTraitGain(pawn, trait, newKeys[i]);
                }
            }
        }

        private void EmitTraitGain(Pawn pawn, Trait trait, string traitKey)
        {
            string traitLabel = TraitGainLabel(pawn, trait);
            string description = TraitPersonalityDescription(pawn, trait);
            string traitDefName = trait?.def?.defName ?? string.Empty;

            // The description is the character-card flavor prose RimWorld shows for the trait, resolved
            // for this pawn and free of stat/mechanic lines. Feeding it (not a hardcoded per-trait table)
            // is the whole point: the model writes the felt personality shift from the supplied words, so
            // any trait — vanilla or modded — works. Semicolons are flattened so the prose cannot split
            // the "; key=value" game-context string.
            string extraContext = "trait=" + traitLabel + "; trait_def=" + traitDefName;
            if (!string.IsNullOrWhiteSpace(description))
            {
                extraContext += "; trait_description=" + description.Replace(";", ",");
            }

            // A trait gain is an addition, not a transition from a prior value, so previous_value is left
            // blank (BuildGameContext omits blank fields).
            ProgressionEventData data = ProgressionData(
                pawn,
                ProgressionEventData.TraitGainedDefName,
                "trait",
                traitLabel,
                string.Empty,
                traitLabel,
                extraContext);
            string label = "PawnDiary.Event.ProgressionTraitLabel".Translate(traitLabel).Resolve();
            string text = "PawnDiary.Event.ProgressionTraitText".Translate(pawn.LabelShortCap, traitLabel).Resolve();
            // A per-trait key so two traits gained in the same scan each record instead of colliding on
            // the dispatcher's generic type+subject key. The saved snapshot already blocks cross-scan
            // repeats, so the short generic window is enough here.
            string dedupKey = "progression-trait|" + pawn.GetUniqueLoadID() + "|" + traitKey;
            DispatchProgression(pawn, data, label, text, majorArcCandidate: false,
                dedupKey: dedupKey, dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks);
        }

        // Gender-aware, cleaned degree label ("Nervous", "Kind", ...). CurrentData can throw for a
        // malformed modded trait, so fall back to the plain label and finally the defName.
        private static string TraitGainLabel(Pawn pawn, Trait trait)
        {
            if (trait == null)
            {
                return string.Empty;
            }

            string label = null;
            try
            {
                label = trait.CurrentData?.GetLabelCapFor(pawn);
            }
            catch
            {
                label = null;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                try
                {
                    label = trait.LabelCap;
                }
                catch
                {
                    label = null;
                }
            }

            string cleaned = DiaryLineCleaner.CleanLine(label);
            return string.IsNullOrWhiteSpace(cleaned) ? (trait.def?.defName ?? string.Empty) : cleaned;
        }

        // Resolves ONLY the trait's flavor description — the first block of RimWorld's Trait.TipString,
        // before it appends skill gains, stat offsets, meditation foci, memes, and genes. Those trailing
        // sections are exactly the "game mechanic" wording we keep out of the diary. Returns empty when a
        // trait has no description (some modded traits) so the model works from the label alone.
        private static string TraitPersonalityDescription(Pawn pawn, Trait trait)
        {
            string resolved = null;
            try
            {
                string description = trait?.CurrentData?.description;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    resolved = description.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
                }
            }
            catch
            {
                resolved = null;
            }

            string cleaned = DiaryLineCleaner.CleanLine(resolved);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            // Defensive cap so an unusually long modded description cannot bloat the prompt game-context.
            const int MaxDescriptionChars = 400;
            if (cleaned.Length > MaxDescriptionChars)
            {
                cleaned = cleaned.Substring(0, MaxDescriptionChars).TrimEnd() + "…";
            }

            return cleaned;
        }

        private bool DispatchProgression(Pawn pawn, ProgressionEventData data, string label, string text,
            bool majorArcCandidate, string dedupKey = null, int dedupWindowTicks = 0,
            List<NarrativeEvidence> narrativeEvidence = null,
            BiotechNarrativeSnapshot biotechNarrative = null)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyProgression(data.DefName);
            bool userEnabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            bool signalEnabled = DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression);
            string instruction = InteractionGroups.InstructionForProgression(group);
            string gameContext = ProgressionEventData.BuildGameContext(
                data.DefName,
                data.Kind,
                data.Label,
                data.PreviousValue,
                data.NewValue,
                data.Context);
            bool emitted = Dispatch(new ProgressionSignal(
                data,
                pawn,
                label,
                text,
                instruction,
                gameContext,
                IsDiaryEligible(pawn),
                userEnabled,
                signalEnabled,
                dedupKey,
                dedupWindowTicks,
                narrativeEvidence,
                biotechNarrative));
            if (emitted && majorArcCandidate)
            {
                ConsiderArcReflectionAfterMajorEvent(pawn);
            }

            return emitted;
        }

        private static ProgressionEventData ProgressionData(Pawn pawn, string defName, string kind,
            string label, string previousValue, string newValue, string context)
        {
            return new ProgressionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = defName,
                Kind = kind,
                Label = label,
                PreviousValue = previousValue,
                NewValue = newValue,
                Context = context,
                AlreadyRecorded = false
            };
        }

        private static int ClampPsylinkLevel(int level)
        {
            if (level < 1)
            {
                return 0;
            }

            return level > 6 ? 6 : level;
        }

        private static bool MatchesDefName(List<string> defNames, string defName)
        {
            if (string.IsNullOrWhiteSpace(defName) || defNames == null)
            {
                return false;
            }

            for (int i = 0; i < defNames.Count; i++)
            {
                if (string.Equals(defNames[i], defName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMajorArcPsylinkLevel(int level)
        {
            int clamped = ClampPsylinkLevel(level);
            return MeetsArcMajorSeverity((clamped * 100) / 6);
        }

        private static bool IsMajorArcXenotype(string defName)
        {
            return MatchesDefName(DiaryTuning.Current.arcReflectionMajorXenotypeDefNames, defName)
                && MeetsArcMajorSeverity(100);
        }

        private static bool MeetsArcMajorSeverity(int severity)
        {
            return severity >= Math.Max(0, DiaryTuning.Current.arcReflectionMajorSeverityThreshold);
        }

        private static string CleanLabel(string label, string fallback)
        {
            string cleaned = DiaryLineCleaner.CleanLine(label);
            return string.IsNullOrWhiteSpace(cleaned) ? (fallback ?? string.Empty) : cleaned;
        }
    }
}
