---
name: repo-wiki-maintenance
description: Maintain Pawn Diary's committed GitHub repository wiki. Use when manually refreshing, correcting, relocating, or validating pages under repowiki/ without changing the existing page hierarchy.
---

# Repository Wiki Maintenance

Use this skill for documentation-only wiki work in this repository. The public wiki lives in
`repowiki/en/content/`; its root landing page is `repowiki/README.md`. Keep the existing folders
and page names stable so links and review history remain useful.

## Workflow

1. Read `AGENTS.md` and the relevant current source/Defs before making claims.
2. Treat `repowiki/en/content/` as a manually maintained snapshot. Do not regenerate or copy the
   `.qoder` metadata, knowledge cache, or opaque generator state into the repository.
3. Update only pages affected by the current code or documentation change. Prefer concise factual
   corrections over broad prose rewrites.
4. Keep citations GitHub-relative. A page directly under `en/content/` needs `../../../` to reach
   the repository root; each additional page subdirectory adds one more `../`.
5. Update `repowiki/README.md` when the page map, snapshot date, or maintenance workflow changes.
6. Keep wiki edits focused. Do not recreate removed root documentation or event maps. Update a
   committed skill only when the work introduces important reusable agent knowledge. Add only a
   short compact changelog line when the change is notable.

## Validation

- Confirm the source and destination Markdown file counts match after any migration.
- Search `repowiki/en/content/` for `file://`, `.qoder/`, absolute Windows paths, and broken
  repository-relative targets.
- Check that every README landing-page link resolves, including links containing spaces or `&`.
- Do not run a RimWorld build for a documentation-only change unless code was also changed; run
  the normal build and relevant tests when source behavior was updated.

## Refresh boundaries

The source and XML remain authoritative for behavior. Focused wiki pages are human-readable
explanations; committed skills hold agent-only workflow and invariants. If a page and the current
source disagree, correct the page; do not preserve stale generated claims merely because they came
from the old export.
