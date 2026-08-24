// MemorySettingsDurableWriter.cs — crash-safe, verified publication for the one mod-settings file.
//
// RimWorld's stock ModSettings.Write writes directly to the canonical XML path. Memory policy
// generations are commit markers, so M5 must not publish them until a complete settings document is
// durable. This adapter writes and verifies a same-directory stage, then atomically replaces the
// canonical file. It deliberately owns no policy decisions; those stay in MemoryPolicyNormalizer.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;
using Verse;

namespace PawnDiary
{
    /// <summary>Typed result from one verified settings-file publication attempt.</summary>
    internal sealed class MemorySettingsWriteResult
    {
        internal bool persisted;
        internal string canonicalPath = string.Empty;
        internal string verifiedSha256 = string.Empty;
        internal string failure = string.Empty;
    }

    /// <summary>Publishes settings through a verified same-volume stage and atomic replace.</summary>
    internal static class MemorySettingsDurableWriter
    {
        private static readonly object PublishSync = new object();
        private static readonly MethodInfo GetSettingsFilenameMethod =
            typeof(LoadedModManager).GetMethod(
                "GetSettingsFilename",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string) },
                null);

        /// <summary>
        /// Writes the exact document shape used by LoadedModManager and commits it atomically.
        /// A false result guarantees that this method did not replace the canonical file.
        /// </summary>
        internal static MemorySettingsWriteResult TryWrite(
            string modIdentifier,
            string modHandleName,
            PawnDiarySettings settings)
        {
            lock (PublishSync)
                return TryWriteLocked(modIdentifier, modHandleName, settings);
        }

        private static MemorySettingsWriteResult TryWriteLocked(
            string modIdentifier,
            string modHandleName,
            PawnDiarySettings settings)
        {
            MemorySettingsWriteResult result = new MemorySettingsWriteResult();
            string stagePath = string.Empty;
            try
            {
                if (GetSettingsFilenameMethod == null)
                    throw new MissingMethodException(
                        "Verse.LoadedModManager.GetSettingsFilename was not found.");
                if (settings == null) throw new ArgumentNullException(nameof(settings));

                string canonicalPath = GetSettingsFilenameMethod.Invoke(
                    null,
                    new object[] { modIdentifier ?? string.Empty, modHandleName ?? string.Empty })
                    as string;
                if (string.IsNullOrEmpty(canonicalPath))
                    throw new InvalidOperationException("RimWorld returned an empty settings path.");
                result.canonicalPath = canonicalPath;

                string directory = Path.GetDirectoryName(canonicalPath);
                if (string.IsNullOrEmpty(directory))
                    throw new InvalidOperationException("The settings path has no parent directory.");
                Directory.CreateDirectory(directory);
                bool predecessorExisted = File.Exists(canonicalPath);
                string predecessorSha256 = predecessorExisted
                    ? Sha256(canonicalPath)
                    : string.Empty;
                stagePath = Path.Combine(
                    directory,
                    Path.GetFileName(canonicalPath) + ".pawndiary-stage-" +
                    Guid.NewGuid().ToString("N"));

                Scribe.saver.InitSaving(stagePath, "SettingsBlock");
                try
                {
                    ModSettings value = settings;
                    Scribe_Deep.Look(ref value, "ModSettings");
                }
                finally
                {
                    Scribe.saver.FinalizeSaving();
                }

                // FinalizeSaving closes its stream. Reopen and request a disk flush before parsing so
                // verification covers the bytes the filesystem accepted, not a managed write buffer.
                using (FileStream stream = new FileStream(
                    stagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    stream.Flush(true);
                }

                XmlDocument document = new XmlDocument { XmlResolver = null };
                document.Load(stagePath);
                if (!string.Equals(document.DocumentElement?.Name,
                        "SettingsBlock", StringComparison.Ordinal)
                    || document.DocumentElement.SelectNodes("ModSettings")?.Count != 1)
                    throw new InvalidDataException("The staged settings XML is incomplete.");
                result.verifiedSha256 = Sha256(stagePath);

                // Refuse to overwrite a settings writer that won while Scribe was staging. This is
                // deliberately checked after every potentially slow serialization/verification step.
                bool currentExists = File.Exists(canonicalPath);
                if (currentExists != predecessorExisted
                    || (currentExists && !string.Equals(
                        Sha256(canonicalPath), predecessorSha256, StringComparison.Ordinal)))
                    throw new IOException("The canonical settings file changed during publication.");

                // Both operations are same-directory and therefore same-volume. File.Replace supplies
                // the atomic overwrite path; File.Move is the create-only equivalent for first run.
                if (File.Exists(canonicalPath))
                    File.Replace(stagePath, canonicalPath, null);
                else
                    File.Move(stagePath, canonicalPath);
                stagePath = string.Empty;
                result.persisted = true;
            }
            catch (Exception exception)
            {
                result.failure = exception.GetType().Name + ": " + exception.Message;
            }
            finally
            {
                if (!string.IsNullOrEmpty(stagePath))
                {
                    try
                    {
                        if (File.Exists(stagePath)) File.Delete(stagePath);
                    }
                    catch
                    {
                        // A stale unique stage is inert. Preserve the primary failure above.
                    }
                }
            }
            return result;
        }

        private static string Sha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
