# Pawn Diary Repository Wiki

This is the committed, GitHub-readable repository wiki for Pawn Diary. It preserves the page
hierarchy produced by the original `.qoder` export under [`en/content`](en/content/).

## Start here

- [Getting Started](en/content/Getting%20Started.md) — installation, configuration, and the main
  runtime pieces.
- [Core Architecture](en/content/Core%20Architecture/Core%20Architecture.md) — lifecycle, layers,
  and data flow.
- [Event System](en/content/Event%20System/Event%20System.md) — event capture and signal handling.
- [AI Generation Engine](en/content/AI%20Generation%20Engine/AI%20Generation%20Engine.md) — prompt,
  model, response, and decoration stages.
- [Configuration & Customization](en/content/Configuration%20%26%20Customization/Configuration%20%26%20Customization.md)
  — settings and XML-driven policy.
- [DLC Integrations](en/content/DLC%20Integrations/DLC%20Integrations.md) — optional DLC behavior and
  no-DLC safety.
- [Integration Framework](en/content/Integration%20Framework/Integration%20Framework.md) — public
  adapters and external integrations.
- [Development Guide](en/content/Development%20Guide/Development%20Guide.md) — setup, testing, and
  contribution workflow.
- [Troubleshooting & FAQ](en/content/Troubleshooting%20%26%20FAQ.md) — common problems and fixes.

## Snapshot status

The pages were manually migrated from the local `.qoder` export on 2026-07-23. The source export
was generated on 2026-07-21, so this is a checked-in documentation snapshot, not a live generated
view. New behavior and structure changes must be reflected here manually; see the
[`repo-wiki-maintenance` skill](../skills/repo-wiki-maintenance/SKILL.md) for the maintenance
workflow.

Generated `file://` citations were converted to repository-relative links so GitHub can open source
files directly. The original page and directory names are intentionally retained to make future
manual refreshes diffable.
