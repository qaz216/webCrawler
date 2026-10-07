import type {
  CreateJobRequest,
  JobDetails,
  JobListItem,
  JobTree,
  PagedResult,
  ProblemDetails,
} from "./types";

// Same origin by default: Vite (dev) and nginx (Docker) proxy /api to the Crawl API.
const BASE_URL = import.meta.env.VITE_API_URL ?? "";

/** A failed API call, carrying the server's problem details when there are any. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails, message?: string) {
    super(message ?? problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  /** Per-field validation messages, keyed by field name in lower camel case. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${BASE_URL}${path}`, {
      ...init,
      headers: {
        Accept: "application/json",
        ...(init?.body ? { "Content-Type": "application/json" } : {}),
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError(0, {}, "Could not reach the Crawl API. Is it running?");
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response));
  }

  return (await response.json()) as T;
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return { status: response.status, title: response.statusText };
  }
}

export const api = {
  createJob: (body: CreateJobRequest) =>
    request<{ jobId: string }>("/api/jobs", { method: "POST", body: JSON.stringify(body) }),

  getJob: (jobId: string) => request<JobDetails>(`/api/jobs/${encodeURIComponent(jobId)}`),

  getTree: (jobId: string) => request<JobTree>(`/api/jobs/${encodeURIComponent(jobId)}/tree`),

  listJobs: (page: number, pageSize: number) =>
    request<PagedResult<JobListItem>>(`/api/jobs?page=${page}&pageSize=${pageSize}`),

  cancelJob: (jobId: string) =>
    request<JobDetails>(`/api/jobs/${encodeURIComponent(jobId)}/cancel`, { method: "POST" }),
};
