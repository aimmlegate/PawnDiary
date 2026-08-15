// Pure/source-backed contract checks for the player-authored diary editor. Runtime mutation belongs
// to RimTest; this file pins the marker parser, the pre-catch-all display guard, localization parity,
// and XML-backed geometry without loading RimWorld, Verse, or Unity assemblies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private const string ManualKeyPrefix = "PawnDiary.ManualEntry.";

        /// <summary>
        /// Locks the pure schema marker and shipped UI resources that make player-created pages distinct
        /// from generated Interaction pages while keeping English/Russian and code/XML style fields even.
        /// </summary>
        private static void TestManualDiaryEntryContract()
        {
            AssertEqual("manual-entry stable context marker",
                "manual_entry=true", ManualDiaryEntryFacts.GameContext);
            AssertTrue("manual-entry exact marker parses",
                ManualDiaryEntryFacts.IsPlayerCreated("manual_entry=true"));
            AssertTrue("manual-entry marker parses case-insensitively among fields",
                ManualDiaryEntryFacts.IsPlayerCreated("source=test; manual_entry=TRUE; note=x"));
            AssertTrue("manual-entry false marker is not player-created",
                !ManualDiaryEntryFacts.IsPlayerCreated("manual_entry=false"));
            AssertTrue("manual-entry near-match key is not player-created",
                !ManualDiaryEntryFacts.IsPlayerCreated("not_manual_entry=true"));
            AssertTrue("manual-entry null context is not player-created",
                !ManualDiaryEntryFacts.IsPlayerCreated(null));

            // The general domain classifier deliberately falls back to Interaction. The loaded-model
            // display adapter must therefore test the manual marker first and return the saved localized
            // label instead of letting Interaction's catch-all group relabel the page.
            AssertEqual("manual-entry domain remains the safe Interaction fallback",
                DiaryEventDomainClassifier.Interaction,
                DiaryEventDomainClassifier.DomainForContext(ManualDiaryEntryFacts.GameContext));
            string eventSource = File.ReadAllText(RepoPath("Source", "Models", "DiaryEvent.cs"));
            int groupStart = eventSource.IndexOf(
                "private static DiaryInteractionGroupDef GroupForDisplay",
                StringComparison.Ordinal);
            int manualGuard = eventSource.IndexOf(
                "if (ManualDiaryEntryFacts.IsPlayerCreated(context))",
                Math.Max(0, groupStart),
                StringComparison.Ordinal);
            int domainFallback = eventSource.IndexOf(
                "DiaryEventDomainClassifier.DomainForContext(context)",
                Math.Max(0, groupStart),
                StringComparison.Ordinal);
            int guardReturn = eventSource.IndexOf(
                "return null;",
                Math.Max(0, manualGuard),
                StringComparison.Ordinal);
            AssertTrue("manual-entry display guard exists before Interaction classification",
                groupStart >= 0
                    && manualGuard > groupStart
                    && guardReturn > manualGuard
                    && guardReturn < domainFallback
                    && domainFallback > manualGuard);

            XDocument english = XDocument.Load(RepoPath(
                "Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russian = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            XElement[] englishRows = ManualRows(english);
            XElement[] russianRows = ManualRows(russian);
            // Composer modes add localized controls over time. Pin the original floor and exact locale
            // parity rather than making every additive label rewrite an unrelated magic count.
            AssertTrue("manual-entry English keyed floor", englishRows.Length >= 18);
            AssertEqual("manual-entry Russian keyed count", englishRows.Length, russianRows.Length);

            string[] englishKeys = englishRows
                .Select(row => row.Name.LocalName)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            string[] russianKeys = russianRows
                .Select(row => row.Name.LocalName)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            for (int i = 0; i < englishKeys.Length; i++)
            {
                AssertEqual("manual-entry EN/RU key parity " + i,
                    englishKeys[i], russianKeys[i]);
                string englishValue = englishRows.Single(
                    row => row.Name.LocalName == englishKeys[i]).Value;
                string russianValue = russianRows.Single(
                    row => row.Name.LocalName == russianKeys[i]).Value;
                AssertTrue("manual-entry English value is nonblank " + englishKeys[i],
                    !string.IsNullOrWhiteSpace(englishValue));
                AssertTrue("manual-entry Russian value is nonblank " + russianKeys[i],
                    !string.IsNullOrWhiteSpace(russianValue));
                AssertEqual("manual-entry placeholder parity " + englishKeys[i],
                    ManualPlaceholderSignature(englishValue),
                    ManualPlaceholderSignature(russianValue));
            }

            XDocument styleDocument = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryUiStyleDef.xml"));
            XElement style = styleDocument
                .Descendants("PawnDiary.DiaryUiStyleDef")
                .Single(row => string.Equals(
                    row.Element("defName")?.Value,
                    "Diary_UiStyle",
                    StringComparison.Ordinal));
            string styleSource = File.ReadAllText(RepoPath(
                "Source", "Defs", "DiaryUiStyleDef.cs"));
            string editorStateSource = File.ReadAllText(RepoPath(
                "Source", "UI", "Dialog_DiaryEntryEditor.cs"));
            string editorLayoutSource = File.ReadAllText(RepoPath(
                "Source", "UI", "Dialog_DiaryEntryEditor.Layout.cs"));
            string editorSource = editorStateSource + "\n" + editorLayoutSource;
            AssertTrue("manual-entry UI does not route through the public integration facade",
                editorSource.IndexOf("PawnDiaryApi.", StringComparison.Ordinal) < 0);
            AssertTrue("manual-entry UI does not own the adapter completion poller",
                editorSource.IndexOf("ExternalLlmCompletionService", StringComparison.Ordinal) < 0);

            // Unity can execute layout/repaint multiple times. The presentation partial may edit detached
            // buffers or dispatch a button handler, but persistence/polling must stay in explicit state
            // methods so a repeated draw pass cannot create a page or consume a completion result.
            AssertContains("manual-entry draw method is discoverable for mutation audit",
                editorLayoutSource, "public override void DoWindowContents");
            AssertTrue("manual-entry draw method performs no component persistence",
                editorLayoutSource.IndexOf("TryCreateManualEntry", StringComparison.Ordinal) < 0
                    && editorLayoutSource.IndexOf("TryEditManualEntry", StringComparison.Ordinal) < 0
                    && editorLayoutSource.IndexOf("NotifyEntryStatusChanged", StringComparison.Ordinal) < 0
                    && editorLayoutSource.IndexOf("PollPlayerEntryDraft", StringComparison.Ordinal) < 0);

            AssertContains("manual-entry UI starts drafts through the game component",
                editorStateSource, "component.StartPlayerEntryDraft(");
            AssertContains("manual-entry UI polls drafts outside the draw path",
                editorStateSource, "component.PollPlayerEntryDraft(");
            AssertContains("manual-entry UI cancels drafts through the game component",
                editorStateSource, "component.CancelPlayerEntryDraft(handle);");
            AssertContains("manual-entry successful draft enters review",
                editorStateSource, "draftStage = ComposerDraftStage.Review;");
            AssertContains("manual-entry unknown draft becomes retryable failure",
                editorStateSource, "draftStage = ComposerDraftStage.Failed;");
            AssertContains("manual-entry failed footer exposes retry",
                editorLayoutSource, "\"PawnDiary.ManualEntry.Retry\".Translate().Resolve()");
            AssertContains("manual-entry review locks the generated type selector",
                editorStateSource, "if (entryTypeLocked || Pending || Reviewing) return;");
            AssertContains("manual-entry review renders its type selector disabled",
                editorLayoutSource, "!entryTypeLocked && !Pending && !Reviewing");
            AssertContains("manual-entry close cancels an active draft",
                SourceSlice(editorStateSource, "public override void Close", "public override void PostClose"),
                "CancelActiveDraft();");
            AssertContains("manual-entry post-close cancels an active draft",
                SourceSlice(editorStateSource, "public override void PostClose", "private void StartGeneration"),
                "CancelActiveDraft();");

            string saveSource = SourceSlice(
                editorStateSource, "private void Save()", "private bool CanGenerate");
            AssertTrue("manual-entry explicit Save method is discoverable",
                saveSource.Length > 0);
            AssertContains("manual-entry create persists only from Save",
                saveSource, "component.TryCreateManualEntry(");
            AssertContains("manual-entry edit persists only from Save",
                saveSource, "component.TryEditManualEntry(");
            AssertEqual("manual-entry create persistence has one UI call site", 1,
                ManualCountOccurrences(editorSource, "component.TryCreateManualEntry("));
            AssertEqual("manual-entry edit persistence has one UI call site", 1,
                ManualCountOccurrences(editorSource, "component.TryEditManualEntry("));
            string[,] styleFields =
            {
                { "manualEntryEditorWidth", "820" },
                { "manualEntryEditorHeight", "700" },
                { "manualEntryEditorScreenMargin", "64" },
                { "manualEntryEditorFieldGap", "8" },
                { "manualEntryEditorButtonWidth", "140" },
                { "manualEntryEditorButtonGap", "10" },
                { "manualEntryComposerCompactWidth", "620" },
                { "manualEntryComposerSectionGap", "12" },
                { "manualEntryComposerPanelPadding", "10" },
                { "manualEntryComposerModeGap", "8" },
                { "manualEntryComposerModeMinHeight", "72" },
                { "manualEntryComposerSelectorGap", "12" },
                { "manualEntryComposerSelectorMinHeight", "34" },
                { "manualEntryComposerDescriptionGap", "4" },
                { "manualEntryComposerCharacterGap", "8" },
                { "manualEntryComposerTextPadding", "6" },
                { "manualEntryComposerShortAreaHeight", "104" },
                { "manualEntryComposerSystemAreaHeight", "120" },
                { "manualEntryComposerLongAreaHeight", "240" },
                { "manualEntryComposerWarningPadding", "10" },
            };
            for (int i = 0; i < styleFields.GetLength(0); i++)
            {
                string field = styleFields[i, 0];
                string expected = styleFields[i, 1];
                AssertEqual("manual-entry XML style default " + field,
                    expected, style.Element(field)?.Value?.Trim() ?? string.Empty);
                AssertContains("manual-entry C# style fallback " + field,
                    styleSource,
                    "public float " + field + " = " + expected + "f;");
                AssertContains("manual-entry editor consumes XML style " + field,
                    editorSource,
                    "style." + field);
            }

            string[,] colorFields =
            {
                { "manualEntryComposerWarningBackground", "ManualEntryComposerWarningBackground" },
                { "manualEntryComposerWarningBorder", "ManualEntryComposerWarningBorder" },
                { "manualEntryComposerMutedText", "ManualEntryComposerMutedText" },
                { "manualEntryComposerErrorText", "ManualEntryComposerErrorText" },
            };
            for (int i = 0; i < colorFields.GetLength(0); i++)
            {
                string field = colorFields[i, 0];
                string property = colorFields[i, 1];
                XElement color = style.Element(field);
                AssertTrue("manual-entry XML style color " + field,
                    color != null
                        && color.Elements().Select(row => row.Name.LocalName)
                            .SequenceEqual(new[] { "r", "g", "b", "a" })
                        && color.Elements().All(row =>
                            !string.IsNullOrWhiteSpace(row.Value)));
                AssertContains("manual-entry C# style color fallback " + field,
                    styleSource, "public DiaryUiColorSpec " + field + " = Color(");
                AssertContains("manual-entry editor consumes XML style color " + field,
                    editorLayoutSource, "style." + property);
            }

            AssertPlayerEntryCatalogXmlContract();
        }

        /// <summary>
        /// The selectable category/template catalogs are XML policy. Keep every shipped category
        /// localized through DefInjected, deterministically ordered, and pointed only at an explicitly
        /// opted solo template so a malformed/custom Def cannot silently become a prompt choice.
        /// </summary>
        private static void AssertPlayerEntryCatalogXmlContract()
        {
            XDocument typeDocument = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryPlayerEntryTypeDefs.xml"));
            XElement[] typeRows = typeDocument
                .Descendants("PawnDiary.DiaryPlayerEntryTypeDef")
                .ToArray();
            string[] expectedKeys =
            {
                "Personal",
                "Important",
                "InnerThoughts",
                "Social",
                "Work",
                "Health",
                "Combat",
                "Colony",
                "Reflection"
            };
            string[] expectedPromptKeys =
            {
                "PlayerPersonal",
                "PlayerImportant",
                "PlayerInnerThoughts",
                "PlayerSocial",
                "PlayerWork",
                "PlayerHealth",
                "PlayerCombat",
                "PlayerColony",
                "PlayerReflection"
            };
            AssertEqual("player-entry type XML row count", expectedKeys.Length, typeRows.Length);

            XDocument eventPromptDocument = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryEventPromptDefs.xml"));
            XDocument englishEventPrompts = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryEventPromptDef", "DiaryEventPromptDefs.xml"));
            XDocument russianEventPrompts = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryEventPromptDef", "DiaryEventPromptDefs.xml"));

            XDocument promptDocument = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryPromptTemplateDefs.xml"));
            XElement[] selectableTemplates = promptDocument
                .Descendants("PawnDiary.DiaryPromptTemplateDef")
                .Where(row => string.Equals(
                    row.Element("playerSelectable")?.Value?.Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string[] expectedTemplateKeys =
            {
                "SoloDefault",
                "SoloImportant",
                "SoloInternalState"
            };
            AssertEqual("player-entry selectable template row count",
                expectedTemplateKeys.Length, selectableTemplates.Length);

            string[] actualTemplateKeys = selectableTemplates
                .Select(row => row.Element("templateKey")?.Value?.Trim() ?? string.Empty)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] sortedExpectedTemplateKeys = expectedTemplateKeys
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            for (int i = 0; i < sortedExpectedTemplateKeys.Length; i++)
            {
                AssertEqual("player-entry selectable template key " + i,
                    sortedExpectedTemplateKeys[i], actualTemplateKeys[i]);
            }

            HashSet<string> defNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> entryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> orders = new HashSet<int>();
            XDocument englishTypes = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryPlayerEntryTypeDef", "DiaryPlayerEntryTypeDefs.xml"));
            XDocument russianTypes = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryPlayerEntryTypeDef", "DiaryPlayerEntryTypeDefs.xml"));
            for (int i = 0; i < typeRows.Length; i++)
            {
                XElement row = typeRows[i];
                string defName = row.Element("defName")?.Value?.Trim() ?? string.Empty;
                string entryKey = row.Element("entryTypeKey")?.Value?.Trim() ?? string.Empty;
                string eventPromptKey = row.Element("eventPromptKey")?.Value?.Trim() ?? string.Empty;
                string defaultTemplateKey = row.Element("defaultTemplateKey")?.Value?.Trim() ?? string.Empty;
                string domain = row.Element("domain")?.Value?.Trim() ?? string.Empty;
                string colorCue = row.Element("colorCue")?.Value?.Trim() ?? string.Empty;
                int displayOrder;
                AssertTrue("player-entry type display order parses " + entryKey,
                    int.TryParse(row.Element("displayOrder")?.Value, out displayOrder));
                AssertTrue("player-entry type defName nonblank " + i, defName.Length > 0);
                AssertTrue("player-entry type key nonblank " + i, entryKey.Length > 0);
                AssertTrue("player-entry type event prompt nonblank " + entryKey,
                    eventPromptKey.Length > 0);
                AssertTrue("player-entry type domain nonblank " + entryKey, domain.Length > 0);
                AssertTrue("player-entry type color cue nonblank " + entryKey, colorCue.Length > 0);
                AssertTrue("player-entry type default uses selectable template " + entryKey,
                    expectedTemplateKeys.Contains(defaultTemplateKey, StringComparer.Ordinal));
                AssertTrue("player-entry type defName unique " + defName, defNames.Add(defName));
                AssertTrue("player-entry type key unique " + entryKey, entryKeys.Add(entryKey));
                AssertTrue("player-entry type display order unique " + displayOrder,
                    orders.Add(displayOrder));
                AssertEqual("player-entry type stable ordered key " + i, expectedKeys[i], entryKey);
                AssertEqual("player-entry type-owned generic prompt key " + entryKey,
                    expectedPromptKeys[i], eventPromptKey);
                AssertDefInjectedPair(
                    "player-entry type " + defName,
                    defName,
                    englishTypes,
                    russianTypes);

                XElement eventPromptRow = eventPromptDocument
                    .Descendants("PawnDiary.DiaryEventPromptDef")
                    .SingleOrDefault(candidate => string.Equals(
                        candidate.Element("eventType")?.Value?.Trim(),
                        eventPromptKey,
                        StringComparison.Ordinal));
                string eventPromptDefName = eventPromptRow?.Element("defName")?.Value?.Trim()
                    ?? string.Empty;
                AssertTrue("player-entry generic event prompt exists " + entryKey,
                    eventPromptRow != null && eventPromptDefName.Length > 0);
                AssertTrue("player-entry generic prompt text nonblank " + entryKey,
                    !string.IsNullOrWhiteSpace(eventPromptRow?.Element("prompt")?.Value));
                AssertTrue("player-entry generic enhancement nonblank " + entryKey,
                    !string.IsNullOrWhiteSpace(eventPromptRow?.Element("enhancement")?.Value));
                AssertDefInjectedFieldsPair(
                    "player-entry generic prompt " + eventPromptDefName,
                    eventPromptDefName,
                    englishEventPrompts,
                    russianEventPrompts,
                    ".label",
                    ".prompt",
                    ".enhancement");
            }

            XDocument englishTemplates = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplateDefs.xml"));
            XDocument russianTemplates = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplateDefs.xml"));
            HashSet<int> templateOrders = new HashSet<int>();
            for (int i = 0; i < selectableTemplates.Length; i++)
            {
                XElement row = selectableTemplates[i];
                string defName = row.Element("defName")?.Value?.Trim() ?? string.Empty;
                string templateKey = row.Element("templateKey")?.Value?.Trim() ?? string.Empty;
                int playerOrder;
                AssertTrue("player-entry template order parses " + templateKey,
                    int.TryParse(row.Element("playerOrder")?.Value, out playerOrder));
                AssertTrue("player-entry template order unique " + playerOrder,
                    templateOrders.Add(playerOrder));
                AssertDefInjectedPair(
                    "player-entry template " + defName,
                    defName,
                    englishTemplates,
                    russianTemplates);
            }

            string catalogSource = File.ReadAllText(RepoPath(
                "Source", "Defs", "DiaryPlayerEntryTypeDef.cs"));
            AssertContains("player-entry types use Def localized labels",
                catalogSource, "label = source.LabelCap.Resolve()");
            AssertContains("player-entry templates use Def localized labels",
                catalogSource, "label = source.LabelCap.Resolve()");
            AssertContains("player-entry type lookup rejects unknown keys",
                catalogSource, "public static bool TryResolve");
            AssertContains("player-entry missing-Def fallback uses generic player guidance",
                catalogSource, "PersonalEventPromptKey = \"PlayerPersonal\"");
            AssertContains("player-entry missing-Def fallback applies generic player guidance",
                catalogSource, "eventPromptKey = PersonalEventPromptKey");
        }

        private static void AssertDefInjectedPair(
            string label,
            string defName,
            XDocument english,
            XDocument russian)
        {
            AssertDefInjectedFieldsPair(
                label, defName, english, russian, ".label", ".description");
        }

        private static void AssertDefInjectedFieldsPair(
            string label,
            string defName,
            XDocument english,
            XDocument russian,
            params string[] suffixes)
        {
            for (int i = 0; i < suffixes.Length; i++)
            {
                string key = defName + suffixes[i];
                XElement englishRow = english.Root?.Element(key);
                XElement russianRow = russian.Root?.Element(key);
                AssertTrue(label + " English DefInjected " + suffixes[i],
                    !string.IsNullOrWhiteSpace(englishRow?.Value));
                AssertTrue(label + " Russian DefInjected " + suffixes[i],
                    !string.IsNullOrWhiteSpace(russianRow?.Value));
            }
        }

        /// <summary>
        /// Pins the pure review-first composer contract. Runtime prompt assembly and transient handle
        /// lifecycle stay in RimTest; this suite proves untrusted text is bounded before either adapter
        /// sees it and that XML-selected types/templates cannot escape their detached allow-lists.
        /// </summary>
        private static void TestPlayerEntryComposerPolicy()
        {
            List<PlayerEntryTypeSnapshot> types = new List<PlayerEntryTypeSnapshot>
            {
                new PlayerEntryTypeSnapshot
                {
                    entryTypeKey = PlayerEntryComposerPolicy.PersonalEntryTypeKey,
                    eventPromptKey = "manual_personal",
                    defaultTemplateKey = "standard",
                    label = "Personal"
                },
                new PlayerEntryTypeSnapshot
                {
                    entryTypeKey = "Battle",
                    eventPromptKey = "manual_battle",
                    defaultTemplateKey = "reflection",
                    label = "Battle"
                }
            };
            List<PlayerEntryTemplateSnapshot> templates = new List<PlayerEntryTemplateSnapshot>
            {
                new PlayerEntryTemplateSnapshot { templateKey = "standard", label = "Standard" },
                new PlayerEntryTemplateSnapshot { templateKey = "reflection", label = "Reflection" }
            };

            PlayerEntryComposerPlan missing = PlayerEntryComposerPolicy.Plan(null, types, templates);
            AssertTrue("composer missing request invalid", !missing.valid);
            AssertEqual("composer missing request error", "missing_request", missing.errorCode);

            AssertComposerError(
                "composer direct blank",
                Request(PlayerEntryComposerMode.Direct, body: " \r\n\t "),
                types,
                templates,
                "blank_body");
            AssertComposerError(
                "composer review blank",
                Request(PlayerEntryComposerMode.Review, body: ""),
                types,
                templates,
                "blank_body");
            AssertComposerError(
                "composer context blank",
                Request(PlayerEntryComposerMode.Context),
                types,
                templates,
                "blank_context_request");
            AssertTrue("composer context accepts factual summary",
                PlayerEntryComposerPolicy.Plan(
                    Request(PlayerEntryComposerMode.Context, factualSummary: "facts"),
                    types,
                    templates).valid);
            AssertTrue("composer context accepts custom instruction",
                PlayerEntryComposerPolicy.Plan(
                    Request(PlayerEntryComposerMode.Context, customInstruction: "focus"),
                    types,
                    templates).valid);
            AssertComposerError(
                "composer full prompt blank",
                Request(PlayerEntryComposerMode.FullPrompt, userPrompt: " \u0000 "),
                types,
                templates,
                "blank_user_prompt");
            AssertComposerError(
                "composer unknown mode",
                Request((PlayerEntryComposerMode)999, body: "body"),
                types,
                templates,
                "unknown_mode");

            PlayerEntryComposerPlan direct = PlayerEntryComposerPolicy.Plan(
                Request(
                    PlayerEntryComposerMode.Direct,
                    entryTypeKey: "battle",
                    templateKey: "reflection",
                    body: "  body\u0001 text  ",
                    title: "  title\u0002 text  ",
                    maxTokens: 1),
                types,
                templates);
            AssertTrue("composer direct valid", direct.valid);
            AssertEqual("composer type lookup case insensitive", "Battle", direct.entryTypeKey);
            AssertEqual("composer direct mode ignores template selection", string.Empty, direct.templateKey);
            AssertEqual("composer prose strips controls and trims", "body text", direct.body);
            AssertEqual("composer title strips controls and trims", "title text", direct.title);
            AssertEqual("composer token floor", PlayerEntryComposerPolicy.MinMaxTokens, direct.maxTokens);

            List<PlayerEntryTemplateSnapshot> corruptTemplates =
                new List<PlayerEntryTemplateSnapshot>
                {
                    null,
                    new PlayerEntryTemplateSnapshot { templateKey = string.Empty },
                    new PlayerEntryTemplateSnapshot { templateKey = "damaged" }
                };
            AssertTrue("composer Direct ignores an unusable template catalog",
                PlayerEntryComposerPolicy.Plan(
                    Request(PlayerEntryComposerMode.Direct, body: "body"),
                    types,
                    corruptTemplates).valid);

            PlayerEntryComposerPlan fallback = PlayerEntryComposerPolicy.Plan(
                Request(
                    PlayerEntryComposerMode.Context,
                    entryTypeKey: " ",
                    templateKey: "missing-template",
                    factualSummary: "fallback facts",
                    maxTokens: int.MaxValue),
                types,
                templates);
            AssertTrue("composer blank type falls back to Personal", fallback.valid);
            AssertEqual("composer blank Personal fallback key",
                PlayerEntryComposerPolicy.PersonalEntryTypeKey, fallback.entryTypeKey);
            AssertEqual("composer type default template fallback", "standard", fallback.templateKey);
            AssertEqual("composer token ceiling", PlayerEntryComposerPolicy.MaxMaxTokens, fallback.maxTokens);

            AssertComposerError(
                "composer explicit unknown type rejects",
                Request(
                    PlayerEntryComposerMode.Context,
                    entryTypeKey: "missing-type",
                    factualSummary: "facts"),
                types,
                templates,
                "unknown_entry_type");
            AssertComposerError(
                "composer missing Personal fallback rejects blank type",
                Request(
                    PlayerEntryComposerMode.Context,
                    entryTypeKey: " ",
                    factualSummary: "facts"),
                new List<PlayerEntryTypeSnapshot> { types[1] },
                templates,
                "unknown_entry_type");
            AssertComposerError(
                "composer missing requested and default template rejects",
                Request(
                    PlayerEntryComposerMode.Context,
                    entryTypeKey: "Battle",
                    templateKey: "missing-template",
                    factualSummary: "facts"),
                types,
                new List<PlayerEntryTemplateSnapshot> { templates[0] },
                "unknown_template");

            string exactSystem = "  system\r\n\tline\u0000tail  ";
            string exactUser = "\n user\u0001 text \r\n";
            PlayerEntryComposerPlan raw = PlayerEntryComposerPolicy.Plan(
                Request(
                    PlayerEntryComposerMode.FullPrompt,
                    entryTypeKey: "Battle",
                    templateKey: "reflection",
                    systemPrompt: exactSystem,
                    userPrompt: exactUser),
                types,
                templates);
            AssertTrue("composer raw valid", raw.valid);
            AssertEqual("composer raw mode ignores template selection", string.Empty, raw.templateKey);
            AssertEqual("composer raw system exact except controls",
                "  system\r\n\tlinetail  ", raw.systemPrompt);
            AssertEqual("composer raw user exact except controls",
                "\n user text \r\n", raw.userPrompt);
            AssertTrue("composer Full Prompt ignores an unusable template catalog",
                PlayerEntryComposerPolicy.Plan(
                    Request(PlayerEntryComposerMode.FullPrompt, userPrompt: "raw"),
                    types,
                    corruptTemplates).valid);

            string bodyAtCap = new string('b', PlayerEntryComposerPolicy.BodyMaxCharacters + 3);
            string titleAtCap = new string('t', PlayerEntryComposerPolicy.TitleMaxCharacters + 3);
            string summaryAtCap = new string('s', PlayerEntryComposerPolicy.ContextSummaryMaxCharacters + 3);
            string instructionAtCap = new string('i', PlayerEntryComposerPolicy.ContextInstructionMaxCharacters + 3);
            PlayerEntryComposerPlan cappedContext = PlayerEntryComposerPolicy.Plan(
                Request(
                    PlayerEntryComposerMode.Context,
                    body: bodyAtCap,
                    title: titleAtCap,
                    factualSummary: summaryAtCap,
                    customInstruction: instructionAtCap),
                types,
                templates);
            AssertEqual("composer body cap", PlayerEntryComposerPolicy.BodyMaxCharacters,
                cappedContext.body.Length);
            AssertEqual("composer title cap", PlayerEntryComposerPolicy.TitleMaxCharacters,
                cappedContext.title.Length);
            AssertEqual("composer factual summary cap",
                PlayerEntryComposerPolicy.ContextSummaryMaxCharacters,
                cappedContext.factualSummary.Length);
            AssertEqual("composer custom instruction cap",
                PlayerEntryComposerPolicy.ContextInstructionMaxCharacters,
                cappedContext.customInstruction.Length);

            string surrogateBoundary = new string('x', PlayerEntryComposerPolicy.RawPromptMaxCharacters - 1)
                + "\ud83d\ude00tail";
            string cappedRaw = PlayerEntryComposerPolicy.CleanRawPrompt(
                surrogateBoundary,
                PlayerEntryComposerPolicy.RawPromptMaxCharacters);
            AssertEqual("composer raw Unicode-safe cap removes split surrogate",
                PlayerEntryComposerPolicy.RawPromptMaxCharacters - 1,
                cappedRaw.Length);
            AssertTrue("composer raw Unicode-safe cap has no dangling high surrogate",
                cappedRaw.Length == 0 || !char.IsHighSurrogate(cappedRaw[cappedRaw.Length - 1]));

            AssertTrue("composer template allow-list accepts opted solo template",
                PlayerEntryComposerPolicy.IsRequestedTemplateAllowed(
                    " reflection ", true, "Reflection", true));
            AssertTrue("composer template allow-list rejects pair template",
                !PlayerEntryComposerPolicy.IsRequestedTemplateAllowed(
                    "reflection", false, "reflection", true));
            AssertTrue("composer template allow-list rejects non-opted template",
                !PlayerEntryComposerPolicy.IsRequestedTemplateAllowed(
                    "reflection", true, "reflection", false));
            AssertTrue("composer template allow-list rejects different template",
                !PlayerEntryComposerPolicy.IsRequestedTemplateAllowed(
                    "reflection", true, "standard", true));
            AssertTrue("composer template allow-list rejects blank request",
                !PlayerEntryComposerPolicy.IsRequestedTemplateAllowed(
                    " ", true, "standard", true));

            DiaryPolicySnapshot promptPolicy = new DiaryPolicySnapshot
            {
                group = new DiaryGroupPolicy { important = false },
                templates = new List<DiaryTemplatePolicy>
                {
                    new DiaryTemplatePolicy
                    {
                        templateKey = DiaryPipelineTemplates.SoloImportant,
                        playerSelectable = true
                    }
                }
            };
            DiaryPromptRequest requestedTemplate = new DiaryPromptRequest
            {
                payload = new DiaryEventPayload { solo = true },
                policy = promptPolicy,
                povRole = DiaryPipelineRoles.Initiator,
                requestedTemplateKey = DiaryPipelineTemplates.SoloImportant
            };
            AssertEqual("composer solo opted template overrides ordinary selection",
                DiaryPipelineTemplates.SoloImportant,
                DiaryPromptPlanner.TemplateKeyFor(requestedTemplate));

            requestedTemplate.payload.solo = false;
            AssertEqual("composer requested template cannot override pair shape",
                DiaryPipelineTemplates.PairDefault,
                DiaryPromptPlanner.TemplateKeyFor(requestedTemplate));
            requestedTemplate.payload.solo = true;
            requestedTemplate.titleRequest = true;
            AssertEqual("composer requested template cannot override title boundary",
                DiaryPipelineTemplates.Title,
                DiaryPromptPlanner.TemplateKeyFor(requestedTemplate));
            requestedTemplate.titleRequest = false;
            requestedTemplate.payload.hasDeathDescription = true;
            AssertEqual("composer requested template cannot override death boundary",
                DiaryPipelineTemplates.DeathDescription,
                DiaryPromptPlanner.TemplateKeyFor(requestedTemplate));
            requestedTemplate.payload.hasDeathDescription = false;
            requestedTemplate.payload.hasArrivalDescription = true;
            AssertEqual("composer requested template cannot override arrival boundary",
                DiaryPipelineTemplates.ArrivalDescription,
                DiaryPromptPlanner.TemplateKeyFor(requestedTemplate));
            requestedTemplate.payload.hasArrivalDescription = false;
            promptPolicy.templates[0].playerSelectable = false;
            AssertEqual("composer non-opted template falls back to ordinary selection",
                DiaryPipelineTemplates.SoloDefault,
                DiaryPromptPlanner.TemplateKeyFor(requestedTemplate));
        }

        private static PlayerEntryComposerRequest Request(
            PlayerEntryComposerMode mode,
            string entryTypeKey = "Personal",
            string templateKey = "standard",
            string body = "",
            string title = "",
            string factualSummary = "",
            string customInstruction = "",
            string systemPrompt = "",
            string userPrompt = "",
            int maxTokens = PlayerEntryComposerPolicy.DefaultMaxTokens)
        {
            return new PlayerEntryComposerRequest
            {
                mode = mode,
                entryTypeKey = entryTypeKey,
                templateKey = templateKey,
                body = body,
                title = title,
                factualSummary = factualSummary,
                customInstruction = customInstruction,
                systemPrompt = systemPrompt,
                userPrompt = userPrompt,
                maxTokens = maxTokens
            };
        }

        private static void AssertComposerError(
            string label,
            PlayerEntryComposerRequest request,
            IList<PlayerEntryTypeSnapshot> types,
            IList<PlayerEntryTemplateSnapshot> templates,
            string expectedError)
        {
            PlayerEntryComposerPlan plan = PlayerEntryComposerPolicy.Plan(request, types, templates);
            AssertTrue(label + " invalid", !plan.valid);
            AssertEqual(label + " error", expectedError, plan.errorCode);
        }

        private static XElement[] ManualRows(XDocument document)
        {
            return document.Root
                .Elements()
                .Where(row => row.Name.LocalName.StartsWith(
                    ManualKeyPrefix, StringComparison.Ordinal))
                .ToArray();
        }

        private static string ManualPlaceholderSignature(string value)
        {
            string safe = value ?? string.Empty;
            return ManualCountOccurrences(safe, "{0}") + "|"
                + ManualCountOccurrences(safe, "{1}") + "|"
                + ManualCountOccurrences(safe, "{2}") + "|"
                + ManualCountOccurrences(safe, "{3}");
        }

        private static string SourceSlice(string source, string startToken, string endToken)
        {
            string safe = source ?? string.Empty;
            int start = safe.IndexOf(startToken, StringComparison.Ordinal);
            int end = start < 0
                ? -1
                : safe.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            return start >= 0 && end > start
                ? safe.Substring(start, end - start)
                : string.Empty;
        }

        private static int ManualCountOccurrences(string value, string token)
        {
            int count = 0;
            int offset = 0;
            while (offset <= value.Length - token.Length)
            {
                int found = value.IndexOf(token, offset, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                count++;
                offset = found + token.Length;
            }

            return count;
        }
    }
}
