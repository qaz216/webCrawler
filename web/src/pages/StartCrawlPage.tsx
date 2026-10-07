import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { ApiError } from "../api/client";
import { useCreateJob } from "../api/hooks";
import { ErrorPanel } from "../components/Feedback";

const DEFAULT_MAX_DEPTH = 2;
const MAX_ALLOWED_DEPTH = 5;

export function StartCrawlPage() {
  const navigate = useNavigate();
  const createJob = useCreateJob();
  const [url, setUrl] = useState("");
  const [maxDepth, setMaxDepth] = useState(String(DEFAULT_MAX_DEPTH));

  const fieldErrors = createJob.error instanceof ApiError ? createJob.error.fieldErrors : {};
  const hasFieldErrors = Object.keys(fieldErrors).length > 0;

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createJob.mutate(
      { url: url.trim(), maxDepth: maxDepth === "" ? undefined : Number(maxDepth) },
      { onSuccess: ({ jobId }) => navigate(`/jobs/${jobId}`) },
    );
  }

  return (
    <section className="card narrow">
      <h1>Start a crawl</h1>
      <p className="muted">
        Crawls HTML pages on the same domain, up to the given depth, and reports each page's{" "}
        <em>Domain Link Ratio</em>: the share of its links that stay on the starting domain.
      </p>

      <form onSubmit={handleSubmit} noValidate className="form">
        <div className="field">
          <label htmlFor="url">Website URL</label>
          <input id="url" name="url" type="text" inputMode="url" autoComplete="url" autoFocus required
            placeholder="https://example.com/" value={url} onChange={(e) => setUrl(e.target.value)}
            aria-invalid={fieldErrors.url ? true : undefined}
            aria-describedby={fieldErrors.url ? "url-error" : undefined} />
          {fieldErrors.url && <p id="url-error" className="field-error">{fieldErrors.url.join(" ")}</p>}
        </div>

        <div className="field field-short">
          <label htmlFor="maxDepth">Max depth <span className="muted">(optional)</span></label>
          <input id="maxDepth" name="maxDepth" type="number" min={0} max={MAX_ALLOWED_DEPTH} step={1}
            value={maxDepth} onChange={(e) => setMaxDepth(e.target.value)}
            aria-invalid={fieldErrors.maxDepth ? true : undefined}
            aria-describedby={fieldErrors.maxDepth ? "depth-error" : "depth-hint"} />
          {fieldErrors.maxDepth ? (
            <p id="depth-error" className="field-error">{fieldErrors.maxDepth.join(" ")}</p>
          ) : (
            <p id="depth-hint" className="hint">0 = only the start page. Default {DEFAULT_MAX_DEPTH}, maximum {MAX_ALLOWED_DEPTH}.</p>
          )}
        </div>

        {createJob.error && !hasFieldErrors && (
          <ErrorPanel error={createJob.error} title="Could not start the crawl" />
        )}

        <div className="form-actions">
          <button type="submit" className="button" disabled={createJob.isPending || url.trim() === ""}>
            {createJob.isPending ? "Starting…" : "Start crawl"}
          </button>
          <Link to="/history" className="muted">View previous crawls</Link>
        </div>
      </form>
    </section>
  );
}
