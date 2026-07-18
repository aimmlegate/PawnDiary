// Optional-mod runtime test for the real RimTalk shared-memory provider. The core RimTest assembly
// deliberately has no compile-time RimTalk/bridge reference: reflection keeps this suite loadable when
// either optional package is absent, while an active bridge run still enters its real PromptContext API.
//
// New to C#/RimWorld? See AGENTS.md ("Optional-mod hooks") and docs/lore/build.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Integration;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that the loaded RimTalk bridge reads completed pair memories without creating a duplicate
    /// or recursively submitting another Pawn Diary entry.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRimTalkBridgeRuntimeTests
    {
        private const string BridgePackageId = "aimmlegate.pawndiary.rimtalkbridge";
        private const string RimTalkPackageId = "cj.rimtalk";
        private const string TestSourceId = "pawndiary.rimtest.rimtalk-memory";
        private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Two completed caller-authored pair memories enter the context variable registered with
        /// RimTalk. Auto-injection adds the variable token to the active preset, and reading the block
        /// is side-effect free: the pair retains exactly the two submitted events.
        /// </summary>
        [Test]
        public static void RegisteredBridgeInjectsPairMemoriesAndAutoEntryWithoutRecursion()
        {
            if (!ModsConfig.IsActive(BridgePackageId) || !ModsConfig.IsActive(RimTalkPackageId))
            {
                Log.Message("[PawnDiary RimTest RimTalk bridge] not applicable (bridge or RimTalk inactive).");
                return;
            }

            Type modType = ResolveType(
                "PawnDiaryRimTalkBridge.PawnDiaryRimTalkBridgeMod, PawnDiaryRimTalkBridge");
            Type injectorType = ResolveType(
                "PawnDiaryRimTalkBridge.SharedMemoryInjector, PawnDiaryRimTalkBridge");
            Type promptContextType = ResolveType("RimTalk.Prompt.PromptContext, RimTalk");
            Type contextRegistryType = ResolveType("RimTalk.API.ContextHookRegistry, RimTalk");
            Type promptApiType = ResolveType("RimTalk.API.RimTalkPromptAPI, RimTalk");
            FieldInfo settingsField = RequireField(modType, "Settings");
            object bridgeSettings = settingsField.GetValue(null);
            PawnDiaryRimTestScope.Require(bridgeSettings != null,
                "The active RimTalk bridge did not expose its initialized settings object.");

            FieldInfo integrationLevelField = RequireField(bridgeSettings.GetType(), "integrationLevel");
            FieldInfo injectSharedMemoryField = RequireField(bridgeSettings.GetType(), "injectSharedMemory");
            FieldInfo sharedMemoryCountField = RequireField(bridgeSettings.GetType(), "sharedMemoryCount");
            FieldInfo autoInjectField = RequireField(bridgeSettings.GetType(), "autoInjectSharedMemory");
            MethodInfo resetMethod = RequireMethod(injectorType, "ResetForNewGame");
            MethodInfo processQueueMethod = RequireMethod(injectorType, "ProcessQueue");
            MethodInfo syncAutoInjectMethod = RequireMethod(injectorType, "SyncAutoInject");
            MethodInfo hasContextVariableMethod = RequireMethod(contextRegistryType, "HasContextVariable");
            MethodInfo tryGetContextVariableMethod = RequireMethod(
                contextRegistryType, "TryGetContextVariable");
            MethodInfo getActivePresetMethod = RequireMethod(promptApiType, "GetActivePreset");

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            Pawn first = null;
            Pawn second = null;
            PawnDiarySettings coreSettings = PawnDiaryMod.Settings;
            bool originalExternal = coreSettings.allowExternalIntegrations;
            int originalLevel = (int)integrationLevelField.GetValue(bridgeSettings);
            bool originalInject = (bool)injectSharedMemoryField.GetValue(bridgeSettings);
            int originalCount = (int)sharedMemoryCountField.GetValue(bridgeSettings);
            bool originalAutoInject = (bool)autoInjectField.GetValue(bridgeSettings);
            try
            {
                coreSettings.allowExternalIntegrations = true;
                integrationLevelField.SetValue(bridgeSettings, 1);
                injectSharedMemoryField.SetValue(bridgeSettings, true);
                sharedMemoryCountField.SetValue(bridgeSettings, 3);
                autoInjectField.SetValue(bridgeSettings, true);
                resetMethod.Invoke(null, null);
                syncAutoInjectMethod.Invoke(null, null);
                PawnDiaryRimTestScope.Require(
                    (bool)hasContextVariableMethod.Invoke(null, new object[] { "diary_shared" }),
                    "RimTalk's registry does not contain the bridge's diary_shared variable.");
                RequireAutoInjectedEntry(getActivePresetMethod.Invoke(null, null));

                first = scope.CreateAdultColonist();
                second = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(first);
                scope.SpawnAsLiveColonist(second);

                DiaryEventSubmissionResult growthLinked = SubmitMemory(
                    first,
                    second,
                    "pawndiary_rimtest_growth_linked_pair_memory",
                    "A growth-linked memory",
                    "They remembered choosing new interests together.");
                DiaryEventSubmissionResult birthLinked = SubmitMemory(
                    first,
                    second,
                    "pawndiary_rimtest_birth_linked_pair_memory",
                    "A birth-linked family memory",
                    "They remembered welcoming a child into the family.");
                PawnDiaryRimTestScope.Require(growthLinked.recorded && birthLinked.recorded,
                    "The bridge fixture could not create its two completed source memories.");

                int beforeCount = ContextEntryCount(first);
                object promptContext = Activator.CreateInstance(promptContextType);
                SetProperty(promptContextType, promptContext, "CurrentPawn", first);
                SetProperty(promptContextType, promptContext, "AllPawns", new List<Pawn> { first, second });
                SetProperty(promptContextType, promptContext, "IsPreview", false);

                // Resolve through RimTalk's registry, not the bridge's helper, so this proves the
                // production RegisterContextVariable attachment as well as provider behavior.
                GetRegisteredContextVariable(tryGetContextVariableMethod, promptContext);
                processQueueMethod.Invoke(null, new object[] { Find.TickManager?.TicksGame ?? 0 });
                string block = GetRegisteredContextVariable(
                    tryGetContextVariableMethod, promptContext);

                PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(block),
                    "The active RimTalk bridge returned no shared-memory block.");
                PawnDiaryRimTestScope.Require(
                    block.IndexOf("A growth-linked memory", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The shared-memory block omitted the completed growth-linked pair memory.");
                PawnDiaryRimTestScope.Require(
                    block.IndexOf("A birth-linked family memory", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The shared-memory block omitted the completed birth-linked pair memory.");
                PawnDiaryRimTestScope.Require(ContextEntryCount(first) == beforeCount,
                    "Reading RimTalk shared memory recursively created or removed a Pawn Diary entry.");
            }
            finally
            {
                try
                {
                    resetMethod.Invoke(null, null);
                    integrationLevelField.SetValue(bridgeSettings, originalLevel);
                    injectSharedMemoryField.SetValue(bridgeSettings, originalInject);
                    sharedMemoryCountField.SetValue(bridgeSettings, originalCount);
                    autoInjectField.SetValue(bridgeSettings, originalAutoInject);
                    coreSettings.allowExternalIntegrations = originalExternal;
                    syncAutoInjectMethod.Invoke(null, null);
                }
                finally
                {
                    scope.TearDown();
                }
            }
        }

        private static string GetRegisteredContextVariable(MethodInfo method, object promptContext)
        {
            object[] arguments = { "diary_shared", promptContext, null };
            bool found = (bool)method.Invoke(null, arguments);
            PawnDiaryRimTestScope.Require(found,
                "RimTalk could not resolve its registered diary_shared context variable.");
            return arguments[2] as string ?? string.Empty;
        }

        private static void RequireAutoInjectedEntry(object preset)
        {
            PawnDiaryRimTestScope.Require(preset != null,
                "RimTalk returned no active prompt preset after bridge auto-injection.");
            FieldInfo entriesField = RequireField(preset.GetType(), "Entries");
            IEnumerable entries = entriesField.GetValue(preset) as IEnumerable;
            PawnDiaryRimTestScope.Require(entries != null,
                "RimTalk's active prompt preset exposed no entry collection.");
            foreach (object entry in entries)
            {
                if (entry == null) continue;
                PropertyInfo sourceProperty = entry.GetType().GetProperty("SourceModId");
                FieldInfo contentField = entry.GetType().GetField("Content");
                string source = sourceProperty?.GetValue(entry, null) as string;
                string content = contentField?.GetValue(entry) as string;
                if (string.Equals(source, "aimmlegate.pawndiary.rimtalkbridge",
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(content, "{{diary_shared}}", StringComparison.Ordinal)) return;
            }
            throw new AssertionException(
                "The active RimTalk preset omitted the bridge-owned {{diary_shared}} prompt entry.");
        }

        private static DiaryEventSubmissionResult SubmitMemory(
            Pawn subject,
            Pawn partner,
            string eventKey,
            string title,
            string text)
        {
            return PawnDiaryApi.SubmitDirectEntry(new ExternalDirectEntryRequest
            {
                sourceId = TestSourceId,
                eventKey = eventKey,
                subject = subject,
                partner = partner,
                text = text,
                partnerText = text,
                title = title,
                partnerTitle = title,
                summaryText = text,
                eventLabel = title,
                forceRecord = true,
                dedupKey = eventKey + "|" + subject.GetUniqueLoadID() + "|" + partner.GetUniqueLoadID()
            });
        }

        private static int ContextEntryCount(Pawn pawn)
        {
            DiaryContextSnapshot snapshot = PawnDiaryApi.GetContextSnapshot(pawn, 32);
            return snapshot?.entries?.Count ?? 0;
        }

        private static Type ResolveType(string qualifiedName)
        {
            Type type = Type.GetType(qualifiedName, false);
            if (type == null)
            {
                throw new AssertionException("Could not resolve optional runtime type '" + qualifiedName + "'.");
            }

            return type;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type?.GetField(name, StaticAny | BindingFlags.Instance);
            if (field == null)
            {
                throw new AssertionException("Could not resolve field '" + name + "' on " + type?.FullName + ".");
            }

            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type?.GetMethod(name, StaticAny);
            if (method == null)
            {
                throw new AssertionException("Could not resolve method '" + name + "' on " + type?.FullName + ".");
            }

            return method;
        }

        private static void SetProperty(Type type, object instance, string name, object value)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite)
            {
                throw new AssertionException("Could not write RimTalk PromptContext." + name + ".");
            }

            property.SetValue(instance, value, null);
        }
    }
}
