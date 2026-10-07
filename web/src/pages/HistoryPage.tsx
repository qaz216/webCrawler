import { Link, useNavigate, useSearchParams } from "react-router";
import { Trash2 } from "lucide-react";
import { useClearJobs, useJobs } from "../api/hooks";
import { EmptyState, ErrorPanel, Loading } from "../components/Feedback";
import { RatioMeter } from "../components/RatioMeter";
import { StatusBadge } from "../components/StatusBadge";
import { formatDateTime, formatDuration } from "../lib/format";

const PAGE_SIZE = 20;

export function HistoryPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get("page")) || 1);
  const jobs = useJobs(page, PAGE_SIZE);
  const clearJobs = useClearJobs();
  const totalCount = jobs.data?.totalCount ?? 0;

  const goTo = (target: number) => setSearchParams(target === 1 ? {} : { page: String(target) });

  function handleClearAll() {
    const message = `Delete all ${totalCount} crawl ${totalCount === 1 ? "job" : "jobs"} and their results? `
      + "Running crawls are stopped. This can't be undone.";
    if (window.confirm(message)) clearJobs.mutate(undefined, { onSuccess: () => goTo(1) });
  }

  return (
    <section className="card">
      <div className="section-header">
        <h1>Crawl history</h1>
        <div className="header-actions">
          <Link to="/" className="button button-small">New crawl</Link>
          <button type="button" className="button button-small button-danger" onClick={handleClearAll}
            disabled={totalCount === 0 || clearJobs.isPending}>
            <Trash2 size={14} aria-hidden="true" /> {clearJobs.isPending ? "Clearing…" : "Clear all"}
          </button>
        </div>
      </div>

      {clearJobs.error && <ErrorPanel error={clearJobs.error} title="Could not clear the history" />}

      {jobs.isPending ? (
        <Loading label="Loading history…" />
      ) : jobs.isError ? (
        <ErrorPanel error={jobs.error} title="Could not load the history" onRetry={() => void jobs.refetch()} />
      ) : jobs.data.totalCount === 0 ? (
        <EmptyState title="No crawls yet">
          <Link to="/">Start your first crawl</Link>
        </EmptyState>
      ) : (
        <>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Start URL</th>
                  <th scope="col">Status</th>
                  <th scope="col">Created</th>
                  <th scope="col">Duration</th>
                  <th scope="col" className="numeric">Pages</th>
                  <th scope="col" className="ratio-col" title="Average Domain Link Ratio of the crawled pages: the share of their links that stay on the starting domain">
                    Avg link ratio
                  </th>
                </tr>
              </thead>
              <tbody className={jobs.isPlaceholderData ? "is-loading" : undefined}>
                {jobs.data.items.map((job) => (
                  <tr key={job.jobId} className="clickable" onClick={() => navigate(`/jobs/${job.jobId}`)}>
                    <td className="url-cell">
                      <Link to={`/jobs/${job.jobId}`} onClick={(e) => e.stopPropagation()} title={job.url}>
                        {job.url}
                      </Link>
                    </td>
                    <td><StatusBadge status={job.status} /></td>
                    <td>{formatDateTime(job.createdAt)}</td>
                    <td>{job.startedAt ? formatDuration(job.startedAt, job.completedAt) : "—"}</td>
                    <td className="numeric">{job.pagesDiscovered}</td>
                    <td className="ratio-col">
                      <RatioMeter ratio={job.averageDomainLinkRatio} label="Average Domain Link Ratio" />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <nav className="pagination" aria-label="History pages">
            <button type="button" className="button button-secondary button-small"
              disabled={page <= 1} onClick={() => goTo(page - 1)}>
              ← Newer
            </button>
            <span className="muted small">
              Page {page} of {Math.max(1, jobs.data.totalPages)} · {jobs.data.totalCount} jobs
            </span>
            <button type="button" className="button button-secondary button-small"
              disabled={page >= jobs.data.totalPages} onClick={() => goTo(page + 1)}>
              Older →
            </button>
          </nav>
        </>
      )}
    </section>
  );
}
