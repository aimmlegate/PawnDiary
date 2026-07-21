// Ideology Phases 1–2 runtime adapter. This is the only place that reads live Pawn_IdeoTracker,
// Pawn.ideo, Ideo, Precept, PreceptComp, sourcePrecept, or HistoryEvent belief metadata. Every entry
// point is double-guarded by ModsConfig.IdeologyActive plus null checks, and every value crossing the
// boundary is a detached, sanitized DTO from Source/Pipeline/Belief.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static partial class DlcContext
    {
        private sealed class PreceptDefProjection
        {
            public BeliefIssueFact issue;
            public int impactRank;
            public bool visible;
            public readonly List<string> associatedMemeDefNames = new List<string>();
            public readonly List<string> requiredMemeDefNames = new List<string>();
            public readonly List<BeliefCorrelationFact> correlations = new List<BeliefCorrelationFact>();
        }

        private static readonly Dictionary<IssueDef, BeliefIssueFact> BeliefIssueCache =
            new Dictionary<IssueDef, BeliefIssueFact>();
        private static readonly Dictionary<MemeDef, BeliefMemeFact> BeliefMemeCache =
            new Dictionary<MemeDef, BeliefMemeFact>();
        private static readonly Dictionary<PreceptDef, PreceptDefProjection> BeliefPreceptDefCache =
            new Dictionary<PreceptDef, PreceptDefProjection>();
        private static readonly Dictionary<Type, List<FieldInfo>> BeliefComponentFieldCache =
            new Dictionary<Type, List<FieldInfo>>();
        private static readonly FieldInfo BeliefTrackerPawnField =
            AccessTools.Field(typeof(Pawn_IdeoTracker), "pawn");
        private static LoadedLanguage beliefProjectionLanguage;

        /// <summary>
        /// Projects one tracker boundary into a detached state. This is the sole mutation adapter that
        /// reads Pawn_IdeoTracker, Pawn.ideo, or live Ideo identity/name/certainty.
        /// </summary>
        internal static bool TryCaptureBeliefMutationState(
            Pawn_IdeoTracker tracker,
            out BeliefMutationState state)
        {
            state = null;
            if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying
                || tracker == null || BeliefTrackerPawnField == null) return false;
            Pawn pawn = BeliefTrackerPawnField.GetValue(tracker) as Pawn;
            if (pawn == null || !ReferenceEquals(pawn.ideo, tracker)) return false;

            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            Ideo ideo = tracker.Ideo;
            string pawnId = SafeBeliefId(pawn.GetUniqueLoadID(), policy);
            string ideologyId = SafeBeliefId(ideo?.GetUniqueLoadID(), policy);
            if (pawnId.Length == 0 || ideologyId.Length == 0) return false;
            state = new BeliefMutationState
            {
                pawnId = pawnId,
                capturedTick = Find.TickManager?.TicksGame ?? 0,
                ideologyId = ideologyId,
                ideologyName = SafeBeliefText(ideo.name, policy.maximumFieldCharacters),
                hasCertainty = true,
                certainty = Clamp01(tracker.Certainty)
            };
            return true;
        }

        /// <summary>Projects the attempted conversion Ideo without leaking the live object.</summary>
        internal static BeliefMutationState CaptureAttemptedBeliefMutationState(Ideo attemptedIdeology)
        {
            if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying
                || attemptedIdeology == null) return null;
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            string ideologyId = SafeBeliefId(attemptedIdeology.GetUniqueLoadID(), policy);
            if (ideologyId.Length == 0) return null;
            return new BeliefMutationState
            {
                ideologyId = ideologyId,
                ideologyName = SafeBeliefText(attemptedIdeology.name, policy.maximumFieldCharacters)
            };
        }

        /// <summary>
        /// Captures the pawn's current live ideoligion into plain immutable-by-convention facts.
        /// Inactive Ideology, a missing tracker, or a partially initialized Ideo returns a normal
        /// empty snapshot and never reaches DLC-owned state.
        /// </summary>
        public static BeliefSnapshot CaptureBeliefSnapshot(Pawn pawn)
        {
            return CaptureBeliefSnapshot(pawn, BeliefPolicySnapshot.CreateDefault());
        }

        /// <summary>
        /// Returns exact active ThoughtDef identities for the resolver-selected live precepts. This is
        /// used only after an authorized body change, where mutually-exclusive situational workers give
        /// stronger mechanical polarity than merged correlation prose. No doctrine names are assumed.
        /// </summary>
        public static List<string> CaptureActiveSelectedPreceptThoughtDefNames(
            Pawn pawn,
            BeliefStanceResolution resolution)
        {
            List<string> result = new List<string>();
            if (!ModsConfig.IdeologyActive || pawn?.ideo?.Ideo == null
                || pawn.needs?.mood?.thoughts == null || resolution?.stances == null)
                return result;

            List<Precept> livePrecepts = pawn.ideo.Ideo.PreceptsListForReading;
            if (livePrecepts == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Thought> activeThoughts = new List<Thought>();
            for (int stanceIndex = 0; stanceIndex < resolution.stances.Count; stanceIndex++)
            {
                BeliefPreceptFact selected = resolution.stances[stanceIndex]?.precept;
                Precept live = FindSelectedLivePrecept(livePrecepts, selected);
                if (live == null || selected?.correlations == null) continue;

                for (int correlationIndex = 0;
                    correlationIndex < selected.correlations.Count && result.Count < 32;
                    correlationIndex++)
                {
                    BeliefCorrelationFact correlation = selected.correlations[correlationIndex];
                    if (correlation == null
                        || !string.Equals(correlation.kind, BeliefCorrelationKindTokens.Thought,
                            StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(correlation.defName))
                        continue;

                    ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(correlation.defName);
                    // ThoughtHandler.GetMoodThoughtsFor always asks its situational handler too. Passing
                    // a memory ThoughtDef (Worker == null) makes vanilla log a NullReferenceException;
                    // passing a social worker lacks the other pawn it needs. Only the non-social
                    // situational correlations can truthfully answer this pawn-local state question.
                    if (thoughtDef == null || !thoughtDef.IsSituational || thoughtDef.IsSocial) continue;
                    activeThoughts.Clear();
                    try
                    {
                        pawn.needs.mood.thoughts.GetMoodThoughtsFor(thoughtDef, activeThoughts);
                    }
                    catch (Exception)
                    {
                        // A modded situational worker may throw while queried. Treat that single
                        // correlation as unavailable; the resolver's normal valence remains the fallback.
                        continue;
                    }

                    for (int thoughtIndex = 0; thoughtIndex < activeThoughts.Count; thoughtIndex++)
                    {
                        Thought thought = activeThoughts[thoughtIndex];
                        if (thought == null || thought.def != thoughtDef
                            || thought.sourcePrecept != null && !ReferenceEquals(thought.sourcePrecept, live))
                            continue;
                        if (seen.Add(thoughtDef.defName)) result.Add(thoughtDef.defName);
                        break;
                    }
                }
            }
            return result;
        }

        private static Precept FindSelectedLivePrecept(List<Precept> livePrecepts, BeliefPreceptFact selected)
        {
            if (selected == null) return null;
            for (int i = 0; i < livePrecepts.Count; i++)
            {
                Precept live = livePrecepts[i];
                if (live == null) continue;
                if (!string.IsNullOrWhiteSpace(selected.instanceId)
                    && string.Equals(live.GetUniqueLoadID(), selected.instanceId, StringComparison.Ordinal))
                    return live;
            }
            Precept unique = null;
            for (int i = 0; i < livePrecepts.Count; i++)
            {
                Precept live = livePrecepts[i];
                if (!string.Equals(live?.def?.defName, selected.defName, StringComparison.Ordinal)) continue;
                if (unique != null) return null;
                unique = live;
            }
            return unique;
        }

        /// <summary>Policy-aware overload used by the event-time builder to share the same caps.</summary>
        internal static BeliefSnapshot CaptureBeliefSnapshot(Pawn pawn, BeliefPolicySnapshot policy)
        {
            BeliefSnapshot empty = new BeliefSnapshot();
            if (!ModsConfig.IdeologyActive || pawn?.ideo == null || pawn.ideo.Ideo == null)
                return empty;

            Ideo ideo = pawn.ideo.Ideo;
            if (ideo.PreceptsListForReading == null || string.IsNullOrWhiteSpace(ideo.GetUniqueLoadID()))
                return empty;

            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            EnsureBeliefProjectionLanguage();
            BeliefSnapshot result = new BeliefSnapshot
            {
                ideologyActive = true,
                pawnId = SafeBeliefId(pawn.GetUniqueLoadID(), effective),
                capturedTick = Find.TickManager?.TicksGame ?? 0,
                ideologyId = SafeBeliefId(ideo.GetUniqueLoadID(), effective),
                ideologyName = SafeBeliefText(ideo.name, effective.maximumFieldCharacters),
                certainty = new BeliefCertaintyFact
                {
                    hasCurrent = true,
                    current = Clamp01(pawn.ideo.Certainty)
                }
            };
            if (result.pawnId.Length == 0 || result.ideologyId.Length == 0)
                return empty;

            Precept_Role role = ideo.GetRole(pawn);
            result.roleName = role == null
                ? string.Empty
                : SafeBeliefText(SafelyRead(() => role.LabelForPawn(pawn)), effective.maximumFieldCharacters);

            MemeDef structure = ideo.StructureMeme;
            if (structure != null)
                result.structure = CloneMeme(ProjectMeme(structure, isStructure: true), effective);

            List<MemeDef> memes = ideo.memes;
            if (memes != null)
            {
                int cap = Math.Min(memes.Count, effective.maximumMemeCandidates);
                for (int i = 0; i < cap; i++)
                {
                    MemeDef meme = memes[i];
                    if (meme == null) continue;
                    BeliefMemeFact fact = CloneMeme(ProjectMeme(meme, ReferenceEquals(meme, structure)), effective);
                    if (fact != null && fact.defName.Length > 0) result.memes.Add(fact);
                }
            }

            int preceptCap = Math.Min(ideo.PreceptsListForReading.Count, effective.maximumPreceptCandidates);
            for (int i = 0; i < preceptCap; i++)
            {
                Precept live = ideo.PreceptsListForReading[i];
                BeliefPreceptFact fact = ProjectPrecept(live, ideo, role, effective);
                if (fact != null) result.precepts.Add(fact);
            }

            CaptureDeities(ideo, result, effective);
            return result;
        }

        /// <summary>
        /// Copies Thought.sourcePrecept identity while the live memory is still in the thought hook.
        /// The current pawn tracker is checked as well as the DLC flag, so no-Ideology and partially
        /// initialized pawns return an empty row.
        /// </summary>
        public static BeliefSourcePreceptFact CaptureThoughtSourcePrecept(Thought thought)
        {
            BeliefSourcePreceptFact empty = new BeliefSourcePreceptFact();
            if (!ModsConfig.IdeologyActive
                || thought?.pawn?.ideo == null
                || thought.pawn.ideo.Ideo == null
                || thought.sourcePrecept == null
                || thought.sourcePrecept.def == null)
                return empty;

            return new BeliefSourcePreceptFact
            {
                instanceId = SafeBeliefId(thought.sourcePrecept.GetUniqueLoadID(), null),
                defName = SafeBeliefId(thought.sourcePrecept.def.defName, null)
            };
        }

        /// <summary>
        /// Projects one HistoryEvent into a plain, non-emitting observation. Only explicit visible
        /// pawn argument roles are retained; arbitrary argument objects and hidden state are ignored.
        /// </summary>
        public static bool TryCaptureBeliefHistoryObservation(
            HistoryEvent historyEvent,
            out BeliefHistoryObservation observation)
        {
            observation = null;
            if (!ModsConfig.IdeologyActive
                || historyEvent.def == null
                || string.IsNullOrWhiteSpace(historyEvent.def.defName)
                || Find.TickManager == null)
                return false;

            BeliefHistoryObservation result = new BeliefHistoryObservation
            {
                tick = Find.TickManager.TicksGame,
                historyEventDefName = SafeBeliefId(historyEvent.def.defName, null)
            };
            IEnumerable<NamedArgument> arguments = historyEvent.args.Args;
            if (arguments == null) return false;
            foreach (NamedArgument argument in arguments)
            {
                if (!VisibleHistoryPawnArgument(argument.label)) continue;
                Pawn pawn = argument.arg as Pawn;
                if (pawn?.ideo == null || pawn.ideo.Ideo == null) continue;
                AddUniqueBeliefId(result.visiblePawnIds, pawn.GetUniqueLoadID(), 16);
            }

            if (result.historyEventDefName.Length == 0 || result.visiblePawnIds.Count == 0)
                return false;
            observation = result;
            return true;
        }

        /// <summary>Clears cached projections at a game boundary; cached field metadata is type-only.</summary>
        internal static void ResetBeliefProjectionCaches()
        {
            BeliefIssueCache.Clear();
            BeliefMemeCache.Clear();
            BeliefPreceptDefCache.Clear();
            beliefProjectionLanguage = null;
        }

        private static BeliefPreceptFact ProjectPrecept(
            Precept live,
            Ideo ideo,
            Precept_Role pawnRole,
            BeliefPolicySnapshot policy)
        {
            PreceptDef def = live?.def;
            if (def == null || string.IsNullOrWhiteSpace(def.defName)) return null;
            PreceptDefProjection projection;
            if (!BeliefPreceptDefCache.TryGetValue(def, out projection))
            {
                projection = ProjectPreceptDef(def, policy);
                BeliefPreceptDefCache[def] = projection;
            }

            BeliefPreceptFact result = new BeliefPreceptFact
            {
                instanceId = SafeBeliefId(live.GetUniqueLoadID(), policy),
                defName = SafeBeliefId(def.defName, policy),
                issue = CloneIssue(projection.issue, policy),
                displayLabel = SafeBeliefText(SafelyRead(() => live.TipLabel), policy.maximumFieldCharacters),
                description = SafeBeliefText(SafelyRead(() => live.Description), policy.maximumDescriptionCharacters),
                impactRank = projection.impactRank,
                visible = projection.visible,
                proselytizes = pawnRole != null && ReferenceEquals(live, pawnRole),
                requiredByCurrentMeme = ideo.GetMemeThatRequiresPrecept(def) != null
            };
            CopyBeliefIds(projection.associatedMemeDefNames, result.associatedMemeDefNames, 32);
            CopyBeliefIds(projection.requiredMemeDefNames, result.requiredMemeDefNames, 32);
            for (int i = 0; i < projection.correlations.Count && result.correlations.Count < 64; i++)
                result.correlations.Add(CloneCorrelation(projection.correlations[i], policy));
            return result;
        }

        private static PreceptDefProjection ProjectPreceptDef(
            PreceptDef def,
            BeliefPolicySnapshot policy)
        {
            PreceptDefProjection result = new PreceptDefProjection
            {
                issue = ProjectIssue(def.issue, policy),
                impactRank = Math.Max(0, Math.Min(3, (int)def.impact)),
                visible = def.visible
            };
            AddMemeIds(result.associatedMemeDefNames, def.associatedMemes);
            AddMemeIds(result.requiredMemeDefNames, def.requiredMemes);
            if (def.comps == null) return result;

            for (int i = 0; i < def.comps.Count && result.correlations.Count < 64; i++)
            {
                PreceptComp component = def.comps[i];
                if (component == null) continue;
                PreceptComp_Thought typedThought = component as PreceptComp_Thought;
                string componentValence = BeliefValenceTokens.Unknown;
                if (typedThought?.thought != null)
                {
                    BeliefCorrelationFact typedCorrelation = ProjectThoughtCorrelation(
                        typedThought.thought, component.GetType(), "thought", policy);
                    AddCorrelation(result.correlations, typedCorrelation);
                    componentValence = MergeValence(componentValence, typedCorrelation?.valence);
                }

                List<FieldInfo> fields = CorrelationFields(component.GetType());
                List<FieldInfo> readableFields = new List<FieldInfo>();
                List<object> values = new List<object>();
                for (int fieldIndex = 0; fieldIndex < fields.Count && result.correlations.Count < 64; fieldIndex++)
                {
                    FieldInfo field = fields[fieldIndex];
                    object value;
                    try { value = field.GetValue(component); }
                    catch { continue; }
                    readableFields.Add(field);
                    values.Add(value);
                    componentValence = MergeValence(componentValence, CorrelationValueValence(value, policy));
                }
                for (int fieldIndex = 0; fieldIndex < readableFields.Count && fieldIndex < values.Count
                    && result.correlations.Count < 64; fieldIndex++)
                {
                    ProjectCorrelationField(result.correlations, values[fieldIndex], component.GetType(),
                        readableFields[fieldIndex].Name, policy, componentValence);
                }
            }
            return result;
        }

        private static BeliefIssueFact ProjectIssue(IssueDef issue, BeliefPolicySnapshot policy)
        {
            if (issue == null) return null;
            BeliefIssueFact cached;
            if (!BeliefIssueCache.TryGetValue(issue, out cached))
            {
                cached = new BeliefIssueFact
                {
                    defName = SafeBeliefId(issue.defName, policy),
                    label = SafeBeliefText(SafelyRead(() => issue.LabelCap.Resolve()), policy.maximumFieldCharacters),
                    description = SafeBeliefText(issue.description, policy.maximumDescriptionCharacters)
                };
                BeliefIssueCache[issue] = cached;
            }
            return CloneIssue(cached, policy);
        }

        private static BeliefMemeFact ProjectMeme(MemeDef meme, bool isStructure)
        {
            if (meme == null) return null;
            BeliefMemeFact cached;
            if (!BeliefMemeCache.TryGetValue(meme, out cached))
            {
                cached = new BeliefMemeFact
                {
                    defName = SafeBeliefId(meme.defName, null),
                    label = SafeBeliefText(SafelyRead(() => meme.LabelCap.Resolve()), 320),
                    description = SafeBeliefText(meme.description, 240),
                    impactRank = Math.Max(0, Math.Min(3, meme.impact)),
                    isStructure = meme.category == MemeCategory.Structure
                };
                BeliefMemeCache[meme] = cached;
            }
            BeliefMemeFact result = CloneMeme(cached, null);
            if (result != null) result.isStructure = result.isStructure || isStructure;
            return result;
        }

        private static void CaptureDeities(
            Ideo ideo,
            BeliefSnapshot target,
            BeliefPolicySnapshot policy)
        {
            IdeoFoundation_Deity foundation = ideo?.foundation as IdeoFoundation_Deity;
            List<IdeoFoundation_Deity.Deity> deities = foundation?.DeitiesListForReading;
            if (deities == null) return;
            string keyName = SafeBeliefText(ideo.KeyDeityName, policy.maximumFieldCharacters);
            int cap = Math.Min(deities.Count, policy.maximumDeityCandidates);
            for (int i = 0; i < cap; i++)
            {
                IdeoFoundation_Deity.Deity deity = deities[i];
                string name = SafeBeliefText(deity?.name, policy.maximumFieldCharacters);
                if (name.Length == 0) continue;
                target.deities.Add(new BeliefDeityFact
                {
                    name = name,
                    typeToken = SafeBeliefText(deity.type, policy.maximumFieldCharacters),
                    genderToken = deity.gender.ToString().ToLowerInvariant(),
                    relatedMemeDefName = SafeBeliefId(deity.relatedMeme?.defName, policy),
                    isKeyDeity = keyName.Length > 0
                        && string.Equals(name, keyName, StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        private static List<FieldInfo> CorrelationFields(Type componentType)
        {
            List<FieldInfo> cached;
            if (componentType == null) return new List<FieldInfo>();
            if (BeliefComponentFieldCache.TryGetValue(componentType, out cached)) return cached;
            cached = new List<FieldInfo>();
            FieldInfo[] fields = componentType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!KnownCorrelationFieldName(field.Name) || !SupportedCorrelationFieldType(field.FieldType))
                    continue;
                cached.Add(field);
            }
            BeliefComponentFieldCache[componentType] = cached;
            return cached;
        }

        private static bool KnownCorrelationFieldName(string value)
        {
            return string.Equals(value, "thought", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "thoughts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "eventDef", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "eventDefs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SupportedCorrelationFieldType(Type fieldType)
        {
            if (fieldType == null) return false;
            if (typeof(ThoughtDef).IsAssignableFrom(fieldType)
                || typeof(HistoryEventDef).IsAssignableFrom(fieldType)) return true;
            if (fieldType.IsArray)
            {
                Type element = fieldType.GetElementType();
                return element != null && (typeof(ThoughtDef).IsAssignableFrom(element)
                    || typeof(HistoryEventDef).IsAssignableFrom(element));
            }
            if (fieldType.IsGenericType)
            {
                Type[] arguments = fieldType.GetGenericArguments();
                return arguments.Length == 1 && (typeof(ThoughtDef).IsAssignableFrom(arguments[0])
                    || typeof(HistoryEventDef).IsAssignableFrom(arguments[0]));
            }
            return false;
        }

        private static void ProjectCorrelationField(
            List<BeliefCorrelationFact> target,
            object value,
            Type componentType,
            string fieldName,
            BeliefPolicySnapshot policy,
            string companionValence)
        {
            ThoughtDef thought = value as ThoughtDef;
            if (thought != null)
            {
                AddCorrelation(target, ProjectThoughtCorrelation(thought, componentType, fieldName, policy));
                return;
            }
            HistoryEventDef history = value as HistoryEventDef;
            if (history != null)
            {
                AddCorrelation(target, ProjectHistoryCorrelation(
                    history, componentType, fieldName, policy, companionValence));
                return;
            }
            IEnumerable collection = value as IEnumerable;
            if (collection == null) return;
            int count = 0;
            foreach (object item in collection)
            {
                if (count++ >= 32 || target.Count >= 64) break;
                ProjectCorrelationField(target, item, componentType, fieldName, policy, companionValence);
            }
        }

        private static string CorrelationValueValence(object value, BeliefPolicySnapshot policy)
        {
            ThoughtDef thought = value as ThoughtDef;
            if (thought != null)
                return ProjectThoughtCorrelation(thought, null, string.Empty, policy)?.valence
                    ?? BeliefValenceTokens.Unknown;
            IEnumerable collection = value as IEnumerable;
            if (collection == null) return BeliefValenceTokens.Unknown;
            string result = BeliefValenceTokens.Unknown;
            int count = 0;
            foreach (object item in collection)
            {
                if (count++ >= 32) break;
                result = MergeValence(result, CorrelationValueValence(item, policy));
            }
            return result;
        }

        private static string MergeValence(string left, string right)
        {
            string first = BeliefValenceTokens.Normalize(left);
            string second = BeliefValenceTokens.Normalize(right);
            if (first == BeliefValenceTokens.Unknown) return second;
            if (second == BeliefValenceTokens.Unknown || first == second) return first;
            if (first == BeliefValenceTokens.Neutral) return second;
            if (second == BeliefValenceTokens.Neutral) return first;
            return BeliefValenceTokens.Mixed;
        }

        private static BeliefCorrelationFact ProjectThoughtCorrelation(
            ThoughtDef thought,
            Type componentType,
            string fieldName,
            BeliefPolicySnapshot policy)
        {
            if (thought == null || string.IsNullOrWhiteSpace(thought.defName)) return null;
            float minMood = 0f;
            float maxMood = 0f;
            float minOpinion = 0f;
            float maxOpinion = 0f;
            bool hasStage = false;
            List<string> descriptions = new List<string>();
            AddBeliefDescription(descriptions, thought.description, policy);
            if (thought.stages != null)
            {
                for (int i = 0; i < thought.stages.Count && i < 32; i++)
                {
                    ThoughtStage stage = thought.stages[i];
                    if (stage == null) continue;
                    if (!hasStage)
                    {
                        minMood = maxMood = stage.baseMoodEffect;
                        minOpinion = maxOpinion = stage.baseOpinionOffset;
                        hasStage = true;
                    }
                    else
                    {
                        minMood = Math.Min(minMood, stage.baseMoodEffect);
                        maxMood = Math.Max(maxMood, stage.baseMoodEffect);
                        minOpinion = Math.Min(minOpinion, stage.baseOpinionOffset);
                        maxOpinion = Math.Max(maxOpinion, stage.baseOpinionOffset);
                    }
                    AddBeliefDescription(descriptions, stage.LabelCap, policy);
                    AddBeliefDescription(descriptions, stage.description, policy);
                }
            }
            return new BeliefCorrelationFact
            {
                kind = BeliefCorrelationKindTokens.Thought,
                defName = SafeBeliefId(thought.defName, policy),
                label = SafeBeliefText(SafelyRead(() => thought.LabelCap.Resolve()), policy.maximumFieldCharacters),
                description = SafeBeliefText(string.Join(" ", descriptions.ToArray()),
                    policy.maximumDescriptionCharacters),
                sourceComponentKind = componentType?.FullName ?? string.Empty,
                sourceFieldToken = SafeBeliefId(fieldName, policy),
                minimumMoodOffset = minMood,
                maximumMoodOffset = maxMood,
                minimumOpinionOffset = minOpinion,
                maximumOpinionOffset = maxOpinion,
                valence = OffsetValence(minMood, maxMood, minOpinion, maxOpinion, hasStage)
            };
        }

        private static BeliefCorrelationFact ProjectHistoryCorrelation(
            HistoryEventDef history,
            Type componentType,
            string fieldName,
            BeliefPolicySnapshot policy,
            string companionValence)
        {
            if (history == null || string.IsNullOrWhiteSpace(history.defName)) return null;
            return new BeliefCorrelationFact
            {
                kind = BeliefCorrelationKindTokens.HistoryEvent,
                defName = SafeBeliefId(history.defName, policy),
                label = SafeBeliefText(SafelyRead(() => history.LabelCap.Resolve()), policy.maximumFieldCharacters),
                description = SafeBeliefText(history.description, policy.maximumDescriptionCharacters),
                sourceComponentKind = componentType?.FullName ?? string.Empty,
                sourceFieldToken = SafeBeliefId(fieldName, policy),
                // Vanilla and modded components commonly pair an eventDef with a thought field. The
                // exact event keeps that typed companion thought's mechanical offset sign; no ID list
                // or English label inference is involved.
                valence = BeliefValenceTokens.Normalize(companionValence)
            };
        }

        private static string OffsetValence(
            float minMood,
            float maxMood,
            float minOpinion,
            float maxOpinion,
            bool hasStage)
        {
            if (!hasStage) return BeliefValenceTokens.Unknown;
            bool positive = maxMood > 0.001f || maxOpinion > 0.001f;
            bool negative = minMood < -0.001f || minOpinion < -0.001f;
            if (positive && negative) return BeliefValenceTokens.Mixed;
            if (positive) return BeliefValenceTokens.Positive;
            if (negative) return BeliefValenceTokens.Negative;
            return BeliefValenceTokens.Neutral;
        }

        private static void AddCorrelation(List<BeliefCorrelationFact> target, BeliefCorrelationFact value)
        {
            if (target == null || value == null || value.defName.Length == 0 || target.Count >= 64) return;
            for (int i = 0; i < target.Count; i++)
            {
                BeliefCorrelationFact row = target[i];
                if (row != null && string.Equals(row.kind, value.kind, StringComparison.Ordinal)
                    && string.Equals(row.defName, value.defName, StringComparison.OrdinalIgnoreCase)) return;
            }
            target.Add(value);
        }

        private static bool VisibleHistoryPawnArgument(string label)
        {
            return string.Equals(label, HistoryEventArgsNames.Doer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, HistoryEventArgsNames.Subject, StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, HistoryEventArgsNames.Victim, StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "WITNESS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "MEMBER", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureBeliefProjectionLanguage()
        {
            if (beliefProjectionLanguage == LanguageDatabase.activeLanguage) return;
            ResetBeliefProjectionCaches();
            beliefProjectionLanguage = LanguageDatabase.activeLanguage;
        }

        private static BeliefIssueFact CloneIssue(BeliefIssueFact source, BeliefPolicySnapshot policy)
        {
            if (source == null) return null;
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            return new BeliefIssueFact
            {
                defName = SafeBeliefId(source.defName, effective),
                label = SafeBeliefText(source.label, effective.maximumFieldCharacters),
                description = SafeBeliefText(source.description, effective.maximumDescriptionCharacters)
            };
        }

        private static BeliefMemeFact CloneMeme(BeliefMemeFact source, BeliefPolicySnapshot policy)
        {
            if (source == null) return null;
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            return new BeliefMemeFact
            {
                defName = SafeBeliefId(source.defName, effective),
                label = SafeBeliefText(source.label, effective.maximumFieldCharacters),
                description = SafeBeliefText(source.description, effective.maximumDescriptionCharacters),
                impactRank = source.impactRank,
                isStructure = source.isStructure
            };
        }

        private static BeliefCorrelationFact CloneCorrelation(
            BeliefCorrelationFact source,
            BeliefPolicySnapshot policy)
        {
            if (source == null) return null;
            return new BeliefCorrelationFact
            {
                kind = source.kind,
                defName = source.defName,
                label = source.label,
                description = source.description,
                sourceComponentKind = source.sourceComponentKind,
                sourceFieldToken = source.sourceFieldToken,
                minimumMoodOffset = source.minimumMoodOffset,
                maximumMoodOffset = source.maximumMoodOffset,
                minimumOpinionOffset = source.minimumOpinionOffset,
                maximumOpinionOffset = source.maximumOpinionOffset,
                valence = source.valence
            };
        }

        private static void AddMemeIds(List<string> target, List<MemeDef> memes)
        {
            if (memes == null) return;
            for (int i = 0; i < memes.Count && target.Count < 32; i++)
                AddUniqueBeliefId(target, memes[i]?.defName, 32);
        }

        private static void CopyBeliefIds(List<string> source, List<string> target, int cap)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count && target.Count < cap; i++)
                AddUniqueBeliefId(target, source[i], cap);
        }

        private static void AddUniqueBeliefId(List<string> target, string value, int cap)
        {
            string cleaned = SafeBeliefId(value, null);
            if (target == null || cleaned.Length == 0 || target.Count >= cap) return;
            for (int i = 0; i < target.Count; i++)
                if (string.Equals(target[i], cleaned, StringComparison.OrdinalIgnoreCase)) return;
            target.Add(cleaned);
        }

        private static void AddBeliefDescription(
            List<string> target,
            string value,
            BeliefPolicySnapshot policy)
        {
            string cleaned = SafeBeliefText(value, policy.maximumDescriptionCharacters);
            if (cleaned.Length == 0) return;
            for (int i = 0; i < target.Count; i++)
                if (string.Equals(target[i], cleaned, StringComparison.OrdinalIgnoreCase)) return;
            target.Add(cleaned);
        }

        private static string SafeBeliefText(string value, int maximumCharacters)
        {
            return BeliefContextFormatter.WholeWord(
                PromptTextSanitizer.LocalizedPromptText(value),
                Math.Max(1, maximumCharacters));
        }

        private static string SafeBeliefId(string value, BeliefPolicySnapshot policy)
        {
            int cap = policy == null ? 160 : policy.maximumIdentifierCharacters;
            return BeliefContextFormatter.Clean(value, cap);
        }

        private static string SafelyRead(Func<string> reader)
        {
            if (reader == null) return string.Empty;
            try { return reader() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
