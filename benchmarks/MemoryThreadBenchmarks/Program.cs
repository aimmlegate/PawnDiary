// Informational Release harness for the M0 capacity search plus the production M4 reducer. It
// generates the exact finite T17.6 vector set, rejects invalid cap relationships, evaluates the
// evaluates the capacity surrogate at N=4/12/64, executes an adversarial M4 retention trace, and
// writes machine/Markdown evidence without claiming that the surrogate is a saved-row size walk.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PawnDiary;

namespace MemoryThreadBenchmarks
{
    internal static class Program
    {
        private const string BenchmarkSchema = "memory-system-benchmark-v1";
        private const string VectorHeader = "memory-system-vector-v1";
        private static volatile int timingSink;
        private static object allocationSink;

        private sealed class Dimension
        {
            public string name;
            public List<string> values;
        }

        private sealed class Candidate
        {
            public Dictionary<string, string> values;
            public HashSet<string> origins = new HashSet<string>(StringComparer.Ordinal);
            public string encoding;
            public string vectorId;
            public ulong[] numericCoordinates;
            public int complexityScore;
            public int vectorOrdinal;
            public ulong surrogateCombinedBytes;
            public ulong ownerTypicalBytes;
            public ulong ownerWorstBytes;
            public ulong pureMaxIndivisibleItemMicroseconds;
            public long pureAllocationTieBreakBytes;
            public ulong maximumCultureLabelDtoBytes;
            public bool feasible;
            public string rejection = string.Empty;
            public List<CoordinateEvaluation> coordinates = new List<CoordinateEvaluation>();
        }

        private sealed class Catalog
        {
            public List<Dimension> dimensions;
            public Dictionary<string, string> start;
            public List<List<string>> bundles;
            public string dimensionGateId;
            public string m0SelectedVectorId;
        }

        private sealed class FixedRow
        {
            public string name;
            public string disposition;
            public string value;
            public string gate;
        }

        private sealed class StatisticalMeasurement
        {
            public int observationCount;
            public ulong median;
            public ulong p95;
            public ulong maximum;
        }

        private sealed class PayloadAtomAudit
        {
            public int typeCount;
            public int atomCount;
            public Dictionary<string, ulong> minimumSchemaLogicalBytes;
            public List<PayloadAtom> atoms;
            public Dictionary<string, Dictionary<string, ulong>> typeBytesByMode;
        }

        private sealed class PayloadAtom
        {
            public string path;
            public string kind;
            public HashSet<string> scopes;
            public Dictionary<string, ulong> bytesByMode;
        }

        private sealed class LogicalAtom
        {
            public string path;
            public ulong bytes;
        }

        private sealed class FillResult
        {
            public ulong admittedBytes;
            public ulong admittedCatalogCycles;
            public ulong visitedAtoms;
            public string firstRefusedPath = string.Empty;
        }

        private sealed class CoordinateEvaluation
        {
            public int threadTarget;
            public string textMode;
            public ulong combinedBytes;
            public ulong ownerWorstBytes;
            public ulong ownerTypicalBytes;
            public FillResult combinedFill;
            public StatisticalMeasurement time;
            public StatisticalMeasurement allocation;
        }

        private sealed class SyntheticScenario
        {
            public string scenarioId;
            public string expectedGate;
            public List<string> coordinates;
        }

        private sealed class ScenarioAudit
        {
            public string scenarioId;
            public string expectedGate;
            public long evaluatedCells;
            public long passedCells;
            public StringBuilder resultEncoding = new StringBuilder();

            public string Fingerprint()
            {
                return HashTuple(
                    "memory-m0-scenario-results-v1",
                    scenarioId,
                    expectedGate,
                    evaluatedCells.ToString(CultureInfo.InvariantCulture),
                    passedCells.ToString(CultureInfo.InvariantCulture),
                    resultEncoding.ToString());
            }
        }

        private sealed class WorkInput
        {
            public int iterations;
            public int allocationBytes;
            public int seed;
        }

        private sealed class ManifestEntry
        {
            public string disposition;
            public string vectorId;
            public int threadTarget;
            public string effectivePolicyHash;
            public string entryId;
            public int entryOrdinal;
        }

        private sealed class ManifestAudit
        {
            public List<ManifestEntry> entries;
            public string fingerprint;
            public string releasePolicyEncodingHash;
            public List<ManifestEntry> defensiveIdentityGoldenRows;
        }

        private static int Main(string[] args)
        {
            bool validateOnly = args.Any(arg => arg == "--validate-only");
            string root = RepoRoot();
            Catalog catalog = LoadCatalog(root);
            HashSet<string> pureGateIds = LoadPureGateIds(root);
            List<FixedRow> fixedRows = LoadFixedRows(root);
            List<SyntheticScenario> scenarios = LoadSyntheticScenarios(root);
            PayloadAtomAudit payloadAtomAudit = ValidatePayloadAtomCatalog(root);
            ValidateCatalog(catalog, pureGateIds);
            ValidateTimingConversionGoldens();
            ValidateCanonicalUtf8HashGoldens();
            ValidateComponentwiseReleaseGoldens();
            ValidateM4ReducerTrace();
            List<Candidate> candidates = GenerateCandidates(catalog);
            ValidateVectorGeneratorCoverage(catalog, candidates);
            ValidateCodeFallback(catalog, candidates);
            Dictionary<string, ScenarioAudit> scenarioAudits = Evaluate(
                candidates,
                scenarios,
                payloadAtomAudit);
            string committedFallback = EncodeVector(catalog.dimensions,
                MemoryCapacityContracts.ProvisionalProduction().ToDictionary(
                    row => row.name, row => row.valueEncoding, StringComparer.Ordinal));
            Candidate production = candidates.SingleOrDefault(
                row => string.Equals(row.encoding, committedFallback, StringComparison.Ordinal));
            if (production == null || !production.feasible)
                throw new InvalidOperationException(
                    "The committed production fallback is absent or fails its release gates.");
            Candidate recomputedSelection = Select(candidates);
            Candidate selected = candidates.SingleOrDefault(row => string.Equals(
                row.vectorId, catalog.m0SelectedVectorId, StringComparison.Ordinal));
            if (selected == null || !selected.feasible)
                throw new InvalidOperationException(
                    "The recorded M0-selected vector is absent or no longer provisionally feasible.");
            if (!string.Equals(
                    recomputedSelection.vectorId, selected.vectorId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The current tie-break no longer reproduces the recorded M0-selected vector.");
            if (!ComponentwiseNoGreater(production, selected))
                throw new InvalidOperationException(
                    "The committed production fallback exceeds the recorded M0-selected vector.");
            ManifestAudit manifestAudit = BuildAndValidateManifestAudit(
                catalog, fixedRows, candidates, selected);

            Console.WriteLine("MemoryThreadBenchmarks generated " + candidates.Count
                + " normalized vectors; " + candidates.Count(row => row.feasible)
                + " are provisionally feasible.");
            Console.WriteLine("Selected provisional vector: " + selected.vectorId);
            Console.WriteLine("Surrogate combined bytes: " + selected.surrogateCombinedBytes);
            Console.WriteLine("Authenticated provisional manifest rows: " + manifestAudit.entries.Count);

            if (validateOnly)
            {
                return 0;
            }

            EnsureCleanRepository(root);
            WriteEvidence(root, catalog, candidates, selected, scenarios, scenarioAudits,
                manifestAudit, payloadAtomAudit);
            return 0;
        }

        private static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && (!File.Exists(Path.Combine(directory.FullName, "Source", "PawnDiary.csproj"))
                    || !File.Exists(Path.Combine(directory.FullName, "benchmarks",
                        "MemoryThreadBenchmarks", "Catalog", "memory-capacity-catalog-v1.json"))))
            {
                directory = directory.Parent;
            }
            if (directory == null) throw new InvalidOperationException("Repository root was not found.");
            return directory.FullName;
        }

