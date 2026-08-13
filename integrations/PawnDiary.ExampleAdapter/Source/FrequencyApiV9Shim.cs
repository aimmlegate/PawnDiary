// Reflection-isolated access to Pawn Diary's optional API-v9 event-frequency surface.
//
// The example adapter is intentionally usable with older Pawn Diary builds. Naming a v9-only DTO
// or calling a v9-only method directly would leave a hard metadata reference in the adapter DLL;
// Mono could then reject or fail that code beside API v8 before a version check could help. This
// helper depends only on System.Type, primitive arguments, and adapter-owned DTOs. It discovers the
// loaded facade members after LoadedApiVersionProbe confirms API v9 or newer.
//
// This file stays free of Verse/RimWorld/PawnDiary references so the console harness can exercise
// the compatibility boundary with dynamically emitted old/new facade shapes.
//
// New to C#? See AGENTS.md.
using System;
using System.Collections;
using System.Reflection;

namespace PawnDiaryExampleAdapter
{
    /// <summary>Safely invokes and detaches the optional API-v9 event-frequency contract.</summary>
    internal static class FrequencyApiV9Shim
    {
        public const int MinimumApiVersion = 9;

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>Returns whether the supplied loaded facade advertises the v9 surface.</summary>
        public static bool IsSupported(Type apiType)
        {
            return LoadedApiVersionProbe.Read(apiType) >= MinimumApiVersion;
        }

        /// <summary>Reads and detaches the v9 frequency snapshot, or null on old/mismatched cores.</summary>
        public static AdapterEventFrequencySettingsSnapshot GetEventFrequencySettings(Type apiType)
        {
            if (!IsSupported(apiType))
            {
                return null;
            }

            try
            {
                MethodInfo method = FindMethod(apiType, "GetEventFrequencySettings", Type.EmptyTypes);
                object source = method == null ? null : method.Invoke(null, null);
                return CopySnapshot(source);
            }
            catch
            {
                // Optional integration features degrade to their documented safe values. Reflection
                // failures must never prevent the adapter itself from loading or opening its explorer.
                return null;
            }
        }

        /// <summary>Selects one loaded preset through API v9, or returns false when unavailable.</summary>
        public static bool SetEventFrequencyPreset(Type apiType, string presetDefName)
        {
            return InvokeBool(apiType, "SetEventFrequencyPreset",
                new[] { typeof(string) }, new object[] { presetDefName });
        }

        /// <summary>Sets one event group's finite multiplier through API v9 when available.</summary>
        public static bool SetEventFrequencyMultiplier(Type apiType, string key, float multiplier)
        {
            return InvokeBool(apiType, "SetEventFrequencyMultiplier",
                new[] { typeof(string), typeof(float) }, new object[] { key, multiplier });
        }

        /// <summary>Returns one event group to preset inheritance through API v9 when available.</summary>
        public static bool ResetEventFrequencyMultiplier(Type apiType, string key)
        {
            return InvokeBool(apiType, "ResetEventFrequencyMultiplier",
                new[] { typeof(string) }, new object[] { key });
        }

        private static bool InvokeBool(
            Type apiType,
            string methodName,
            Type[] parameterTypes,
            object[] arguments)
        {
            if (!IsSupported(apiType))
            {
                return false;
            }

            try
            {
                MethodInfo method = FindMethod(apiType, methodName, parameterTypes);
                object result = method == null ? null : method.Invoke(null, arguments);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindMethod(Type apiType, string methodName, Type[] parameterTypes)
        {
            if (apiType == null)
            {
                return null;
            }

            return apiType.GetMethod(
                methodName,
                PublicStatic,
                binder: null,
                types: parameterTypes,
                modifiers: null);
        }

        private static AdapterEventFrequencySettingsSnapshot CopySnapshot(object source)
        {
            if (source == null)
            {
                return null;
            }

            AdapterEventFrequencySettingsSnapshot copy = new AdapterEventFrequencySettingsSnapshot
            {
                selectedPresetDefName = ReadString(source, "selectedPresetDefName"),
                selectedPresetLabel = ReadString(source, "selectedPresetLabel"),
                hasCustomOverrides = ReadBool(source, "hasCustomOverrides")
            };

            IEnumerable rows = ReadMember(source, "filters") as IEnumerable;
            if (rows == null)
            {
                return copy;
            }

            foreach (object row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                copy.filters.Add(new AdapterEventFrequencyFilterSnapshot
                {
                    key = ReadString(row, "key"),
                    label = ReadString(row, "label"),
                    domain = ReadString(row, "domain"),
                    enabled = ReadBool(row, "enabled"),
                    defaultEnabled = ReadBool(row, "defaultEnabled"),
                    hasOverride = ReadBool(row, "hasOverride"),
                    frequencyTier = ReadString(row, "frequencyTier"),
                    presetFrequencyMultiplier = ReadFloat(row, "presetFrequencyMultiplier"),
                    effectiveFrequencyMultiplier = ReadFloat(row, "effectiveFrequencyMultiplier"),
                    hasFrequencyOverride = ReadBool(row, "hasFrequencyOverride")
                });
            }

            return copy;
        }

        private static object ReadMember(object source, string name)
        {
            if (source == null)
            {
                return null;
            }

            Type sourceType = source.GetType();
            FieldInfo field = sourceType.GetField(name, PublicInstance);
            if (field != null)
            {
                return field.GetValue(source);
            }

            PropertyInfo property = sourceType.GetProperty(name, PublicInstance);
            return property != null && property.GetIndexParameters().Length == 0
                ? property.GetValue(source, null)
                : null;
        }

        private static string ReadString(object source, string name)
        {
            return ReadMember(source, name) as string ?? string.Empty;
        }

        private static bool ReadBool(object source, string name)
        {
            object value = ReadMember(source, name);
            return value is bool && (bool)value;
        }

        private static float ReadFloat(object source, string name)
        {
            object value = ReadMember(source, name);
            return value is float ? (float)value : 0f;
        }
    }
}
