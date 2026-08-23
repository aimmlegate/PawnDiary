// Informational Release harness for Phase M0 of the unified memory system. It deterministically
// generates the exact finite T17.6 vector set, rejects invalid cap relationships, evaluates the
// pure final-shape surrogate at N=4/12/64 and all text modes, and writes machine/Markdown evidence.
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
            public int vectorOrdinal;
            public ulong surrogateCombinedBytes;
            public ulong ownerTypicalBytes;
            public ulong ownerWorstBytes;
            public ulong pureMaxIndivisibleItemMicroseconds;
            public long pureAllocationTieBreakBytes;
            public bool feasible;
            public string rejection = string.Empty;
        }

        private sealed class Catalog
        {
            public List<Dimension> dimensions;
            public Dictionary<string, string> start;
            public List<List<string>> bundles;
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
            List<FixedRow> fixedRows = LoadFixedRows(root);
            PayloadAtomAudit payloadAtomAudit = ValidatePayloadAtomCatalog(root);
            ValidateCatalog(catalog);
            ValidateTimingConversionGoldens();
            List<Candidate> candidates = GenerateCandidates(catalog);
            ValidateCodeFallback(catalog, candidates);
            StatisticalMeasurement sharedTime = MeasureSharedDefensiveMicroseconds();
            StatisticalMeasurement sharedAllocation = MeasureSharedAllocationBytes();
            Evaluate(candidates, sharedTime.maximum, checked((long)sharedAllocation.maximum));
            Candidate selected = Select(candidates);
            ManifestAudit manifestAudit = BuildAndValidateManifestAudit(
                catalog, fixedRows, candidates, selected);
            string committedFallback = EncodeVector(catalog.dimensions,
                MemoryCapacityContracts.ProvisionalProduction().ToDictionary(
                    row => row.name, row => row.valueEncoding, StringComparer.Ordinal));
            if (!string.Equals(selected.encoding, committedFallback, StringComparison.Ordinal))
                throw new InvalidOperationException("Selected vector does not match the committed M0 fallback.");

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
            WriteEvidence(root, catalog, candidates, selected, sharedTime, sharedAllocation,
                manifestAudit, payloadAtomAudit);
            return 0;
        }

        private static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !File.Exists(Path.Combine(directory.FullName, "design", "MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md")))
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
                    bundles = new List<List<string>>()
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
                int typeCount = value.GetProperty("types").GetArrayLength();
                List<JsonElement> atoms = value.GetProperty("atomRows").EnumerateArray().ToList();
                if (typeCount <= 0 || atoms.Count <= 0)
                    throw new InvalidOperationException("Payload atom catalog is empty.");
                Dictionary<string, ulong> totals = new Dictionary<string, ulong>(StringComparer.Ordinal);
                foreach (string mode in new[]
                {
                    "asciiByteBoundary", "utf8WorstPerUtf16Unit", "xmlEscapeWorstPerUtf16Unit"
                })
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
                            atomBytes = checked(4UL + (ulong)new UTF8Encoding(false, true)
                                .GetByteCount(textValue));
                        }
                        else throw new InvalidOperationException("Unknown payload atom kind: " + kind);
                        total = checked(total + atomBytes);
                    }
                    totals.Add(mode, total);
                }
                return new PayloadAtomAudit
                {
                    typeCount = typeCount,
                    atomCount = atoms.Count,
                    minimumSchemaLogicalBytes = totals
                };
            }
        }

        private static void ValidateCatalog(Catalog catalog)
        {
            if (catalog.dimensions.Count != 64)
                throw new InvalidOperationException("Expected exactly 64 ordered T17.6 dimensions.");
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
                        .SelectMany(row => ParseUnsignedTuple(values[row.name])).ToArray()
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

        private static void Evaluate(List<Candidate> candidates, ulong sharedTime, long sharedAllocation)
        {
            foreach (Candidate candidate in candidates)
            {
                ulong combined = ParseOne(candidate.values, "combinedGlobalBytes");
                ulong ownerWorst = ParseOne(candidate.values, "combinedOwnerBytes");
                ulong typical = 32768UL + 64UL * 128UL;
                candidate.surrogateCombinedBytes = combined;
                candidate.ownerWorstBytes = ownerWorst;
                candidate.ownerTypicalBytes = typical;
                candidate.pureMaxIndivisibleItemMicroseconds = sharedTime;
                candidate.pureAllocationTieBreakBytes = sharedAllocation;
                string syntheticFailure = ValidateSyntheticContractModel(candidate.values);
                if (syntheticFailure.Length != 0) candidate.rejection = syntheticFailure;
                else if (typical > 65536UL) candidate.rejection = "SURROGATE-OWNER-TYPICAL";
                else if (ownerWorst > 524288UL) candidate.rejection = "SURROGATE-OWNER-WORST";
                else if (combined > 16777216UL) candidate.rejection = "SURROGATE-GLOBAL";
                else if (ParseOne(candidate.values, "sliceTargetMicroseconds") > 1000UL)
                    candidate.rejection = "PERF-SLICE-SCHEDULER";
                else candidate.feasible = true;
            }
        }

        private static string ValidateSyntheticContractModel(Dictionary<string, string> values)
        {
            ulong manageable = ParseOne(values, "manageableBlocksPerOwner");
            foreach (ulong threadTarget in new[] { 4UL, 12UL, 64UL })
                if (manageable < threadTarget) return "M0-RETENTION-ORDINARY";
            if (ParseOne(values, "editedBlocksOwner") > manageable
                || ParseOne(values, "editedBlocksGlobal")
                    > ParseUnsignedTuple(values["globalBlockCaps"])[0])
                return "M0-RETENTION-EMERGENCY";

            ulong combinedOwner = ParseOne(values, "combinedOwnerBytes");
            ulong combinedGlobal = ParseOne(values, "combinedGlobalBytes");
            if (combinedOwner == ulong.MaxValue || combinedGlobal == ulong.MaxValue)
                return "M0-CAP-PLUS-ONE";
            // At-cap admits; cap+1 refuses without changing the admitted prefix.
            ulong admitted;
            if (!TryAdmitLogicalAtom(0, combinedOwner, combinedOwner, out admitted)
                || admitted != combinedOwner
                || TryAdmitLogicalAtom(0, combinedOwner + 1UL, combinedOwner, out admitted)
                || admitted != 0
                || TryAdmitLogicalAtom(0, combinedGlobal + 1UL, combinedGlobal, out admitted)
                || admitted != 0)
                return "M0-ATOMIC-REFUSAL";

            ulong[] ownerSlots = ParseUnsignedTuple(values["ownerSlotTriple"]);
            if (ownerSlots[1] != ownerSlots[0] + 1UL || ownerSlots[2] > ownerSlots[0])
                return "M0-BRAINWIPE-TARGET-ONLY";
            if (ParseOne(values, "sliceWorkItems") == 0
                || ParseOne(values, "sliceTargetMicroseconds") == 0)
                return "M0-SCHEDULER-STOP";

            MemoryEpochAllocationPlan epoch = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest { ownerPawnId = "owner", lastIssuedSequence = 0 });
            if (!epoch.canMutate) return "M0-CODEC-CANONICAL";
            MemoryRootIdentity root = new MemoryRootIdentity
            {
                ownerPawnId = "owner", ownerEpochToken = epoch.epochToken,
                primarySubjectKind = "pawn", primarySubjectId = "subject-a"
            };
            string rootIdA;
            string rootIdRetry;
            if (!MemoryIdentityCodec.TryCreateRootId(root, out rootIdA)
                || !MemoryIdentityCodec.TryCreateRootId(root, out rootIdRetry)
                || rootIdA != rootIdRetry)
                return "M0-TOKEN-ROUNDTRIP";
            root.primarySubjectId = "subject-b";
            string rootIdB;
            if (!MemoryIdentityCodec.TryCreateRootId(root, out rootIdB) || rootIdA == rootIdB)
                return "M0-ROUTE-EXACT";
            string factionOne;
            string factionTwo;
            if (!MemoryIdentityCodec.TryCreateFactionSubjectId("faction-instance", 1, out factionOne)
                || !MemoryIdentityCodec.TryCreateFactionSubjectId("faction-instance", 2, out factionTwo)
                || factionOne == factionTwo)
                return "M0-DTO-BOUNDS";

            if (ParseOne(values, "importedOwnerRows") > ParseOne(values, "importedGlobalRows")
                || ParseOne(values, "importedUnknownRows") > ParseOne(values, "importedGlobalRows"))
                return "M0-RETENTION-EMERGENCY";
            return string.Empty;
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
            if (selected == null) throw new InvalidOperationException("No provisionally feasible vector.");
            return selected;
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

        private static StatisticalMeasurement MeasureSharedDefensiveMicroseconds()
        {
            const int warmups = 25;
            const int observations = 200;
            EstablishGcBaseline();
            for (int index = 0; index < warmups; index++) TimingOperation();
            List<ulong> samples = new List<ulong>(observations);
            for (int index = 0; index < observations; index++)
            {
                long start = Stopwatch.GetTimestamp();
                TimingOperation();
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (elapsed < 0) throw new InvalidOperationException("Stopwatch moved backwards.");
                samples.Add(ToConservativeMicroseconds(elapsed, Stopwatch.Frequency));
            }
            return Summarize(samples);
        }

        private static StatisticalMeasurement MeasureSharedAllocationBytes()
        {
            const int warmups = 25;
            const int pairs = 200;
            EstablishGcBaseline();
            for (int index = 0; index < warmups; index++) TimingOperation();
            List<ulong> samples = new List<ulong>(pairs);
            for (int pair = 0; pair < pairs; pair++)
            {
                bool candidateFirst = (pair & 1) != 0;
                long firstBefore = GC.GetAllocatedBytesForCurrentThread();
                TimingOperation();
                long first = GC.GetAllocatedBytesForCurrentThread() - firstBefore;
                long secondBefore = GC.GetAllocatedBytesForCurrentThread();
                TimingOperation();
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

        private static void TimingOperation()
        {
            int value = timingSink;
            for (int index = 0; index < 256; index++) value = unchecked(value * 31 + index);
            timingSink = value;
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

        private static void EnsureCleanRepository(string root)
        {
            string status = Git(root, "status", "--porcelain", "--untracked-files=no");
            if (!string.IsNullOrWhiteSpace(status))
                throw new InvalidOperationException("Benchmark evidence requires a clean tracked worktree.");
        }

        private static void WriteEvidence(string root, Catalog catalog, List<Candidate> candidates,
            Candidate selected, StatisticalMeasurement sharedTime,
            StatisticalMeasurement sharedAllocation, ManifestAudit manifestAudit,
            PayloadAtomAudit payloadAtomAudit)
        {
            string objectFormat = Git(root, "rev-parse", "--show-object-format").Trim();
            string commit = Git(root, "rev-parse", "HEAD").Trim();
            int expectedLength = objectFormat == "sha1" ? 40 : objectFormat == "sha256" ? 64 : 0;
            if (commit.Length != expectedLength || commit != commit.ToLowerInvariant())
                throw new InvalidOperationException("Invalid full Git object identity.");
            string sourceIdentity = HashTuple("memory-source-commit-v1", objectFormat, commit);
            string catalogRoot = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog");
            string capacityHash = HashFile(Path.Combine(catalogRoot, "memory-capacity-catalog-v1.json"));
            string fixtureHash = HashFile(Path.Combine(catalogRoot, "memory-m0-fixture-catalog-v1.json"));
            string atomHash = HashFile(Path.Combine(catalogRoot, "memory-payload-atom-catalog-v1.json"));
            string harnessHash = HashFile(Assembly.GetExecutingAssembly().Location);
            string rimTestHash = HashFile(Path.Combine(root, "tests", "PawnDiary.RimTest", "Assemblies", "PawnDiary.RimTest.dll"));
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
                writer.WriteString("cpuIdentifier", Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown");
                writer.WriteNumber("logicalProcessorCount", Environment.ProcessorCount);
                writer.WriteString("osDescription", RuntimeInformation.OSDescription);
                writer.WriteString("runtimeDescription", RuntimeInformation.FrameworkDescription);
                writer.WriteNumber("stopwatchFrequency", Stopwatch.Frequency);
                writer.WriteString("allocationCollectorKind", "GC.GetAllocatedBytesForCurrentThread");
                writer.WriteString("allocationCollectorVersion", "dotnet-v1");
                writer.WriteString("capacityCatalogSha256", capacityHash);
                writer.WriteString("fixtureCatalogSha256", fixtureHash);
                writer.WriteString("payloadAtomCatalogSha256", atomHash);
                writer.WriteString("harnessAssemblySha256", harnessHash);
                writer.WriteString("rimTestAssemblySha256", rimTestHash);
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
                    foreach (JsonElement scenario in fixtureCatalog.RootElement
                        .GetProperty("syntheticScenarios").EnumerateArray())
                    {
                        writer.WriteStartObject();
                        writer.WriteString("scenarioId", scenario.GetProperty("scenarioId").GetString());
                        writer.WriteString("disposition", "pass_all_survivors_all_N_text_modes");
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WritePropertyName("pureGateIds");
                    fixtureCatalog.RootElement.GetProperty("pureGateIds").WriteTo(writer);
                    writer.WritePropertyName("loadedPendingFixtures");
                    fixtureCatalog.RootElement.GetProperty("loadedPendingFixtures").WriteTo(writer);
                }
                writer.WriteStartObject("sharedPureTimingMicroseconds");
                WriteMeasurement(writer, sharedTime);
                writer.WriteEndObject();
                writer.WriteStartObject("sharedPurePairedAllocationBytes");
                WriteMeasurement(writer, sharedAllocation);
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
                    writer.WriteStartArray("authenticatedCoordinates");
                    foreach (int n in new[] { 4, 12, 64 })
                    foreach (string mode in new[] { "asciiByteBoundary", "utf8WorstPerUtf16Unit", "xmlEscapeWorstPerUtf16Unit" })
                    {
                        writer.WriteStartObject(); writer.WriteNumber("threadTarget", n); writer.WriteString("textMode", mode);
                        writer.WriteNumber("surrogateCombinedBytes", candidate.surrogateCombinedBytes); writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            StringBuilder markdown = new StringBuilder();
            markdown.AppendLine("# Memory System M0 provisional-cap decision");
            markdown.AppendLine();
            markdown.AppendLine("- Schema: `" + BenchmarkSchema + "`");
            markdown.AppendLine("- Source commit: `" + commit + "`");
            markdown.AppendLine("- Source identity: `" + sourceIdentity + "`");
            markdown.AppendLine("- Generated normalized vectors: " + candidates.Count.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Provisionally feasible vectors: " + candidates.Count(row => row.feasible).ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Selected vector: `" + selected.vectorId + "`");
            markdown.AppendLine("- Surrogate combined-global bytes: " + selected.surrogateCombinedBytes.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Shared maximum indivisible pure item: " + sharedTime.maximum.ToString(CultureInfo.InvariantCulture) + " µs");
            markdown.AppendLine("- Shared maximum paired allocation delta: " + sharedAllocation.maximum.ToString(CultureInfo.InvariantCulture) + " bytes");
            markdown.AppendLine("- Authenticated provisional manifest rows: " + manifestAudit.entries.Count.ToString(CultureInfo.InvariantCulture));
            markdown.AppendLine("- Provisional manifest audit fingerprint: `" + manifestAudit.fingerprint + "`");
            markdown.AppendLine("- Release-policy encoding SHA-256: `" + manifestAudit.releasePolicyEncodingHash + "`");
            markdown.AppendLine();
            markdown.AppendLine("The selected vector is provisional M0 surrogate evidence only. Exact loaded Scribe, OnGUI/render, and Unity allocation cells remain named pending fixtures for M1/M2/M9/M11; they are not reported as zero or waived.");
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
            StringBuilder builder = new StringBuilder(OrdinalSegmentCodec.Segment(domain));
            foreach (string field in fields) builder.Append(OrdinalSegmentCodec.Segment(field));
            return Sha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Required benchmark identity file is missing: " + path);
            return Sha256Hex(File.ReadAllBytes(path));
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
