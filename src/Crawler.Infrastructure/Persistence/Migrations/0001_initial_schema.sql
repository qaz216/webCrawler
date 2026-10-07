-- Crawl jobs. Progress counters are denormalized so status polling is a single PK lookup.
CREATE TABLE crawl_jobs (
    id               uuid        PRIMARY KEY,
    url              text        NOT NULL,
    root_host        text        NOT NULL,
    max_depth        int         NOT NULL CHECK (max_depth >= 0),
    max_pages        int         NOT NULL CHECK (max_pages > 0),
    status           text        NOT NULL CHECK (status IN ('Pending', 'Running', 'Completed', 'Failed', 'Canceled')),
    failure_reason   text        NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    started_at       timestamptz NULL,
    completed_at     timestamptz NULL,
    pages_discovered int         NOT NULL DEFAULT 0,
    pages_completed  int         NOT NULL DEFAULT 0,  -- includes Skipped
    pages_failed     int         NOT NULL DEFAULT 0
);

-- History screen: most recent first; (created_at, id) also supports keyset pagination.
CREATE INDEX ix_crawl_jobs_created_at ON crawl_jobs (created_at DESC, id DESC);

-- One row per distinct normalized URL per job. The unique key is what makes
-- "do not reprocess the same URL" and duplicate deliveries safe.
CREATE TABLE pages (
    id                  uuid         PRIMARY KEY,
    job_id              uuid         NOT NULL REFERENCES crawl_jobs (id) ON DELETE CASCADE,
    url                 text         NOT NULL,
    depth               int          NOT NULL CHECK (depth >= 0),
    parent_page_id      uuid         NULL REFERENCES pages (id) ON DELETE CASCADE,  -- first discoverer: tree shape
    status              text         NOT NULL CHECK (status IN ('Pending', 'Processing', 'Completed', 'Skipped', 'Failed')),
    http_status         int          NULL,
    content_type        text         NULL,
    error               text         NULL,
    domain_link_ratio   numeric(5,4) NULL,
    outgoing_link_count int          NULL,
    attempts            int          NOT NULL DEFAULT 0,
    lease_until         timestamptz  NULL,
    discovered_at       timestamptz  NOT NULL DEFAULT now(),
    finished_at         timestamptz  NULL,
    CONSTRAINT ux_pages_job_url UNIQUE (job_id, url)  -- also serves "all pages of a job" (tree query)
);

-- Every outgoing link of a crawled page, internal and external (parent -> child edges).
CREATE TABLE page_links (
    id           bigint  GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    job_id       uuid    NOT NULL REFERENCES crawl_jobs (id) ON DELETE CASCADE,
    from_page_id uuid    NOT NULL REFERENCES pages (id) ON DELETE CASCADE,
    to_url       text    NOT NULL,
    to_page_id   uuid    NULL REFERENCES pages (id) ON DELETE SET NULL,  -- set when the target is a page of this job
    is_internal  boolean NOT NULL,
    CONSTRAINT ux_page_links_from_to UNIQUE (from_page_id, to_url)
);

CREATE INDEX ix_page_links_job ON page_links (job_id);

-- Transactional outbox: messages are written in the same transaction as the state change
-- that produces them, then published to the broker by a dispatcher.
CREATE TABLE outbox_messages (
    id             uuid        PRIMARY KEY,  -- = message id; for page tasks, the page id
    message_type   text        NOT NULL,
    payload        jsonb       NOT NULL,
    correlation_id uuid        NULL,         -- job id
    created_at     timestamptz NOT NULL DEFAULT now(),
    sent_at        timestamptz NULL
);

CREATE INDEX ix_outbox_messages_unsent ON outbox_messages (created_at) WHERE sent_at IS NULL;
