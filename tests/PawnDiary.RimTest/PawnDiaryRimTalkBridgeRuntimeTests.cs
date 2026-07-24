// Optional-mod runtime contract for the retired RimTalk shared-memory integration. Reflection keeps
// this suite loadable when RimTalk/the bridge are absent. When both are active, it proves the redesign
// removed the provider and cleans the legacy preset entry instead of resurrecting old behavior.
using System;
using System.Collections;
using System.Reflection;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Checks the negative shared-memory contract introduced by the memory redesign.</summary>
    [TestSuite]
    public static class PawnDiaryRimTalkBridgeRuntimeTests
    {
        private const string BridgePackageId = "aimmlegate.pawndiary.rimtalkbridge";
        private const string RimTalkPackageId = "cj.rimtalk";
        private const string LegacyVariable = "diary_shared";
        private const string LegacyToken = "{{diary_shared}}";
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// The retired context variable is absent and the compatibility pass removes any bridge-owned
        /// legacy token from RimTalk's active preset.
        /// </summary>
        [Test]
        public static void RetiredSharedMemoryProviderIsAbsentAndLegacyPresetEntryIsRemoved()
        {
            if (!ModsConfig.IsActive(BridgePackageId) || !ModsConfig.IsActive(RimTalkPackageId))
            {
                Log.Message(
                    "[PawnDiary RimTest RimTalk bridge] not applicable (bridge or RimTalk inactive).");
                return;
            }

            Type cleanupType = ResolveType(
                "PawnDiaryRimTalkBridge.SharedMemoryLegacyCleanup, PawnDiaryRimTalkBridge");
            Type registryType = ResolveType("RimTalk.API.ContextHookRegistry, RimTalk");
            Type promptApiType = ResolveType("RimTalk.API.RimTalkPromptAPI, RimTalk");
            MethodInfo hasVariable = RequireMethod(registryType, "HasContextVariable");
            MethodInfo getPreset = RequireMethod(promptApiType, "GetActivePreset");
            MethodInfo createEntry = RequireMethod(promptApiType, "CreatePromptEntry");
            MethodInfo addEntry = RequireMethod(promptApiType, "AddPromptEntry");
            MethodInfo removeEntries = RequireMethod(promptApiType, "RemovePromptEntriesByModId");
            MethodInfo reset = RequireMethod(cleanupType, "ResetForNewGame");
            MethodInfo run = RequireMethod(cleanupType, "RunOnce");

            PawnDiaryRimTestScope.Require(
                !(bool)hasVariable.Invoke(null, new object[] { LegacyVariable }),
                "The retired RimTalk diary_shared context variable is still registered.");

            object preset = getPreset.Invoke(null, null);
            PawnDiaryRimTestScope.Require(
                preset != null, "RimTalk returned no active prompt preset for cleanup validation.");
            try
            {
                // Seed the exact persisted row older bridge releases created. Merely asserting that
                // no row exists before cleanup is vacuous: it passes even when RunOnce removes
                // nothing. Optional enum/default parameters must be supplied to MethodInfo.Invoke.
                object legacyEntry = createEntry.Invoke(
                    null,
                    new object[]
                    {
                        "Pawn Diary legacy shared-memory test",
                        LegacyToken,
                        Type.Missing,
                        Type.Missing,
                        Type.Missing,
                        BridgePackageId
                    });
                PawnDiaryRimTestScope.Require(
                    legacyEntry != null && (bool)addEntry.Invoke(null, new[] { legacyEntry }),
                    "Could not seed the bridge-owned {{diary_shared}} entry for cleanup validation.");
                PawnDiaryRimTestScope.Require(
                    ContainsLegacyBridgeEntry(preset),
                    "The seeded bridge-owned {{diary_shared}} entry was not present before cleanup.");

                reset.Invoke(null, null);
                run.Invoke(null, null);
                PawnDiaryRimTestScope.Require(
                    !ContainsLegacyBridgeEntry(preset),
                    "The active RimTalk preset still contains the bridge-owned {{diary_shared}} entry.");
            }
            finally
            {
                // Failure isolation: never leave the synthetic legacy row in a developer's active
                // preset when an assertion or optional-mod API call fails midway through the test.
                removeEntries.Invoke(null, new object[] { BridgePackageId });
            }
        }

        private static bool ContainsLegacyBridgeEntry(object preset)
        {
            FieldInfo entriesField = RequireField(preset.GetType(), "Entries");
            IEnumerable entries = entriesField.GetValue(preset) as IEnumerable;
            PawnDiaryRimTestScope.Require(
                entries != null, "RimTalk's active prompt preset exposed no entry collection.");
            foreach (object entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                PropertyInfo sourceProperty = entry.GetType().GetProperty(
                    "SourceModId",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo contentField = entry.GetType().GetField(
                    "Content",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string source = sourceProperty?.GetValue(entry, null) as string;
                string content = contentField?.GetValue(entry) as string;
                if (string.Equals(source, BridgePackageId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(content, LegacyToken, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Type ResolveType(string qualifiedName)
        {
            Type type = Type.GetType(qualifiedName, false);
            if (type == null)
            {
                throw new AssertionException(
                    "Could not resolve optional runtime type '" + qualifiedName + "'.");
            }

            return type;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type?.GetMethod(name, AnyStatic);
            if (method == null)
            {
                throw new AssertionException(
                    "Could not resolve method '" + name + "' on " + type?.FullName + ".");
            }

            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type?.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new AssertionException(
                    "Could not resolve field '" + name + "' on " + type?.FullName + ".");
            }

            return field;
        }
    }
}
