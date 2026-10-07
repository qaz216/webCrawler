import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, api } from "./client";
import { isActive, type JobStatus } from "./types";

/** Polling intervals: fast enough to feel live, slow enough to be cheap (status is a PK lookup). */
export const JOB_POLL_MS = 1500;
export const TREE_POLL_MS = 2000;
export const HISTORY_POLL_MS = 5000;

const keys = {
  job: (jobId: string) => ["job", jobId] as const,
  tree: (jobId: string, status: JobStatus | undefined) => ["job", jobId, "tree", status] as const,
  jobs: (page: number, pageSize: number) => ["jobs", page, pageSize] as const,
};

// Don't retry what can't succeed (404, validation); do retry network blips and 5xx.
const retryUnlessClientError = (failureCount: number, error: Error) =>
  !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 3;

export function useJob(jobId: string) {
  return useQuery({
    queryKey: keys.job(jobId),
    queryFn: () => api.getJob(jobId),
    refetchInterval: (query) => (isActive(query.state.data?.status) ? JOB_POLL_MS : false),
    retry: retryUnlessClientError,
  });
}

/**
 * The tree is polled while the job runs (partial tree), and fetched once more when the status
 * changes: the status is part of the key, so the final tree loads as soon as the job completes.
 */
export function useJobTree(jobId: string, status: JobStatus | undefined) {
  return useQuery({
    queryKey: keys.tree(jobId, status),
    queryFn: () => api.getTree(jobId),
    enabled: status !== undefined,
    placeholderData: keepPreviousData,
    refetchInterval: isActive(status) ? TREE_POLL_MS : false,
    retry: retryUnlessClientError,
  });
}

export function useJobs(page: number, pageSize: number) {
  return useQuery({
    queryKey: keys.jobs(page, pageSize),
    queryFn: () => api.listJobs(page, pageSize),
    placeholderData: keepPreviousData,
    refetchInterval: (query) =>
      query.state.data?.items.some((job) => isActive(job.status)) ? HISTORY_POLL_MS : false,
    retry: retryUnlessClientError,
  });
}

export function useCreateJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.createJob,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["jobs"] }),
  });
}

/** Deletes all jobs; drops every cached job, tree and history page so nothing stale is shown. */
export function useClearJobs() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.clearJobs,
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: ["job"] });
      return queryClient.invalidateQueries({ queryKey: ["jobs"] });
    },
  });
}

export function useCancelJob(jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.cancelJob(jobId),
    onSuccess: (job) => {
      queryClient.setQueryData(keys.job(jobId), job);
      void queryClient.invalidateQueries({ queryKey: ["jobs"] });
    },
  });
}
