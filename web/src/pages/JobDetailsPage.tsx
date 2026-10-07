import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { ApiError } from "../api/client";
import { useCancelJob, useJob, useJobTree } from "../api/hooks";
import { isActive, type JobDetails } from "../api/types";
import { EmptyState, ErrorPanel, Loading } from "../components/Feedback";
import { JobProgressBar } from "../components/JobProgressBar";
import { PageTree } from "../components/PageTree";
import { StatusBadge } from "../components/StatusBadge";
import { formatDateTime, formatDuration } from "../lib/format";

export function JobDetailsPage() {
  const { jobId = "" } = useParams();
  const job = useJob(jobId);

  if (job.isPending) return <Loading label="Loading job…" />;

  if (job.isError) {
    if (job.error instanceof ApiError && job.error.isNotFound) {
      return (
        <EmptyState title="Job not found">
          There is no crawl job with id <code>{jobId}</code>. <Link to="/history">Back to history</Link>
        </EmptyState>
      );
    }
    return <ErrorPanel error={job.error} title="Could not load the job" onRetry={() => void job.refetch()} />;
  }

  return (
    <div className="stack">
      <JobSummary job={job.data} stale={job.isRefetchError} />
      <JobPages job={job.data} />
    </div>
  );
}

function JobSummary({ job, stale }: { job: JobDetails; stale: boolean }) {
  const cancel = useCancelJob(job.jobId);
  const now = useNow(isActive(job.status));

  return (
    <section className="card">
      <div className="job-header">
        <div className="job-title">
          <p className="eyebrow">Crawl job</p>
          <h1>
            <a href={job.url} target="_blank" rel="noopener noreferrer">{job.url}</a>
          </h1>
          <p className="muted small mono">{job.jobId}</p>
        </div>
        <div className="job-header-actions">
          <StatusBadge status={job.status} />
          {isActive(job.status) && (
            <button type="button" className="button button-danger button-small" disabled={cancel.isPending}
              onClick={() => {
                if (window.confirm("Cancel this crawl? Pages already crawled are kept.")) cancel.mutate();
              }}>
              {cancel.isPending ? "Canceling…" : "Cancel crawl"}
            </button>
          )}
        </div>
      </div>

      {stale && (
        <p className="alert alert-warning small" role="status">
          Lost contact with the API; showing the last known status. Retrying…
        </p>
      )}
      {cancel.error && <ErrorPanel error={cancel.error} title="Could not cancel the crawl" />}
      {job.status === "Failed" && job.failureReason && (
        <div className="alert alert-error" role="alert">
          <strong>Crawl failed</strong>
          <p>{job.failureReason}</p>
        </div>
      )}

      <JobProgressBar job={job} />

      <dl className="meta">
        <div><dt>Created</dt><dd>{formatDateTime(job.createdAt)}</dd></div>
        <div><dt>Started</dt><dd>{formatDateTime(job.startedAt)}</dd></div>
        <div><dt>Completed</dt><dd>{formatDateTime(job.completedAt)}</dd></div>
        <div><dt>Duration</dt><dd>{formatDuration(job.startedAt, job.completedAt, now)}</dd></div>
        <div><dt>Max depth</dt><dd>{job.maxDepth}</dd></div>
        <div><dt>Max pages</dt><dd>{job.maxPages}</dd></div>
      </dl>
    </section>
  );
}

function JobPages({ job }: { job: JobDetails }) {
  const tree = useJobTree(job.jobId, job.status);
  const running = isActive(job.status);

  return (
    <section className="card">
      <div className="section-header">
        <h2>Pages</h2>
        {running && <span className="muted small">Updating live as pages are crawled</span>}
      </div>
      <p className="muted small">
        Each page appears under the page that first linked to it. <strong>Ratio</strong> is the Domain Link
        Ratio: the share of a page's distinct outgoing links that stay on the starting domain.
      </p>

      {tree.isPending ? (
        <Loading label="Loading pages…" />
      ) : tree.isError && !tree.data ? (
        <ErrorPanel error={tree.error} title="Could not load the pages" onRetry={() => void tree.refetch()} />
      ) : !tree.data?.root ? (
        <EmptyState title="No pages yet">The crawl hasn't started yet.</EmptyState>
      ) : (
        <PageTree root={tree.data.root} />
      )}
    </section>
  );
}

/** Ticks every second while `active`, so a running job's duration counts up. */
function useNow(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active]);
  return now;
}
