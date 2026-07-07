// Cloudflare Worker: ingest endpoint for Pawn Diary's opt-out error reporter.
//
// The mod POSTs a small JSON body (see ErrorReportPayload.ToJson in the mod) that has already been
// scrubbed of personal data on the client. This Worker validates the shape, clamps sizes as defense
// in depth, and folds each report into a D1 (SQLite) aggregate so triage is a single SQL query:
// how many times a distinct crash happened, and — more importantly — across how many installs.
//
// It always answers fast and never asks the client to retry (a failed store still returns 204), so a
// bug in the endpoint can never turn into a retry storm from thousands of games.

export interface Env {
  // D1 database binding (see wrangler.toml). Holds the two tables from schema.sql.
  DB: D1Database;
  // Optional shared token. When set (via `wrangler secret put INGEST_TOKEN`), requests must send it
  // in the X-PawnDiary-Token header. NOTE: this is noise-filtering, not real auth — the token would
  // ship inside the mod DLL and is trivially extractable. Leave unset to accept unauthenticated POSTs.
  INGEST_TOKEN?: string;
  // Retention window in days for the cron cleanup (see wrangler.toml [vars]). Default 90.
  RETENTION_DAYS?: string;
}

// The client caps its message at ~4 KB plus small metadata; 16 KB leaves generous headroom.
const MAX_BODY_BYTES = 16 * 1024;
// Highest payload schema this endpoint understands (matches ErrorReportPayload.SchemaVersion).
const MAX_SCHEMA_VERSION = 1;

// Server-side length clamps (belt and suspenders — the client already limits these).
const LIMITS = {
  message: 8000,
  fingerprint: 64,
  modVersion: 64,
  rimworldVersion: 64,
  os: 128,
  installId: 64,
  timestampUtc: 40,
  dlcItem: 32,
  dlcCount: 8,
};

interface ErrorReport {
  schemaVersion: number;
  modVersion: string;
  rimworldVersion: string;
  os: string;
  installId: string;
  fingerprint: string;
  timestampUtc: string;
  activeDlc: string[];
  message: string;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    // A GET is a convenient health check ("is it deployed?").
    if (request.method === "GET") {
      return new Response("Pawn Diary error endpoint: OK\n", {
        status: 200,
        headers: { "content-type": "text/plain; charset=utf-8" },
      });
    }

    if (request.method !== "POST") {
      return json({ error: "method_not_allowed" }, 405);
    }

    // Optional shared-token gate.
    if (env.INGEST_TOKEN) {
      if (request.headers.get("X-PawnDiary-Token") !== env.INGEST_TOKEN) {
        return json({ error: "forbidden" }, 403);
      }
    }

    // Reject oversized bodies before reading them, when the client declares a length.
    const declaredLength = Number(request.headers.get("content-length") ?? "0");
    if (declaredLength > MAX_BODY_BYTES) {
      return json({ error: "payload_too_large" }, 413);
    }

    let raw: string;
    try {
      raw = await request.text();
    } catch {
      return json({ error: "bad_body" }, 400);
    }
    // Guard again after reading, in case content-length was absent or wrong.
    if (raw.length > MAX_BODY_BYTES) {
      return json({ error: "payload_too_large" }, 413);
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      return json({ error: "invalid_json" }, 400);
    }

    const report = validate(parsed);
    if (!report) {
      // Junk / wrong-shape POSTs (bots, scanners) never reach storage.
      return json({ error: "invalid_report" }, 422);
    }

    try {
      await store(env, report);
    } catch (err) {
      // Log for us; still 204 so the client treats it as delivered and never retries.
      console.error("store_failed", err);
    }

    // 204 No Content: accepted, nothing to return.
    return new Response(null, { status: 204 });
  },

  // Cron trigger (see wrangler.toml [triggers]). Runs on a schedule to prune old data so the D1
  // tables do not grow without bound. waitUntil keeps the Worker alive until the deletes finish.
  async scheduled(_event: ScheduledEvent, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(cleanup(env));
  },
};

/**
 * Deletes error groups whose last sighting is older than the retention window, then removes any
 * install rows orphaned by those deletes. Idempotent — safe to run as often as the cron fires.
 */
