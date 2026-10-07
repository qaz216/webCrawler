import { Ban, CircleCheck, CircleSlash, Clock, Copy, LoaderCircle, TriangleAlert, type LucideIcon } from "lucide-react";
import type { JobStatus, PageStatus } from "../api/types";

const LABELS: Partial<Record<JobStatus | PageStatus, string>> = {
  Processing: "Crawling",
};

const ICONS: Record<JobStatus | PageStatus, LucideIcon> = {
  Pending: Clock,
  Running: LoaderCircle,
  Processing: LoaderCircle,
  Completed: CircleCheck,
  Failed: TriangleAlert,
  Canceled: Ban,
  Skipped: CircleSlash,
  Duplicate: Copy,
};

/** Colour-coded status pill with an icon. Colour is never the only signal: the text is always shown. */
export function StatusBadge({ status }: { status: JobStatus | PageStatus }) {
  const Icon = ICONS[status];
  const spinning = status === "Running" || status === "Processing";

  return (
    <span className={`badge badge-${status.toLowerCase()}`} data-status={status}>
      <Icon size={12} strokeWidth={2.5} className={spinning ? "spin" : undefined} aria-hidden="true" />
      {LABELS[status] ?? status}
    </span>
  );
}
