// Pure policy for the one question the error reporter's capture hook asks: "is this logged error one
// of ours?" The reporter forwards a message only when it starts with a Pawn Diary family log prefix,
// so the base game's and other mods' log lines are never sent. This file is the single source of truth
// for what "ours" means: the main mod PLUS its first-party integration submods (the bridges), each of
// which tags its own log lines with its own prefix but all of which log through the same Verse.Log —
// so one global postfix (DiaryLogReportPatch) captures the whole family.
//
// It is split out from that Harmony patch on purpose: the match is plain string logic with no
// Verse/Harmony dependency, so it lives here as a pure helper and is unit-tested in tests/ (see
// SKILL.md step 6 — "if a function can be pure, make it pure"). DiaryLogReportPatch owns the capture
// wiring; DiaryErrorReporter owns the transport.
//
// New to C#/RimWorld? `StartsWith(..., StringComparison.Ordinal)` is a plain, culture-independent
// prefix test (like JS `str.startsWith(prefix)`), the right choice for matching a fixed tag we author.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Decides whether a logged error string belongs to the Pawn Diary mod family (the main mod or one
    /// of its first-party integration submods) and should therefore be reported.
    /// </summary>
    internal static class ModErrorPrefixPolicy
    {
        // Each entry is matched with StartsWith, i.e. "any log line beginning with this text is ours."
        // We match the family's two name ROOTS, not each submod's full tag, so a new "[Pawn Diary: X]"
        // bridge is covered the day it ships — no edit here needed:
        //   "[Pawn Diary]"  — the MAIN mod's own log lines.
        //   "[Pawn Diary:"  — submods that tag with the spaced name; this colon root covers every
        //                     "[Pawn Diary: X]" bridge, e.g. "[Pawn Diary: 1-2-3 Personalities]"
        //                     (PawnDiary.PersonalitiesBridge) and "[Pawn Diary: VSIE]" (PawnDiary.Vsie).
        //   "[PawnDiary"    — submods that tag with the no-space name, e.g.
        //                     "[PawnDiary: RimTalk bridge]"        (integrations/PawnDiary.RimTalkBridge).
        // Deliberately NOT matched: "[Pawn Diary Example Adapter]" — that is the copy-me template for
        // third-party adapters (integrations/PawnDiary.ExampleAdapter), whose errors are not ours to
        // report. Only a submod that invents a brand-new root (neither spelling above) needs another
        // entry here — and a matching row in the pure test.
        private static readonly string[] Prefixes =
        {
            "[Pawn Diary]",
            "[Pawn Diary:",
            "[PawnDiary"
        };

        /// <summary>
        /// True when <paramref name="text"/> starts with a Pawn Diary family log prefix (the main mod or
        /// a first-party submod). Null or empty is never ours. Case-sensitive, to match how the prefixes
        /// are written in the log lines.
        /// </summary>
        public static bool IsModErrorMessage(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < Prefixes.Length; i++)
            {
                if (text.StartsWith(Prefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