        private static Catalog LoadCatalog(string root)
        {
            string path = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog",
                "memory-capacity-catalog-v1.json");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement rootElement = document.RootElement;
                if (rootElement.GetProperty("schema").GetString() != "memory-capacity-catalog-v1")
                    throw new InvalidOperationException("Unexpected capacity catalog schema.");
                Catalog catalog = new Catalog
                {
                    dimensions = new List<Dimension>(),
                    start = new Dictionary<string, string>(StringComparer.Ordinal),
                    bundles = new List<List<string>>(),
                    dimensionGateId = rootElement.GetProperty("dimensionGateId").GetString(),
                    m0SelectedVectorId = rootElement.GetProperty("m0SelectedVectorId").GetString()
                };
                foreach (JsonProperty property in rootElement.GetProperty("startVector").EnumerateObject())
                    catalog.start.Add(property.Name, property.Value.GetString());
                foreach (JsonElement row in rootElement.GetProperty("dimensions").EnumerateArray())
                {
                    catalog.dimensions.Add(new Dimension
                    {
                        name = row.GetProperty("name").GetString(),
                        values = row.GetProperty("values").EnumerateArray()
                            .Select(value => value.GetString()).ToList()
                    });
                }
                foreach (JsonElement bundle in rootElement.GetProperty("bundles").EnumerateArray())
                    catalog.bundles.Add(bundle.EnumerateArray().Select(value => value.GetString()).ToList());
                return catalog;
            }
        }

        private static HashSet<string> LoadPureGateIds(string root)
        {
            string path = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog",
                "memory-m0-fixture-catalog-v1.json");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                return new HashSet<string>(document.RootElement.GetProperty("pureGateIds")
                    .EnumerateArray().Select(value => value.GetString()), StringComparer.Ordinal);
            }
        }

        private static List<FixedRow> LoadFixedRows(string root)
        {
            string path = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog",
                "memory-m0-fixture-catalog-v1.json");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                if (document.RootElement.GetProperty("schema").GetString()
                    != "memory-m0-fixture-catalog-v1")
                {
                    throw new InvalidOperationException("Unexpected M0 fixture catalog schema.");
                }

                return document.RootElement.GetProperty("fixedRows").EnumerateArray()
                    .Select(row => new FixedRow
                    {
                        name = row.GetProperty("name").GetString(),
                        disposition = row.GetProperty("disposition").GetString(),
                        value = row.GetProperty("value").GetString(),
                        gate = row.GetProperty("gate").GetString()
                    }).ToList();
            }
        }

        private static List<SyntheticScenario> LoadSyntheticScenarios(string root)
        {
            string path = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog",
                "memory-m0-fixture-catalog-v1.json");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                List<SyntheticScenario> scenarios = document.RootElement
                    .GetProperty("syntheticScenarios")
                    .EnumerateArray()
                    .Select(row => new SyntheticScenario
                    {
                        scenarioId = row.GetProperty("scenarioId").GetString(),
                        expectedGate = row.GetProperty("expectedGate").GetString(),
                        coordinates = row.GetProperty("coordinates").EnumerateArray()
                            .Select(value => value.GetString()).ToList()
                    }).ToList();
                string[] expected =
                {
                    "worst-case-owner-v1",
                    "exact-subject-collision-v1",
                    "duplicate-root-v1",
                    "summary-saturation-v1",
                    "player-edited-saturation-v1",
                    "global-faction-state-v1",
                    "migration-overflow-v1"
                };
                if (scenarios.Count != expected.Length
                    || !scenarios.Select(row => row.scenarioId).SequenceEqual(expected)
                    || scenarios.Any(row => string.IsNullOrWhiteSpace(row.expectedGate)
                        || row.coordinates == null
                        || row.coordinates.Count == 0))
                {
                    throw new InvalidOperationException(
                        "The executable synthetic-scenario registry drifted from the fixture catalog.");
                }
                return scenarios;
            }
        }

        private static PayloadAtomAudit ValidatePayloadAtomCatalog(string root)
        {
            string path = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog",
                "memory-payload-atom-catalog-v1.json");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement value = document.RootElement;
                if (value.GetProperty("schema").GetString()
                    != "memory-benchmark-payload-atom-catalog-v1")
                    throw new InvalidOperationException("Unexpected payload atom catalog schema.");
                List<string> typeNames = value.GetProperty("types").EnumerateArray()
                    .Select(row => row.GetProperty("name").GetString()).ToList();
                int typeCount = typeNames.Count;
                List<JsonElement> atoms = value.GetProperty("atomRows").EnumerateArray().ToList();
                if (typeCount <= 0 || atoms.Count <= 0)
                    throw new InvalidOperationException("Payload atom catalog is empty.");
                HashSet<string> exactFreeTextPaths = new HashSet<string>(new[]
                {
                    "PawnKnowledgeState.playerBackground",
                    "SavedMemoryThreadRoot.frozenSubjectLabel",
                    "SavedMemoryBlock.automaticWording",
                    "SavedMemoryBlock.playerWording",
                    "SavedMemorySubjectRef.frozenLabel",
                    "SavedMemorySummaryPayload.deterministicWording",
                    "SavedMemorySummaryPayload.optionalLlmWording",
                    "SavedImportedMemoryRow.importedWording",
                    "SavedGlobalFactionSnapshot.frozenDisplayLabel",
                    "SavedFrozenPromptVariantV1.systemPrompt",
                    "SavedFrozenPromptVariantV1.userPrompt"
                }, StringComparer.Ordinal);
                UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                string[] modes =
                {
                    "asciiByteBoundary", "utf8WorstPerUtf16Unit", "xmlEscapeWorstPerUtf16Unit"
                };
                Dictionary<string, ulong> totals = new Dictionary<string, ulong>(StringComparer.Ordinal);
                Dictionary<string, Dictionary<string, ulong>> typeBytes = typeNames.ToDictionary(
                    type => type,
                    type => modes.ToDictionary(
                        mode => mode,
                        mode => checked((ulong)value.GetProperty("rowFramingBytes").GetInt32()),
                        StringComparer.Ordinal),
                    StringComparer.Ordinal);
                List<PayloadAtom> parsedAtoms = new List<PayloadAtom>(atoms.Count);
                foreach (string mode in modes)
                {
                    ulong total = checked((ulong)typeCount
                        * checked((ulong)value.GetProperty("rowFramingBytes").GetInt32()));
                    for (int index = 0; index < atoms.Count; index++)
                    {
                        JsonElement atom = atoms[index];
                        if (atom.GetProperty("pathOrdinal").GetInt32() != index)
                            throw new InvalidOperationException("Payload atom path ordinal drifted.");
                        string kind = atom.GetProperty("atomKindToken").GetString();
                        ulong atomBytes;
                        if (kind == "bool") atomBytes = 1;
                        else if (kind == "int32") atomBytes = 4;
                        else if (kind == "int64") atomBytes = 8;
                        else if (kind == "nullable_row") atomBytes = 1;
                        else if (kind == "row") atomBytes = 0;
                        else if (kind == "list") atomBytes = 4;
                        else if (kind == "string")
                        {
                            string textValue = atom.GetProperty("candidateValueEncoding").GetString()
                                ?? string.Empty;
                            if (atom.GetProperty("freeTextModeEligible").GetBoolean())
                            {
                                textValue += mode == "asciiByteBoundary" ? "x"
                                    : mode == "utf8WorstPerUtf16Unit" ? "\uE000" : "&";
                            }
                            atomBytes = checked(4UL + (ulong)strictUtf8.GetByteCount(textValue));
                        }
                        else throw new InvalidOperationException("Unknown payload atom kind: " + kind);
                        total = checked(total + atomBytes);
                        string fieldPath = atom.GetProperty("canonicalFieldPath").GetString();
                        string typeName = fieldPath.Substring(0, fieldPath.IndexOf('.'));
                        typeBytes[typeName][mode] = checked(typeBytes[typeName][mode] + atomBytes);
                        if (mode == modes[0])
                        {
                            bool freeText = atom.GetProperty("freeTextModeEligible").GetBoolean();
                            if (freeText != exactFreeTextPaths.Contains(fieldPath))
                                throw new InvalidOperationException(
                                    "Payload free-text classification drifted: " + fieldPath);
                            string candidate = atom.GetProperty("candidateValueEncoding").GetString()
                                ?? string.Empty;
                            int int32;
                            long int64;
                            if (kind == "int32" && (!int.TryParse(candidate,
                                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int32)
                                || candidate != int32.ToString(CultureInfo.InvariantCulture)))
                                throw new InvalidOperationException("Invalid int32 payload candidate: " + fieldPath);
                            if (kind == "int64" && (!long.TryParse(candidate,
                                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int64)
                                || candidate != int64.ToString(CultureInfo.InvariantCulture)))
                                throw new InvalidOperationException("Invalid int64 payload candidate: " + fieldPath);
                            if (kind == "bool" && candidate != "0" && candidate != "1")
                                throw new InvalidOperationException("Invalid Boolean payload candidate: " + fieldPath);
                            if ((kind == "list" || kind == "row" || kind == "nullable_row")
                                && string.IsNullOrWhiteSpace(
                                    atom.GetProperty("minimumRowFactoryId").GetString()))
                                throw new InvalidOperationException("Payload row/list lacks a factory: " + fieldPath);
                            parsedAtoms.Add(new PayloadAtom
                            {
                                path = fieldPath,
                                kind = kind,
                                scopes = new HashSet<string>(atom.GetProperty("scopeMask")
                                    .EnumerateArray().Select(scope => scope.GetString()),
                                    StringComparer.Ordinal),
                                bytesByMode = new Dictionary<string, ulong>(StringComparer.Ordinal)
                            });
                        }
                        parsedAtoms[index].bytesByMode[mode] = atomBytes;
                    }
                    totals.Add(mode, total);
                }
                return new PayloadAtomAudit
                {
                    typeCount = typeCount,
                    atomCount = atoms.Count,
                    minimumSchemaLogicalBytes = totals,
                    atoms = parsedAtoms,
                    typeBytesByMode = typeBytes
                };
            }
        }

        private static void ValidateCatalog(Catalog catalog, HashSet<string> pureGateIds)
        {
            if (catalog.dimensions.Count != 64)
                throw new InvalidOperationException("Expected exactly 64 ordered T17.6 dimensions.");
            if (string.IsNullOrEmpty(catalog.m0SelectedVectorId)
                || catalog.m0SelectedVectorId.Length != 64
                || catalog.m0SelectedVectorId.Any(value => !((value >= '0' && value <= '9')
                    || (value >= 'a' && value <= 'f'))))
                throw new InvalidOperationException("The recorded M0-selected vector ID is invalid.");
            if (string.IsNullOrWhiteSpace(catalog.dimensionGateId)
                || pureGateIds == null || !pureGateIds.Contains(catalog.dimensionGateId))
                throw new InvalidOperationException(
                    "Every capacity dimension must map to a registered executable gate.");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Dimension dimension in catalog.dimensions)
            {
                if (string.IsNullOrWhiteSpace(dimension.name) || !names.Add(dimension.name)
                    || dimension.values == null || dimension.values.Count == 0
                    || dimension.values.Distinct(StringComparer.Ordinal).Count() != dimension.values.Count)
                    throw new InvalidOperationException("Invalid dimension: " + dimension.name);
                string start;
                if (!catalog.start.TryGetValue(dimension.name, out start)
                    || !dimension.values.Contains(start, StringComparer.Ordinal))
                    throw new InvalidOperationException("Missing/list-invalid start for " + dimension.name);
                foreach (string value in dimension.values) ParseUnsignedTuple(value);
            }
            if (catalog.start.Count != catalog.dimensions.Count)
                throw new InvalidOperationException("Start vector contains an extra dimension.");
            foreach (List<string> bundle in catalog.bundles)
            {
                if (bundle.Count == 0 || bundle.Distinct(StringComparer.Ordinal).Count() != bundle.Count
                    || bundle.Any(name => !names.Contains(name)))
                    throw new InvalidOperationException("Invalid interaction bundle.");
            }
        }

        private static List<Candidate> GenerateCandidates(Catalog catalog)
        {
            Dictionary<string, Candidate> byEncoding = new Dictionary<string, Candidate>(StringComparer.Ordinal);
            AddCandidate(catalog, byEncoding, catalog.start, "seed:S");
            AddCandidate(catalog, byEncoding,
                MemoryCapacityContracts.ProvisionalProduction().ToDictionary(
                    row => row.name, row => row.valueEncoding, StringComparer.Ordinal),
                "codeFallback:provisionalProduction");
            foreach (Dimension dimension in catalog.dimensions)
            {
                for (int index = 0; index < dimension.values.Count; index++)
                {
                    Dictionary<string, string> values = Copy(catalog.start);
                    values[dimension.name] = dimension.values[index];
                    AddCandidate(catalog, byEncoding, values,
                        "oneFactor:" + dimension.name + ":" + index.ToString(CultureInfo.InvariantCulture));
                }
            }
            AddCandidate(catalog, byEncoding, catalog.dimensions.ToDictionary(
                row => row.name, row => row.values[0], StringComparer.Ordinal), "allLow");
            AddCandidate(catalog, byEncoding, catalog.dimensions.ToDictionary(
                row => row.name, row => row.values[row.values.Count - 1], StringComparer.Ordinal), "allHigh");

            for (int bundleIndex = 0; bundleIndex < catalog.bundles.Count; bundleIndex++)
                AddBundleCandidates(catalog, byEncoding, catalog.bundles[bundleIndex], bundleIndex, 0,
                    Copy(catalog.start), new List<int>());

            List<Candidate> result = byEncoding.Values.OrderBy(row => row.encoding, StringComparer.Ordinal).ToList();
            for (int index = 0; index < result.Count; index++) result[index].vectorOrdinal = index;
            return result;
        }

        /// <summary>
        /// Proves the catalog's shared M0-VECTOR-GENERATOR gate actually visits every valid
        /// one-factor value for every retained capacity dimension.
        /// </summary>
        private static void ValidateVectorGeneratorCoverage(
            Catalog catalog,
            List<Candidate> candidates)
        {
            Dictionary<string, Candidate> byEncoding = candidates.ToDictionary(
                row => row.encoding, StringComparer.Ordinal);
            foreach (Dimension dimension in catalog.dimensions)
            {
                for (int index = 0; index < dimension.values.Count; index++)
                {
                    Dictionary<string, string> values = Copy(catalog.start);
                    values[dimension.name] = dimension.values[index];
                    if (!CrossCapsValid(values)) continue;
                    string encoding = EncodeVector(catalog.dimensions, values);
                    Candidate candidate;
                    string origin = "oneFactor:" + dimension.name + ":"
                        + index.ToString(CultureInfo.InvariantCulture);
                    if (!byEncoding.TryGetValue(encoding, out candidate)
                        || !candidate.origins.Contains(origin))
                    {
                        throw new InvalidOperationException(
                            catalog.dimensionGateId + " missed " + dimension.name
                            + " value " + dimension.values[index] + ".");
                    }
                }
            }
        }

        private static void AddBundleCandidates(Catalog catalog, Dictionary<string, Candidate> target,
            List<string> bundle, int bundleIndex, int memberIndex, Dictionary<string, string> values,
            List<int> coordinates)
        {
            if (memberIndex == bundle.Count)
            {
                AddCandidate(catalog, target, values, "bundle:"
                    + (bundleIndex + 1).ToString(CultureInfo.InvariantCulture) + ":"
                    + string.Join("/", coordinates.Select(value => value.ToString(CultureInfo.InvariantCulture))));
                return;
            }

            Dimension dimension = catalog.dimensions.First(row => row.name == bundle[memberIndex]);
            string[] points = { dimension.values[0], catalog.start[dimension.name], dimension.values[dimension.values.Count - 1] };
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                Dictionary<string, string> next = Copy(values);
                next[dimension.name] = points[pointIndex];
                List<int> nextCoordinates = new List<int>(coordinates) { pointIndex };
                AddBundleCandidates(catalog, target, bundle, bundleIndex, memberIndex + 1, next, nextCoordinates);
            }
        }

        private static void AddCandidate(Catalog catalog, Dictionary<string, Candidate> target,
            Dictionary<string, string> values, string origin)
        {
            if (!CrossCapsValid(values)) return;
            string encoding = EncodeVector(catalog.dimensions, values);
            Candidate candidate;
            if (!target.TryGetValue(encoding, out candidate))
            {
                candidate = new Candidate
                {
                    values = Copy(values),
                    encoding = encoding,
                    vectorId = Sha256Hex(Encoding.UTF8.GetBytes(encoding)),
                    numericCoordinates = catalog.dimensions
                        .SelectMany(row => ParseUnsignedTuple(values[row.name])).ToArray(),
                    complexityScore = catalog.dimensions.Sum(row =>
                        row.values.IndexOf(values[row.name]))
                };
                target.Add(encoding, candidate);
            }
            candidate.origins.Add(origin);
        }

        private static bool CrossCapsValid(Dictionary<string, string> values)
        {
            ulong[] libraryOwners = ParseUnsignedTuple(values["libraryOwnerEntries"]);
            ulong[] ownerSlots = ParseUnsignedTuple(values["ownerSlotTriple"]);
            ulong[] importedOwnerUnknown = ParseUnsignedTuple(values["importedOwnerUnknownBytes"]);
            ulong[] requestOwnerGlobal = ParseUnsignedTuple(values["activeRequestsOwnerGlobal"]);
            ulong[] variantsAttempts = ParseUnsignedTuple(values["frozenVariantAttemptCaps"]);
            ulong[] audit = ParseUnsignedTuple(values["attemptAuditRowsPerRequestGlobal"]);
            ulong[] evidence = ParseUnsignedTuple(values["frozenEvidenceGuardDiagnosticCaps"]);
            ulong[] globalBlocks = ParseUnsignedTuple(values["globalBlockCaps"]);
            return libraryOwners[0] >= ownerSlots[0] + ParseOne(values, "importedOwnerCount") + 1
                && ParseOne(values, "activeOwnerBytes") <= ParseOne(values, "combinedOwnerBytes")
                && importedOwnerUnknown[0] <= ParseOne(values, "combinedOwnerBytes")
                && ParseOne(values, "activeGlobalBytes") <= ParseOne(values, "combinedGlobalBytes")
                && ParseOne(values, "importedGlobalBytes") <= ParseOne(values, "combinedGlobalBytes")
                && requestOwnerGlobal[0] <= requestOwnerGlobal[1]
                && requestOwnerGlobal[1] <= ParseOne(values, "runtimeQueueEntries")
                && variantsAttempts[0] <= variantsAttempts[1] && variantsAttempts[1] <= audit[0]
                && evidence[0] == 2
                && ParseOne(values, "editedBlocksOwner") <= ParseOne(values, "manageableBlocksPerOwner")
                && ParseOne(values, "editedBlocksGlobal") <= globalBlocks[0]
                && globalBlocks[1] >= globalBlocks[0]
                && globalBlocks[1] - globalBlocks[0] >= ownerSlots[0]
                && ownerSlots[1] >= ownerSlots[0] + 1 && ownerSlots[2] <= ownerSlots[0]
                && ParseOne(values, "importedGlobalRows") >= ParseOne(values, "importedOwnerRows")
                && ParseOne(values, "importedGlobalRows") >= ParseOne(values, "importedUnknownRows");
        }

        private static Dictionary<string, ScenarioAudit> Evaluate(
            List<Candidate> candidates,
            List<SyntheticScenario> scenarios,
            PayloadAtomAudit payload)
        {
            Dictionary<string, ScenarioAudit> audits = scenarios.ToDictionary(
                scenario => scenario.scenarioId,
                scenario => new ScenarioAudit
                {
                    scenarioId = scenario.scenarioId,
                    expectedGate = scenario.expectedGate
                },
                StringComparer.Ordinal);
            string[] modes =
            {
                "asciiByteBoundary", "utf8WorstPerUtf16Unit", "xmlEscapeWorstPerUtf16Unit"
            };
            foreach (Candidate candidate in candidates)
            {
                EstablishGcBaseline();
                foreach (int threadTarget in new[] { 4, 12, 64 })
                foreach (string mode in modes)
                {
                    List<LogicalAtom> combinedAtoms = ScopeTemplate(payload, "combined_global", mode);
                    List<LogicalAtom> ownerAtoms = ScopeTemplate(payload, "combined_owner", mode);
                    FillResult combined = RunCatalogCycleSurrogateFill(
                        combinedAtoms,
                        ParseOne(candidate.values, "combinedGlobalBytes"));
                    FillResult owner = RunCatalogCycleSurrogateFill(
                        ownerAtoms,
                        ParseOne(candidate.values, "combinedOwnerBytes"));
                    WorkInput work = BuildWorkInput(candidate, threadTarget, mode);
                    StatisticalMeasurement time = MeasureCoordinateMicroseconds(work);
                    StatisticalMeasurement allocation = MeasureCoordinateAllocationBytes(work);
                    CoordinateEvaluation coordinate = new CoordinateEvaluation
                    {
                        threadTarget = threadTarget,
                        textMode = mode,
                        combinedBytes = combined.admittedBytes,
                        ownerWorstBytes = owner.admittedBytes,
                        ownerTypicalBytes = MeasureTypicalOwnerBytes(payload),
                        combinedFill = combined,
                        time = time,
                        allocation = allocation
                    };
                    candidate.coordinates.Add(coordinate);

                    string commonFailure = ValidateCommonContractCells(
                        candidate.values, threadTarget, combined, owner);
                    if (commonFailure.Length != 0 && candidate.rejection.Length == 0)
                        candidate.rejection = commonFailure;

                    foreach (SyntheticScenario scenario in scenarios)
                    foreach (string scenarioCoordinate in ApplicableScenarioCoordinates(
                        scenario, threadTarget))
                    {
                        bool passed = RunSyntheticScenario(
                            scenario.scenarioId,
                            scenarioCoordinate,
                            candidate.values,
                            threadTarget,
                            mode,
                            payload,
                            combined,
                            owner);
                        ScenarioAudit audit = audits[scenario.scenarioId];
                        audit.evaluatedCells++;
                        if (passed) audit.passedCells++;
                        audit.resultEncoding.Append(OrdinalSegmentCodec.Segment(candidate.vectorId));
                        audit.resultEncoding.Append(OrdinalSegmentCodec.Segment(
                            threadTarget.ToString(CultureInfo.InvariantCulture)));
                        audit.resultEncoding.Append(OrdinalSegmentCodec.Segment(mode));
                        audit.resultEncoding.Append(OrdinalSegmentCodec.Segment(scenarioCoordinate));
                        audit.resultEncoding.Append(OrdinalSegmentCodec.Segment(passed ? "pass" : "fail"));
                        if (!passed && candidate.rejection.Length == 0)
                            candidate.rejection = scenario.expectedGate;
                    }
                }

                candidate.surrogateCombinedBytes = candidate.coordinates.Max(row => row.combinedBytes);
                candidate.ownerWorstBytes = candidate.coordinates.Max(row => row.ownerWorstBytes);
                candidate.ownerTypicalBytes = candidate.coordinates.Max(row => row.ownerTypicalBytes);
                candidate.pureMaxIndivisibleItemMicroseconds = candidate.coordinates
                    .Max(row => row.time.maximum);
                candidate.pureAllocationTieBreakBytes = checked((long)candidate.coordinates
                    .Max(row => row.allocation.maximum));
                candidate.maximumCultureLabelDtoBytes = MeasureCultureLabelDtoBytes(candidate.values);
                if (candidate.rejection.Length != 0) continue;
                if (candidate.ownerTypicalBytes > 65536UL)
                    candidate.rejection = "SURROGATE-OWNER-TYPICAL";
                else if (candidate.ownerWorstBytes > 524288UL)
                    candidate.rejection = "SURROGATE-OWNER-WORST";
                else if (candidate.surrogateCombinedBytes > 16777216UL)
                    candidate.rejection = "SURROGATE-GLOBAL";
                else if (candidate.maximumCultureLabelDtoBytes > 131072UL)
                    candidate.rejection = "SURROGATE-DTO-LIST-CULTURE-STRINGS";
                else if (ParseOne(candidate.values, "sliceTargetMicroseconds") > 1000UL
                    || candidate.pureMaxIndivisibleItemMicroseconds > 2000UL)
                    candidate.rejection = "PERF-SLICE-SCHEDULER";
                else candidate.feasible = true;
            }

            foreach (SyntheticScenario scenario in scenarios)
            {
                ScenarioAudit audit = audits[scenario.scenarioId];
                long coordinatesPerVector = scenario.scenarioId == "worst-case-owner-v1"
                    ? 3L * 3L
                    : checked(3L * 3L * scenario.coordinates.Count);
                long expectedCells = checked((long)candidates.Count * coordinatesPerVector);
                if (audit.evaluatedCells != expectedCells)
                    throw new InvalidOperationException(
                        "Synthetic scenario coverage count drifted: " + audit.scenarioId);
            }
            return audits;
        }

        /// <summary>
        /// M11's owner-directory corpus always materializes both bounded culture labels in every
        /// row. Count their exact logical UTF-16 payload so a benchmark cannot silently omit the
        /// culture panel while claiming the Library string gate.
        /// </summary>
        private static ulong MeasureCultureLabelDtoBytes(Dictionary<string, string> values)
        {
            checked
            {
                ulong rows = ParseOne(values, "libraryWindowRows");
                ulong unitsPerLabel = ParseOne(values, "frozenDisplayLabelUnits");
                return rows * 2UL * unitsPerLabel * 2UL;
            }
        }

        private static IEnumerable<string> ApplicableScenarioCoordinates(
            SyntheticScenario scenario,
            int threadTarget)
        {
            if (scenario.scenarioId != "worst-case-owner-v1") return scenario.coordinates;
            string exact = "N=" + threadTarget.ToString(CultureInfo.InvariantCulture);
            List<string> matching = scenario.coordinates.Where(coordinate =>
                string.Equals(coordinate, exact, StringComparison.Ordinal)).ToList();
            if (matching.Count != 1)
            {
                throw new InvalidOperationException(
                    "Worst-case owner scenario must declare exactly one coordinate for " + exact);
            }
            return matching;
        }

        private static List<LogicalAtom> ScopeTemplate(
            PayloadAtomAudit payload,
            string scope,
            string mode)
        {
            List<LogicalAtom> result = new List<LogicalAtom>();
            HashSet<string> framedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (PayloadAtom atom in payload.atoms)
            {
                if (!atom.scopes.Contains(scope)) continue;
                string typeName = atom.path.Substring(0, atom.path.IndexOf('.'));
                if (framedTypes.Add(typeName))
                    result.Add(new LogicalAtom { path = typeName + ".$rowFraming", bytes = 64UL });
                result.Add(new LogicalAtom { path = atom.path, bytes = atom.bytesByMode[mode] });
            }
            if (result.Count == 0 || result.All(atom => atom.bytes == 0))
                throw new InvalidOperationException("Payload scope has no logical atoms: " + scope);
            return result;
        }

        private static FillResult RunCatalogCycleSurrogateFill(List<LogicalAtom> atoms, ulong cap)
        {
            // M0 has contracts but no production reducer/schema walker yet. Repeating the complete
            // catalog atom cycle gives a deterministic byte-boundary surrogate; it is deliberately
            // named and reported as such so it cannot be mistaken for the T17.3 field-cap walk.
            if (cap == ulong.MaxValue) throw new InvalidOperationException("Unbounded payload cap.");
            FillResult result = new FillResult();
            foreach (LogicalAtom atom in atoms)
            {
                result.visitedAtoms++;
                ulong next;
                if (!TryAdmitLogicalAtom(result.admittedBytes, atom.bytes, cap, out next))
                {
                    result.firstRefusedPath = atom.path;
                    return result;
                }
                result.admittedBytes = next;
            }
            result.admittedCatalogCycles = 1;
            ulong cycleBytes = result.admittedBytes;
            if (cycleBytes == 0) throw new InvalidOperationException("Zero-byte payload schema cycle.");
            ulong additionalCycles = (cap - result.admittedBytes) / cycleBytes;
            result.admittedBytes = checked(result.admittedBytes + additionalCycles * cycleBytes);
            result.admittedCatalogCycles = checked(result.admittedCatalogCycles + additionalCycles);
            foreach (LogicalAtom atom in atoms)
            {
                result.visitedAtoms++;
                ulong next;
                if (!TryAdmitLogicalAtom(result.admittedBytes, atom.bytes, cap, out next))
                {
                    result.firstRefusedPath = atom.path;
                    break;
                }
                result.admittedBytes = next;
            }
            if (result.firstRefusedPath.Length == 0)
                throw new InvalidOperationException(
                    "Catalog-cycle surrogate failed to reach a refusal boundary.");
            ulong unchanged;
            if (TryAdmitLogicalAtom(cap, 1UL, cap, out unchanged) || unchanged != cap
                || TryAdmitLogicalAtom(0UL, checked(cap + 1UL), cap, out unchanged) || unchanged != 0UL)
                throw new InvalidOperationException("Cap/cap+1 atomic-refusal golden failed.");
            return result;
        }

        private static ulong MeasureTypicalOwnerBytes(PayloadAtomAudit payload)
        {
            // T17.3 fixes the typical corpus at 20 owners, each with 12 roots and 12 blocks.
            // This method returns the selected owner's charge, so the 20-owner population does not
            // multiply the result and neither N nor the saturation text mode changes its shape.
            const ulong roots = 12UL;
            ulong blocks = checked(roots * 12UL);
            checked
            {
                // The typical corpus uses minimum-valid strings rather than the all-fields saturation
                // candidates: every block has its primary subject, alternate blocks have one fact,
                // and every fourth block has one provenance row.
                return MinimumTypeBytes(payload, "PawnKnowledgeState")
                    + MinimumTypeBytes(payload, "PawnReflectionStateMemoryFields")
                    + roots * (MinimumTypeBytes(payload, "SavedMemoryThreadRoot")
                        + MinimumTypeBytes(payload, "SavedMemoryChapter"))
                    + blocks * (MinimumTypeBytes(payload, "SavedMemoryBlock")
                        + MinimumTypeBytes(payload, "SavedMemorySubjectRef"))
                    + ((blocks + 1UL) / 2UL)
                        * MinimumTypeBytes(payload, "SavedMemoryCanonicalFact")
                    + ((blocks + 3UL) / 4UL)
                        * MinimumTypeBytes(payload, "SavedMemoryProvenance");
            }
        }

        private static ulong MinimumTypeBytes(PayloadAtomAudit payload, string typeName)
        {
            ulong total = 64UL;
            string prefix = typeName + ".";
            foreach (PayloadAtom atom in payload.atoms.Where(row =>
                row.path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                total = checked(total + (atom.kind == "string"
                    ? 4UL
                    : atom.bytesByMode["asciiByteBoundary"]));
            }
            return total;
        }

        private static ulong TypeBytes(PayloadAtomAudit payload, string typeName, string mode)
        {
            Dictionary<string, ulong> byMode;
            if (!payload.typeBytesByMode.TryGetValue(typeName, out byMode))
                throw new InvalidOperationException("Unknown payload type: " + typeName);
            return byMode[mode];
        }

        private static string ValidateCommonContractCells(
            Dictionary<string, string> values,
            int threadTarget,
            FillResult combined,
            FillResult owner)
        {
            ulong manageable = ParseOne(values, "manageableBlocksPerOwner");
            if (manageable < (ulong)threadTarget) return "M0-RETENTION-ORDINARY";
            if (ParseOne(values, "editedBlocksOwner") > manageable
                || ParseOne(values, "editedBlocksGlobal")
                    > ParseUnsignedTuple(values["globalBlockCaps"])[0])
                return "M0-RETENTION-EMERGENCY";
            if (combined.admittedBytes > ParseOne(values, "combinedGlobalBytes")
                || owner.admittedBytes > ParseOne(values, "combinedOwnerBytes")
                || combined.firstRefusedPath.Length == 0
                || owner.firstRefusedPath.Length == 0)
                return "M0-CAP-PLUS-ONE";
            ulong[] ownerSlots = ParseUnsignedTuple(values["ownerSlotTriple"]);
            if (ownerSlots[1] != ownerSlots[0] + 1UL || ownerSlots[2] > ownerSlots[0])
                return "M0-BRAINWIPE-TARGET-ONLY";
            if (ParseOne(values, "sliceWorkItems") == 0
                || ParseOne(values, "sliceTargetMicroseconds") == 0)
                return "M0-SCHEDULER-STOP";
            return string.Empty;
        }

        private static bool RunSyntheticScenario(
            string scenarioId,
            string scenarioCoordinate,
            Dictionary<string, string> values,
            int threadTarget,
            string mode,
            PayloadAtomAudit payload,
            FillResult combined,
            FillResult owner)
        {
            switch (scenarioId)
            {
                case "worst-case-owner-v1":
                    return ValidateWorstCaseOwnerScenario(
                        scenarioCoordinate, threadTarget, values, owner);
                case "exact-subject-collision-v1":
                    return ValidateExactSubjectScenario(scenarioCoordinate);
                case "duplicate-root-v1":
                    return ValidateDuplicateRootScenario(scenarioCoordinate);
                case "summary-saturation-v1":
                    return ValidateSummarySaturationScenario(scenarioCoordinate, values);
                case "player-edited-saturation-v1":
                    return ValidatePlayerEditedScenario(scenarioCoordinate, values);
                case "global-faction-state-v1":
                    return ValidateFactionScenario(scenarioCoordinate, values);
                case "migration-overflow-v1":
                    return ValidateMigrationOverflowScenario(
                        scenarioCoordinate, values, payload, mode, combined);
                default:
                    throw new InvalidOperationException(
                        "Synthetic scenario has no executable implementation: " + scenarioId);
            }
        }

        private static bool ValidateWorstCaseOwnerScenario(
            string coordinate,
            int threadTarget,
            Dictionary<string, string> values,
            FillResult owner)
        {
            int declaredTarget;
            if (!coordinate.StartsWith("N=", StringComparison.Ordinal)
                || !int.TryParse(
                    coordinate.Substring(2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out declaredTarget)
                || (declaredTarget != 4 && declaredTarget != 12 && declaredTarget != 64))
            {
                throw new InvalidOperationException(
                    "Unknown worst-case-owner coordinate: " + coordinate);
            }

            // The N coordinate selects its matching authenticated outer N cell; it is not a
            // decorative label or a second Cartesian copy of the same owner fixture.
            return declaredTarget == threadTarget
                && owner.admittedBytes <= ParseOne(values, "combinedOwnerBytes")
                && owner.firstRefusedPath.Length != 0;
        }

        private static bool ValidateExactSubjectScenario(string coordinate)
        {
            MemoryThreadRouteRule route = new MemoryThreadRouteRule { subjectKind = "pawn" };
            route.equivalentExtractors.Add(new MemoryRouteExtractor { extractorToken = "primary" });
            route.equivalentExtractors.Add(new MemoryRouteExtractor { extractorToken = "fallback" });
            MemoryRouteResolution selected = MemoryThreadRoutingPolicy.Resolve("owner", route, new[]
            {
                RouteCandidate("fallback", "subject", "fallback-label"),
                RouteCandidate("primary", "subject", "primary-label")
            });
            MemoryRouteResolution collision = MemoryThreadRoutingPolicy.Resolve("owner", route, new[]
            {
                RouteCandidate("primary", "subject-a", "same-label"),
                RouteCandidate("fallback", "subject-b", "same-label")
            });
            MemoryRouteResolution ownerSelf = MemoryThreadRoutingPolicy.Resolve("owner", route, new[]
            {
                RouteCandidate("primary", "owner", "owner")
            });
            switch (coordinate)
            {
                case "same-label-different-id":
                    return collision.reasonToken
                        == MemoryThreadRoutingPolicy.StandaloneAmbiguousIdentity;
                case "same-id-different-label":
                    return selected.isThreaded && selected.subjectId == "subject"
                        && selected.frozenLabel == "primary-label";
                case "owner-self":
                    return ownerSelf.reasonToken == MemoryThreadRoutingPolicy.StandaloneOwnerSelf;
                default:
                    throw new InvalidOperationException(
                        "Unknown exact-subject coordinate: " + coordinate);
            }
        }

        private static MemoryRouteCandidate RouteCandidate(
            string extractor,
            string subjectId,
            string label)
        {
            return new MemoryRouteCandidate
            {
                extractorToken = extractor,
                subjectKind = "pawn",
                subjectId = subjectId,
                frozenLabel = label
            };
        }

        private static bool ValidateDuplicateRootScenario(string coordinate)
        {
            MemoryEpochAllocationPlan epoch = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest { ownerPawnId = "owner", lastIssuedSequence = 0 });
            MemoryRootIdentity root = new MemoryRootIdentity
            {
                ownerPawnId = "owner",
                ownerEpochToken = epoch.epochToken,
                primarySubjectKind = "pawn",
                primarySubjectId = "subject-a"
            };
            string first;
            string retry;
            string other;
            if (!epoch.canMutate
                || !MemoryIdentityCodec.TryCreateRootId(root, out first)
                || !MemoryIdentityCodec.TryCreateRootId(root, out retry)) return false;
            root.primarySubjectId = "subject-b";
            bool distinct = MemoryIdentityCodec.TryCreateRootId(root, out other)
                && first != other;
            switch (coordinate)
            {
                case "semantic-duplicate":
                    return first == retry;
                case "opaque-id-conflict":
                    return distinct;
                case "permutation":
                    MemorySourceOccurrenceFallback fallback = new MemorySourceOccurrenceFallback
                    {
                        stableSignalToken = "fixture",
                        eventTickInvariant = 1,
                        sourceLocalSequenceInvariant = 1,
                        factDiscriminator = "fact",
                        sourceProvesUniqueness = true,
                        subjects = new List<MemoryTypedSubject>
                        {
                            new MemoryTypedSubject { subjectKind = "pawn", subjectId = "subject-b" },
                            new MemoryTypedSubject { subjectKind = "pawn", subjectId = "subject-a" }
                        }
                    };
                    string ordered;
                    string permuted;
                    if (!MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(
                            fallback, out ordered)) return false;
                    fallback.subjects.Reverse();
                    return MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(
                            fallback, out permuted)
                        && ordered == permuted;
                default:
                    throw new InvalidOperationException(
                        "Unknown duplicate-root coordinate: " + coordinate);
            }
        }

        private static bool ValidateSummarySaturationScenario(
            string coordinate,
            Dictionary<string, string> values)
        {
            ulong buckets = ParseOne(values, "factBuckets");
            ulong contributions = ParseUnsignedTuple(
                values["datedContributionDescriptorMatchCaps"])[0];
            ulong unchanged;
            MemoryRootIdentity root = new MemoryRootIdentity
            {
                ownerPawnId = "owner",
                ownerEpochToken = OrdinalSegmentCodec.Segment("memory-epoch-v1")
                    + OrdinalSegmentCodec.Segment("1"),
                primarySubjectKind = "pawn",
                primarySubjectId = "subject"
            };
            string first;
            string second;
            switch (coordinate)
            {
                case "rolling-one":
                    return MemoryIdentityCodec.TryCreateRollingSummaryId(root, out first)
                        && MemoryIdentityCodec.TryCreateRollingSummaryId(root, out second)
                        && first == second;
                case "closed-many":
                    return MemoryIdentityCodec.TryCreateClosedSummaryId(root, 1, out first)
                        && MemoryIdentityCodec.TryCreateClosedSummaryId(root, 2, out second)
                        && first != second;
                case "contribution-cap+1":
                    return buckets > 0 && contributions > 0
                        && TryAdmitLogicalAtom(0, buckets, buckets, out unchanged)
                        && unchanged == buckets
                        && !TryAdmitLogicalAtom(buckets, 1, buckets, out unchanged)
                        && unchanged == buckets
                        && !TryAdmitLogicalAtom(
                            0, checked(contributions + 1), contributions, out unchanged)
                        && unchanged == 0;
                default:
                    throw new InvalidOperationException(
                        "Unknown summary-saturation coordinate: " + coordinate);
            }
        }

        private static bool ValidatePlayerEditedScenario(
            string coordinate,
            Dictionary<string, string> values)
        {
            ulong ownerEdited = ParseOne(values, "editedBlocksOwner");
            ulong globalEdited = ParseOne(values, "editedBlocksGlobal");
            ulong ownerCap = ParseOne(values, "manageableBlocksPerOwner");
            ulong globalCap = ParseUnsignedTuple(values["globalBlockCaps"])[0];
            ulong unchanged;
            switch (coordinate)
            {
                case "owner-cap":
                    return ownerEdited <= ownerCap
                        && TryAdmitLogicalAtom(0, ownerEdited, ownerCap, out unchanged);
                case "global-cap":
                    return globalEdited <= globalCap
                        && TryAdmitLogicalAtom(0, globalEdited, globalCap, out unchanged);
                case "protected-cap+1":
                    return !TryAdmitLogicalAtom(ownerCap, 1, ownerCap, out unchanged)
                        && unchanged == ownerCap;
                default:
                    throw new InvalidOperationException(
                        "Unknown player-edited coordinate: " + coordinate);
            }
        }

        private static bool ValidateFactionScenario(
            string coordinate,
            Dictionary<string, string> values)
        {
            string one;
            string two;
            ulong cap = ParseOne(values, "factionSnapshots");
            ulong unchanged;
            switch (coordinate)
            {
                case "same-def-different-instance":
                    return MemoryIdentityCodec.TryCreateFactionSubjectId("faction-instance-a", 1, out one)
                        && MemoryIdentityCodec.TryCreateFactionSubjectId("faction-instance-b", 1, out two)
                        && one != two;
                case "generation-reuse":
                    return MemoryIdentityCodec.TryCreateFactionSubjectId("faction-instance", 1, out one)
                        && MemoryIdentityCodec.TryCreateFactionSubjectId("faction-instance", 2, out two)
                        && one != two;
                case "cap+1":
                    return cap > 0
                        && TryAdmitLogicalAtom(0, cap, cap, out unchanged)
                        && !TryAdmitLogicalAtom(cap, 1, cap, out unchanged)
                        && unchanged == cap;
                default:
                    throw new InvalidOperationException(
                        "Unknown faction-state coordinate: " + coordinate);
            }
        }

        private static bool ValidateMigrationOverflowScenario(
            string coordinate,
            Dictionary<string, string> values,
            PayloadAtomAudit payload,
            string mode,
            FillResult combined)
        {
            ulong resolvedRows = ParseOne(values, "importedOwnerRows");
            ulong unknownRows = ParseOne(values, "importedUnknownRows");
            ulong globalRows = ParseOne(values, "importedGlobalRows");
            ulong unitBytes = checked(TypeBytes(payload, "SavedImportedMemoryRow", mode)
                * resolvedRows);
            ulong cap = ParseOne(values, "combinedGlobalBytes");
            ulong prefix = unitBytes <= cap ? cap - unitBytes : 0;
            ulong admitted;
            bool wholeUnit = unitBytes <= cap
                && TryAdmitLogicalAtom(prefix, unitBytes, cap, out admitted)
                && admitted == cap;
            bool refusedWhole = !TryAdmitLogicalAtom(prefix, checked(unitBytes + 1), cap, out admitted)
                && admitted == prefix;
            switch (coordinate)
            {
                case "resolved":
                    return resolvedRows <= globalRows && wholeUnit;
                case "unknown-owner":
                    return unknownRows <= globalRows;
                case "raw-sidecar":
                    return combined.admittedBytes <= cap;
                case "global-cap+1":
                    return refusedWhole;
                default:
                    throw new InvalidOperationException(
                        "Unknown migration-overflow coordinate: " + coordinate);
            }
        }

        private static bool TryAdmitLogicalAtom(
            ulong current,
            ulong atomBytes,
            ulong cap,
            out ulong next)
        {
            next = current;
            if (current > cap || atomBytes > cap - current) return false;
            next = current + atomBytes;
            return true;
        }

        private static Candidate Select(List<Candidate> candidates)
        {
            Candidate selected = candidates.Where(row => row.feasible)
                .OrderBy(row => row.surrogateCombinedBytes)
                .ThenBy(row => row.pureMaxIndivisibleItemMicroseconds)
                .ThenBy(row => row.pureAllocationTieBreakBytes)
                .ThenBy(row => row, new NumericVectorComparer())
                .FirstOrDefault();
            if (selected == null)
            {
                string failures = string.Join(", ", candidates
                    .GroupBy(row => row.rejection, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .Select(group => group.Key + "=" + group.Count().ToString(
                        CultureInfo.InvariantCulture)));
                throw new InvalidOperationException(
                    "No provisionally feasible vector. Rejections: " + failures);
            }
            return selected;
        }

        /// <summary>
        /// M11 may retain the M0-selected vector or lower individual scalar/tuple members, but a
        /// release may never raise even one member without reopening M0 with new evidence.
        /// </summary>
        private static bool ComponentwiseNoGreater(Candidate release, Candidate selected)
        {
            if (release?.numericCoordinates == null || selected?.numericCoordinates == null
                || release.numericCoordinates.Length != selected.numericCoordinates.Length)
                return false;
            for (int index = 0; index < release.numericCoordinates.Length; index++)
                if (release.numericCoordinates[index] > selected.numericCoordinates[index])
                    return false;
            return true;
        }

        private static void ValidateComponentwiseReleaseGoldens()
        {
            Candidate selected = new Candidate { numericCoordinates = new ulong[] { 4, 8, 16 } };
            Candidate exact = new Candidate { numericCoordinates = new ulong[] { 4, 8, 16 } };
            Candidate lower = new Candidate { numericCoordinates = new ulong[] { 2, 8, 12 } };
            Candidate raisedTupleMember = new Candidate
                { numericCoordinates = new ulong[] { 4, 9, 12 } };
            Candidate malformed = new Candidate { numericCoordinates = new ulong[] { 4, 8 } };
            if (!ComponentwiseNoGreater(exact, selected)
                || !ComponentwiseNoGreater(lower, selected)
                || ComponentwiseNoGreater(raisedTupleMember, selected)
                || ComponentwiseNoGreater(malformed, selected))
                throw new InvalidOperationException(
                    "Componentwise M11 release-vector goldens failed.");
        }

        private static ManifestAudit BuildAndValidateManifestAudit(
            Catalog catalog,
            List<FixedRow> fixedRows,
            List<Candidate> candidates,
            Candidate selected)
        {
            if (fixedRows == null || fixedRows.Count == 0
                || fixedRows.Select(row => row.name).Distinct(StringComparer.Ordinal).Count()
                    != fixedRows.Count)
            {
                throw new InvalidOperationException("The fixed/derived policy registry is invalid.");
            }

            List<ManifestEntry> entries = new List<ManifestEntry>();
            foreach (Candidate candidate in candidates.Where(row => row.feasible))
            foreach (int threadTarget in new[] { 4, 12, 64 })
            {
                entries.Add(CreateManifestEntry(
                    "releaseCandidate", candidate.vectorId, candidate.encoding, candidate.values,
                    threadTarget, fixedRows));
            }

            Dictionary<string, string> defensiveValues = MemoryCapacityContracts.DefensiveCeilings()
                .ToDictionary(row => row.name, row => row.valueEncoding, StringComparer.Ordinal);
            if (!CrossCapsValid(defensiveValues))
                throw new InvalidOperationException("Defensive bundle D violates a cross-cap equation.");
            string defensiveEncoding = EncodeVector(catalog.dimensions, defensiveValues);
            string defensiveVectorId = Sha256Hex(Encoding.UTF8.GetBytes(defensiveEncoding));
            Candidate generatedAllHigh = candidates.Single(row => row.origins.Contains("allHigh"));
            if (!string.Equals(
                    generatedAllHigh.encoding, defensiveEncoding, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Defensive bundle D is not byte-equal to allHigh.");
            }
            foreach (int threadTarget in new[] { 4, 12, 64 })
            {
                entries.Add(CreateManifestEntry(
                    "defensiveCeilingAudit", defensiveVectorId, defensiveEncoding, defensiveValues,
                    threadTarget, fixedRows));
            }

            entries = entries.OrderBy(row => row.entryId, StringComparer.Ordinal).ToList();
            if (entries.Select(row => row.entryId).Distinct(StringComparer.Ordinal).Count()
                != entries.Count)
            {
                throw new InvalidOperationException("Provisional manifest entry IDs are not unique.");
            }
            for (int index = 0; index < entries.Count; index++) entries[index].entryOrdinal = index;

            foreach (IGrouping<string, ManifestEntry> siblings in entries.GroupBy(
                row => row.disposition + "\0" + row.vectorId, StringComparer.Ordinal))
            {
                string axes = string.Join("/", siblings.OrderBy(row => row.threadTarget)
                    .Select(row => row.threadTarget.ToString(CultureInfo.InvariantCulture)));
                if (siblings.Count() != 3 || axes != "4/12/64")
                    throw new InvalidOperationException("A provisional manifest sibling set is incomplete.");
            }

            // Selected-release audit rows must be derivable byte-for-byte even though loaded rerun
            // manifests remain pending until their production fixture exists.
            List<string> selectedAuditIds = new List<string>();
            foreach (int threadTarget in new[] { 4, 12, 64 })
            {
                selectedAuditIds.Add(CreateManifestEntry(
                    "selectedReleaseRerun", selected.vectorId, selected.encoding, selected.values,
                    threadTarget, fixedRows).entryId);
            }
            if (selectedAuditIds.Distinct(StringComparer.Ordinal).Count() != 3)
                throw new InvalidOperationException("Selected rerun entry IDs alias.");

            Dictionary<string, string> releaseValues = MemoryCapacityContracts.ProvisionalProduction()
                .ToDictionary(row => row.name, row => row.valueEncoding, StringComparer.Ordinal);
            string releaseEncoding = EncodeVector(catalog.dimensions, releaseValues);
            string releasePolicyEncoding = BuildPolicyEncoding(
                "memory-release-policy-v1", releaseEncoding, releaseValues, 12, fixedRows,
                "release_defaults_v1", new MemorySettingsPolicyFieldsV1());
            List<ManifestEntry> defensiveIdentityGoldens = new List<ManifestEntry>();
            foreach (string disposition in new[] { "releaseCandidate", "defensiveCeilingAudit" })
            foreach (int threadTarget in new[] { 4, 12, 64 })
            {
                defensiveIdentityGoldens.Add(CreateManifestEntry(
                    disposition, defensiveVectorId, defensiveEncoding, defensiveValues,
                    threadTarget, fixedRows));
            }
            if (defensiveIdentityGoldens.Select(row => row.entryId)
                    .Distinct(StringComparer.Ordinal).Count() != 6)
            {
                throw new InvalidOperationException("The allHigh/D six-entry identity golden aliases.");
            }

            return new ManifestAudit
            {
                entries = entries,
                fingerprint = HashTuple(
                    "memory-m0-provisional-manifest-audit-v1",
                    entries.Count.ToString(CultureInfo.InvariantCulture),
                    string.Join(string.Empty, entries.Select(row => OrdinalSegmentCodec.Segment(row.entryId)))),
                releasePolicyEncodingHash = Sha256Hex(Encoding.UTF8.GetBytes(releasePolicyEncoding)),
                defensiveIdentityGoldenRows = defensiveIdentityGoldens
            };
        }

        private static ManifestEntry CreateManifestEntry(
            string disposition,
            string vectorId,
            string vectorEncoding,
            Dictionary<string, string> vectorValues,
            int threadTarget,
            List<FixedRow> fixedRows)
        {
            string policyEncoding = BuildPolicyEncoding(
                "memory-effective-policy-v1", vectorEncoding, vectorValues, threadTarget,
                fixedRows, "all_features_on_v1",
                MemorySettingsPolicyFieldsV1.CreateBenchmarkProfile(threadTarget));
            string policyHash = Sha256Hex(Encoding.UTF8.GetBytes(policyEncoding));
            return new ManifestEntry
            {
                disposition = disposition,
                vectorId = vectorId,
                threadTarget = threadTarget,
                effectivePolicyHash = policyHash,
                entryId = HashTuple("memory-manifest-entry-v1", disposition, vectorId, policyHash)
            };
        }

        private static string BuildPolicyEncoding(
            string domain,
            string vectorEncoding,
            Dictionary<string, string> vectorValues,
            int threadTarget,
            List<FixedRow> fixedRows,
            string settingsProfile,
            MemorySettingsPolicyFieldsV1 settings)
        {
            StringBuilder policy = new StringBuilder();
            policy.Append(OrdinalSegmentCodec.Segment(domain));
            policy.Append(OrdinalSegmentCodec.Segment(vectorEncoding));
            policy.Append(OrdinalSegmentCodec.Segment(
                fixedRows.Count.ToString(CultureInfo.InvariantCulture)));
            foreach (FixedRow row in fixedRows)
            {
                string effectiveValue;
                if (row.name == "threadTarget")
                    effectiveValue = threadTarget.ToString(CultureInfo.InvariantCulture);
                else if (row.name == "summarySubjectLookupEntries")
                    effectiveValue = vectorValues["distinctSubjects"];
                else if (row.name == "summarySearchScratchUnits")
                    effectiveValue = vectorValues["normalizedSearchFieldUnits"];
                else if (row.name == "brainwipeMetadataReserveBytes")
                    effectiveValue = ComputeBrainwipeMetadataReserveBytes(vectorValues)
                        .ToString(CultureInfo.InvariantCulture);
                else
                {
                    ParseUnsignedTuple(row.value);
                    effectiveValue = row.value;
                }
                policy.Append(OrdinalSegmentCodec.Segment(row.name));
                policy.Append(OrdinalSegmentCodec.Segment(row.disposition));
                policy.Append(OrdinalSegmentCodec.Segment(effectiveValue));
                policy.Append(OrdinalSegmentCodec.Segment(row.gate));
            }
            policy.Append(OrdinalSegmentCodec.Segment(settingsProfile));
            policy.Append(OrdinalSegmentCodec.Segment(MemorySettingsPolicyCodec.Encode(settings)));
            return policy.ToString();
        }

        private static ulong ComputeBrainwipeMetadataReserveBytes(
            Dictionary<string, string> values)
        {
            // M0 has no Scribe rows yet. This is the catalog-owned logical-size surrogate for the
            // maximum empty epoch-fence envelope, one bounded diagnostic, and allocator-chain delta;
            // M1 replaces the atom walk with the shared production MemoryLogicalPayloadSizer.
            ulong rawIdentityUnits = ParseOne(values, "rawIdentitySegmentUnits");
            ulong completeKeyUnits = ParseUnsignedTuple(values["compositeKeyUnits"])[1];
            ulong diagnosticTextUnits = ParseUnsignedTuple(values["devReasonCountTextCaps"])[1];
            checked
            {
                const ulong rowFraming = 64UL;
                ulong ownerAndEpochStrings = 4UL + 3UL * rawIdentityUnits
                    + 4UL + 3UL * completeKeyUnits;
                ulong ownerScalars = 4UL + 2UL + (5UL * 8UL) + (7UL * 4UL);
                ulong diagnostic = rowFraming + 4UL + 4UL + 3UL * diagnosticTextUnits + 8UL;
                ulong fallbackChain = 4UL + 64UL;
                return rowFraming + ownerAndEpochStrings + ownerScalars
                    + diagnostic + fallbackChain;
            }
        }

        private sealed class NumericVectorComparer : IComparer<Candidate>
        {
            public int Compare(Candidate left, Candidate right)
            {
                ulong[] a = left.numericCoordinates;
                ulong[] b = right.numericCoordinates;
                for (int index = 0; index < Math.Min(a.Length, b.Length); index++)
                {
                    int comparison = a[index].CompareTo(b[index]);
                    if (comparison != 0) return comparison;
                }
                return a.Length.CompareTo(b.Length);
            }
        }

        private static void ValidateCodeFallback(Catalog catalog, List<Candidate> candidates)
        {
            List<MemoryCapacityContractRow> rows = MemoryCapacityContracts.ProvisionalProduction();
            if (rows.Count != catalog.dimensions.Count) throw new InvalidOperationException("Code fallback dimension count mismatch.");
            Dictionary<string, string> fallback = rows.ToDictionary(row => row.name, row => row.valueEncoding, StringComparer.Ordinal);
            string encoding = EncodeVector(catalog.dimensions, fallback);
            Candidate match = candidates.FirstOrDefault(row => row.encoding == encoding);
            if (match == null) throw new InvalidOperationException("Code fallback is not one normalized generated vector.");
        }

        private static WorkInput BuildWorkInput(Candidate candidate, int threadTarget, string mode)
        {
            int modeOrdinal = mode == "asciiByteBoundary" ? 0
                : mode == "utf8WorstPerUtf16Unit" ? 1 : 2;
            int seed = int.Parse(candidate.vectorId.Substring(0, 7), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            return new WorkInput
            {
                iterations = checked(64 + candidate.complexityScore * 4
                    + threadTarget + modeOrdinal * 8),
                allocationBytes = checked(32 + candidate.complexityScore * 8
                    + threadTarget + modeOrdinal * 16),
                seed = seed
            };
        }

        private static StatisticalMeasurement MeasureCoordinateMicroseconds(WorkInput input)
        {
            const int warmups = 25;
            const int observations = 200;
            for (int index = 0; index < warmups; index++) TimingOperation(input);
            List<ulong> samples = new List<ulong>(observations);
            for (int index = 0; index < observations; index++)
            {
                long start = Stopwatch.GetTimestamp();
                TimingOperation(input);
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (elapsed < 0) throw new InvalidOperationException("Stopwatch moved backwards.");
                samples.Add(ToConservativeMicroseconds(elapsed, Stopwatch.Frequency));
            }
            return Summarize(samples);
        }

        private static StatisticalMeasurement MeasureCoordinateAllocationBytes(WorkInput input)
        {
            const int warmups = 25;
            const int pairs = 200;
            for (int index = 0; index < warmups; index++) AllocationCandidate(input);
            List<ulong> samples = new List<ulong>(pairs);
            for (int pair = 0; pair < pairs; pair++)
            {
                bool candidateFirst = (pair & 1) != 0;
                long firstBefore = GC.GetAllocatedBytesForCurrentThread();
                if (candidateFirst) AllocationCandidate(input);
                else AllocationControl(input);
                long first = GC.GetAllocatedBytesForCurrentThread() - firstBefore;
                long secondBefore = GC.GetAllocatedBytesForCurrentThread();
                if (candidateFirst) AllocationControl(input);
                else AllocationCandidate(input);
                long second = GC.GetAllocatedBytesForCurrentThread() - secondBefore;
                long candidate = candidateFirst ? first : second;
                long control = candidateFirst ? second : first;
                long delta = candidate - control;
                if (delta < 0) throw new InvalidOperationException("Negative paired allocation delta.");
                samples.Add((ulong)delta);
            }
            return Summarize(samples);
        }

        private static void EstablishGcBaseline()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static StatisticalMeasurement Summarize(List<ulong> samples)
        {
            if (samples == null || samples.Count == 0)
                throw new InvalidOperationException("A statistical cell has no observations.");
            samples.Sort();
            int medianIndex = (samples.Count - 1) / 2;
            int p95Index = checked((int)Math.Ceiling(samples.Count * 0.95d) - 1);
            return new StatisticalMeasurement
            {
                observationCount = samples.Count,
                median = samples[medianIndex],
                p95 = samples[p95Index],
                maximum = samples[samples.Count - 1]
            };
        }

        private static void TimingOperation(WorkInput input)
        {
            int value = timingSink ^ input.seed;
            for (int index = 0; index < input.iterations; index++)
                value = unchecked(value * 31 + index);
            timingSink = value;
        }

        private static void AllocationCandidate(WorkInput input)
        {
            byte[] bytes = new byte[input.allocationBytes];
            bytes[0] = unchecked((byte)input.seed);
            bytes[bytes.Length - 1] = unchecked((byte)input.iterations);
            allocationSink = bytes;
        }

        private static void AllocationControl(WorkInput input)
        {
            timingSink = unchecked(timingSink ^ input.seed ^ input.iterations);
        }

        private static ulong ToConservativeMicroseconds(long rawTicks, long frequency)
        {
            if (rawTicks < 0 || frequency <= 0) throw new InvalidOperationException("Invalid timing input.");
            checked
            {
                ulong ticks = (ulong)rawTicks;
                ulong freq = (ulong)frequency;
                ulong quotient = ticks / freq;
                ulong remainder = ticks % freq;
                ulong ceiling = remainder == 0 ? 0 : (remainder * 1000000UL + freq - 1UL) / freq;
                return quotient * 1000000UL + ceiling;
            }
        }

        private static void ValidateTimingConversionGoldens()
        {
            if (ToConservativeMicroseconds(0, 10) != 0
                || ToConservativeMicroseconds(1, 3) != 333334
                || ToConservativeMicroseconds(3, 3) != 1000000)
            {
                throw new InvalidOperationException("Conservative microsecond conversion drifted.");
            }
            bool overflowed = false;
            try { ToConservativeMicroseconds(long.MaxValue, 1); }
            catch (OverflowException) { overflowed = true; }
            if (!overflowed)
                throw new InvalidOperationException("Timing conversion failed to reject overflow.");
        }

        private static void ValidateCanonicalUtf8HashGoldens()
        {
            UTF8Encoding utf8 = new UTF8Encoding(false, true);
            byte[] lf = utf8.GetBytes("{\n  \"value\": 1\n}\n");
            byte[] crlf = utf8.GetBytes("{\r\n  \"value\": 1\r\n}\r\n");
            byte[] bareCr = utf8.GetBytes("{\r  \"value\": 1\r}\r");
            string expected = Sha256Hex(CanonicalUtf8Bytes(lf));
            if (Sha256Hex(CanonicalUtf8Bytes(crlf)) != expected
                || Sha256Hex(CanonicalUtf8Bytes(bareCr)) != expected)
            {
                throw new InvalidOperationException(
                    "Canonical UTF-8 hashing is not newline-independent.");
            }
        }

        private static void ValidateM4ReducerTrace()
        {
            MemoryReducerRoot root = BenchmarkRoot(1000);
            for (int i = 0; i < 20; i++) root.visibleBlocks.Add(BenchmarkBlock(
                root, i + 1, i + 1,
                i % 3 == 0 ? MemoryContractTokens.ImportanceMinor
                    : i % 3 == 1 ? MemoryContractTokens.ImportanceRegular
                    : MemoryContractTokens.ImportanceImportant,
                i < 5));
            MemoryReducerPolicy policy = new MemoryReducerPolicy
            {
                nowTick = 1000,
                minorLifetimeTicks = 10000,
                regularLifetimeTicks = 10000,
                chapterInactivityTicks = 10000,
                targetVisibleBlocks = 12,
                maximumVisibleBlocks = 128,
                maximumFactBuckets = 16,
                maximumContributionsPerBucket = 32,
                maximumContributionsPerSummary = 32
            };
            MemoryThreadReductionResult first = MemoryThreadReducer.Reduce(root, policy);
            if (first.refused || first.replacement.visibleBlocks.Count != 12
                || BenchmarkBlockCount(first.replacement) != 13
                || first.replacement.rollingSummaryBlock == null)
                throw new InvalidOperationException("M4 reducer target trace failed.");
            string once = MemoryThreadReducer.CanonicalState(first.replacement);
            MemoryThreadReductionResult fixedPoint = MemoryThreadReducer.Reduce(
                first.replacement, policy);
            if (fixedPoint.refused || fixedPoint.changed || once != MemoryThreadReducer.CanonicalState(
                    fixedPoint.replacement))
                throw new InvalidOperationException("M4 reducer fixed-point trace failed.");

            MemoryReducerRoot ttl = BenchmarkRoot(0);
            ttl.visibleBlocks.Add(BenchmarkBlock(
                ttl, 1, 0, MemoryContractTokens.ImportanceMinor, false));
            MemoryThreadReductionResult expired = MemoryThreadReducer.Reduce(ttl,
                new MemoryReducerPolicy
                {
                    nowTick = 100,
                    minorLifetimeTicks = 100,
                    regularLifetimeTicks = 1000,
                    chapterInactivityTicks = 1000,
                    targetVisibleBlocks = 12
                });
            if (expired.refused || expired.expiredBlocks != 1
                || BenchmarkBlockCount(expired.replacement) != 0)
                throw new InvalidOperationException("M4 reducer TTL trace failed.");

            MemoryPressurePlan pressure = KnowledgeEvictionPlanner.PlanMemoryPressure(
                new MemoryPressurePlanRequest
                {
                    bytesToRelease = 2,
                    blocksToRelease = 2,
                    atoms = new List<MemoryPressureAtom>
                    {
                        BenchmarkAtom("high", MemoryContractTokens.ImportanceImportant, 1, false),
                        BenchmarkAtom("low", MemoryContractTokens.ImportanceMinor, 2, false),
                        BenchmarkAtom("medium", MemoryContractTokens.ImportanceRegular, 0, false),
                        BenchmarkAtom("edited", MemoryContractTokens.ImportanceMinor, 0, true)
                    }
                });
            if (!pressure.canApply || pressure.removals.Count != 2
                || pressure.removals[0].recordId != "low"
                || pressure.removals[1].recordId != "medium")
                throw new InvalidOperationException("M4 emergency-order trace failed.");

            MemoryMaintenanceSlicePlan slice = MemoryMaintenancePolicy.Plan(
                new MemoryMaintenanceSliceRequest
                {
                    nowTick = 1000,
                    lastRunTick = 0,
                    intervalTicks = 100,
                    itemCount = 100,
                    maximumWorkItems = 30
                });
            if (!slice.due || slice.workItems != 30 || slice.completedCycle)
                throw new InvalidOperationException("M4 elapsed maintenance trace failed.");
        }

        private static MemoryReducerRoot BenchmarkRoot(long lastActivityTick)
        {
            MemoryEpochAllocationPlan epoch = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = "benchmark-owner",
                    lastIssuedSequence = 0
                });
            MemoryRootIdentity identity = new MemoryRootIdentity
            {
                ownerPawnId = "benchmark-owner",
                ownerEpochToken = epoch.epochToken,
                primarySubjectKind = MemoryContractTokens.SubjectPawn,
                primarySubjectId = OrdinalSegmentCodec.Segment("benchmark-subject")
            };
            string rootId;
            string chapterId;
            if (!MemoryIdentityCodec.TryCreateRootId(identity, out rootId)
                || !MemoryIdentityCodec.TryCreateChapterId(rootId, 1, out chapterId))
                throw new InvalidOperationException("M4 benchmark identity construction failed.");
            MemoryReducerRoot root = new MemoryReducerRoot
            {
                rootId = rootId,
                ownerPawnId = identity.ownerPawnId,
                ownerEpochToken = identity.ownerEpochToken,
                subjectKind = identity.primarySubjectKind,
                subjectId = identity.primarySubjectId,
                nextChapterOrdinal = 2
            };
            root.chapters.Add(new MemoryReducerChapter
            {
                chapterId = chapterId,
                ordinal = 1,
                openedTick = 0,
                lastActivityTick = lastActivityTick
            });
            return root;
        }

        private static MemoryReducerBlock BenchmarkBlock(
            MemoryReducerRoot root,
            int ordinal,
            long tick,
            string importance,
            bool edited)
        {
            string source = OrdinalSegmentCodec.Segment("benchmark-occurrence-" + ordinal);
            string recordId;
            if (!MemoryIdentityCodec.TryCreateRecordId(new MemoryRecordIdentity
            {
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                sourceOccurrenceId = source,
                captureRuleId = "benchmark-rule",
                factDiscriminator = "benchmark-fact"
            }, out recordId)) throw new InvalidOperationException("M4 benchmark record ID failed.");
            string factId;
            if (!MemoryIdentityCodec.TryCreateFactId(
                "benchmark-rule", "benchmark-fact", "status",
                MemoryContractTokens.SubjectPawn, root.subjectId,
                MemoryFactContractTokens.LatestState, out factId))
                throw new InvalidOperationException("M4 benchmark fact ID failed.");
            MemoryReducerBlock block = new MemoryReducerBlock
            {
                recordId = recordId,
                sourceOccurrenceId = source,
                captureRuleId = "benchmark-rule",
                factDiscriminator = "benchmark-fact",
                ownerPawnId = root.ownerPawnId,
                ownerEpochToken = root.ownerEpochToken,
                kind = MemoryContractTokens.KindEvent,
                summaryRole = MemoryContractTokens.SummaryRoleNone,
                category = MemoryContractTokens.CategoryPersonal,
                importance = importance,
                originalEventTick = tick,
                rootId = root.rootId,
                chapterId = root.chapters[0].chapterId,
                playerEdited = edited,
                playerWording = edited ? "benchmark edited wording " + ordinal : string.Empty
            };
            block.facts.Add(new MemoryReducerFact
            {
                factId = factId,
                factKind = "status",
                canonicalSubjectKind = MemoryContractTokens.SubjectPawn,
                canonicalSubjectId = root.subjectId,
                aggregationToken = MemoryFactContractTokens.LatestState,
                canonicalValueKind = MemoryFactContractTokens.ValueState,
                canonicalValue = "value-" + ordinal
            });
            return block;
        }

        private static MemoryPressureAtom BenchmarkAtom(
            string id,
            string importance,
            long tick,
            bool edited)
        {
            return new MemoryPressureAtom
            {
                ownerPawnId = "benchmark-owner",
                rootId = "benchmark-root",
                recordId = id,
                importance = importance,
                originalEventTick = tick,
                playerEdited = edited,
                logicalBytes = 1,
                blockUnits = 1
            };
        }

        private static int BenchmarkBlockCount(MemoryReducerRoot root)
        {
            return root.visibleBlocks.Count + (root.rollingSummaryBlock == null ? 0 : 1);
        }

        private static void EnsureCleanRepository(string root)
        {
            // The SDK project glob compiles local .cs files, so an untracked source file can change
            // the measured assembly just as surely as a tracked edit. Release evidence therefore
            // requires the entire non-ignored worktree, including untracked paths, to be clean.
            string status = Git(root, "status", "--porcelain=v1", "--untracked-files=all");
            if (!string.IsNullOrWhiteSpace(status))
                throw new InvalidOperationException(
                    "Benchmark evidence requires a clean committed worktree, including untracked files.");
        }

        private static void WriteEvidence(string root, Catalog catalog, List<Candidate> candidates,
            Candidate selected, List<SyntheticScenario> scenarios,
            Dictionary<string, ScenarioAudit> scenarioAudits, ManifestAudit manifestAudit,
            PayloadAtomAudit payloadAtomAudit)
        {
            string objectFormat = Git(root, "rev-parse", "--show-object-format").Trim();
            string commit = Git(root, "rev-parse", "HEAD").Trim();
            int expectedLength = objectFormat == "sha1" ? 40 : objectFormat == "sha256" ? 64 : 0;
            if (commit.Length != expectedLength || commit != commit.ToLowerInvariant())
                throw new InvalidOperationException("Invalid full Git object identity.");
            string sourceIdentity = HashTuple("memory-source-commit-v1", objectFormat, commit);
            string catalogRoot = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog");
            string capacityHash = HashCanonicalUtf8File(
                Path.Combine(catalogRoot, "memory-capacity-catalog-v1.json"));
            string fixtureHash = HashCanonicalUtf8File(
                Path.Combine(catalogRoot, "memory-m0-fixture-catalog-v1.json"));
            string atomHash = HashCanonicalUtf8File(
                Path.Combine(catalogRoot, "memory-payload-atom-catalog-v1.json"));
            string harnessHash = HashFile(Assembly.GetExecutingAssembly().Location);
            string rimTestHash = HashFile(Path.Combine(root, "tests", "PawnDiary.RimTest", "Assemblies", "PawnDiary.RimTest.dll"));
            string scenarioDefinitionsEncoding = BuildScenarioDefinitionsEncoding(scenarios);
            string scenarioDefinitionsHash = Sha256Hex(
                Encoding.UTF8.GetBytes(scenarioDefinitionsEncoding));
            string implementationEncoding = BuildTupleEncoding(
                "memory-benchmark-implementation-v1",
                harnessHash,
                rimTestHash,
                fixtureHash,
                scenarioDefinitionsHash,
                atomHash);
            string implementationHash = Sha256Hex(Encoding.UTF8.GetBytes(implementationEncoding));

            string cpuIdentifier = NormalizeEnvironmentText(
                Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                    ?? RuntimeInformation.ProcessArchitecture.ToString(),
                256,
                "cpuIdentifier");
            string osDescription = NormalizeEnvironmentText(
                RuntimeInformation.OSDescription, 256, "osDescription");
            string runtimeDescription = NormalizeEnvironmentText(
                RuntimeInformation.FrameworkDescription, 256, "runtimeDescription");
            string languageFolderName = NormalizeEnvironmentText(
                Environment.GetEnvironmentVariable("PAWNDIARY_BENCHMARK_LANGUAGE_FOLDER")
                    ?? CultureInfo.CurrentUICulture.Name,
                128,
                "languageFolderName");
            string logicalProcessorCount = checked((ulong)Environment.ProcessorCount)
                .ToString(CultureInfo.InvariantCulture);
            string stopwatchFrequency = checked((ulong)Stopwatch.Frequency)
                .ToString(CultureInfo.InvariantCulture);
            string osArchitectureToken = ArchitectureToken(RuntimeInformation.OSArchitecture);
            string processArchitectureToken = ArchitectureToken(
                RuntimeInformation.ProcessArchitecture);
            const string allocationCollectorKind = "GC.GetAllocatedBytesForCurrentThread";
            const string allocationCollectorVersion = "dotnet-v1";
            string resolutionWidthPixels = RequirePositiveEnvironmentInteger(
                "PAWNDIARY_BENCHMARK_RESOLUTION_WIDTH_PIXELS");
            string resolutionHeightPixels = RequirePositiveEnvironmentInteger(
                "PAWNDIARY_BENCHMARK_RESOLUTION_HEIGHT_PIXELS");
            string uiScaleFloat32Bits = RequireEightLowercaseHexEnvironment(
                "PAWNDIARY_BENCHMARK_UI_SCALE_FLOAT32_BITS");
            string environmentEncoding = BuildTupleEncoding(
                "memory-benchmark-environment-v1",
                cpuIdentifier,
                logicalProcessorCount,
                osDescription,
                runtimeDescription,
                osArchitectureToken,
                processArchitectureToken,
                stopwatchFrequency,
                allocationCollectorKind,
                allocationCollectorVersion,
                languageFolderName,
                resolutionWidthPixels,
                resolutionHeightPixels,
                uiScaleFloat32Bits);
            string environmentHash = Sha256Hex(Encoding.UTF8.GetBytes(environmentEncoding));
            string resultDirectory = Path.Combine(root, "benchmarks", "results", "memory-system");
            Directory.CreateDirectory(resultDirectory);
            string jsonPath = Path.Combine(resultDirectory, sourceIdentity + "-pure.json");
            string markdownPath = Path.Combine(resultDirectory, sourceIdentity + "-decision.md");

            using (FileStream stream = File.Create(jsonPath))
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", BenchmarkSchema);
                writer.WriteString("gitObjectFormat", objectFormat);
                writer.WriteString("gitCommitObjectId", commit);
                writer.WriteString("sourceCommitIdentity", sourceIdentity);
                writer.WriteString("activationBuildState", MemorySystemActivationGate.BuildState);
                writer.WriteString("configuration", "Release");
                writer.WriteString("cpuIdentifier", cpuIdentifier);
                writer.WriteNumber("logicalProcessorCount", Environment.ProcessorCount);
                writer.WriteString("osDescription", osDescription);
                writer.WriteString("runtimeDescription", runtimeDescription);
                writer.WriteString("osArchitectureToken", osArchitectureToken);
                writer.WriteString("processArchitectureToken", processArchitectureToken);
                writer.WriteNumber("stopwatchFrequency", Stopwatch.Frequency);
                writer.WriteString("allocationCollectorKind", allocationCollectorKind);
                writer.WriteString("allocationCollectorVersion", allocationCollectorVersion);
                writer.WriteString("languageFolderName", languageFolderName);
                writer.WriteNumber("resolutionWidthPixels", ulong.Parse(
                    resolutionWidthPixels, NumberStyles.None, CultureInfo.InvariantCulture));
                writer.WriteNumber("resolutionHeightPixels", ulong.Parse(
                    resolutionHeightPixels, NumberStyles.None, CultureInfo.InvariantCulture));
                writer.WriteString("uiScaleFloat32Bits", uiScaleFloat32Bits);
                writer.WriteString("capacityCatalogSha256", capacityHash);
                writer.WriteString("fixtureCatalogSha256", fixtureHash);
                writer.WriteString("scenarioDefinitionsSha256", scenarioDefinitionsHash);
                writer.WriteString("payloadAtomCatalogSha256", atomHash);
                writer.WriteString("harnessAssemblySha256", harnessHash);
                writer.WriteString("rimTestAssemblySha256", rimTestHash);
                writer.WriteString("benchmarkImplementationEncoding", implementationEncoding);
                writer.WriteString("benchmarkImplementationEncodingSha256", implementationHash);
                writer.WriteString("benchmarkEnvironmentEncoding", environmentEncoding);
                writer.WriteString("benchmarkEnvironmentEncodingSha256", environmentHash);
                writer.WriteString("pureCoverageDisposition", "m0_capacity_surrogate_plus_m4_reducer_trace");
                writer.WriteBoolean("retentionReducerTraceExecuted", true);
                writer.WriteStartObject("payloadAtomAudit");
                writer.WriteNumber("typeCount", payloadAtomAudit.typeCount);
                writer.WriteNumber("atomCount", payloadAtomAudit.atomCount);
                writer.WriteStartObject("minimumSchemaLogicalBytes");
                foreach (KeyValuePair<string, ulong> pair in payloadAtomAudit.minimumSchemaLogicalBytes)
                    writer.WriteNumber(pair.Key, pair.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
                using (JsonDocument fixtureCatalog = JsonDocument.Parse(File.ReadAllText(
                    Path.Combine(catalogRoot, "memory-m0-fixture-catalog-v1.json"))))
                {
                    writer.WritePropertyName("currentBaselineMetrics");
                    fixtureCatalog.RootElement.GetProperty("currentBaselineMetrics").WriteTo(writer);
                    writer.WritePropertyName("syntheticScenarios");
                    fixtureCatalog.RootElement.GetProperty("syntheticScenarios").WriteTo(writer);
                    writer.WriteStartArray("syntheticScenarioResults");
                    foreach (SyntheticScenario scenario in scenarios)
                    {
                        ScenarioAudit audit = scenarioAudits[scenario.scenarioId];
                        writer.WriteStartObject();
                        writer.WriteString("scenarioId", scenario.scenarioId);
                        writer.WriteString("expectedGate", scenario.expectedGate);
                        writer.WriteNumber("evaluatedCellCount", audit.evaluatedCells);
                        writer.WriteNumber("passedCellCount", audit.passedCells);
                        writer.WriteNumber("declaredCoordinateCount", scenario.coordinates.Count);
                        writer.WriteString("resultFingerprint", audit.Fingerprint());
                        writer.WriteString("disposition", audit.evaluatedCells == audit.passedCells
                            ? "pass_all_generated_vectors_all_N_text_modes_all_declared_coordinates"
                            : "fail");
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WritePropertyName("pureGateIds");
                    fixtureCatalog.RootElement.GetProperty("pureGateIds").WriteTo(writer);
                    writer.WritePropertyName("loadedPendingFixtures");
                    fixtureCatalog.RootElement.GetProperty("loadedPendingFixtures").WriteTo(writer);
                }
                CoordinateEvaluation selectedTime = selected.coordinates
                    .OrderByDescending(row => row.time.maximum).First();
                CoordinateEvaluation selectedAllocation = selected.coordinates
                    .OrderByDescending(row => row.allocation.maximum).First();
                writer.WriteStartObject("selectedWorstPureTimingMicroseconds");
                writer.WriteNumber("threadTarget", selectedTime.threadTarget);
                writer.WriteString("textMode", selectedTime.textMode);
                WriteMeasurement(writer, selectedTime.time);
                writer.WriteEndObject();
                writer.WriteStartObject("selectedWorstPurePairedAllocationBytes");
                writer.WriteNumber("threadTarget", selectedAllocation.threadTarget);
                writer.WriteString("textMode", selectedAllocation.textMode);
                WriteMeasurement(writer, selectedAllocation.allocation);
                writer.WriteEndObject();
                writer.WriteNumber("generatedVectorCount", candidates.Count);
                writer.WriteNumber("provisionallyFeasibleVectorCount", candidates.Count(row => row.feasible));
                writer.WriteString("selectedVectorId", selected.vectorId);
                writer.WriteNumber("provisionalManifestEntryCount", manifestAudit.entries.Count);
                writer.WriteString("provisionalManifestAuditFingerprint", manifestAudit.fingerprint);
                writer.WriteString("releasePolicyEncodingSha256",
                    manifestAudit.releasePolicyEncodingHash);
                writer.WriteStartArray("selectedAndDefensiveManifestAuditRows");
                foreach (ManifestEntry entry in manifestAudit.entries.Where(row =>
                    row.vectorId == selected.vectorId || row.disposition == "defensiveCeilingAudit"))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("manifestEntryOrdinal", entry.entryOrdinal);
                    writer.WriteString("manifestEntryId", entry.entryId);
                    writer.WriteString("disposition", entry.disposition);
                    writer.WriteString("vectorId", entry.vectorId);
                    writer.WriteNumber("threadTarget", entry.threadTarget);
                    writer.WriteString("effectivePolicyEncodingSha256", entry.effectivePolicyHash);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("allHighDefensiveSixEntryIdentityGolden");
                foreach (ManifestEntry entry in manifestAudit.defensiveIdentityGoldenRows)
                {
                    writer.WriteStartObject();
                    writer.WriteString("manifestEntryId", entry.entryId);
                    writer.WriteString("disposition", entry.disposition);
                    writer.WriteString("vectorId", entry.vectorId);
                    writer.WriteNumber("threadTarget", entry.threadTarget);
                    writer.WriteString("effectivePolicyEncodingSha256", entry.effectivePolicyHash);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("vectors");
                foreach (Candidate candidate in candidates)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("vectorOrdinal", candidate.vectorOrdinal);
                    writer.WriteString("vectorId", candidate.vectorId);
                    writer.WriteString("vectorEncoding", candidate.encoding);
                    writer.WriteStartArray("generatorOrigins");
                    foreach (string origin in candidate.origins.OrderBy(value => value, StringComparer.Ordinal)) writer.WriteStringValue(origin);
                    writer.WriteEndArray();
                    writer.WriteBoolean("provisionallyFeasible", candidate.feasible);
                    writer.WriteString("rejectionGateId", candidate.rejection);
                    writer.WriteNumber("surrogateCombinedBytes", candidate.surrogateCombinedBytes);
                    writer.WriteNumber("surrogateOwnerTypicalBytes", candidate.ownerTypicalBytes);
                    writer.WriteNumber("surrogateOwnerWorstBytes", candidate.ownerWorstBytes);
                    writer.WriteNumber("pureMaxIndivisibleItemMicroseconds", candidate.pureMaxIndivisibleItemMicroseconds);
                    writer.WriteNumber("pureAllocationTieBreakBytes", candidate.pureAllocationTieBreakBytes);
                    writer.WriteNumber("maximumCultureLabelDtoBytes",
                        candidate.maximumCultureLabelDtoBytes);
                    writer.WriteStartArray("authenticatedCoordinates");
                    foreach (CoordinateEvaluation coordinate in candidate.coordinates)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("threadTarget", coordinate.threadTarget);
                        writer.WriteString("textMode", coordinate.textMode);
                        writer.WriteNumber("surrogateCombinedBytes", coordinate.combinedBytes);
                        writer.WriteNumber("surrogateOwnerTypicalBytes", coordinate.ownerTypicalBytes);
                        writer.WriteNumber("surrogateOwnerWorstBytes", coordinate.ownerWorstBytes);
                        writer.WriteNumber("surrogateCatalogCycles", coordinate.combinedFill.admittedCatalogCycles);
                        writer.WriteNumber("visitedAtomCount", coordinate.combinedFill.visitedAtoms);
                        writer.WriteString("firstRefusedPath", coordinate.combinedFill.firstRefusedPath);
                        writer.WriteNumber("maximumIndivisibleItemMicroseconds", coordinate.time.maximum);
                        writer.WriteNumber("maximumPairedAllocationBytes", coordinate.allocation.maximum);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            StringBuilder markdown = new StringBuilder();
            markdown.AppendLine("# Memory System M0 capacity and M4 reducer decision");
            markdown.AppendLine();
            markdown.AppendLine("- Schema: `" + BenchmarkSchema + "`");
            markdown.AppendLine("- Source commit: `" + commit + "`");
            markdown.AppendLine("- Source identity: `" + sourceIdentity + "`");
            markdown.AppendLine("- Generated normalized vectors: " + candidates.Count.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Provisionally feasible vectors: " + candidates.Count(row => row.feasible).ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Selected vector: `" + selected.vectorId + "`");
            markdown.AppendLine("- Surrogate combined-global bytes: " + selected.surrogateCombinedBytes.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Selected maximum indivisible pure item: " + selected.pureMaxIndivisibleItemMicroseconds.ToString(CultureInfo.InvariantCulture) + " µs");
            markdown.AppendLine("- Selected maximum paired allocation delta: " + selected.pureAllocationTieBreakBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
            markdown.AppendLine("- Selected two-culture-label Library payload: "
                + selected.maximumCultureLabelDtoBytes.ToString(CultureInfo.InvariantCulture)
                + " UTF-16 bytes");
            markdown.AppendLine("- Authenticated provisional manifest rows: " + manifestAudit.entries.Count.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Provisional manifest audit fingerprint: `" + manifestAudit.fingerprint + "`");
            markdown.AppendLine("- Release-policy encoding SHA-256: `" + manifestAudit.releasePolicyEncodingHash + "`");
            markdown.AppendLine("- Benchmark implementation encoding SHA-256: `" + implementationHash + "`");
            markdown.AppendLine("- Benchmark environment encoding SHA-256: `" + environmentHash + "`");
            markdown.AppendLine();
            markdown.AppendLine("The selected vector remains provisional M0 schema-cycle byte evidence, while this harness now also executes the production M4 reducer through target, TTL, fixed-point, edited saturation, and emergency-order traces. The surrogate is still not mislabeled as a saved-row size walk. Exact loaded Scribe, OnGUI/render, and Unity allocation cells remain named pending fixtures; none is reported as zero or waived.");
            markdown.AppendLine();
            markdown.AppendLine("## Selected vector encoding");
            markdown.AppendLine(); markdown.AppendLine("```text"); markdown.Append(selected.encoding); markdown.AppendLine("```");
            File.WriteAllText(markdownPath, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void WriteMeasurement(Utf8JsonWriter writer, StatisticalMeasurement measurement)
        {
            writer.WriteNumber("observationCount", measurement.observationCount);
            writer.WriteNumber("median", measurement.median);
            writer.WriteNumber("p95", measurement.p95);
            writer.WriteNumber("max", measurement.maximum);
        }

        private static string EncodeVector(List<Dimension> dimensions, Dictionary<string, string> values)
        {
            StringBuilder builder = new StringBuilder(VectorHeader).Append('\n');
            foreach (Dimension dimension in dimensions)
                builder.Append(dimension.name).Append('=').Append(values[dimension.name]).Append('\n');
            return builder.ToString();
        }

        private static Dictionary<string, string> Copy(Dictionary<string, string> source)
        {
            return new Dictionary<string, string>(source, StringComparer.Ordinal);
        }

        private static ulong ParseOne(Dictionary<string, string> values, string name)
        {
            return ParseUnsignedTuple(values[name])[0];
        }

        private static ulong[] ParseUnsignedTuple(string value)
        {
            string[] members = (value ?? string.Empty).Split('/');
            ulong[] parsed = new ulong[members.Length];
            for (int index = 0; index < members.Length; index++)
            {
                if (members[index].Length == 0 || (members[index].Length > 1 && members[index][0] == '0')
                    || !ulong.TryParse(members[index], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[index]))
                    throw new InvalidOperationException("Invalid unsigned tuple: " + value);
            }
            return parsed;
        }

        private static string HashTuple(string domain, params string[] fields)
        {
            return Sha256Hex(Encoding.UTF8.GetBytes(BuildTupleEncoding(domain, fields)));
        }

        private static string BuildTupleEncoding(string domain, params string[] fields)
        {
            StringBuilder builder = new StringBuilder(OrdinalSegmentCodec.Segment(domain));
            foreach (string field in fields) builder.Append(OrdinalSegmentCodec.Segment(field));
            return builder.ToString();
        }

        private static string BuildScenarioDefinitionsEncoding(List<SyntheticScenario> scenarios)
        {
            StringBuilder builder = new StringBuilder(
                OrdinalSegmentCodec.Segment("memory-m0-scenario-definitions-v1"));
            builder.Append(OrdinalSegmentCodec.Segment(
                scenarios.Count.ToString(CultureInfo.InvariantCulture)));
            foreach (SyntheticScenario scenario in scenarios)
            {
                builder.Append(OrdinalSegmentCodec.Segment(scenario.scenarioId));
                builder.Append(OrdinalSegmentCodec.Segment(scenario.expectedGate));
                builder.Append(OrdinalSegmentCodec.Segment(
                    scenario.coordinates.Count.ToString(CultureInfo.InvariantCulture)));
                foreach (string coordinate in scenario.coordinates)
                    builder.Append(OrdinalSegmentCodec.Segment(coordinate));
            }
            return builder.ToString();
        }

        private static string ArchitectureToken(Architecture architecture)
        {
            if (architecture == Architecture.X86) return "x86";
            if (architecture == Architecture.X64) return "x64";
            if (architecture == Architecture.Arm) return "arm";
            if (architecture == Architecture.Arm64) return "arm64";
            throw new InvalidOperationException("Unsupported benchmark architecture: " + architecture);
        }

        private static string NormalizeEnvironmentText(string value, int maximumUnits, string field)
        {
            if (value == null) value = string.Empty;
            string normalized = value.Normalize(NormalizationForm.FormKC);
            StringBuilder collapsed = new StringBuilder(normalized.Length);
            bool pendingSpace = false;
            foreach (char character in normalized)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = collapsed.Length > 0;
                    continue;
                }
                if (pendingSpace) collapsed.Append(' ');
                collapsed.Append(character);
                pendingSpace = false;
            }
            string result = collapsed.ToString();
            if (result.Length == 0 || result.Length > maximumUnits
                || !MemoryIdentityCodec.IsWellFormedUtf16(result))
            {
                throw new InvalidOperationException(
                    "Invalid benchmark environment field: " + field);
            }
            return result;
        }

        private static string RequirePositiveEnvironmentInteger(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
            ulong parsed;
            if (value.Length == 0 || (value.Length > 1 && value[0] == '0')
                || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                || parsed == 0)
            {
                throw new InvalidOperationException(
                    "Evidence generation requires a positive invariant-decimal " + variableName + ".");
            }
            return value;
        }

        private static string RequireEightLowercaseHexEnvironment(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
            if (value.Length != 8 || value.Any(character =>
                    !(character >= '0' && character <= '9')
                    && !(character >= 'a' && character <= 'f')))
            {
                throw new InvalidOperationException(
                    "Evidence generation requires eight lowercase hexadecimal digits in "
                    + variableName + ".");
            }
            return value;
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Required benchmark identity file is missing: " + path);
            return Sha256Hex(File.ReadAllBytes(path));
        }

        private static string HashCanonicalUtf8File(string path)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    "Required canonical UTF-8 identity file is missing: " + path);
            return Sha256Hex(CanonicalUtf8Bytes(File.ReadAllBytes(path)));
        }

        private static byte[] CanonicalUtf8Bytes(byte[] bytes)
        {
            if (bytes == null) throw new InvalidOperationException("Canonical UTF-8 input is null.");
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                throw new InvalidOperationException("Canonical catalog UTF-8 must not contain a BOM.");
            string text = new UTF8Encoding(false, true).GetString(bytes);
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return new UTF8Encoding(false, true).GetBytes(normalized);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string Git(string root, params string[] arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo("git")
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("git failed: " + error);
                return output;
            }
        }
    }
}
