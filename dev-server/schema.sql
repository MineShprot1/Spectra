CREATE TABLE IF NOT EXISTS testing_keys (
 key_hash TEXT PRIMARY KEY,
 label TEXT NOT NULL,
 expires INTEGER NOT NULL,
 created INTEGER NOT NULL,
 installation_id TEXT,
 session_hash TEXT UNIQUE,
 redeemed INTEGER,
 revoked INTEGER
);
CREATE INDEX IF NOT EXISTS testing_sessions ON testing_keys(session_hash) WHERE redeemed IS NOT NULL;
