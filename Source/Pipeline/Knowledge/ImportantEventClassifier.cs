// ImportantEventClassifier.cs — pure classification of capture signals against the XML-owned
// important-event allowlist (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2). One signal either matches
// exactly one rule (first match in ascending order within its capture channel) or produces
// nothing; a match yields one detached record draft per resolved owner.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings
// references here — the impure listeners build KnowledgeCaptureSignal snapshots and persist the
// returned drafts.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Matches capture signals to important-event rules and drafts per-owner records.</summary>
    internal static class ImportantEventClassifier
    {
        /// <summary>
        /// Classifies one signal. Returns an empty list when no enabled rule of the signal's
        /// channel matches — the closed-list design (§2.1): everything not allowlisted is ignored.
        /// </summary>
        public static List<ImportantMemoryDraft> Classify(
            KnowledgeCaptureSignal signal,
            List<ImportantEventRule> rules,
            KnowledgePolicySnapshot policy)
        {
            List<ImportantMemoryDraft> drafts = new List<ImportantMemoryDraft>();
            if (signal == null || rules == null)
            {
                return drafts;
            }

            ImportantEventRule rule = FirstMatch(signal, rules);
            if (rule == null)
            {
                return drafts;
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            List<string> ownerIds = ResolveOwners(signal, rule);
            for (int i = 0; i < ownerIds.Count; i++)
            {
                string ownerId = ownerIds[i];
                if (string.IsNullOrWhiteSpace(ownerId))
                {
                    continue;
                }

                drafts.Add(BuildDraft(signal, rule, ownerId, safePolicy));
            }

            return drafts;
        }

        /// <summary>First enabled rule of the signal's channel that matches, in ascending
        /// <c>order</c> then defName order — mirrors the interaction-group first-match-wins rule.</summary>
        public static ImportantEventRule FirstMatch(
            KnowledgeCaptureSignal signal, List<ImportantEventRule> rules)
        {
            ImportantEventRule best = null;
            for (int i = 0; i < rules.Count; i++)
            {
                ImportantEventRule rule = rules[i];
                if (rule == null || !rule.enabled
                    || !string.Equals(rule.signal, signal.signal, StringComparison.OrdinalIgnoreCase)
                    || !Matches(signal, rule))
                {
                    continue;
                }

                if (best == null || Compare(rule, best) < 0)
                {
                    best = rule;
                }
            }

            return best;
        }

        /// <summary>
        /// Cheap identity-only prefilter for hot listeners. False proves that no enabled rule in the
        /// channel can match this defName; true means context may still accept or reject it later.
        /// </summary>
        public static bool MayMatchIdentity(
            string signalToken, string defName, List<ImportantEventRule> rules)
        {
            if (string.IsNullOrWhiteSpace(signalToken) || rules == null)
            {
                return false;
            }

            string candidate = defName ?? string.Empty;
            for (int i = 0; i < rules.Count; i++)
            {
                ImportantEventRule rule = rules[i];
                if (rule == null || !rule.enabled
                    || !string.Equals(
                        rule.signal, signalToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!HasAnyNameMatcher(rule))
                {
                    return true;
                }

                if (rule.matchDefNames != null)
                {
                    for (int j = 0; j < rule.matchDefNames.Count; j++)
                    {
                        if (!string.IsNullOrWhiteSpace(rule.matchDefNames[j])
                            && string.Equals(candidate, rule.matchDefNames[j].Trim(),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                if (rule.matchSuffixes != null)
                {
                    for (int j = 0; j < rule.matchSuffixes.Count; j++)
                    {
                        string suffix = rule.matchSuffixes[j];
                        if (!string.IsNullOrWhiteSpace(suffix)
                            && candidate.EndsWith(
                                suffix.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True when an exact changed relation Def name is already owned by an authoritative
        /// relationship page rule. Observation adapters use this to avoid a second factual source.
        /// </summary>
        public static bool AuthoritativePageOwnsRelationTransition(
            IEnumerable<string> previous,
            IEnumerable<string> current,
            List<ImportantEventRule> rules)
        {
            HashSet<string> previousSet = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> currentSet = new HashSet<string>(StringComparer.Ordinal);
            if (previous != null)
                foreach (string value in previous)
                    if (!string.IsNullOrWhiteSpace(value)) previousSet.Add(value.Trim());
            if (current != null)
                foreach (string value in current)
                    if (!string.IsNullOrWhiteSpace(value)) currentSet.Add(value.Trim());
            HashSet<string> changed = new HashSet<string>(previousSet, StringComparer.Ordinal);
            changed.SymmetricExceptWith(currentSet);
            if (changed.Count == 0 || rules == null) return false;
            foreach (ImportantEventRule rule in rules)
            {
                if (rule == null || !rule.enabled || !rule.authoritativePageOwned
                    || rule.memoryCategory != MemoryContractTokens.CategoryRelationships
                    || rule.authoritativeRelationDefNames == null) continue;
                for (int i = 0; i < rule.authoritativeRelationDefNames.Count; i++)
                    if (changed.Contains(
                        (rule.authoritativeRelationDefNames[i] ?? string.Empty).Trim())) return true;
            }
            return false;
        }

        private static int Compare(ImportantEventRule left, ImportantEventRule right)
        {
            int order = left.order.CompareTo(right.order);
            return order != 0
                ? order
                : string.Compare(left.defName, right.defName, StringComparison.Ordinal);
        }

        private static bool Matches(KnowledgeCaptureSignal signal, ImportantEventRule rule)
        {
            string defName = signal.defName ?? string.Empty;
            // A row with no name matchers is context-gated only (e.g. "any hediff event with
            // part_kind=missingpart") — its requireContext rows below are the whole gate.
            bool nameMatched = !HasAnyNameMatcher(rule);
            if (rule.matchDefNames != null)
            {
                for (int i = 0; i < rule.matchDefNames.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rule.matchDefNames[i])
                        && string.Equals(defName, rule.matchDefNames[i].Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        nameMatched = true;
                        break;
                    }
                }
            }

            if (!nameMatched && rule.matchSuffixes != null)
            {
                string lower = defName.ToLowerInvariant();
                for (int i = 0; i < rule.matchSuffixes.Count; i++)
                {
                    string suffix = rule.matchSuffixes[i];
                    if (!string.IsNullOrWhiteSpace(suffix)
                        && lower.EndsWith(suffix.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        nameMatched = true;
                        break;
                    }
                }
            }

            if (!nameMatched)
            {
                return false;
            }

            // Extra context gates: every row must hold. "key=value" is exact; "key=" (or a bare
            // key) means "present with a meaningful, non-sentinel value".
            if (rule.requireContext != null)
            {
                for (int i = 0; i < rule.requireContext.Count; i++)
                {
                    string row = rule.requireContext[i];
                    if (string.IsNullOrWhiteSpace(row))
                    {
                        continue;
                    }

                    string trimmed = row.Trim();
                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex > 0 && equalsIndex < trimmed.Length - 1)
                    {
                        if (!DiaryContextFields.FieldEquals(signal.gameContext,
                            trimmed.Substring(0, equalsIndex), trimmed.Substring(equalsIndex + 1)))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        string key = equalsIndex > 0 ? trimmed.Substring(0, equalsIndex) : trimmed;
                        string value = DiaryContextFields.Value(signal.gameContext, key);
                        if (KnowledgeTokens.IsSentinelValue(value))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool HasAnyNameMatcher(ImportantEventRule rule)
        {
            if (rule.matchDefNames != null)
            {
                for (int i = 0; i < rule.matchDefNames.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rule.matchDefNames[i]))
                    {
                        return true;
                    }
                }
            }

            if (rule.matchSuffixes != null)
            {
                for (int i = 0; i < rule.matchSuffixes.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rule.matchSuffixes[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<string> ResolveOwners(KnowledgeCaptureSignal signal, ImportantEventRule rule)
        {
            List<string> owners = new List<string>();
            string token = rule.owners ?? string.Empty;
            if (string.Equals(token, KnowledgeTokens.OwnersProvided, StringComparison.OrdinalIgnoreCase))
            {
                owners.Add(signal.providedOwnerPawnId);
                return owners;
            }

            bool initiator = string.Equals(token, KnowledgeTokens.OwnersInitiator, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, KnowledgeTokens.OwnersBoth, StringComparison.OrdinalIgnoreCase);
            bool recipient = string.Equals(token, KnowledgeTokens.OwnersRecipient, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, KnowledgeTokens.OwnersBoth, StringComparison.OrdinalIgnoreCase);
            if (initiator)
            {
                owners.Add(signal.initiatorPawnId);
            }

            if (recipient)
            {
                owners.Add(signal.recipientPawnId);
            }

            return owners;
        }

        private static ImportantMemoryDraft BuildDraft(
            KnowledgeCaptureSignal signal,
            ImportantEventRule rule,
            string ownerId,
            KnowledgePolicySnapshot policy)
        {
            ImportantMemoryRecordSnapshot record = new ImportantMemoryRecordSnapshot
            {
                ownerPawnId = ownerId,
                sourceEventId = signal.sourceEventId ?? string.Empty,
                eventKind = rule.eventKind ?? string.Empty,
                topicKey = rule.topicKey ?? string.Empty,
                tick = signal.tick,
                dateLabel = (signal.dateLabel ?? string.Empty).Trim()
            };

            AddCounterpartParticipant(record, signal, ownerId);
            AddContextParticipants(record, signal, rule, ownerId);
            if (signal.extraParticipants != null)
            {
                for (int i = 0; i < signal.extraParticipants.Count; i++)
                {
                    KnowledgeParticipant extra = signal.extraParticipants[i];
                    if (extra != null && !string.IsNullOrWhiteSpace(extra.pawnId)
                        && !string.Equals(extra.pawnId, ownerId, StringComparison.OrdinalIgnoreCase))
                    {
                        record.participants.Add(new KnowledgeParticipant
                        {
                            pawnId = extra.pawnId.Trim(),
                            name = (extra.name ?? string.Empty).Trim()
                        });
                    }
                }
            }

            if (rule.subjectKeyRules != null)
            {
                for (int i = 0; i < rule.subjectKeyRules.Count; i++)
                {
                    KnowledgeSubjectKeyRule keyRule = rule.subjectKeyRules[i];
                    if (keyRule == null || string.IsNullOrWhiteSpace(keyRule.contextKey))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(signal.gameContext, keyRule.contextKey);
                    if (KnowledgeTokens.IsSentinelValue(value))
                    {
                        continue;
                    }

                    string key = ComposeSubjectKey(keyRule.prefix, value);
                    if (!ContainsOrdinalIgnoreCase(record.subjectKeys, key))
                    {
                        record.subjectKeys.Add(key);
                    }
                }
            }

            if (rule.constantSubjectKeys != null)
            {
                for (int i = 0; i < rule.constantSubjectKeys.Count; i++)
                {
                    string constant = rule.constantSubjectKeys[i];
                    if (!string.IsNullOrWhiteSpace(constant)
                        && !ContainsOrdinalIgnoreCase(record.subjectKeys, constant.Trim()))
                    {
                        record.subjectKeys.Add(constant.Trim());
                    }
                }
            }

            if (rule.factKeys != null)
            {
                for (int i = 0; i < rule.factKeys.Count; i++)
                {
                    string factKey = rule.factKeys[i];
                    if (string.IsNullOrWhiteSpace(factKey))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(signal.gameContext, factKey.Trim());
                    if (!KnowledgeTokens.IsSentinelValue(value))
                    {
                        record.facts.Add(new KnowledgeFact { key = factKey.Trim(), value = value });
                    }
                }
            }

            // Deterministic identity (§2.2): same owner + kind + primary subject + tick collapses
            // to one record no matter how many listeners observed the same gameplay change.
            string primarySubject = record.subjectKeys.Count > 0
                ? record.subjectKeys[0]
                : (record.participants.Count > 0 ? record.participants[0].pawnId : string.Empty);
            record.dedupKey = ownerId + "|" + record.eventKind + "|" + primarySubject + "|" + signal.tick;
            record.recordId = record.dedupKey;

            record.fallbackSummary = ImportantMemoryLineRenderer.Render(
                record, rule.lineTemplate, policy.fallbackSummaryMaxChars);
            ImportantMemoryDraft draft = new ImportantMemoryDraft
            {
                ownerPawnId = ownerId,
                matchedRuleDefName = rule.defName ?? string.Empty,
                record = record
            };
            draft.factual = BuildFactualDraft(signal, rule, ownerId, record);
            return draft;
        }

        /// <summary>
        /// Activates the XML-owned M7 metadata without changing the legacy record projection. Every
        /// optional failure returns null, allowing the main-thread adapter to keep current truth settled.
        /// </summary>
        private static FactualMemoryDraft BuildFactualDraft(
            KnowledgeCaptureSignal signal,
            ImportantEventRule rule,
            string ownerId,
            ImportantMemoryRecordSnapshot legacyRecord)
        {
            if (MemoryThreadRoutingPolicy.ValidateRuleContract(rule).Length != 0) return null;

            const string factDiscriminator = "primary";
            List<MemoryRouteCandidate> routeCandidates = BuildRouteCandidates(signal, rule, ownerId);
            MemoryRouteResolution route = MemoryThreadRoutingPolicy.Resolve(
                ownerId, rule.threadRoute, routeCandidates);
            bool routeReliable = route.isThreaded
                && rule.threadRoute.chapterDirective
                    != MemoryChapterDirectiveTokens.RemainStandalone;

            string canonicalSubjectKind;
            string canonicalSubjectId;
            string canonicalSubjectLabel;
            if (routeReliable)
            {
                canonicalSubjectKind = route.subjectKind;
                canonicalSubjectId = route.subjectId;
                canonicalSubjectLabel = route.frozenLabel;
            }
            else
            {
                SelectStandaloneCanonicalSubject(
                    signal, rule.threadRoute == null, ownerId,
                    out canonicalSubjectKind, out canonicalSubjectId,
                    out canonicalSubjectLabel);
            }
            if (!MemoryContractTokens.IsValidRootSubject(
                    canonicalSubjectKind, canonicalSubjectId)) return null;

            string sourceOccurrenceId = (signal.sourceOccurrenceId ?? string.Empty).Trim();
            string sourceEventId = (signal.sourceEventId ?? string.Empty).Trim();
            if (sourceOccurrenceId.Length == 0 && sourceEventId.Length > 0)
                sourceOccurrenceId = sourceEventId;
            if (sourceOccurrenceId.Length == 0)
            {
                MemorySourceOccurrenceFallback fallback = new MemorySourceOccurrenceFallback
                {
                    stableSignalToken = StableSignalToken(signal),
                    eventTickInvariant = Math.Max(0, signal.tick),
                    sourceLocalSequenceInvariant = signal.sourceLocalSequenceInvariant,
                    factDiscriminator = factDiscriminator,
                    sourceProvesUniqueness = signal.sourceProvesUniqueness,
                    subjects = OccurrenceSubjects(
                        signal, routeCandidates, ownerId,
                        canonicalSubjectKind, canonicalSubjectId)
                };
                if (!MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(
                        fallback, out sourceOccurrenceId)) return null;
            }

            FactualMemoryDraft draft = new FactualMemoryDraft
            {
                ownerPawnId = ownerId,
                sourceOccurrenceId = sourceOccurrenceId,
                sourceEventId = sourceEventId,
                sourceKindToken = sourceEventId.Length > 0 ? "diary_event" : "capture_signal",
                captureRuleId = rule.defName ?? string.Empty,
                factDiscriminator = factDiscriminator,
                kind = rule.memoryKind ?? string.Empty,
                category = rule.memoryCategory ?? string.Empty,
                importance = rule.baseImportance ?? string.Empty,
                originalEventTick = Math.Max(0, signal.tick),
                consolidationEligible = rule.consolidationEligible,
                authoritativePageOwned = rule.authoritativePageOwned,
                routeReliable = routeReliable,
                routeReasonToken = route.reasonToken ?? string.Empty,
                subjectKind = routeReliable ? route.subjectKind : string.Empty,
                subjectId = routeReliable ? route.subjectId : string.Empty,
                frozenSubjectLabel = routeReliable ? route.frozenLabel : string.Empty,
                chapterPhaseToken = routeReliable
                    ? rule.threadRoute.chapterPhasePolicy ?? string.Empty
                    : FirstFactKind(rule),
                chapterDirective = routeReliable
                    ? rule.threadRoute.chapterDirective
                    : MemoryChapterDirectiveTokens.RemainStandalone,
                chapterClosureReasonToken = routeReliable
                    ? rule.threadRoute.chapterClosureReasonToken ?? string.Empty
                    : string.Empty,
                automaticWording = legacyRecord?.fallbackSummary ?? string.Empty
            };

            draft.primarySubject = CreateSubject(
                canonicalSubjectKind, canonicalSubjectId, canonicalSubjectLabel, "primary", "direct");
            if (draft.primarySubject == null) return null;
            AddSecondarySubjects(draft, signal, ownerId);

            bool reversal = DiaryContextFields.FieldEquals(
                signal.gameContext, "reversal", "true");
            for (int i = 0; i < rule.memoryFacts.Count; i++)
            {
                MemoryFactDescriptor descriptor = rule.memoryFacts[i];
                string value = string.IsNullOrWhiteSpace(descriptor.contextKey)
                    ? string.Empty
                    : DiaryContextFields.Value(signal.gameContext, descriptor.contextKey);
                if (!MemoryThreadRoutingPolicy.IsValidCanonicalValue(descriptor, value)) return null;
                string factId;
                if (!MemoryIdentityCodec.TryCreateFactId(
                        draft.captureRuleId,
                        factDiscriminator,
                        descriptor.factKind,
                        canonicalSubjectKind,
                        canonicalSubjectId,
                        descriptor.aggregationToken,
                        out factId)) return null;
                draft.facts.Add(new FactualMemoryFactDraft
                {
                    factId = factId,
                    factKind = descriptor.factKind,
                    canonicalSubjectKind = canonicalSubjectKind,
                    canonicalSubjectId = canonicalSubjectId,
                    aggregationToken = descriptor.aggregationToken,
                    canonicalValueKind = descriptor.canonicalValueKind,
                    canonicalValue = value ?? string.Empty,
                    majorTurningPoint = rule.memoryKind == MemoryContractTokens.KindLandmark,
                    reversal = reversal
                });
            }
            draft.facts.Sort((left, right) => string.CompareOrdinal(left.factId, right.factId));

            if (!MemoryIdentityCodec.TryCreateProvenanceRefId(
                    draft.sourceKindToken,
                    draft.sourceOccurrenceId,
                    draft.sourceEventId,
                    draft.captureRuleId,
                    factDiscriminator,
                    string.Empty,
                    out draft.provenanceRefId)) return null;
            return draft;
        }

        private static List<MemoryRouteCandidate> BuildRouteCandidates(
            KnowledgeCaptureSignal signal,
            ImportantEventRule rule,
            string ownerId)
        {
            List<MemoryRouteCandidate> candidates = new List<MemoryRouteCandidate>();
            MemoryThreadRouteRule route = rule.threadRoute;
            if (route?.equivalentExtractors == null) return candidates;
            for (int i = 0; i < route.equivalentExtractors.Count; i++)
            {
                string token = route.equivalentExtractors[i]?.extractorToken ?? string.Empty;
                if (token.StartsWith("constant:", StringComparison.Ordinal))
                {
                    candidates.Add(new MemoryRouteCandidate
                    {
                        extractorToken = token,
                        subjectKind = route.subjectKind,
                        subjectId = token.Substring("constant:".Length),
                        frozenLabel = FallbackLabel(signal, route.fallbackLabelSource, ownerId)
                    });
                }
                else if (token == "counterpart_pawn")
                {
                    string id;
                    string label;
                    Counterpart(signal, ownerId, out id, out label);
                    if (id.Length > 0)
                        candidates.Add(Candidate(token, route.subjectKind, id, label));
                }
                else if (token.StartsWith("context:", StringComparison.Ordinal))
                {
                    string id = DiaryContextFields.Value(
                        signal.gameContext, token.Substring("context:".Length));
                    if (!KnowledgeTokens.IsSentinelValue(id))
                        candidates.Add(Candidate(token, route.subjectKind, id,
                            FallbackLabel(signal, route.fallbackLabelSource, ownerId)));
                }
                else if (token.StartsWith("extra_participant:", StringComparison.Ordinal)
                    && signal.extraParticipants != null)
                {
                    for (int j = 0; j < signal.extraParticipants.Count; j++)
                    {
                        KnowledgeParticipant participant = signal.extraParticipants[j];
                        if (participant != null && !string.IsNullOrWhiteSpace(participant.pawnId))
                            candidates.Add(Candidate(token, route.subjectKind,
                                participant.pawnId, participant.name));
                    }
                }
            }
            return candidates;
        }

        private static MemoryRouteCandidate Candidate(
            string extractor, string kind, string id, string label)
        {
            return new MemoryRouteCandidate
            {
                extractorToken = extractor,
                subjectKind = kind ?? string.Empty,
                subjectId = (id ?? string.Empty).Trim(),
                frozenLabel = (label ?? string.Empty).Trim()
            };
        }

        private static string FallbackLabel(
            KnowledgeCaptureSignal signal, string source, string ownerId)
        {
            string token = source ?? string.Empty;
            if (token == "counterpart_name")
            {
                string ignored;
                string label;
                Counterpart(signal, ownerId, out ignored, out label);
                return label;
            }
            if (token == "owner") return OwnerName(signal, ownerId);
            if (token.StartsWith("context:", StringComparison.Ordinal))
            {
                string value = DiaryContextFields.Value(
                    signal.gameContext, token.Substring("context:".Length));
                return KnowledgeTokens.IsSentinelValue(value) ? string.Empty : value.Trim();
            }
            return string.Empty;
        }

        private static void Counterpart(
            KnowledgeCaptureSignal signal, string ownerId, out string id, out string label)
        {
            if (string.Equals(ownerId, signal.initiatorPawnId, StringComparison.Ordinal))
            {
                id = (signal.recipientPawnId ?? string.Empty).Trim();
                label = (signal.recipientName ?? string.Empty).Trim();
            }
            else if (string.Equals(ownerId, signal.recipientPawnId, StringComparison.Ordinal))
            {
                id = (signal.initiatorPawnId ?? string.Empty).Trim();
                label = (signal.initiatorName ?? string.Empty).Trim();
            }
            else
            {
                id = string.Empty;
                label = string.Empty;
            }
        }

        private static string OwnerName(KnowledgeCaptureSignal signal, string ownerId)
        {
            if (string.Equals(ownerId, signal.initiatorPawnId, StringComparison.Ordinal))
                return (signal.initiatorName ?? string.Empty).Trim();
            if (string.Equals(ownerId, signal.recipientPawnId, StringComparison.Ordinal))
                return (signal.recipientName ?? string.Empty).Trim();
            return string.Empty;
        }

        private static void SelectStandaloneCanonicalSubject(
            KnowledgeCaptureSignal signal,
            bool ruleHasNoRoute,
            string ownerId,
            out string kind,
            out string id,
            out string label)
        {
            kind = MemoryContractTokens.SubjectPawn;
            id = ownerId;
            label = OwnerName(signal, ownerId);
            if (!ruleHasNoRoute) return;

            List<KnowledgeParticipant> distinct = ExactOtherParticipants(signal, ownerId);
            if (distinct.Count != 1) return;
            id = distinct[0].pawnId;
            label = distinct[0].name;
        }

        private static List<KnowledgeParticipant> ExactOtherParticipants(
            KnowledgeCaptureSignal signal, string ownerId)
        {
            List<KnowledgeParticipant> result = new List<KnowledgeParticipant>();
            AddExactParticipant(result, signal.initiatorPawnId, signal.initiatorName, ownerId);
            AddExactParticipant(result, signal.recipientPawnId, signal.recipientName, ownerId);
            if (signal.extraParticipants != null)
                for (int i = 0; i < signal.extraParticipants.Count; i++)
                    AddExactParticipant(result, signal.extraParticipants[i]?.pawnId,
                        signal.extraParticipants[i]?.name, ownerId);
            result.Sort((left, right) => string.CompareOrdinal(left.pawnId, right.pawnId));
            return result;
        }

        private static void AddExactParticipant(
            List<KnowledgeParticipant> target, string id, string label, string ownerId)
        {
            string clean = (id ?? string.Empty).Trim();
            if (clean.Length == 0 || clean == ownerId
                || target.Exists(row => row.pawnId == clean)) return;
            target.Add(new KnowledgeParticipant
            {
                pawnId = clean,
                name = (label ?? string.Empty).Trim()
            });
        }

        private static FactualMemorySubjectDraft CreateSubject(
            string kind, string id, string label, string role, string knownness)
        {
            string subjectRefId;
            if (!MemoryIdentityCodec.TryCreateSubjectRefId(
                    kind, id, role, knownness, out subjectRefId)) return null;
            return new FactualMemorySubjectDraft
            {
                subjectRefId = subjectRefId,
                subjectKind = kind,
                subjectId = id,
                frozenLabel = label ?? string.Empty,
                roleToken = role,
                knownnessToken = knownness
            };
        }

        private static void AddSecondarySubjects(
            FactualMemoryDraft draft, KnowledgeCaptureSignal signal, string ownerId)
        {
            List<KnowledgeParticipant> participants = ExactOtherParticipants(signal, ownerId);
            for (int i = 0; i < participants.Count && draft.secondarySubjects.Count < 8; i++)
            {
                KnowledgeParticipant participant = participants[i];
                if (draft.primarySubject.subjectKind == MemoryContractTokens.SubjectPawn
                    && draft.primarySubject.subjectId == participant.pawnId) continue;
                FactualMemorySubjectDraft subject = CreateSubject(
                    MemoryContractTokens.SubjectPawn, participant.pawnId,
                    participant.name, "participant", "direct");
                if (subject != null) draft.secondarySubjects.Add(subject);
            }
        }

        private static List<MemoryTypedSubject> OccurrenceSubjects(
            KnowledgeCaptureSignal signal,
            List<MemoryRouteCandidate> routeCandidates,
            string ownerId,
            string canonicalKind,
            string canonicalId)
        {
            List<MemoryTypedSubject> result = new List<MemoryTypedSubject>();
            AddOccurrenceSubject(result, MemoryContractTokens.SubjectPawn, ownerId);
            List<KnowledgeParticipant> participants = ExactOtherParticipants(signal, ownerId);
            for (int i = 0; i < participants.Count; i++)
                AddOccurrenceSubject(result, MemoryContractTokens.SubjectPawn, participants[i].pawnId);
            for (int i = 0; i < routeCandidates.Count; i++)
                AddOccurrenceSubject(result, routeCandidates[i].subjectKind, routeCandidates[i].subjectId);
            AddOccurrenceSubject(result, canonicalKind, canonicalId);
            return result;
        }

        private static void AddOccurrenceSubject(
            List<MemoryTypedSubject> target, string kind, string id)
        {
            if (!MemoryContractTokens.IsValidRootSubject(kind, id)) return;
            target.Add(new MemoryTypedSubject { subjectKind = kind, subjectId = id });
        }

        private static string StableSignalToken(KnowledgeCaptureSignal signal)
        {
            return ((signal.signal ?? string.Empty).Trim() + "."
                + (signal.defName ?? string.Empty).Trim()).Trim('.');
        }

        private static string FirstFactKind(ImportantEventRule rule)
        {
            return rule?.memoryFacts != null && rule.memoryFacts.Count > 0
                ? rule.memoryFacts[0]?.factKind ?? string.Empty
                : string.Empty;
        }

        private static void AddContextParticipants(ImportantMemoryRecordSnapshot record,
            KnowledgeCaptureSignal signal, ImportantEventRule rule, string ownerId)
        {
            if (rule.participantKeyRules == null)
            {
                return;
            }

            for (int i = 0; i < rule.participantKeyRules.Count; i++)
            {
                KnowledgeParticipantKeyRule participantRule = rule.participantKeyRules[i];
                if (participantRule == null || string.IsNullOrWhiteSpace(participantRule.contextKey))
                {
                    continue;
                }

                string pawnId = DiaryContextFields.Value(
                    signal.gameContext, participantRule.contextKey);
                if (KnowledgeTokens.IsSentinelValue(pawnId)
                    || string.Equals(pawnId, ownerId, StringComparison.OrdinalIgnoreCase)
                    || HasParticipant(record.participants, pawnId))
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(participantRule.nameContextKey)
                    ? string.Empty
                    : DiaryContextFields.Value(signal.gameContext, participantRule.nameContextKey);
                record.participants.Add(new KnowledgeParticipant
                {
                    pawnId = pawnId.Trim(),
                    name = KnowledgeTokens.IsSentinelValue(name) ? string.Empty : name.Trim()
                });
            }
        }

        /// <summary>The other diary-event POV becomes the record's first participant.</summary>
        private static void AddCounterpartParticipant(
            ImportantMemoryRecordSnapshot record, KnowledgeCaptureSignal signal, string ownerId)
        {
            string otherId;
            string otherName;
            if (string.Equals(ownerId, signal.initiatorPawnId, StringComparison.OrdinalIgnoreCase))
            {
                otherId = signal.recipientPawnId;
                otherName = signal.recipientName;
            }
            else
            {
                otherId = signal.initiatorPawnId;
                otherName = signal.initiatorName;
            }

            if (!string.IsNullOrWhiteSpace(otherId)
                && !string.Equals(otherId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                record.participants.Add(new KnowledgeParticipant
                {
                    pawnId = otherId.Trim(),
                    name = (otherName ?? string.Empty).Trim()
                });
            }
        }

        /// <summary>"prefix:value" with a blank prefix collapsing to just the value.</summary>
        public static string ComposeSubjectKey(string prefix, string value)
        {
            string cleanValue = (value ?? string.Empty).Trim();
            string cleanPrefix = (prefix ?? string.Empty).Trim();
            return cleanPrefix.Length == 0 ? cleanValue : cleanPrefix + ":" + cleanValue;
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> values, string target)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasParticipant(List<KnowledgeParticipant> participants, string pawnId)
        {
            for (int i = 0; i < participants.Count; i++)
            {
                if (string.Equals(
                    participants[i]?.pawnId, pawnId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
