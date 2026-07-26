// Player-facing per-pawn Markdown export.
//
// This is an impure edge adapter: it snapshots the same completed pages visible in normal diary
// play, supplies localized labels to the pure formatter, and writes one UTF-8 .md file under
// RimWorld's save-data folder. The separate developer export remains unchanged.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string PlayerExportFolderName = "PawnDiaryExports";
        private const int PlayerExportFileNamePartLimit = 64;

        /// <summary>
        /// Writes one pawn's complete player-visible diary to a timestamped Markdown file. The caller
        /// remains responsible for showing UI feedback and copying the returned path to the clipboard.
        /// </summary>
        internal bool TryExportPawnDiaryMarkdown(
            string pawnId,
            string pawnName,
            bool pawnAliveForBounds,
            out string filePath,
            out int pageCount,
            out string error)
        {
            return TryExportPawnDiaryMarkdown(
                pawnId,
                pawnName,
                pawnAliveForBounds,
                null,
                out filePath,
                out pageCount,
                out error);
        }

        /// <summary>
        /// Writes the completed pages selected by the current reader view. The request is a plain UI
        /// snapshot: its year and entry keys apply the live filters, while its metadata records that
        /// context in the document header.
        /// </summary>
        internal bool TryExportPawnDiaryMarkdown(
            string pawnId,
            string pawnName,
            bool pawnAliveForBounds,
            DiaryMarkdownExportRequest request,
            out string filePath,
            out int pageCount,
            out string error)
        {
            filePath = string.Empty;
            pageCount = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(pawnId))
            {
                error = "PawnDiary.Export.InvalidSubject".Translate();
                return false;
            }

            try
            {
                string displayName = string.IsNullOrWhiteSpace(pawnName)
                    ? "PawnDiary.Reader.UnknownPawn".Translate().ToString()
                    : pawnName.Trim();
                DiaryMarkdownDocument document = BuildPawnDiaryMarkdownDocument(
                    pawnId,
                    displayName,
                    pawnAliveForBounds,
                    request,
                    out pageCount);

                string exportFolder = Path.Combine(GenFilePaths.SaveDataFolderPath, PlayerExportFolderName);
                Directory.CreateDirectory(exportFolder);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
                string fileNamePart = SafeExportFileNamePart(displayName, pawnId);
                filePath = Path.Combine(
                    exportFolder,
                    "PawnDiary-" + fileNamePart + "-" + stamp + ".md");
                File.WriteAllText(filePath, DiaryMarkdownFormatter.Format(document), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                filePath = string.Empty;
                pageCount = 0;
                return false;
            }
        }

        private DiaryMarkdownDocument BuildPawnDiaryMarkdownDocument(
            string pawnId,
            string displayName,
            bool pawnAliveForBounds,
            DiaryMarkdownExportRequest request,
            out int pageCount)
        {
            DiaryMarkdownDocument document = new DiaryMarkdownDocument
            {
                title = "PawnDiary.Export.MarkdownTitle".Translate(displayName).Resolve(),
                dateLabel = "PawnDiary.Export.DateLabel".Translate().Resolve(),
                categoryLabel = "PawnDiary.Export.CategoryLabel".Translate().Resolve(),
                untitledEntryLabel = "PawnDiary.Export.UntitledEntry".Translate().Resolve(),
                emptyDiaryText = (request == null
                    ? "PawnDiary.Export.EmptyDiary"
                    : "PawnDiary.Export.EmptyFilteredDiary").Translate().Resolve()
            };
            if (request != null)
            {
                for (int i = 0; i < request.metadata.Count; i++)
                {
                    DiaryMarkdownMetadata item = request.metadata[i];
                    if (item != null)
                    {
                        document.metadata.Add(item);
                    }
                }
            }

            // The normal-play flags deliberately exclude pending, prompt-only, and raw debug pages.
            // Running the existing index builder to completion also preserves archive de-duplication and
            // arrival/death boundaries instead of inventing a second definition of "this pawn's diary".
            DiaryTabYearIndexBuild build = BeginTabYearIndexBuild(
                pawnId,
                pawnAliveForBounds,
                false,
                false,
                false);
            while (!build.IsComplete)
            {
                build.ProcessSlice(int.MaxValue, float.MaxValue);
            }

            List<DiaryEntryView> views = new List<DiaryEntryView>();
            if (request == null)
            {
                for (int i = 0; i < build.index.years.Count; i++)
                {
                    build.index.AppendEntriesForYear(views, pawnId, build.index.years[i]);
                }
            }
            else
            {
                // The year is an applied reader filter, not merely display metadata. Materialize only
                // that page before intersecting it with the search/favorite/tag result keys below.
                build.index.AppendEntriesForYear(views, pawnId, request.selectedYear);
            }

            HashSet<string> includedEntryKeys = null;
            if (request != null)
            {
                includedEntryKeys = new HashSet<string>(
                    request.includedEntryKeys,
                    StringComparer.Ordinal);
            }

            for (int i = 0; i < views.Count; i++)
            {
                DiaryEntryView view = views[i];
                string body = view?.DisplayText;
                if (view == null
                    || string.IsNullOrWhiteSpace(body)
                    || (includedEntryKeys != null && !includedEntryKeys.Contains(view.EntryKey)))
                {
                    continue;
                }

                document.entries.Add(new DiaryMarkdownEntry
                {
                    tick = view.Tick,
                    boundaryRank = view.BoundaryRank,
                    date = view.Date,
                    title = view.Title,
                    category = view.GroupLabel,
                    body = body
                });
            }

            pageCount = document.entries.Count;
            return document;
        }

        private static string SafeExportFileNamePart(string preferredName, string fallbackId)
        {
            string cleaned = ReplaceInvalidFileNameCharacters(preferredName);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = ReplaceInvalidFileNameCharacters(fallbackId);
            }

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = "Pawn";
            }

            cleaned = cleaned.Trim(' ', '.');
            if (cleaned.Length > PlayerExportFileNamePartLimit)
            {
                cleaned = cleaned.Substring(0, PlayerExportFileNamePartLimit).TrimEnd(' ', '.');
            }

            return cleaned;
        }

        private static string ReplaceInvalidFileNameCharacters(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                bool replace = char.IsControl(current) || Array.IndexOf(invalid, current) >= 0;
                result.Append(replace ? '_' : current);
            }

            return result.ToString();
        }
    }
}
