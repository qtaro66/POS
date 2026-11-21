CREATE TABLE public.closing_reports (
    report_id text PRIMARY KEY CHECK (btrim(report_id) <> ''),
    branch_id text NOT NULL CHECK (btrim(branch_id) <> ''),
    request_id text NOT NULL CHECK (btrim(request_id) <> ''),
    store_session_id text NOT NULL CHECK (btrim(store_session_id) <> ''),
    shift_id text NOT NULL CHECK (btrim(shift_id) <> ''),
    operation text NOT NULL CHECK (operation IN ('CloseShift','CloseStore')),
    business_date date NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    payload_json text NOT NULL CHECK (json_typeof(payload_json::json) = 'object'),
    received_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_closing_reports_branch_request UNIQUE (branch_id, request_id),
    CONSTRAINT uq_closing_reports_branch_shift UNIQUE (branch_id, store_session_id, shift_id)
);

CREATE INDEX ix_closing_reports_branch_business_date
    ON public.closing_reports(branch_id,business_date,received_at);

INSERT INTO public.central_schema_migrations (version, name)
VALUES (2, '0002_closing_reports');
