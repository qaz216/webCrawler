import { ChartColumn } from "lucide-react";
import type { PageTreeNode } from "../api/types";
import { displayUrl, formatRatio } from "../lib/format";
import { ratioColor, ratioStats } from "../lib/insights";
import { useAnimatedNumber } from "../lib/useAnimatedNumber";

/** Job-level view of the Domain Link Ratio: average, distribution, and the most outward-linking pages. */
export function RatioInsights({ root, onSelectPage }: { root: PageTreeNode | null; onSelectPage: (pageId: string) => void }) {
  const stats = ratioStats(root);
  const average = useAnimatedNumber(stats.average ?? 0);
  const maxBand = Math.max(1, ...stats.bands.map((b) => b.count));

  return (
    <section className="card side-card" aria-labelledby="insights-title">
      <div className="side-card-header">
        <h2 id="insights-title"><ChartColumn size={16} aria-hidden="true" /> Domain Link Ratio</h2>
      </div>

      {stats.measured === 0 ? (
        <p className="muted small">Appears once pages have been crawled.</p>
      ) : (
        <>
          <div className="ratio-hero">
            <span className="ratio-hero-value" style={{ color: ratioColor(stats.average ?? 0) }}>
              {formatRatio(average)}
            </span>
            <span className="muted small">average across {stats.measured} crawled {stats.measured === 1 ? "page" : "pages"}<br />share of links staying on the site</span>
          </div>

          <div className="bands" role="list" aria-label="Pages by Domain Link Ratio">
            {stats.bands.map((band) => (
              <div key={band.label} className="band" role="listitem" aria-label={`${band.label}: ${band.count} pages`}>
                <span className="band-label">{band.label}</span>
                <span className="band-track">
                  <span className="band-fill" style={{
                    width: `${(band.count / maxBand) * 100}%`,
                    background: ratioColor((band.min + Math.min(band.max, 1)) / 2),
                  }} />
                </span>
                <span className="band-count">{band.count}</span>
              </div>
            ))}
          </div>

          {stats.mostOutward.length > 0 && root && (
            <div className="outward">
              <p className="eyebrow">Most links leaving the site</p>
              <ul>
                {stats.mostOutward.map((page) => (
                  <li key={page.pageId}>
                    <button type="button" className="link-button" onClick={() => onSelectPage(page.pageId)} title={page.url}>
                      {displayUrl(page.url, root.url)}
                    </button>
                    <span className="muted small">{formatRatio(page.domainLinkRatio)}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}
    </section>
  );
}
