// Mirrors the Crawl API responses (see README → API). Enums are serialized as strings.

export type JobStatus = "Pending" | "Running" | "Completed" | "Failed" | "Canceled";

export type PageStatus = "Pending" | "Processing" | "Completed" | "Skipped" | "Failed" | "Duplicate";

export interface JobProgress {
  discovered: number;
  completed: number;
  failed: number;
  duplicates: number;
  pending: number;
  percent: number;
}

export interface JobDetails {
  jobId: string;
  url: string;
  /** Host whose links count as internal: the URL's host, or where the start page redirected to. */
  startingDomain: string;
  status: JobStatus;
  maxDepth: number;
  maxPages: number;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
  progress: JobProgress;
}

export interface JobListItem {
  jobId: string;
  url: string;
  status: JobStatus;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  pagesDiscovered: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PageTreeNode {
  pageId: string;
  url: string;
  depth: number;
  status: PageStatus;
  httpStatus: number | null;
  error: string | null;
  domainLinkRatio: number | null;
  outgoingLinkCount: number | null;
  duplicateOfPageId: string | null;
  duplicateOfUrl: string | null;
  /** When the page reached its final status (null while pending/processing). */
  finishedAt: string | null;
  children: PageTreeNode[];
}

export interface JobTree {
  jobId: string;
  status: JobStatus;
  root: PageTreeNode | null;
}

export interface CreateJobRequest {
  url: string;
  maxDepth?: number;
}

/** RFC 7807 problem details as returned by the API. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
  correlationId?: string;
}

export const isActive = (status: JobStatus | undefined): boolean =>
  status === "Pending" || status === "Running";
