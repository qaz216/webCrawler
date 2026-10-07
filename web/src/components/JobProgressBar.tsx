import type { JobDetails } from "../api/types";
import { isActive } from "../api/types";

/**
 * Finished ÷ discovered. The total is unknown up front and grows as links are found,
 * so a running job can move backwards; the counts underneath explain why.
 */
export function JobProgressBar({ job }: { job: JobDetails }) {
  const { progress } = job;
  const indeterminate = job.status === "Pending";
  const finished = progress.discovered - progress.pending;

  return (
    <div className="progress-block">
      <div
        className={`progress ${indeterminate ? "progress-indeterminate" : ""} ${isActive(job.status) ? "progress-active" : ""}`}
        role="progressbar"
        aria-label="Crawl progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={indeterminate ? undefined : progress.percent}
      >
        <div className="progress-fill" style={{ width: indeterminate ? undefined : `${progress.percent}%` }} />
      </div>

      <dl className="counts">
        <Count label="Finished" value={`${finished} / ${progress.discovered}`} />
        <Count label="Crawled" value={progress.completed} />
        <Count label="Failed" value={progress.failed} tone={progress.failed > 0 ? "error" : undefined} />
        <Count label="Duplicates" value={progress.duplicates} />
        <Count label="Pending" value={progress.pending} />
      </dl>
      {isActive(job.status) && (
        <p className="muted small">The total grows as new links are discovered.</p>
      )}
    </div>
  );
}

function Count({ label, value, tone }: { label: string; value: number | string; tone?: "error" }) {
  return (
    <div className={`count ${tone ? `count-${tone}` : ""}`}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
