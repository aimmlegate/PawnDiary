-- D1 migration 0002: record where the mod is installed from ("workshop" | "local" | "unknown").
-- Added after the endpoint first shipped, so this column is missing on databases created by migration
-- 0001 (or by the old schema.sql). The Worker's INSERT lists install_source, so without this column
-- every store() throws and is silently swallowed (the client still gets 204) — i.e. reports stop
-- persisting. Applying this migration fixes that.
--
-- NOTE: SQLite has no "ADD COLUMN IF NOT EXISTS". If you already added this column by hand, this
-- migration will error with "duplicate column name"; mark it applied instead of re-running it.
ALTER TABLE error_groups ADD COLUMN install_source TEXT;
