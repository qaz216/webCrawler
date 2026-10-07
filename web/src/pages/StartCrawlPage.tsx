import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { ArrowRight, Globe, Zap } from "lucide-react";
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
    <div className="hero">
      <div className="hero-glow" aria-hidden="true" />
      <div className="hero-copy">
        <p className="hero-kicker"><Zap size={14} aria-hidden="true" /> Asynchronous, event-driven crawling</p>
        <h1 className="hero-title">Map any website. <span className="gradient-text">See where its links lead.</span></h1>
        <p className="hero-subtitle">
          Enter a URL and watch the crawl unfold live. Every page gets a <strong>Domain Link Ratio</strong>: the
          share of its links that stay on the starting domain.
        </p>
      </div>

      <section className="card hero-card">
        <form onSubmit={handleSubmit} noValidate className="form">
          <div className="field">
            <label htmlFor="url">Website URL</label>
            <div className={`input-with-icon ${fieldErrors.url ? "has-error" : ""}`}>
              <Globe size={18} className="input-icon" aria-hidden="true" />
              <input id="url" name="url" type="text" inputMode="url" autoComplete="url" autoFocus required
                placeholder="example.com or https://example.com/page" value={url} onChange={(e) => setUrl(e.target.value)}
                aria-invalid={fieldErrors.url ? true : undefined}
                aria-describedby={fieldErrors.url ? "url-error" : undefined} />
            </div>
            {fieldErrors.url && <p id="url-error" className="field-error">{fieldErrors.url.join(" ")}</p>}
          </div>

          <div className="form-row">
            <div className="field field-short">
              <label htmlFor="maxDepth">Max depth <span className="muted">(optional)</span></label>
              <input id="maxDepth" name="maxDepth" type="number" min={0} max={MAX_ALLOWED_DEPTH} step={1}
                value={maxDepth} onChange={(e) => setMaxDepth(e.target.value)}
                aria-invalid={fieldErrors.maxDepth ? true : undefined}
                aria-describedby={fieldErrors.maxDepth ? "depth-error" : "depth-hint"} />
            </div>
            <div className="form-row-hint">
              {fieldErrors.maxDepth ? (
                <p id="depth-error" className="field-error">{fieldErrors.maxDepth.join(" ")}</p>
              ) : (
                <p id="depth-hint" className="hint">0 = only the start page. Default {DEFAULT_MAX_DEPTH}, maximum {MAX_ALLOWED_DEPTH}.</p>
              )}
            </div>
          </div>

          {createJob.error && !hasFieldErrors && (
            <ErrorPanel error={createJob.error} title="Could not start the crawl" />
          )}

          <div className="form-actions">
            <button type="submit" className="button button-large" disabled={createJob.isPending || url.trim() === ""}>
              {createJob.isPending ? "Starting…" : "Start crawl"}
              <ArrowRight size={18} aria-hidden="true" />
            </button>
            <Link to="/history" className="muted">View previous crawls</Link>
          </div>
        </form>
      </section>
    </div>
  );
}
