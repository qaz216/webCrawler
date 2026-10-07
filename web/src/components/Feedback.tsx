import type { ReactNode } from "react";
import { ApiError } from "../api/client";

export function Loading({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="loading" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      {label}
    </div>
  );
}

/** Error box with the server's message, its correlation id (for log lookup) and an optional retry. */
export function ErrorPanel({
  error,
  title = "Something went wrong",
  onRetry,
  children,
}: {
  error: unknown;
  title?: string;
  onRetry?: () => void;
  children?: ReactNode;
}) {
  const message = error instanceof Error ? error.message : String(error);
  const correlationId = error instanceof ApiError ? error.problem.correlationId : undefined;

  return (
    <div className="alert alert-error" role="alert">
      <strong>{title}</strong>
      <p>{message}</p>
      {correlationId && <p className="muted small">Reference: {correlationId}</p>}
      {(onRetry || children) && (
        <div className="alert-actions">
          {onRetry && (
            <button type="button" className="button button-secondary" onClick={onRetry}>
              Try again
            </button>
          )}
          {children}
        </div>
      )}
    </div>
  );
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <strong>{title}</strong>
      {children && <div>{children}</div>}
    </div>
  );
}
