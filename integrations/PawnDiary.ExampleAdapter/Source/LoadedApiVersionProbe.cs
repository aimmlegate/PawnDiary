// Pure compatibility probe for the version field on the Pawn Diary assembly that is actually loaded
// at runtime. C# substitutes a referenced `const int` into the adapter at compile time, so reading
// PawnDiaryApi.ApiVersion directly would forever report the version this adapter was built against.
// Reflection reads the loaded type's metadata instead and also supports a future static-field shape.
//
// This helper deliberately depends only on System.Type/Reflection so the RimWorld-free example-
// adapter test harness can prove the compatibility behavior without loading Pawn Diary or the game.
using System;
using System.Reflection;

namespace PawnDiaryExampleAdapter
{
    /// <summary>Reads a non-negative API version from a loaded facade type, or zero on any mismatch.</summary>
    internal static class LoadedApiVersionProbe
    {
        private const string VersionFieldName = "ApiVersion";

        /// <summary>
        /// Reads a public static integer <c>ApiVersion</c> field. Literal constants use their raw
        /// metadata value; ordinary static/readonly fields use their current loaded value.
        /// </summary>
        public static int Read(Type apiType)
        {
            if (apiType == null)
            {
                return 0;
            }

            try
            {
                FieldInfo field = apiType.GetField(
                    VersionFieldName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (field == null || field.FieldType != typeof(int))
                {
                    return 0;
                }

                object raw = field.IsLiteral
                    ? field.GetRawConstantValue()
                    : field.GetValue(null);
                int version = raw is int ? (int)raw : 0;
                return version > 0 ? version : 0;
            }
            catch
            {
                // Missing/incompatible older assemblies must make optional features stay idle, never
                // prevent the adapter or RimWorld from loading.
                return 0;
            }
        }
    }
}
