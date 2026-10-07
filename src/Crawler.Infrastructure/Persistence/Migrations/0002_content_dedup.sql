-- Content-based de-duplication: different URLs serving identical HTML (/ vs /index.html,
-- tracking parameters, trailing-slash variants) are recorded once; later aliases become
-- 'Duplicate' pages that point at the original and are not expanded.

ALTER TABLE pages
    ADD COLUMN content_hash         text NULL,  -- SHA-256 (hex) of the HTML body, Completed pages only
    ADD COLUMN duplicate_of_page_id uuid NULL REFERENCES pages (id) ON DELETE SET NULL;

ALTER TABLE pages DROP CONSTRAINT pages_status_check;
ALTER TABLE pages ADD CONSTRAINT pages_status_check
    CHECK (status IN ('Pending', 'Processing', 'Completed', 'Skipped', 'Failed', 'Duplicate'));

-- One original per content per job. Claims are already serialized by the job row lock;
-- this index is the database-level guarantee behind it.
CREATE UNIQUE INDEX ux_pages_job_content_hash ON pages (job_id, content_hash) WHERE status = 'Completed';

ALTER TABLE crawl_jobs ADD COLUMN pages_duplicate int NOT NULL DEFAULT 0;
