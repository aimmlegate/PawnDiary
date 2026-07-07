# Pawn Diary — error ingest endpoint

A tiny [Cloudflare Worker](https://developers.cloudflare.com/workers/) + [D1](https://developers.cloudflare.com/d1/)
database that receives the mod's opt-out error reports and aggregates them for triage.

It is the server side of the reporter documented in `DOCUMENTATION.md §8.1`. The mod only sends when
`DiaryErrorReporter.ErrorReportEndpoint` is set — until you deploy this and paste the URL there, the
reporter is inert.

## What a report contains

The mod scrubs everything before sending. The body is exactly `ErrorReportPayload.ToJson`:

```json
{
  "schemaVersion": 1,
  "modVersion": "1.2.3.4",
  "rimworldVersion": "1.6.4241",
  "os": "Microsoft Windows NT 10.0.22631.0",
  "installSource": "workshop",        // "workshop" | "local" | "unknown"
  "installId": "a1b2c3...",           // anonymous random GUID, not a machine id
  "fingerprint": "9f8e7d6c5b4a3210",  // deterministic hash of the normalized stack
  "timestampUtc": "2026-07-07T12:00:00.0000000Z",
  "activeDlc": ["Royalty", "Anomaly"],
  "message": "…scrubbed error text + stack…"
}
```

No usernames, file paths, save/colony/pawn names, API keys, or diary text ever leave the client.

## Deploy

```bash
cd services/error-endpoint
npm install
npx wrangler login

# 1. Create the D1 database, then paste the printed database_id into wrangler.toml
npx wrangler d1 create pawndiary_errors

# 2. Create the tables (remote D1)
npm run db:init

# 3. Deploy — prints your endpoint URL, e.g. https://pawndiary-error-endpoint.<you>.workers.dev
npm run deploy
```

Then point the mod at it: set the endpoint constant in
[`Source/Diagnostics/DiaryErrorReporter.cs`](../../Source/Diagnostics/DiaryErrorReporter.cs) —

```csharp
private const string ErrorReportEndpoint = "https://pawndiary-error-endpoint.<you>.workers.dev";
```

— and rebuild the mod (`MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`).

### Optional: shared token

To filter random internet noise, set a token as a secret and require it:

```bash
npx wrangler secret put INGEST_TOKEN   # paste any random string
```

Then send it from the mod by adding one header in `DiaryErrorReporter.SendAsync`:

```csharp
content.Headers.Add("X-PawnDiary-Token", "<the same random string>");
```

This is noise-filtering, **not** security — the token ships inside the DLL and is extractable. Leave
`INGEST_TOKEN` unset to accept unauthenticated POSTs (shape validation already rejects junk).

## Local run + smoke test

```bash
npm run db:init:local
npm run dev            # serves on http://localhost:8787

curl -sS -X POST http://localhost:8787 \
  -H 'content-type: application/json' \
  -d '{"schemaVersion":1,"modVersion":"1.2.3.4","rimworldVersion":"1.6","os":"test","installId":"install-A","fingerprint":"deadbeefcafef00d","timestampUtc":"2026-07-07T12:00:00Z","activeDlc":["Royalty"],"message":"NullReferenceException in Diary.Foo\n  at Diary.Foo:line 12"}' \
  -w '\nHTTP %{http_code}\n'
# expect: HTTP 204   (GET / returns 200 as a health check; junk bodies return 400/422)
```

## Triage query

The signal that matters is **how many installs** hit a crash, not raw count (1000 hits from one
looping game is noise; 1000 hits from 500 installs is a real bug):

```sql
SELECT
  g.fingerprint,
  g.mod_version,
  g.count                              AS total_hits,
  (SELECT COUNT(*) FROM error_group_installs i
     WHERE i.fingerprint = g.fingerprint AND i.mod_version = g.mod_version) AS installs,
  g.first_seen,
  g.last_seen,
  g.rimworld_version,
  g.install_source,
  g.active_dlc,
  substr(g.sample_message, 1, 300)     AS sample
FROM error_groups g
ORDER BY installs DESC, total_hits DESC
LIMIT 50;
```

Run it against the deployed DB with:

```bash
npx wrangler d1 execute pawndiary_errors --remote --command "PASTE_QUERY_HERE"
```

## Retention (automatic)

A cron trigger prunes old data so D1 never grows without bound. It is built in — no extra setup:

- Schedule: `[triggers] crons = ["0 3 * * *"]` in `wrangler.toml` (daily, 03:00 UTC).
- Window: `[vars] RETENTION_DAYS = "90"`. The `scheduled` handler deletes `error_groups` whose
  `last_seen` is older than that, then removes install rows orphaned by those deletes. Change the
  number and re-`deploy` to keep data longer/shorter (clamped to 1–3650 days).

Test the cleanup locally without waiting for the clock:

```bash
npm run dev -- --test-scheduled           # exposes http://localhost:8787/__scheduled
curl "http://localhost:8787/__scheduled?cron=0+3+*+*+*"   # runs it once → "Ran scheduled event"
```

After deploy, confirm the schedule is registered: `wrangler deploy` prints the cron, and you can
trigger it on demand with `npx wrangler triggers` / from the Cloudflare dashboard (Workers →
your worker → Triggers / Cron).

## Notes

- The Worker always returns fast and never asks the client to retry (a failed DB write still returns
  `204`), so an endpoint problem can never cause a retry storm from many games.
- `schemaVersion` lets the payload evolve; bump `MAX_SCHEMA_VERSION` in `src/index.ts` when it does.
