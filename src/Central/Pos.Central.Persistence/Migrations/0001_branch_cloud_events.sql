CREATE TABLE IF NOT EXISTS central_schema_migrations (
    version integer PRIMARY KEY CHECK (version >= 1),
    name text NOT NULL UNIQUE CHECK (btrim(name) <> ''),
    applied_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE branch_cloud_events (
    event_id text PRIMARY KEY CHECK (btrim(event_id) <> ''),
    branch_id text NOT NULL CHECK (btrim(branch_id) <> ''),
    branch_sequence bigint NOT NULL CHECK (branch_sequence >= 1),
    request_id text NOT NULL CHECK (btrim(request_id) <> ''),
    table_session_id text NOT NULL CHECK (btrim(table_session_id) <> ''),
    action text NOT NULL CHECK (btrim(action) <> ''),
    table_version bigint NOT NULL CHECK (table_version >= 1),
    occurred_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_branch_cloud_events_branch_sequence
        UNIQUE (branch_id, branch_sequence)
);

INSERT INTO central_schema_migrations (version, name)
VALUES (1, '0001_branch_cloud_events');
