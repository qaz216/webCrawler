import { lazy, Suspense, useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { CircleStop, ExternalLink, ListTree, Network, X } from "lucide-react";
import { ApiError } from "../api/client";
import { useCancelJob, useJob, useJobTree } from "../api/hooks";
import { isActive, type JobDetails, type JobTree } from "../api/types";
import { ActivityFeed } from "../components/ActivityFeed";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { EmptyState, ErrorPanel, Loading } from "../components/Feedback";
import { JobProgressBar } from "../components/JobProgressBar";
import { PageTree } from "../components/PageTree";
import { RatioInsights } from "../components/RatioInsights";
import { StatusBadge } from "../components/StatusBadge";
import { formatDateTime, formatDuration } from "../lib/format";

// The graph library is only downloaded when someone opens the Graph tab.
const SiteGraph = lazy(() => import("../components/SiteGraph"));

type View = "tree" | "graph";

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

  return <JobView job={job.data} stale={job.isRefetchError} />;
}

function JobView({ job, stale }: { job: JobDetails; stale: boolean }) {
  const tree = useJobTree(job.jobId, job.status);
  const [view, setView] = useState<View>("tree");
  const [focus, setFocus] = useState<{ pageId: string; nonce: number } | null>(null);

  // From the graph: show that page in the tree.
  const selectPage = useCallback((pageId: string) => {
    setView("tree");
    setFocus({ pageId, nonce: Date.now() });
  }, []);

  return (
    <div className="stack">
      <JobSummary job={job} stale={stale} />

      <div className="job-layout">
        <PagesPanel job={job} tree={tree.data} isPending={tree.isPending} error={tree.isError && !tree.data ? tree.error : null}
          onRetry={() => void tree.refetch()} view={view} onViewChange={setView} focus={focus} onSelectPage={selectPage} />

        <aside className="job-sidebar">
          <ActivityFeed root={tree.data?.root ?? null} live={isActive(job.status)} />
          <RatioInsights root={tree.data?.root ?? null} />
        </aside>
      </div>
    </div>
  );
}

function JobSummary({ job, stale }: { job: JobDetails; stale: boolean }) {
  const cancel = useCancelJob(job.jobId);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const now = useNow(isActive(job.status));
  const from = job.redirectedFrom;

  return (
    <section className={`card job-card job-${job.status.toLowerCase()}`}>
      <div className="job-header">
        <div className="job-title">
          <p className="eyebrow">Crawl job · <span className="mono">{job.jobId}</span></p>
          <h1>
            <a href={job.url} target="_blank" rel="noopener noreferrer">
              {job.url} <ExternalLink size={16} aria-hidden="true" />
            </a>
          </h1>
        </div>
        <div className="job-header-actions">
          <StatusBadge status={job.status} />
          {isActive(job.status) && (
            <button type="button" className="button button-danger button-small"
              onClick={() => { cancel.reset(); setConfirmCancel(true); }}>
              <X size={14} aria-hidden="true" /> Cancel crawl
            </button>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={confirmCancel && isActive(job.status)}
        icon={CircleStop}
        title="Stop this crawl?"
        confirmLabel="Stop crawl"
        busyLabel="Stopping…"
        cancelLabel="Keep crawling"
        busy={cancel.isPending}
        error={cancel.error ? `Could not stop the crawl: ${cancel.error.message}` : null}
        onConfirm={() => cancel.mutate(undefined, { onSuccess: () => setConfirmCancel(false) })}
        onCancel={() => setConfirmCancel(false)}>
        <p>No new pages will be crawled. The <strong>{job.progress.completed} pages</strong> already crawled are kept.</p>
      </ConfirmDialog>

      {stale && (
        <p className="alert alert-warning small" role="status">
          Lost contact with the API; showing the last known status. Retrying…
        </p>
      )}
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
        <div>
          <dt title="Links to this domain or any of its subdomains count as internal">Starting domain</dt>
          <dd>
            {job.startingDomain}
            {from && <span className="muted small"> (redirected from {from})</span>}
          </dd>
        </div>
        <div><dt>Limits</dt><dd>depth {job.maxDepth} · {job.maxPages} pages</dd></div>
      </dl>
    </section>
  );
}

interface PagesPanelProps {
  job: JobDetails;
  tree: JobTree | undefined;
  isPending: boolean;
  error: Error | null;
  onRetry: () => void;
  view: View;
  onViewChange: (view: View) => void;
  focus: { pageId: string; nonce: number } | null;
  onSelectPage: (pageId: string) => void;
}

function PagesPanel({ job, tree, isPending, error, onRetry, view, onViewChange, focus, onSelectPage }: PagesPanelProps) {
  const running = isActive(job.status);
  const root = tree?.root ?? null;

  return (
    <section className="card pages-card">
      <div className="section-header">
        <h2>Pages</h2>
        <div className="segmented" role="tablist" aria-label="Pages view">
          <button type="button" role="tab" aria-selected={view === "tree"} className={view === "tree" ? "active" : ""}
            onClick={() => onViewChange("tree")}>
            <ListTree size={15} aria-hidden="true" /> Tree
          </button>
          <button type="button" role="tab" aria-selected={view === "graph"} className={view === "graph" ? "active" : ""}
            onClick={() => onViewChange("graph")}>
            <Network size={15} aria-hidden="true" /> Graph
          </button>
        </div>
      </div>
      <p className="muted small">
        {view === "tree"
          ? "Each page appears under the page that first linked to it. Ratio = share of a page's distinct outgoing links that stay on the starting domain."
          : "Each dot is a page, coloured by its Domain Link Ratio and sized by its number of links. Click a page to open it in the tree."}
        {running && <> <span className="live-inline">Updating live.</span></>}
      </p>

      {isPending ? (
        <Loading label="Loading pages…" />
      ) : error ? (
        <ErrorPanel error={error} title="Could not load the pages" onRetry={onRetry} />
      ) : !root ? (
        <EmptyState title="No pages yet">The crawl hasn't started yet.</EmptyState>
      ) : view === "tree" ? (
        <PageTree root={root} focus={focus} />
      ) : (
        <Suspense fallback={<Loading label="Loading graph…" />}>
          <SiteGraph root={root} live={running} onSelectPage={onSelectPage} />
        </Suspense>
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
