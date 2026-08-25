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
using System.Text;
using System.Threading;
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

                // The platform adapter serializes the compare-and-commit window across Pawn Diary
                // processes. It rechecks the predecessor while owning that mutex and performs only the
                // exact existing/absent primitive selected by the captured state.
                MemorySettingsPlatformAdapter.Commit(
                    canonicalPath,
                    stagePath,
                    predecessorExisted,
                    predecessorSha256);
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

    /// <summary>
    /// Windows same-volume settings commit boundary. A path-derived named mutex extends the in-process
    /// lock across game processes, so every Pawn Diary writer compares the predecessor and replaces or
    /// creates within one serialized platform operation.
    /// </summary>
    internal static class MemorySettingsPlatformAdapter
    {
        internal static void Commit(
            string canonicalPath,
            string stagePath,
            bool predecessorExisted,
            string predecessorSha256)
        {
            string mutexName = "Local\\PawnDiary.MemorySettings." + Sha256Text(
                Path.GetFullPath(canonicalPath).ToUpperInvariant());
            using (Mutex mutex = new Mutex(false, mutexName))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                        throw new IOException("Another Pawn Diary settings commit is in progress.");

                    bool currentExists = File.Exists(canonicalPath);
                    string currentSha256 = currentExists
                        ? Sha256File(canonicalPath) : string.Empty;
                    if (!MemorySettingsCommitPolicy.PredecessorMatches(
                            predecessorExisted,
                            predecessorSha256,
                            currentExists,
                            currentSha256))
                        throw new IOException(
                            "The canonical settings file changed during publication.");

                    if (predecessorExisted)
                        File.Replace(stagePath, canonicalPath, null);
                    else
                        File.Move(stagePath, canonicalPath);
                }
                finally
                {
                    if (acquired) mutex.ReleaseMutex();
                }
            }
        }

        private static string Sha256Text(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? string.Empty))).Replace("-", string.Empty);
        }

        private static string Sha256File(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
