import { Activity, CircleCheck, CircleSlash, Copy, TriangleAlert } from "lucide-react";
import type { PageStatus, PageTreeNode } from "../api/types";
import { displayUrl, formatRatio } from "../lib/format";
import { recentlyFinished } from "../lib/insights";

const ICONS: Partial<Record<PageStatus, typeof CircleCheck>> = {
  Completed: CircleCheck,
  Failed: TriangleAlert,
  Skipped: CircleSlash,
  Duplicate: Copy,
};

/**
 * The most recently finished pages, newest first. While a crawl runs, new entries slide in as the
 * workers report them, which makes the asynchronous pipeline visible.
 */
export function ActivityFeed({ root, live }: { root: PageTreeNode | null; live: boolean }) {
  const recent = recentlyFinished(root, 8);

  return (
    <section className="card side-card" aria-labelledby="activity-title">
      <div className="side-card-header">
        <h2 id="activity-title"><Activity size={16} aria-hidden="true" /> {live ? "Live activity" : "Last pages crawled"}</h2>
        {live && <span className="live-dot" title="Updating live">Live</span>}
      </div>

      {recent.length === 0 ? (
        <p className="muted small">{live ? "Waiting for the first page…" : "No pages were crawled."}</p>
      ) : (
        <ol className="feed" aria-live={live ? "polite" : undefined}>
          {recent.map((page) => {
            const Icon = ICONS[page.status] ?? CircleCheck;
            return (
              <li key={page.pageId} className={`feed-item feed-${page.status.toLowerCase()}`}>
                <Icon size={15} className="feed-icon" aria-hidden="true" />
                <span className="feed-url" title={page.url}>{root ? displayUrl(page.url, root.url) : page.url}</span>
                <span className="feed-meta">{describe(page)}</span>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

function describe(page: PageTreeNode): string {
  switch (page.status) {
    case "Completed":
      return `${page.outgoingLinkCount ?? 0} links · ${formatRatio(page.domainLinkRatio)}`;
    case "Duplicate":
      return "duplicate";
    case "Failed":
      return page.httpStatus ? `HTTP ${page.httpStatus}` : "failed";
    default:
      return page.status.toLowerCase();
  }
}