async function cleanup(env: Env): Promise<void> {
  const days = retentionDays(env.RETENTION_DAYS);
  const cutoff = new Date(Date.now() - days * 86_400_000).toISOString();

  await env.DB.batch([
    env.DB.prepare(`DELETE FROM error_groups WHERE last_seen < ?1`).bind(cutoff),
    // Drop install rows whose group no longer exists (their crash aged out above).
    env.DB.prepare(
      `DELETE FROM error_group_installs
       WHERE NOT EXISTS (
         SELECT 1 FROM error_groups g
         WHERE g.fingerprint = error_group_installs.fingerprint
           AND g.mod_version = error_group_installs.mod_version)`
    ),
  ]);

  console.log(`cleanup: pruned groups with last_seen < ${cutoff} (retention ${days}d)`);
}

/** Parses the retention window, clamped to [1, 3650] days, defaulting to 90. */
function retentionDays(raw?: string): number {
  const n = Number(raw);
  if (!Number.isFinite(n) || n < 1) {
    return 90;
  }
  return Math.min(Math.floor(n), 3650);
}

/**
 * Validates and normalizes an untrusted body into an ErrorReport, or returns null if it is not a
 * plausible report. Everything is coerced/clamped so a malformed field can never reach the database.
 */
function validate(body: unknown): ErrorReport | null {
  if (typeof body !== "object" || body === null) {
    return null;
  }

  const b = body as Record<string, unknown>;

  const schemaVersion = typeof b.schemaVersion === "number" ? b.schemaVersion : NaN;
  if (!Number.isInteger(schemaVersion) || schemaVersion < 1 || schemaVersion > MAX_SCHEMA_VERSION) {
    return null;
  }

  const message = str(b.message, LIMITS.message);
  const fingerprint = str(b.fingerprint, LIMITS.fingerprint);
  // The two fields that make a report meaningful.
  if (message.length === 0 || fingerprint.length === 0) {
    return null;
  }

  return {
    schemaVersion,
    modVersion: str(b.modVersion, LIMITS.modVersion) || "unknown",
    rimworldVersion: str(b.rimworldVersion, LIMITS.rimworldVersion) || "unknown",
    os: str(b.os, LIMITS.os) || "unknown",
    installId: str(b.installId, LIMITS.installId) || "unknown",
    fingerprint,
    timestampUtc: str(b.timestampUtc, LIMITS.timestampUtc),
    activeDlc: strArray(b.activeDlc, LIMITS.dlcCount, LIMITS.dlcItem),
    message,
  };
}

/** Folds one report into the aggregate group and records its install (deduped by install id). */
async function store(env: Env, report: ErrorReport): Promise<void> {
  const now = new Date().toISOString();
  const dlcJson = JSON.stringify(report.activeDlc);

  await env.DB.batch([
    // Group row keyed by (fingerprint, mod_version): bump count + last_seen, keep first sample.
    env.DB.prepare(
      `INSERT INTO error_groups
         (fingerprint, mod_version, first_seen, last_seen, count, sample_message, rimworld_version, os, active_dlc)
       VALUES (?1, ?2, ?3, ?3, 1, ?4, ?5, ?6, ?7)
       ON CONFLICT(fingerprint, mod_version) DO UPDATE SET
         last_seen = ?3,
         count = count + 1`
    ).bind(
      report.fingerprint,
      report.modVersion,
      now,
      report.message,
      report.rimworldVersion,
      report.os,
      dlcJson
    ),
    // Distinct-install tracking: INSERT OR IGNORE so each install counts once per group.
    env.DB.prepare(
      `INSERT OR IGNORE INTO error_group_installs (fingerprint, mod_version, install_id)
       VALUES (?1, ?2, ?3)`
    ).bind(report.fingerprint, report.modVersion, report.installId),
  ]);
}

/** Coerces a value to a trimmed string clamped to maxLen (empty string for non-strings). */
function str(value: unknown, maxLen: number): string {
  if (typeof value !== "string") {
    return "";
  }
  const trimmed = value.trim();
  return trimmed.length > maxLen ? trimmed.slice(0, maxLen) : trimmed;
}

/** Coerces a value to an array of clamped strings, capped at maxItems. */
function strArray(value: unknown, maxItems: number, maxItemLen: number): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  const out: string[] = [];
  for (const item of value) {
    if (out.length >= maxItems) {
      break;
    }
    const s = str(item, maxItemLen);
    if (s.length > 0) {
      out.push(s);
    }
  }
  return out;
}

/** Small JSON response helper. */
function json(payload: unknown, status: number): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}
