// Prompt-footprint baselines and catalog guards (PROMPT_FOOTPRINT_OPTIMIZATION plan, P0).
//
// This file measures the complete shipped ENGLISH model-facing prompt catalog from the real
// repository XML — no RimWorld, no API, no live model — and renders one canonical deep-talk
// fixture through the production pure planner/assembler at Full/Balanced/Compact.
//
// What lives here:
//  * EnglishPromptCatalog — loads every English instructional prose block that can reach a model
//    (systems, finals, wrappers, speech tails, voice rules, event prompts, group instructions,
//    observed-condition/event-window/enchantment bundles, and other policy instructions).
//  * A generic English DefInjected <-> base Def XML synchronization guard: wherever the English
//    DefInjected tree carries a copy of base prompt policy, the copy must stay byte-identical.
//  * The canonical PairImportant deep-talk fixture (plan §5.1) rendered at all three context
//    detail levels with exact .NET string.Length accounting.
//  * Release-gated character caps: P1 (§5.2 systems/finals, §5.4 wrappers/tails), P2 (§5.3 voice
//    rules), P4 (§5.5 catalog + §5.1 canonical ceilings) add their assertions to RunAll as each
//    release lands. P0 prints baselines without applying the future caps to the old catalog.
//
// All size gates use exact char counts of the decoded XML text. No token estimates, no word
// counts (plan §5). Fixed wrapper/tail prose is measured with "{0}"/"{1}" placeholder tokens
// removed and without substituting pawn names (plan §5.4).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    /// <summary>
    /// Deterministic English prompt-footprint fixtures, drift guards, and catalog inventory.
    /// Called from Program.Main; returns the number of assertions it ran.
    /// </summary>
    internal static class PromptFootprintTests
    {
        private static int assertions;

        // Legacy world-lore primer opening removed before this plan started. It must never come
        // back through any composed canonical prompt (plan §2.1).
        private const string LegacyLorePrimerFragment = "Setting, unless supplied facts";

        public static int RunAll()
        {
            assertions = 0;
            EnglishPromptCatalog catalog = EnglishPromptCatalog.Load();

            TestEnglishDefInjectedCopiesStaySynchronized(catalog);
            TestCatalogInventoryCoversKnownPolicyOwners(catalog);
            TestVoiceWrapperAndSpeechTailContracts(catalog);
            TestCanonicalDeepTalkFixture(catalog);
            TestRecipientAndSoloSpeechPathsUseKeyedTails(catalog);
            TestNeutralAndTitleTemplatesDropVoice(catalog);
            TestP1SystemAndFinalFamilyCaps(catalog);
            TestP1WrapperAndTailCaps(catalog);
            TestP1SentenceCountAnchorsAgree(catalog);
            TestP1VoiceBlockSeparators(catalog);
            TestP1TemplateFieldRowsFrozen(catalog);
            PrintBaselineReport(catalog);

            Console.WriteLine("PromptFootprintTests passed " + assertions + " assertions.");
            return assertions;
        }

        // =========================================================================================
        // English DefInjected synchronization guard
        // =========================================================================================

        /// <summary>
        /// Every entry in the English DefInjected tree is a copy of a base Def XML value (RimWorld
        /// loads the DefInjected copy for English players, the base value for everyone else). If the
        /// two drift apart, English players and English-fallback players silently see different
        /// prompts. This walks EVERY English DefInjected entry and requires byte equality with the
        /// base element it targets.
        /// </summary>
        private static void TestEnglishDefInjectedCopiesStaySynchronized(EnglishPromptCatalog catalog)
        {
            int checkedEntries = 0;
            List<string> problems = new List<string>();
            foreach (KeyValuePair<string, string> entry in catalog.EnglishDefInjected)
            {
                string key = entry.Key;
                string[] segments = key.Split('.');
                if (segments.Length < 2)
                {
                    continue;
                }

                XElement def;
                if (!catalog.DefsByName.TryGetValue(segments[0], out def))
                {
                    problems.Add("DefInjected entry targets unknown def: " + key);
                    continue;
                }

                XElement node = def;
                bool resolved = true;
                for (int i = 1; i < segments.Length; i++)
                {
                    string segment = segments[i];
                    int index;
                    if (int.TryParse(segment, out index))
                    {
                        List<XElement> items = node.Elements("li").ToList();
                        if (index < 0 || index >= items.Count)
                        {
                            problems.Add("DefInjected entry targets missing list row: " + key);
                            resolved = false;
                            break;
                        }

                        node = items[index];
                    }
                    else
                    {
                        XElement child = node.Element(segment);
                        if (child == null)
                        {
                            problems.Add("DefInjected entry targets missing element: " + key);
                            resolved = false;
                            break;
                        }

                        node = child;
                    }
                }

                if (!resolved)
                {
                    continue;
                }

                checkedEntries++;
                if (!string.Equals(node.Value, entry.Value, StringComparison.Ordinal))
                {
                    problems.Add("English DefInjected drifted from base XML: " + key);
                }
            }

            AssertTrue("English DefInjected sync guard walked a real entry set (" + checkedEntries + ")",
                checkedEntries > 500);
            AssertTrue(
                "English DefInjected copies are byte-identical to base Def XML"
                + (problems.Count > 0 ? ":\n  " + string.Join("\n  ", problems.Take(20).ToArray()) : string.Empty),
                problems.Count == 0);
        }

        // =========================================================================================
        // Inventory coverage
        // =========================================================================================

        private static void TestCatalogInventoryCoversKnownPolicyOwners(EnglishPromptCatalog catalog)
        {
            // Every known model-facing policy owner must be present in the inventory, so a future
            // release cannot silently drop a source from measurement (plan §4.2/§6.3).
            AssertEntry(catalog, "system", "Diary_Prompts.systemPrompt");
            AssertEntry(catalog, "system", "Diary_Prompts.systemPromptReflection");
            AssertEntry(catalog, "system", "Diary_Prompts.systemPromptNeutral");
            AssertEntry(catalog, "system", "Diary_Prompts.titleSystemPrompt");
            AssertEntry(catalog, "system", "DiaryPromptTemplate_PairImportant.systemPrompt");
            AssertEntry(catalog, "system", "DiaryPromptTemplate_PairCombat.systemPrompt");
            AssertEntry(catalog, "system", "DiaryPromptTemplate_SoloImportant.systemPrompt");
            AssertEntry(catalog, "system", "DiaryPromptTemplate_SoloQuadrumReflection.systemPrompt");
            AssertEntry(catalog, "system", "DiaryPromptTemplate_SoloArcReflection.systemPrompt");

            AssertEntry(catalog, "final", "Diary_Prompts.singlePovInstruction");
            AssertEntry(catalog, "final", "Diary_Prompts.recipientFollowupInstruction");
            AssertEntry(catalog, "final", "Diary_Prompts.deathDescriptionInstruction");
            AssertEntry(catalog, "final", "Diary_Prompts.arrivalDescriptionInstruction");
            AssertEntry(catalog, "final", "Diary_Prompts.titleUserInstruction");
            AssertEntry(catalog, "final", "DiaryPromptTemplate_PairImportant.finalInstruction");
            AssertEntry(catalog, "final", "DiaryPromptTemplate_PairImportant.recipientFinalInstruction");
            AssertEntry(catalog, "final", "DiaryPromptTemplate_SoloQuadrumReflection.finalInstruction");
            AssertEntry(catalog, "final", "DiaryPromptTemplate_SoloArcReflection.finalInstruction");

            AssertEntry(catalog, "wrapper", "PawnDiary.Prompt.PsychotypeLens");
            AssertEntry(catalog, "wrapper", "PawnDiary.Prompt.PersonaVoice");
            AssertEntry(catalog, "wrapper", "PawnDiary.Prompt.HumorVoice");
            AssertEntry(catalog, "speechTail", "PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator");
            AssertEntry(catalog, "speechTail", "PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient");
            AssertEntry(catalog, "speechTail", "PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction");

            AssertEntry(catalog, "psychotypeRule", "DiaryPsychotype_Dutiful");
            AssertEntry(catalog, "styleRule", "DiaryPersona_InhumanizedVoid");
            AssertEntry(catalog, "styleRule", "DiaryPersona_TraumaSavantSilent");
            AssertEntry(catalog, "humorRule", "DiaryHumorCue_UnderstatementCoda");

            AssertTrue("inventory carries the full psychotype catalog",
                catalog.Entries.Count(e => e.category == "psychotypeRule") >= 20);
            AssertTrue("inventory carries the full writing-style catalog",
                catalog.Entries.Count(e => e.category == "styleRule") >= 45);
            AssertTrue("inventory carries the full humor-cue catalog",
                catalog.Entries.Count(e => e.category == "humorRule") >= 18);
            AssertTrue("inventory carries the event prompt catalog",
                catalog.Entries.Count(e => e.category == "eventPrompt" || e.category == "eventEnhancement") >= 50);
            AssertTrue("inventory carries the interaction-group instruction catalog",
                catalog.Entries.Count(e => e.category == "groupInstruction") >= 60);
            AssertTrue("inventory carries the group tone catalog",
                catalog.Entries.Count(e => e.category == "groupTone") >= 60);
            AssertTrue("inventory carries observed-condition bundles",
                catalog.Entries.Count(e => e.category == "observedConditionBundle") >= 20);
            AssertTrue("inventory carries prompt-enabled event-window bundles",
                catalog.Entries.Count(e => e.category == "eventWindowBundle") >= 4);
            AssertTrue("inventory carries health-enchantment prompt texts",
                catalog.Entries.Count(e => e.category == "enchantmentText") >= 25);

            AssertEntry(catalog, "observedConditionInstruction", "AnomalyGrayFleshEvidence");
            AssertEntry(catalog, "observedConditionBundle", "AnomalyGrayFleshEvidence");
            AssertEntry(catalog, "policyInstruction", "Diary_NarrativeContinuity.promptFieldInstruction");
            AssertEntry(catalog, "policyInstruction", "Diary_Memory.memoryContextInstruction");
            AssertEntry(catalog, "policyInstruction", "Diary_BeliefPolicy.promptFieldInstruction");
            AssertEntry(catalog, "policyInstruction", "Diary_AuthoritySpeechPolicy.speakerPromptInstruction");
            AssertEntry(catalog, "policyInstruction", "Diary_AuthoritySpeechPolicy.witnessPromptInstruction");
            AssertEntry(catalog, "policyInstruction", "PawnDiary.Event.DayReflectionInstruction");
            AssertEntry(catalog, "policyInstruction", "PawnDiary.Event.QuadrumReflectionInstruction");
        }

        private static void AssertEntry(EnglishPromptCatalog catalog, string category, string id)
        {
            AssertTrue("catalog inventory includes " + category + " entry " + id,
                catalog.Entries.Any(e => e.category == category && e.id == id));
        }

        // =========================================================================================
        // Wrapper / speech-tail structural contracts
        // =========================================================================================

        private static void TestVoiceWrapperAndSpeechTailContracts(EnglishPromptCatalog catalog)
        {
            // Voice wrappers take exactly one {0} (the raw rule) and never a {1}.
            foreach (string key in new[]
            {
                "PawnDiary.Prompt.PsychotypeLens",
                "PawnDiary.Prompt.PersonaVoice",
                "PawnDiary.Prompt.HumorVoice"
            })
            {
                string wrapper = catalog.Keyed(key);
                AssertTrue(key + " exists in English Keyed", !string.IsNullOrWhiteSpace(wrapper));
                AssertEqual(key + " has exactly one {0} placeholder", 1, CountToken(wrapper, "{0}"));
                AssertEqual(key + " has no {1} placeholder", 0, CountToken(wrapper, "{1}"));
            }

            // Pair tails address {0}=POV pawn and {1}=the other pawn; the solo tail only {0}.
            string initiator = catalog.Keyed("PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator");
            string recipient = catalog.Keyed("PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient");
            string solo = catalog.Keyed("PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction");
            AssertTrue("initiator tail uses {0} and {1}",
                CountToken(initiator, "{0}") >= 1 && CountToken(initiator, "{1}") >= 1);
            AssertTrue("recipient tail uses {0} and {1}",
                CountToken(recipient, "{0}") >= 1 && CountToken(recipient, "{1}") >= 1);
            AssertTrue("solo tail uses {0} only",
                CountToken(solo, "{0}") >= 1 && CountToken(solo, "{1}") == 0);

            // Exact marker vocabulary: DiaryDirectSpeechParser depends on these tokens verbatim.
            AssertTrue("initiator tail teaches the exact speech markers",
                initiator.Contains("[[speech]]") && initiator.Contains("[[/speech]]"));
            AssertTrue("solo tail teaches the exact speech markers",
                solo.Contains("[[speech]]") && solo.Contains("[[/speech]]"));
            AssertTrue("recipient tail forbids speech blocks by naming the marker",
                recipient.Contains("[[speech]]"));
        }

        // =========================================================================================
        // Canonical deep-talk fixture (plan §5.1)
        // =========================================================================================

        private static void TestCanonicalDeepTalkFixture(EnglishPromptCatalog catalog)
        {
            CanonicalPlans plans = BuildCanonicalPlans(catalog);
            DiaryPromptPlan full = plans.full;
            DiaryPromptPlan balanced = plans.balanced;
            DiaryPromptPlan compact = plans.compact;

            AssertEqual("canonical fixture selects PairImportant", DiaryPipelineTemplates.PairImportant,
                full.templateKey);

            // The system prompt is the XML important system + one blank line + the combined voice
            // block, exactly as PromptAssembler.ComposeSystem produces it.
            string importantSystem = catalog.TemplateSystemPrompt(DiaryPipelineTemplates.PairImportant);
            AssertEqual("canonical system = important system + blank line + voice block",
                importantSystem.TrimEnd() + "\n\n" + plans.fullVoiceBlock, full.systemPrompt);

            // Voice order stays psychotype -> writing style -> humor (plan §3.3.7).
            int psychotypeAt = full.systemPrompt.IndexOf(WrapperAnchor(catalog, "PawnDiary.Prompt.PsychotypeLens"), StringComparison.Ordinal);
            int styleAt = full.systemPrompt.IndexOf(WrapperAnchor(catalog, "PawnDiary.Prompt.PersonaVoice"), StringComparison.Ordinal);
            int humorAt = full.systemPrompt.IndexOf(WrapperAnchor(catalog, "PawnDiary.Prompt.HumorVoice"), StringComparison.Ordinal);
            AssertTrue("voice order is psychotype, then style, then humor",
                psychotypeAt >= 0 && styleAt > psychotypeAt && humorAt > styleAt);

            // The raw shipped rules reach the system prompt verbatim inside their wrappers.
            AssertContains("Dutiful psychotype rule reaches the system prompt", full.systemPrompt,
                catalog.PsychotypeRule("DiaryPsychotype_Dutiful"));
            AssertContains("void-silence style rule reaches the system prompt", full.systemPrompt,
                catalog.StyleRule("DiaryPersona_InhumanizedVoid"));
            AssertContains("understatement humor cue reaches the system prompt", full.systemPrompt,
                catalog.HumorRule("DiaryHumorCue_UnderstatementCoda"));

            // Core user-prompt facts survive at every level.
            foreach (DiaryPromptPlan plan in new[] { full, balanced, compact })
            {
                string label = plan.contextSelectionReport.level.ToString();
                AssertContains(label + " keeps the event noun", plan.userPrompt, "event: deep talk");
                AssertContains(label + " keeps the POV pawn", plan.userPrompt, "pov: Vlad");
                AssertContains(label + " keeps the partner", plan.userPrompt, "with: Ia");
                AssertContains(label + " keeps the supplied fact", plan.userPrompt,
                    "what you saw: " + CanonicalFacts.PovText);
                AssertContains(label + " keeps the group instruction", plan.userPrompt,
                    "instruction: " + plans.groupInstruction);
                AssertContains(label + " ends on the final instruction plus initiator speech tail",
                    plan.userPrompt, plans.finalInstruction + " " + plans.initiatorTail);
                AssertTrue(label + " canonical prompts carry no legacy lore primer",
                    !plan.systemPrompt.Contains(LegacyLorePrimerFragment)
                    && !plan.userPrompt.Contains(LegacyLorePrimerFragment));
            }

            // The gray-flesh suspicion line rides the "important context" field at Full.
            AssertContains("Full keeps the gray-flesh important context", full.userPrompt,
                "important context: " + plans.grayFleshBundle);

            // P0 baseline: continuity duplicates are still rendered twice (lastOpener AND the
            // identical previousEntryEnding). P3 flips this to exact-duplicate suppression.
            AssertContains("Full renders the last opener", full.userPrompt, CanonicalFacts.LastOpener);
            AssertContains("Full still renders the duplicate previous ending (pre-P3 baseline)",
                full.userPrompt,
                "how my previous entry ended (continuity; do not retell it): " + CanonicalFacts.PreviousEntryEnding);

            // Size ordering must hold at every stage of the plan (plan §5.1).
            int fullChars = Combined(full);
            int balancedChars = Combined(balanced);
            int compactChars = Combined(compact);
            AssertTrue("canonical Compact <= Balanced <= Full ("
                + compactChars + " <= " + balancedChars + " <= " + fullChars + ")",
                compactChars <= balancedChars && balancedChars <= fullChars);
        }

        /// <summary>Recipient and solo interaction paths compose their own Keyed tails.</summary>
        private static void TestRecipientAndSoloSpeechPathsUseKeyedTails(EnglishPromptCatalog catalog)
        {
            // Recipient second-pass request: prior initiator entry present -> recipient final + tail.
            DiaryPromptRequest recipientRequest = CanonicalRequest(catalog, PromptContextDetailLevel.Full);
            recipientRequest.povRole = DiaryPipelineRoles.Recipient;
            recipientRequest.priorInitiatorEntry = "A private line Ia never read.";
            string recipientTail = Format(catalog.Keyed("PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient"),
                CanonicalFacts.Recipient, CanonicalFacts.Initiator);
            recipientRequest.directSpeechInstruction = recipientTail;
            DiaryPromptPlan recipientPlan = DiaryPromptPlanner.Build(recipientRequest);
            string recipientFinal = catalog.TemplateRecipientFinalInstruction(DiaryPipelineTemplates.PairImportant);
            AssertContains("recipient plan ends on recipient final + Keyed recipient tail",
                recipientPlan.userPrompt, recipientFinal + " " + recipientTail);

            // Solo interaction: the solo Keyed tail rides the ordinary final.
            DiaryPromptRequest soloRequest = CanonicalRequest(catalog, PromptContextDetailLevel.Full);
            soloRequest.payload.solo = true;
            soloRequest.payload.eventNoun = "quiet prayer";
            string soloTail = Format(catalog.Keyed("PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction"),
                CanonicalFacts.Initiator);
            soloRequest.directSpeechInstruction = soloTail;
            DiaryPromptPlan soloPlan = DiaryPromptPlanner.Build(soloRequest);
            AssertEqual("solo canonical fixture selects SoloImportant", DiaryPipelineTemplates.SoloImportant,
                soloPlan.templateKey);
            AssertContains("solo plan ends on its final + Keyed solo tail", soloPlan.userPrompt,
                catalog.TemplateFinalInstruction(DiaryPipelineTemplates.SoloImportant) + " " + soloTail);
        }

        /// <summary>Neutral death/arrival and title templates suppress the whole voice block.</summary>
        private static void TestNeutralAndTitleTemplatesDropVoice(EnglishPromptCatalog catalog)
        {
            string voiceBlock = BuildVoiceBlock(catalog, true);

            DiaryPromptRequest deathRequest = CanonicalRequest(catalog, PromptContextDetailLevel.Full);
            deathRequest.payload.hasDeathDescription = true;
            deathRequest.payload.neutralText = "Vlad died of a gunshot.";
            deathRequest.personaVoiceBlock = voiceBlock;
            deathRequest.directSpeechInstruction = string.Empty;
            DiaryPromptPlan deathPlan = DiaryPromptPlanner.Build(deathRequest);
            AssertEqual("death template selected", DiaryPipelineTemplates.DeathDescription, deathPlan.templateKey);
            AssertEqual("neutral death system carries no voice block",
                catalog.TemplateSystemPrompt(DiaryPipelineTemplates.DeathDescription), deathPlan.systemPrompt);

            DiaryPromptRequest titleRequest = CanonicalRequest(catalog, PromptContextDetailLevel.Full);
            titleRequest.titleRequest = true;
            titleRequest.entryText = "The frost held and we talked history until the lamp died.";
            titleRequest.personaVoiceBlock = voiceBlock;
            titleRequest.directSpeechInstruction = string.Empty;
            DiaryPromptPlan titlePlan = DiaryPromptPlanner.Build(titleRequest);
            AssertEqual("title template selected", DiaryPipelineTemplates.Title, titlePlan.templateKey);
            AssertEqual("title system carries no voice block",
                catalog.TemplateSystemPrompt(DiaryPipelineTemplates.Title), titlePlan.systemPrompt);
            AssertContains("title user prompt carries the entry", titlePlan.userPrompt,
                "diary entry to title: " + titleRequest.entryText);
            AssertContains("title user prompt ends on the title instruction", titlePlan.userPrompt,
                catalog.TemplateFinalInstruction(DiaryPipelineTemplates.Title));
        }

        // =========================================================================================
        // P1 — system/final family caps (plan §5.2) and fixed wrapper/tail caps (plan §5.4)
        // =========================================================================================

        /// <summary>Every shipped English system prompt and final instruction meets its family cap.</summary>
        private static void TestP1SystemAndFinalFamilyCaps(EnglishPromptCatalog catalog)
        {
            AssertCap(catalog, "ordinary system", 750, catalog.PromptDefText("systemPrompt"));
            foreach (string key in new[]
            {
                DiaryPipelineTemplates.PairImportant,
                DiaryPipelineTemplates.PairCombat,
                DiaryPipelineTemplates.SoloImportant
            })
            {
                AssertCap(catalog, key + " system", 850, catalog.TemplateSystemPrompt(key));
            }

            AssertCap(catalog, "day-reflection system", 600, catalog.PromptDefText("systemPromptReflection"));
            AssertCap(catalog, "quadrum-reflection system", 550,
                catalog.TemplateSystemPrompt(DiaryPipelineTemplates.SoloQuadrumReflection));
            AssertCap(catalog, "arc-reflection system", 550,
                catalog.TemplateSystemPrompt(DiaryPipelineTemplates.SoloArcReflection));
            AssertCap(catalog, "neutral system", 220, catalog.PromptDefText("systemPromptNeutral"));
            AssertCap(catalog, "title system", 220, catalog.PromptDefText("titleSystemPrompt"));

            AssertCap(catalog, "ordinary final", 100, catalog.PromptDefText("singlePovInstruction"));
            foreach (string key in new[]
            {
                DiaryPipelineTemplates.PairImportant,
                DiaryPipelineTemplates.PairCombat,
                DiaryPipelineTemplates.SoloImportant
            })
            {
                AssertCap(catalog, key + " final", 120, catalog.TemplateFinalInstruction(key));
            }

            AssertCap(catalog, "ordinary recipient final", 200, catalog.PromptDefText("recipientFollowupInstruction"));
            AssertCap(catalog, "PairImportant recipient final", 200,
                catalog.TemplateRecipientFinalInstruction(DiaryPipelineTemplates.PairImportant));
            AssertCap(catalog, "PairCombat recipient final", 200,
                catalog.TemplateRecipientFinalInstruction(DiaryPipelineTemplates.PairCombat));
            AssertCap(catalog, "death final", 250, catalog.PromptDefText("deathDescriptionInstruction"));
            AssertCap(catalog, "arrival final", 330, catalog.PromptDefText("arrivalDescriptionInstruction"));
            AssertCap(catalog, "quadrum-reflection final", 200,
                catalog.TemplateFinalInstruction(DiaryPipelineTemplates.SoloQuadrumReflection));
            AssertCap(catalog, "arc-reflection final", 200,
                catalog.TemplateFinalInstruction(DiaryPipelineTemplates.SoloArcReflection));
            AssertCap(catalog, "title final", 150, catalog.PromptDefText("titleUserInstruction"));
        }

        /// <summary>Fixed wrapper/tail prose (placeholders removed) meets the §5.4 caps.</summary>
        private static void TestP1WrapperAndTailCaps(EnglishPromptCatalog catalog)
        {
            AssertFixedProseCap(catalog, "psychotype wrapper", 110, "PawnDiary.Prompt.PsychotypeLens");
            AssertFixedProseCap(catalog, "writing-style wrapper", 110, "PawnDiary.Prompt.PersonaVoice");
            AssertFixedProseCap(catalog, "humor wrapper", 110, "PawnDiary.Prompt.HumorVoice");
            AssertFixedProseCap(catalog, "pair-initiator speech tail", 160,
                "PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator");
            AssertFixedProseCap(catalog, "pair-recipient speech tail", 105,
                "PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient");
            AssertFixedProseCap(catalog, "solo speech tail", 150,
                "PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction");
        }

        /// <summary>
        /// The sentence-count anchors of each family's system and final must agree, because small
        /// models obey the last length instruction they read (plan §7.6).
        /// </summary>
        private static void TestP1SentenceCountAnchorsAgree(EnglishPromptCatalog catalog)
        {
            AssertContains("ordinary system anchors 1-3", catalog.PromptDefText("systemPrompt"), "1-3");
            AssertContains("ordinary final anchors 1-3", catalog.PromptDefText("singlePovInstruction"), "1-3");
            AssertContains("ordinary recipient final anchors 1-3",
                catalog.PromptDefText("recipientFollowupInstruction"), "1-3");
            foreach (string key in new[]
            {
                DiaryPipelineTemplates.PairImportant,
                DiaryPipelineTemplates.PairCombat,
                DiaryPipelineTemplates.SoloImportant
            })
            {
                AssertContains(key + " system anchors 2-5", catalog.TemplateSystemPrompt(key), "2-5");
                AssertContains(key + " final anchors 2-5", catalog.TemplateFinalInstruction(key), "2-5");
            }

            AssertContains("PairImportant recipient final anchors 2-5",
                catalog.TemplateRecipientFinalInstruction(DiaryPipelineTemplates.PairImportant), "2-5");
        }

        /// <summary>
        /// The voice block joins to the system with exactly one blank line, and blank voice parts
        /// never leave an empty paragraph behind.
        /// </summary>
        private static void TestP1VoiceBlockSeparators(EnglishPromptCatalog catalog)
        {
            DiaryPromptPlan full = DiaryPromptPlanner.Build(CanonicalRequest(catalog, PromptContextDetailLevel.Full));
            AssertTrue("composed system never contains an empty paragraph (no triple newline)",
                !full.systemPrompt.Contains("\n\n\n"));

            string noHumor = BuildVoiceBlock(catalog, false);
            AssertTrue("voice block without humor has no trailing separator",
                !noHumor.EndsWith("\n") && !noHumor.Contains("\n\n\n"));

            DiaryPromptRequest request = CanonicalRequest(catalog, PromptContextDetailLevel.Full);
            request.personaVoiceBlock = noHumor;
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(request);
            AssertTrue("system with humor-free voice block has no empty paragraph",
                !plan.systemPrompt.Contains("\n\n\n"));
        }

        /// <summary>
        /// Template field rows may not be removed or reordered (indexed DefInjected labels depend
        /// on their positions, plan §3.2). This freezes each template's row count and disabled-row
        /// indexes; appending new rows requires updating this snapshot deliberately.
        /// </summary>
        private static void TestP1TemplateFieldRowsFrozen(EnglishPromptCatalog catalog)
        {
            Dictionary<string, string> expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "PairDefault", "40|18" },
                { "PairImportant", "87|18" },
                { "PairCombat", "33|18" },
                { "PairBatched", "29|18" },
                { "SoloDefault", "57|32" },
                { "SoloImportant", "137|33" },
                { "SoloInternalState", "41|28" },
                { "SoloBatched", "41|28" },
                { "SoloDayReflection", "40|27" },
                { "SoloQuadrumReflection", "15|" },
                { "SoloArcReflection", "18|" },
                { "DeathDescription", "15|" },
                { "ArrivalDescription", "8|" },
                { "Title", "1|" }
            };

            DiaryPolicySnapshot snapshot = catalog.BuildPolicySnapshot();
            AssertEqual("frozen template count", expected.Count, snapshot.templates.Count);
            foreach (DiaryTemplatePolicy template in snapshot.templates)
            {
                List<string> disabled = new List<string>();
                for (int i = 0; i < template.fields.Count; i++)
                {
                    if (!template.fields[i].enabled)
                    {
                        disabled.Add(i.ToString());
                    }
                }

                string signature = template.fields.Count + "|" + string.Join(",", disabled.ToArray());
                string want;
                AssertTrue("template " + template.templateKey + " is a known frozen template",
                    expected.TryGetValue(template.templateKey, out want));
                AssertEqual("template " + template.templateKey + " field rows unchanged (count|disabled)",
                    want, signature);
            }
        }

        private static void AssertCap(EnglishPromptCatalog catalog, string name, int cap, string text)
        {
            AssertTrue(name + " has text", !string.IsNullOrWhiteSpace(text));
            AssertTrue(name + " within cap (" + text.Length + " <= " + cap + ")", text.Length <= cap);
        }

        private static void AssertFixedProseCap(EnglishPromptCatalog catalog, string name, int cap, string key)
        {
            string text = catalog.Keyed(key);
            AssertTrue(name + " has text", !string.IsNullOrWhiteSpace(text));
            string fixedProse = text.Replace("{0}", string.Empty).Replace("{1}", string.Empty);
            AssertTrue(name + " fixed prose within cap (" + fixedProse.Length + " <= " + cap + ")",
                fixedProse.Length <= cap);
        }

        // =========================================================================================
        // Canonical fixture assembly (plan §5.1)
        // =========================================================================================

        /// <summary>Fixed fixture values. Facts are runtime data, so wording here is arbitrary but frozen.</summary>
        private static class CanonicalFacts
        {
            public const string Initiator = "Vlad";
            public const string Recipient = "Ia";
            public const string PovText = "Vlad exchanged ideas about history with Ia.";
            public const string Setting =
                "outdoors, hideous surroundings, chunk of granite, wooden stool, doing: tending Sunny";
            public const string Relationship = "Ia: neutral";
            public const string MemoryRecall =
                "The long talk with Ia about history stayed with me; trading ideas felt like the room got warmer.";
            public const string LastOpener = "The frost held through the morning.";
            // Deliberately identical to LastOpener: the P3 release suppresses this duplicate at
            // projection time; until then it renders twice and the fixture pins that baseline.
            public const string PreviousEntryEnding = "The frost held through the morning.";
        }

        private sealed class CanonicalPlans
        {
            public DiaryPromptPlan full;
            public DiaryPromptPlan balanced;
            public DiaryPromptPlan compact;
            public string fullVoiceBlock;
            public string groupInstruction;
            public string finalInstruction;
            public string initiatorTail;
            public string grayFleshBundle;
        }

        private static CanonicalPlans BuildCanonicalPlans(EnglishPromptCatalog catalog)
        {
            CanonicalPlans plans = new CanonicalPlans();
            plans.fullVoiceBlock = BuildVoiceBlock(catalog, true);
            plans.groupInstruction = catalog.DeepTalkGroupInstructionVariant(1);
            plans.finalInstruction = catalog.TemplateFinalInstruction(DiaryPipelineTemplates.PairImportant);
            plans.initiatorTail = Format(
                catalog.Keyed("PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator"),
                CanonicalFacts.Initiator, CanonicalFacts.Recipient);
            plans.grayFleshBundle = catalog.ObservedConditionBundle("AnomalyGrayFleshEvidence");

            plans.full = DiaryPromptPlanner.Build(CanonicalRequest(catalog, PromptContextDetailLevel.Full));
            plans.balanced = DiaryPromptPlanner.Build(CanonicalRequest(catalog, PromptContextDetailLevel.Balanced));
            plans.compact = DiaryPromptPlanner.Build(CanonicalRequest(catalog, PromptContextDetailLevel.Compact));
            return plans;
        }

        /// <summary>
        /// Builds the plain request the impure adapter would produce for the canonical deep talk,
        /// using only real English XML text plus frozen fixture facts. Mirrors
        /// DiaryPipelineAdapters.BuildPromptRequest's composition contract (voice block, tails,
        /// budgets) without touching RimWorld.
        /// </summary>
        private static DiaryPromptRequest CanonicalRequest(EnglishPromptCatalog catalog, PromptContextDetailLevel level)
        {
            DiaryEventPayload payload = new DiaryEventPayload
            {
                eventId = "fixture-deep-talk",
                defName = "DeepTalk",
                label = "deep talk",
                eventNoun = "deep talk",
                domain = "Interaction",
                solo = false,
                gameContext = string.Empty,
                instruction = catalog.DeepTalkGroupInstructionVariant(1),
                supportsDirectSpeechInstruction = true
            };
            payload.initiator.name = CanonicalFacts.Initiator;
            payload.initiator.rawText = CanonicalFacts.PovText;
            payload.initiator.surroundings = CanonicalFacts.Setting;
            payload.initiator.continuity = CanonicalFacts.Relationship;
            payload.initiator.memoryContext = CanonicalFacts.MemoryRecall;
            payload.initiator.lastOpener = CanonicalFacts.LastOpener;
            payload.initiator.previousEntryEnding = CanonicalFacts.PreviousEntryEnding;
            payload.recipient.name = CanonicalFacts.Recipient;
            payload.recipient.rawText = "Ia listened and offered her own view of the old wars.";

            DiaryPolicySnapshot policy = catalog.BuildPolicySnapshot();
            policy.group = new DiaryGroupPolicy
            {
                defName = catalog.DeepTalkGroupDefName,
                domain = "Interaction",
                classifierKey = "DeepTalk",
                eventPromptKey = "Interaction",
                eventPrompt = catalog.EventPrompt("Interaction"),
                eventEnhancement = catalog.EventEnhancement("Interaction"),
                important = true,
                combat = false,
                tone = catalog.DeepTalkGroupToneVariant(0)
            };

            return new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = BuildVoiceBlock(catalog, true),
                promptEnchantment = catalog.ObservedConditionBundle("AnomalyGrayFleshEvidence"),
                directSpeechInstruction = Format(
                    catalog.Keyed("PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator"),
                    CanonicalFacts.Initiator, CanonicalFacts.Recipient),
                contextDetailLevel = level,
                contextBudgets = catalog.Budgets,
                maxTokens = 200
            };
        }

        /// <summary>
        /// Mirrors the adapter's voice-block composition: each Keyed wrapper takes the raw rule via
        /// "{0}" replacement (never args-Translate), blocks joined by one blank line in the order
        /// psychotype, writing style, humor. P2 makes humor level-dependent; the flag is here so the
        /// fixture can follow that production decision.
        /// </summary>
        private static string BuildVoiceBlock(EnglishPromptCatalog catalog, bool includeHumor)
        {
            List<string> parts = new List<string>();
            parts.Add(catalog.Keyed("PawnDiary.Prompt.PsychotypeLens")
                .Replace("{0}", catalog.PsychotypeRule("DiaryPsychotype_Dutiful").Trim()));
            parts.Add(catalog.Keyed("PawnDiary.Prompt.PersonaVoice")
                .Replace("{0}", catalog.StyleRule("DiaryPersona_InhumanizedVoid").Trim()));
            if (includeHumor)
            {
                parts.Add(catalog.Keyed("PawnDiary.Prompt.HumorVoice")
                    .Replace("{0}", catalog.HumorRule("DiaryHumorCue_UnderstatementCoda").Trim()));
            }

            return string.Join("\n\n", parts.ToArray());
        }

        /// <summary>Wrapper text before its {0} placeholder — a stable anchor for order checks.</summary>
        private static string WrapperAnchor(EnglishPromptCatalog catalog, string key)
        {
            string wrapper = catalog.Keyed(key);
            int at = wrapper.IndexOf("{0}", StringComparison.Ordinal);
            return at <= 0 ? wrapper : wrapper.Substring(0, at);
        }

        private static int Combined(DiaryPromptPlan plan)
        {
            return plan.systemPrompt.Length + plan.userPrompt.Length;
        }

        // =========================================================================================
        // Baseline report (plan §6.3): counts only, no caps applied at P0
        // =========================================================================================

        private static void PrintBaselineReport(EnglishPromptCatalog catalog)
        {
            Console.WriteLine("--- Prompt footprint baseline (exact chars, decoded XML) ---");
            foreach (IGrouping<string, CatalogEntry> group in catalog.Entries
                .GroupBy(e => e.category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int total = group.Sum(e => e.chars);
                CatalogEntry largest = group.OrderByDescending(e => e.chars).First();
                Console.WriteLine("  " + group.Key + ": entries=" + group.Count()
                    + " totalChars=" + total
                    + " largest=" + largest.id + " (" + largest.chars + ")");
            }

            CanonicalPlans plans = BuildCanonicalPlans(catalog);
            Console.WriteLine("  canonical PairImportant deep talk:"
                + " Full=" + Combined(plans.full)
                + " (sys=" + plans.full.systemPrompt.Length + ", user=" + plans.full.userPrompt.Length + ")"
                + " Balanced=" + Combined(plans.balanced)
                + " Compact=" + Combined(plans.compact));
            Console.WriteLine("--- end baseline ---");
        }

        // =========================================================================================
        // English catalog loader
        // =========================================================================================

        private sealed class CatalogEntry
        {
            public string file;
            public string id;
            public string category;
            public string text;
            public int chars;
        }

        /// <summary>
        /// The complete shipped English instructional catalog, loaded from real repository XML.
        /// Base Def XML is the documented single fixture source; the DefInjected sync guard keeps
        /// the English copies byte-identical, so reading base text here is not a silent mix.
        /// </summary>
        private sealed class EnglishPromptCatalog
        {
            public readonly List<CatalogEntry> Entries = new List<CatalogEntry>();
            public readonly Dictionary<string, XElement> DefsByName =
                new Dictionary<string, XElement>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> EnglishDefInjected =
                new Dictionary<string, string>(StringComparer.Ordinal);
            public PromptContextBudgets Budgets;
            public string DeepTalkGroupDefName;

            private readonly Dictionary<string, string> keyed =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> observedBundles =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private XElement promptDef;
            private readonly Dictionary<string, XElement> templatesByKey =
                new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            private XElement deepTalkGroup;
            private readonly Dictionary<string, XElement> eventPromptsByType =
                new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

            public static EnglishPromptCatalog Load()
            {
                EnglishPromptCatalog catalog = new EnglishPromptCatalog();
                string root = RepoRoot();

                catalog.LoadDefsTree(Path.Combine(root, "1.6", "Defs"));
                catalog.LoadKeyed(Path.Combine(root, "Languages", "English", "Keyed", "PawnDiary.xml"));
                catalog.LoadEnglishDefInjected(Path.Combine(root, "Languages", "English", "DefInjected"));
                catalog.CollectEntries();
                return catalog;
            }

            // -------------------------------------------------------------------- loading helpers

            private void LoadDefsTree(string defsRoot)
            {
                foreach (string file in Directory.GetFiles(defsRoot, "*.xml", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal))
                {
                    XDocument document = XDocument.Load(file);
                    if (document.Root == null)
                    {
                        continue;
                    }

                    foreach (XElement def in document.Root.Elements())
                    {
                        string defName = def.Element("defName")?.Value;
                        if (string.IsNullOrWhiteSpace(defName))
                        {
                            continue;
                        }

                        DefsByName[defName] = def;
                        string typeName = def.Name.LocalName;
                        if (typeName == "PawnDiary.DiaryPromptDef")
                        {
                            promptDef = def;
                        }
                        else if (typeName == "PawnDiary.DiaryPromptTemplateDef")
                        {
                            string key = def.Element("templateKey")?.Value;
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                templatesByKey[key] = def;
                            }
                        }
                        else if (typeName == "PawnDiary.DiaryEventPromptDef")
                        {
                            string type = def.Element("eventType")?.Value;
                            if (!string.IsNullOrWhiteSpace(type) && !eventPromptsByType.ContainsKey(type))
                            {
                                eventPromptsByType[type] = def;
                            }
                        }
                        else if (typeName == "PawnDiary.DiaryInteractionGroupDef" && deepTalkGroup == null)
                        {
                            bool matchesDeepTalk = def.Element("matchDefNames") != null
                                && def.Element("matchDefNames").Elements("li").Any(li => li.Value == "DeepTalk");
                            if (matchesDeepTalk
                                && string.Equals(def.Element("domain")?.Value, "Interaction", StringComparison.OrdinalIgnoreCase))
                            {
                                deepTalkGroup = def;
                                DeepTalkGroupDefName = defName;
                            }
                        }
                    }
                }

                Budgets = new PromptContextBudgets();
                XElement detail;
                if (DefsByName.TryGetValue("Diary_ContextDetail", out detail))
                {
                    Budgets.balancedDefault = IntOf(detail, "balancedDefaultBudget", Budgets.balancedDefault);
                    Budgets.compactDefault = IntOf(detail, "compactDefaultBudget", Budgets.compactDefault);
                    Budgets.balancedReflection = IntOf(detail, "balancedReflectionBudget", Budgets.balancedReflection);
                    Budgets.compactReflection = IntOf(detail, "compactReflectionBudget", Budgets.compactReflection);
                    Budgets.balancedNeutral = IntOf(detail, "balancedNeutralBudget", Budgets.balancedNeutral);
                    Budgets.compactNeutral = IntOf(detail, "compactNeutralBudget", Budgets.compactNeutral);
                }
            }

            private void LoadKeyed(string path)
            {
                XDocument document = XDocument.Load(path);
                if (document.Root == null)
                {
                    return;
                }

                foreach (XElement element in document.Root.Elements())
                {
                    keyed[element.Name.LocalName] = element.Value;
                }
            }

            private void LoadEnglishDefInjected(string root)
            {
                foreach (string file in Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal))
                {
                    XDocument document = XDocument.Load(file);
                    if (document.Root == null)
                    {
                        continue;
                    }

                    foreach (XElement element in document.Root.Elements())
                    {
                        EnglishDefInjected[element.Name.LocalName] = element.Value;
                    }
                }
            }

            // ------------------------------------------------------------------ resolution helpers

            public string Keyed(string key)
            {
                string value;
                return keyed.TryGetValue(key, out value) ? value : string.Empty;
            }

            public string PromptDefText(string field)
            {
                return promptDef?.Element(field)?.Value ?? string.Empty;
            }

            /// <summary>Mirrors DiaryPromptTemplates.SystemPromptFor with default settings.</summary>
            public string TemplateSystemPrompt(string templateKey)
            {
                string own = TemplateText(templateKey, "systemPrompt");
                if (!string.IsNullOrWhiteSpace(own))
                {
                    return own;
                }

                if (Eq(templateKey, DiaryPipelineTemplates.DeathDescription)
                    || Eq(templateKey, DiaryPipelineTemplates.ArrivalDescription))
                {
                    return PromptDefText("systemPromptNeutral");
                }

                if (Eq(templateKey, DiaryPipelineTemplates.SoloDayReflection)
                    || Eq(templateKey, DiaryPipelineTemplates.SoloQuadrumReflection)
                    || Eq(templateKey, DiaryPipelineTemplates.SoloArcReflection))
                {
                    return PromptDefText("systemPromptReflection");
                }

                if (Eq(templateKey, DiaryPipelineTemplates.Title))
                {
                    return PromptDefText("titleSystemPrompt");
                }

                return PromptDefText("systemPrompt");
            }

            /// <summary>Mirrors DiaryPromptTemplates.FinalInstructionFor.</summary>
            public string TemplateFinalInstruction(string templateKey)
            {
                string own = TemplateText(templateKey, "finalInstruction");
                if (!string.IsNullOrWhiteSpace(own))
                {
                    return own;
                }

                if (Eq(templateKey, DiaryPipelineTemplates.Title))
                {
                    return PromptDefText("titleUserInstruction");
                }

                if (Eq(templateKey, DiaryPipelineTemplates.DeathDescription))
                {
                    return PromptDefText("deathDescriptionInstruction");
                }

                if (Eq(templateKey, DiaryPipelineTemplates.ArrivalDescription))
                {
                    return PromptDefText("arrivalDescriptionInstruction");
                }

                return PromptDefText("singlePovInstruction");
            }

            /// <summary>Mirrors DiaryPromptTemplates.RecipientFinalInstruction.</summary>
            public string TemplateRecipientFinalInstruction(string templateKey)
            {
                string own = TemplateText(templateKey, "recipientFinalInstruction");
                return string.IsNullOrWhiteSpace(own) ? PromptDefText("recipientFollowupInstruction") : own;
            }

            private string TemplateText(string templateKey, string field)
            {
                XElement template;
                return templatesByKey.TryGetValue(templateKey, out template)
                    ? template.Element(field)?.Value ?? string.Empty
                    : string.Empty;
            }

            public string PsychotypeRule(string defName)
            {
                return DefText(defName, "rule");
            }

            public string StyleRule(string defName)
            {
                return DefText(defName, "rule");
            }

            public string HumorRule(string defName)
            {
                return DefText(defName, "rule");
            }

            public string EventPrompt(string eventType)
            {
                XElement def;
                return eventPromptsByType.TryGetValue(eventType, out def)
                    ? def.Element("prompt")?.Value ?? string.Empty
                    : string.Empty;
            }

            public string EventEnhancement(string eventType)
            {
                XElement def;
                return eventPromptsByType.TryGetValue(eventType, out def)
                    ? def.Element("enhancement")?.Value ?? string.Empty
                    : string.Empty;
            }

            public string DeepTalkGroupInstructionVariant(int index)
            {
                List<XElement> pool = deepTalkGroup?.Element("instructions")?.Elements("li").ToList();
                if (pool != null && index >= 0 && index < pool.Count)
                {
                    return pool[index].Value;
                }

                return deepTalkGroup?.Element("instruction")?.Value ?? string.Empty;
            }

            public string DeepTalkGroupToneVariant(int index)
            {
                List<XElement> pool = deepTalkGroup?.Element("tones")?.Elements("li").ToList();
                if (pool != null && index >= 0 && index < pool.Count)
                {
                    return pool[index].Value;
                }

                return deepTalkGroup?.Element("tone")?.Value ?? string.Empty;
            }

            public string ObservedConditionBundle(string defName)
            {
                string bundle;
                return observedBundles.TryGetValue(defName, out bundle) ? bundle : string.Empty;
            }

            private string DefText(string defName, string field)
            {
                XElement def;
                return DefsByName.TryGetValue(defName, out def)
                    ? def.Element(field)?.Value ?? string.Empty
                    : string.Empty;
            }

            /// <summary>All 14 templates as the plain policy snapshot the pure planner reads.</summary>
            public DiaryPolicySnapshot BuildPolicySnapshot()
            {
                DiaryPolicySnapshot snapshot = new DiaryPolicySnapshot
                {
                    narrativeContextFieldLabel = DefText("Diary_NarrativeContinuity", "promptFieldLabel"),
                    narrativeContextInstruction = DefText("Diary_NarrativeContinuity", "promptFieldInstruction"),
                    memoryContextInstruction = DefText("Diary_Memory", "memoryContextInstruction"),
                    beliefContextFieldLabel = DefText("Diary_BeliefPolicy", "promptFieldLabel"),
                    beliefContextInstruction = DefText("Diary_BeliefPolicy", "promptFieldInstruction")
                };

                foreach (KeyValuePair<string, XElement> pair in templatesByKey)
                {
                    DiaryTemplatePolicy template = new DiaryTemplatePolicy
                    {
                        templateKey = pair.Key,
                        systemPrompt = TemplateSystemPrompt(pair.Key),
                        finalInstruction = TemplateFinalInstruction(pair.Key),
                        recipientFinalInstruction = TemplateRecipientFinalInstruction(pair.Key),
                        includePromptEnchantment = BoolOf(pair.Value, "includePromptEnchantment", true),
                        includePersona = BoolOf(pair.Value, "includePersona", true),
                        appendDirectSpeechInstruction = BoolOf(pair.Value, "appendDirectSpeechInstruction", true),
                        maxTokens = IntOf(pair.Value, "maxTokens", 0)
                    };

                    XElement fields = pair.Value.Element("fields");
                    if (fields != null)
                    {
                        foreach (XElement li in fields.Elements("li"))
                        {
                            template.fields.Add(new DiaryPromptFieldPolicy
                            {
                                enabled = BoolOf(li, "enabled", true),
                                label = li.Element("label")?.Value,
                                source = li.Element("source")?.Value,
                                contextKey = li.Element("contextKey")?.Value
                            });
                        }
                    }

                    snapshot.templates.Add(template);
                }

                return snapshot;
            }

            // ------------------------------------------------------------------- entry collection

            private void CollectEntries()
            {
                // System prompts and finals owned by DiaryPromptDef.
                foreach (string field in new[]
                {
                    "systemPrompt", "systemPromptReflection", "systemPromptNeutral", "titleSystemPrompt"
                })
                {
                    AddEntry("1.6/Defs/DiaryPromptDef.xml", "Diary_Prompts." + field, "system", PromptDefText(field));
                }

                foreach (string field in new[]
                {
                    "singlePovInstruction", "recipientFollowupInstruction", "deathDescriptionInstruction",
                    "arrivalDescriptionInstruction", "titleUserInstruction"
                })
                {
                    AddEntry("1.6/Defs/DiaryPromptDef.xml", "Diary_Prompts." + field, "final", PromptDefText(field));
                }

                // Per-template overrides.
                foreach (KeyValuePair<string, XElement> pair in templatesByKey)
                {
                    string defName = pair.Value.Element("defName")?.Value ?? pair.Key;
                    AddOptionalEntry("1.6/Defs/DiaryPromptTemplateDefs.xml", defName + ".systemPrompt",
                        "system", pair.Value.Element("systemPrompt")?.Value);
                    AddOptionalEntry("1.6/Defs/DiaryPromptTemplateDefs.xml", defName + ".finalInstruction",
                        "final", pair.Value.Element("finalInstruction")?.Value);
                    AddOptionalEntry("1.6/Defs/DiaryPromptTemplateDefs.xml", defName + ".recipientFinalInstruction",
                        "final", pair.Value.Element("recipientFinalInstruction")?.Value);
                }

                // Fixed voice wrappers and direct-speech tails, measured without placeholder tokens.
                foreach (string key in new[]
                {
                    "PawnDiary.Prompt.PsychotypeLens", "PawnDiary.Prompt.PersonaVoice", "PawnDiary.Prompt.HumorVoice"
                })
                {
                    AddEntry("Languages/English/Keyed/PawnDiary.xml", key, "wrapper", StripPlaceholders(Keyed(key)));
                }

                foreach (string key in new[]
                {
                    "PawnDiary.Prompt.PairDirectSpeechInstruction.Initiator",
                    "PawnDiary.Prompt.PairDirectSpeechInstruction.Recipient",
                    "PawnDiary.Prompt.SoloInteractionDirectSpeechInstruction"
                })
                {
                    AddEntry("Languages/English/Keyed/PawnDiary.xml", key, "speechTail", StripPlaceholders(Keyed(key)));
                }

                // Voice rules.
                CollectDefRules("PawnDiary.DiaryPsychotypeDef", "psychotypeRule", "1.6/Defs/DiaryPsychotypeDefs.xml");
                CollectDefRules("PawnDiary.DiaryPersonaDef", "styleRule", "1.6/Defs/DiaryPersonaDefs.xml");
                CollectDefRules("PawnDiary.DiaryHumorCueDef", "humorRule", "1.6/Defs/DiaryHumorCueDefs.xml");

                // Event prompt policy, interaction groups, observed conditions, event windows.
                foreach (KeyValuePair<string, XElement> pair in DefsByName)
                {
                    XElement def = pair.Value;
                    string type = def.Name.LocalName;
                    if (type == "PawnDiary.DiaryEventPromptDef")
                    {
                        AddOptionalEntry("1.6/Defs/DiaryEventPromptDefs.xml", pair.Key + ".prompt", "eventPrompt",
                            def.Element("prompt")?.Value);
                        AddOptionalEntry("1.6/Defs/DiaryEventPromptDefs.xml", pair.Key + ".enhancement",
                            "eventEnhancement", def.Element("enhancement")?.Value);
                    }
                    else if (type == "PawnDiary.DiaryInteractionGroupDef")
                    {
                        AddOptionalEntry("1.6/Defs/DiaryInteractionGroupDefs.xml", pair.Key + ".instruction",
                            "groupInstruction", def.Element("instruction")?.Value);
                        AddListEntries("1.6/Defs/DiaryInteractionGroupDefs.xml", pair.Key + ".instructions",
                            "groupInstruction", def.Element("instructions"));
                        AddOptionalEntry("1.6/Defs/DiaryInteractionGroupDefs.xml", pair.Key + ".tone",
                            "groupTone", def.Element("tone")?.Value);
                        AddListEntries("1.6/Defs/DiaryInteractionGroupDefs.xml", pair.Key + ".tones",
                            "groupTone", def.Element("tones"));
                    }
                    else if (type == "PawnDiary.DiaryObservedConditionDef")
                    {
                        AddOptionalEntry("1.6/Defs/DiaryObservedConditionDefs.xml", pair.Key,
                            "observedConditionInstruction", def.Element("instruction")?.Value);
                        string bundle = AssembleBundle(def,
                            "PawnDiary.Prompt.ObservedCondition.Priority",
                            "PawnDiary.Prompt.ObservedCondition.ConditionFallback",
                            "PawnDiary.Prompt.ObservedCondition.Detail");
                        observedBundles[pair.Key] = bundle;
                        AddOptionalEntry("1.6/Defs/DiaryObservedConditionDefs.xml + Keyed", pair.Key,
                            "observedConditionBundle", bundle);
                    }
                    else if (type == "PawnDiary.DiaryEventWindowDef")
                    {
                        AddOptionalEntry("1.6/Defs/DiaryEventWindowDefs.xml", pair.Key,
                            "eventWindowInstruction", def.Element("instruction")?.Value);
                        AddOptionalEntry("1.6/Defs/DiaryEventWindowDefs.xml + Keyed", pair.Key,
                            "eventWindowBundle", AssembleBundle(def,
                                "PawnDiary.Prompt.EventWindow.Priority",
                                "PawnDiary.Prompt.EventWindow.ConditionFallback",
                                "PawnDiary.Prompt.EventWindow.Detail"));
                    }
                    else if (type == "PawnDiary.DiaryPromptEnchantmentDef")
                    {
                        CollectEnchantmentTexts(pair.Key, def);
                    }
                }

                // Other model-facing policy instructions.
                AddOptionalEntry("1.6/Defs/DiaryNarrativeContinuityDefs.xml",
                    "Diary_NarrativeContinuity.promptFieldInstruction", "policyInstruction",
                    DefText("Diary_NarrativeContinuity", "promptFieldInstruction"));
                AddOptionalEntry("1.6/Defs/DiaryMemoryTuningDef.xml",
                    "Diary_Memory.memoryContextInstruction", "policyInstruction",
                    DefText("Diary_Memory", "memoryContextInstruction"));
                AddOptionalEntry("1.6/Defs/DiaryBeliefPolicyDef.xml",
                    "Diary_BeliefPolicy.promptFieldInstruction", "policyInstruction",
                    DefText("Diary_BeliefPolicy", "promptFieldInstruction"));
                AddOptionalEntry("1.6/Defs/DiaryAuthoritySpeechPolicyDef.xml",
                    "Diary_AuthoritySpeechPolicy.speakerPromptInstruction", "policyInstruction",
                    DefText("Diary_AuthoritySpeechPolicy", "speakerPromptInstruction"));
                AddOptionalEntry("1.6/Defs/DiaryAuthoritySpeechPolicyDef.xml",
                    "Diary_AuthoritySpeechPolicy.witnessPromptInstruction", "policyInstruction",
                    DefText("Diary_AuthoritySpeechPolicy", "witnessPromptInstruction"));

                // Keyed instruction strings that become a diary event's "instruction" field
                // (ambient day batches, reflections, and similar policy-owned guidance).
                foreach (KeyValuePair<string, string> pair in keyed)
                {
                    if (pair.Key.StartsWith("PawnDiary.Event.", StringComparison.Ordinal)
                        && pair.Key.EndsWith("Instruction", StringComparison.Ordinal))
                    {
                        AddEntry("Languages/English/Keyed/PawnDiary.xml", pair.Key, "policyInstruction", pair.Value);
                    }
                }
            }

            private void CollectDefRules(string defType, string category, string file)
            {
                foreach (KeyValuePair<string, XElement> pair in DefsByName)
                {
                    if (pair.Value.Name.LocalName != defType)
                    {
                        continue;
                    }

                    string rule = pair.Value.Element("rule")?.Value;
                    // Neutral's empty rule is a real, intentional catalog member; record it at 0 chars.
                    if (rule != null)
                    {
                        AddEntry(file, pair.Key, category, rule);
                    }
                }
            }

            /// <summary>
            /// Health prompt-enchantment rows resolve their Keyed condition/cue/description text at
            /// runtime; the shipped English text elements are measured here individually.
            /// </summary>
            private void CollectEnchantmentTexts(string defName, XElement def)
            {
                AddOptionalEntry("1.6/Defs/DiaryPromptEnchantmentDefs.xml + Keyed", defName + ".condition",
                    "enchantmentText", ResolveKeyedRef(def.Element("conditionKey")?.Value));
                AddOptionalEntry("1.6/Defs/DiaryPromptEnchantmentDefs.xml + Keyed", defName + ".description",
                    "enchantmentText", ResolveKeyedRef(def.Element("descriptionOverrideKey")?.Value));
                XElement cueKeys = def.Element("cueKeys");
                if (cueKeys != null)
                {
                    int index = 0;
                    foreach (XElement li in cueKeys.Elements("li"))
                    {
                        AddOptionalEntry("1.6/Defs/DiaryPromptEnchantmentDefs.xml + Keyed",
                            defName + ".cue." + index, "enchantmentText", ResolveKeyedRef(li.Value));
                        index++;
                    }
                }
            }

            /// <summary>
            /// Assembles an observed-condition/event-window candidate exactly as the runtime does:
            /// resolved priority and condition, then the description wrapped in the Detail frame plus
            /// configured cues, joined through the production PromptEnchantmentPlanner (3-cue cap).
            /// Live evidence labels are runtime data and excluded from this static measurement.
            /// </summary>
            private string AssembleBundle(XElement def, string priorityFallbackKey,
                string conditionFallbackKey, string detailKey)
            {
                if (!string.Equals(def.Element("promptEnabled")?.Value ?? "true", "true",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                string label = def.Element("label")?.Value ?? def.Element("defName")?.Value ?? string.Empty;
                string priority = FirstNonEmpty(
                    def.Element("promptPriorityText")?.Value,
                    ResolveKeyedRef(def.Element("promptPriorityKey")?.Value),
                    Keyed(priorityFallbackKey));
                string condition = FirstNonEmpty(
                    def.Element("promptConditionText")?.Value,
                    ResolveKeyedRef(def.Element("promptConditionKey")?.Value),
                    Format(Keyed(conditionFallbackKey), label));

                List<string> cues = new List<string>();
                string description = FirstNonEmpty(
                    def.Element("promptDescriptionText")?.Value,
                    ResolveKeyedRef(def.Element("promptDescriptionKey")?.Value),
                    string.Empty);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    cues.Add(Format(Keyed(detailKey), description));
                }

                XElement cueTexts = def.Element("promptCueTexts");
                if (cueTexts != null)
                {
                    cues.AddRange(cueTexts.Elements("li").Select(li => li.Value));
                }

                XElement cueKeys = def.Element("promptCueKeys");
                if (cueKeys != null)
                {
                    cues.AddRange(cueKeys.Elements("li").Select(li => ResolveKeyedRef(li.Value)));
                }

                PromptEnchantmentCandidate candidate = new PromptEnchantmentCandidate
                {
                    weight = 1f,
                    priorityText = priority,
                    conditionText = condition,
                    configuredCues = cues
                };
                return PromptEnchantmentPlanner.Build(
                    new List<PromptEnchantmentCandidate> { candidate },
                    new PromptEnchantmentTuning(), 0f);
            }

            private string ResolveKeyedRef(string key)
            {
                return string.IsNullOrWhiteSpace(key) ? string.Empty : Keyed(key.Trim());
            }

            private void AddEntry(string file, string id, string category, string text)
            {
                string value = text ?? string.Empty;
                Entries.Add(new CatalogEntry
                {
                    file = file,
                    id = id,
                    category = category,
                    text = value,
                    chars = value.Length
                });
            }

            private void AddOptionalEntry(string file, string id, string category, string text)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AddEntry(file, id, category, text);
                }
            }

            private void AddListEntries(string file, string idPrefix, string category, XElement list)
            {
                if (list == null)
                {
                    return;
                }

                int index = 0;
                foreach (XElement li in list.Elements("li"))
                {
                    AddOptionalEntry(file, idPrefix + "." + index, category, li.Value);
                    index++;
                }
            }

            private static string FirstNonEmpty(params string[] values)
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return string.Empty;
            }

            private static string StripPlaceholders(string text)
            {
                return (text ?? string.Empty).Replace("{0}", string.Empty).Replace("{1}", string.Empty);
            }

            private static bool BoolOf(XElement element, string name, bool fallback)
            {
                string value = element.Element(name)?.Value;
                bool parsed;
                return bool.TryParse(value, out parsed) ? parsed : fallback;
            }

            private static int IntOf(XElement element, string name, int fallback)
            {
                string value = element.Element(name)?.Value;
                int parsed;
                return int.TryParse(value, out parsed) ? parsed : fallback;
            }

            private static bool Eq(string a, string b)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            private static string RepoRoot()
            {
                string directory = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(directory))
                {
                    if (Directory.Exists(Path.Combine(directory, "1.6"))
                        && Directory.Exists(Path.Combine(directory, "Source")))
                    {
                        return directory;
                    }

                    DirectoryInfo parent = Directory.GetParent(directory);
                    directory = parent?.FullName;
                }

                throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
            }
        }

        // =========================================================================================
        // Shared helpers
        // =========================================================================================

        /// <summary>Literal {0}/{1} substitution, mirroring how the adapter feeds the Keyed tails.</summary>
        private static string Format(string template, params string[] args)
        {
            string result = template ?? string.Empty;
            for (int i = 0; i < args.Length; i++)
            {
                result = result.Replace("{" + i + "}", args[i]);
            }

            return result;
        }

        private static int CountToken(string text, string token)
        {
            int count = 0;
            int at = 0;
            while (text != null && (at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += token.Length;
            }

            return count;
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(name + " failed.");
            }
        }

        private static void AssertEqual(string name, string expected, string actual)
        {
            assertions++;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertEqual(string name, int expected, int actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertContains(string name, string text, string expectedFragment)
        {
            assertions++;
            if (text == null || text.IndexOf(expectedFragment, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected fragment: [" + expectedFragment + "]\nActual: [" + text + "]");
            }
        }
    }
}
