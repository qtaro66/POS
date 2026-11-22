CREATE TABLE public.closing_report_contexts (
    context_id text PRIMARY KEY CHECK (btrim(context_id) <> ''),
    report_id text NOT NULL,
    branch_id text NOT NULL CHECK (btrim(branch_id) <> ''),
    request_id text NOT NULL CHECK (btrim(request_id) <> ''),
    payload_json text NOT NULL CHECK (json_typeof(payload_json::json) = 'object'),
    received_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_closing_report_contexts_report UNIQUE (report_id),
    CONSTRAINT fk_closing_report_contexts_report FOREIGN KEY (report_id)
        REFERENCES public.closing_reports(report_id)
);

CREATE INDEX ix_closing_report_contexts_branch ON public.closing_report_contexts(branch_id,received_at);
INSERT INTO public.central_schema_migrations (version,name) VALUES (3,'0003_closing_report_contexts');
