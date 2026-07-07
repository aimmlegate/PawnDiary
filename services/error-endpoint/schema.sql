-- D1 (SQLite) schema for the Pawn Diary error endpoint.
-- Apply with:  npm run db:init         (remote)
--          or  npm run db:init:local   (local dev DB)

-- One row per distinct crash, keyed by fingerprint + mod version. `count` is total occurrences;
-- distinct installs are counted from error_group_installs (see the triage query in README.md).
CREATE TABLE IF NOT EXISTS error_groups (
  fingerprint      TEXT NOT NULL,
  mod_version      TEXT NOT NULL,
  first_seen       TEXT NOT NULL,   -- ISO-8601 UTC, set by the Worker on first sighting
  last_seen        TEXT NOT NULL,   -- ISO-8601 UTC, updated every report
  count            INTEGER NOT NULL DEFAULT 0,
  sample_message   TEXT,            -- scrubbed message + stack from the first sighting
  rimworld_version TEXT,
  os               TEXT,
  active_dlc       TEXT,            -- JSON array string, e.g. ["Royalty","Anomaly"]
  PRIMARY KEY (fingerprint, mod_version)
);

-- One row per (crash, install). PRIMARY KEY makes INSERT OR IGNORE dedupe installs automatically.
CREATE TABLE IF NOT EXISTS error_group_installs (
  fingerprint TEXT NOT NULL,
  mod_version TEXT NOT NULL,
  install_id  TEXT NOT NULL,
  PRIMARY KEY (fingerprint, mod_version, install_id)
);

CREATE INDEX IF NOT EXISTS idx_groups_last_seen ON error_groups (last_seen);
