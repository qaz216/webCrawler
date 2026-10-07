import type { JobStatus, PageStatus } from "../api/types";

const LABELS: Partial<Record<JobStatus | PageStatus, string>> = {
  Processing: "Crawling",
};

/** Colour-coded status pill. Colour is never the only signal: the text is always shown. */
export function StatusBadge({ status }: { status: JobStatus | PageStatus }) {
  return (
    <span className={`badge badge-${status.toLowerCase()}`} data-status={status}>
      {(status === "Running" || status === "Processing") && <span className="pulse" aria-hidden="true" />}
      {LABELS[status] ?? status}
    </span>
  );
}
