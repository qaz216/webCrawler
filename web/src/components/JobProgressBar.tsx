import type { JobDetails } from "../api/types";
import { isActive } from "../api/types";
import { useAnimatedNumber } from "../lib/useAnimatedNumber";

/**
 * Finished ÷ discovered. The total is unknown up front and grows as links are found,
 * so a running job can move backwards; the counts underneath explain why.
 */
export function JobProgressBar({ job }: { job: JobDetails }) {
  const { progress } = job;
  const indeterminate = job.status === "Pending";
  const percent = useAnimatedNumber(progress.percent);

  return (
    <div className="progress-block">
      <div className="progress-label">
        <span className="progress-percent">{indeterminate ? "Queued" : `${Math.round(percent)}%`}</span>
        {isActive(job.status) && <span className="muted small">The total grows as new links are discovered</span>}
      </div>
      <div
        className={`progress ${indeterminate ? "progress-indeterminate" : ""} ${isActive(job.status) ? "progress-active" : ""}`}
        role="progressbar"
        aria-label="Crawl progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={indeterminate ? undefined : progress.percent}
      >
        <div className="progress-fill" style={{ width: indeterminate ? undefined : `${percent}%` }} />
      </div>

      <dl className="counts">
        <Count label="Discovered" value={progress.discovered} />
        <Count label="Crawled" value={progress.completed} tone="good" />
        <Count label="Failed" value={progress.failed} tone={progress.failed > 0 ? "error" : undefined} />
        <Count label="Duplicates" value={progress.duplicates} tone="duplicate" />
        <Count label="Pending" value={progress.pending} />
      </dl>
    </div>
  );
}

function Count({ label, value, tone }: { label: string; value: number; tone?: "error" | "good" | "duplicate" }) {
  const animated = useAnimatedNumber(value, 500);
  return (
    <div className={`count ${tone ? `count-${tone}` : ""}`}>
      <dt>{label}</dt>
      <dd>{Math.round(animated)}</dd>
    </div>
  );
}
