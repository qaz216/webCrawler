import { useMemo, useState } from "react";
import { Link, useParams } from "react-router";
import { ArrowLeft, ChevronRight, CornerDownRight, ExternalLink as ExternalIcon, Globe, House, Search } from "lucide-react";
import { ApiError } from "../api/client";
import { usePageLinks } from "../api/hooks";
import type { ExternalLink, InternalLink, PageLinks } from "../api/types";
import { EmptyState, ErrorPanel, Loading } from "../components/Feedback";
import { RatioMeter } from "../components/RatioMeter";
import { StatusBadge } from "../components/StatusBadge";
import { displayUrl, formatRatio, shortUrl } from "../lib/format";
import { ratioColor } from "../lib/insights";

/** One crawled page: its Domain Link Ratio, then its in-domain links, then its outbound links. */
export function PageLinksPage() {
  const { jobId = "", pageId = "" } = useParams();
  const page = usePageLinks(jobId, pageId);
  const [filter, setFilter] = useState("");

  if (page.isPending) return <Loading label="Loading links…" />;

  if (page.isError) {
    if (page.error instanceof ApiError && page.error.isNotFound) {
      return (
        <EmptyState title="Page not found">
          This page isn't part of the crawl. <Link to={`/jobs/${jobId}`}>Back to the crawl</Link>
        </EmptyState>
      );
    }
    return <ErrorPanel error={page.error} title="Could not load the links" onRetry={() => void page.refetch()} />;
  }

  const data = page.data;
  const query = filter.trim().toLowerCase();
  const matches = (url: string) => query === "" || url.toLowerCase().includes(query);
  const internal = data.internalLinks.filter((l) => matches(l.url));
  const external = data.externalLinks.filter((l) => matches(l.url));

  return (
    <div className="stack">
      <Link to={`/jobs/${jobId}`} className="back-link"><ArrowLeft size={16} aria-hidden="true" /> Back to the crawl</Link>

      <PageHeader page={data} />

      <div className="links-filter">
        <Search size={17} className="input-icon" aria-hidden="true" />
        <input type="search" placeholder="Filter links…" value={filter} onChange={(e) => setFilter(e.target.value)}
          aria-label="Filter links by URL" />
      </div>

      <InternalLinksSection page={data} links={internal} filtered={query !== ""} />
      <ExternalLinksSection links={external} filtered={query !== ""} domain={data.startingDomain} />
    </div>
  );
}

function PageHeader({ page }: { page: PageLinks }) {
  const total = page.internalLinks.length + page.externalLinks.length;
  const internalShare = total === 0 ? 0 : (page.internalLinks.length / total) * 100;

  return (
    <section className="card page-hero">
      <p className="eyebrow">Page · depth {page.depth}</p>
      <h1 className="page-hero-url">
        <a href={page.url} target="_blank" rel="noopener noreferrer">{page.url} <ExternalIcon size={16} aria-hidden="true" /></a>
      </h1>
      <div className="page-hero-meta">
        <StatusBadge status={page.status} />
        {page.httpStatus !== null && <span className="mono muted">HTTP {page.httpStatus}</span>}
        {page.parentUrl ? (
          <span className="muted small"><CornerDownRight size={14} aria-hidden="true" /> first linked from {displayUrl(page.parentUrl, page.url)}</span>
        ) : (
          <span className="muted small"><House size={14} aria-hidden="true" /> start page</span>
        )}
      </div>
      {page.error && <p className="muted small">{page.error}</p>}

      <div className="split">
        <div className="split-ratio">
          <span className="split-value" style={{ color: page.domainLinkRatio === null ? undefined : ratioColor(page.domainLinkRatio) }}>
            {formatRatio(page.domainLinkRatio)}
          </span>
          <span className="muted small">Domain Link Ratio<br />links staying on {page.startingDomain}</span>
        </div>
        <div className="split-bar-wrap">
          <div className="split-bar" role="img"
            aria-label={`${page.internalLinks.length} in-domain and ${page.externalLinks.length} outbound links`}>
            <span className="split-in" style={{ width: `${internalShare}%` }} />
            <span className="split-out" style={{ width: `${100 - internalShare}%` }} />
          </div>
          <div className="split-legend">
            <a href="#in-domain"><span className="dot dot-in" aria-hidden="true" /> <strong>{page.internalLinks.length}</strong> in-domain</a>
            <a href="#outbound"><span className="dot dot-out" aria-hidden="true" /> <strong>{page.externalLinks.length}</strong> outbound</a>
          </div>
        </div>
      </div>
    </section>
  );
}

function InternalLinksSection({ page, links, filtered }: { page: PageLinks; links: InternalLink[]; filtered: boolean }) {
  return (
    <section className="card" id="in-domain" aria-labelledby="in-domain-title">
      <div className="links-header">
        <h2 id="in-domain-title"><span className="dot dot-in" aria-hidden="true" /> In-domain links</h2>
        <span className="count-pill">{links.length}</span>
      </div>
      <p className="muted small">Links to {page.startingDomain} and its subdomains.</p>

      {links.length === 0 ? (
        <EmptyState title={filtered ? "No matching in-domain links" : "No in-domain links"} />
      ) : (
        <ul className="link-list">
          {links.map((link) => <InternalRow key={link.url} link={link} page={page} />)}
        </ul>
      )}
    </section>
  );
}

function InternalRow({ link, page }: { link: InternalLink; page: PageLinks }) {
  const label = shortUrl(link.url, page.url);
  const canOpen = link.pageId !== null && (link.outgoingLinkCount ?? 0) > 0 && link.pageId !== page.pageId;

  return (
    <li className="link-row">
      <div className="link-main">
        {canOpen ? (
          <Link to={`/jobs/${page.jobId}/pages/${link.pageId}`} className="link-url" title={`Open ${link.url}`}>{label}</Link>
        ) : (
          <a href={link.url} target="_blank" rel="noopener noreferrer" className="link-url" title={link.url}>{label}</a>
        )}
        {link.pageId === page.pageId && <span className="muted small">this page</span>}
      </div>
      <div className="link-meta">
        {link.status ? <StatusBadge status={link.status} /> : (
          <span className="badge badge-muted" title="Beyond the max depth or page limit">Not crawled</span>
        )}
        <RatioMeter ratio={link.domainLinkRatio} />
        <span className="link-open" aria-hidden="true">{canOpen && <ChevronRight size={16} />}</span>
      </div>
    </li>
  );
}

function ExternalLinksSection({ links, filtered, domain }: { links: ExternalLink[]; filtered: boolean; domain: string }) {
  // Group by site, busiest first, so repeated destinations (social, app stores) read at a glance.
  const groups = useMemo(() => {
    const byHost = new Map<string, ExternalLink[]>();
    for (const link of links) byHost.set(link.host, [...(byHost.get(link.host) ?? []), link]);
    return [...byHost.entries()].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]));
  }, [links]);

  return (
    <section className="card" id="outbound" aria-labelledby="outbound-title">
      <div className="links-header">
        <h2 id="outbound-title"><span className="dot dot-out" aria-hidden="true" /> Outbound links</h2>
        <span className="count-pill count-pill-out">{links.length}</span>
      </div>
      <p className="muted small">Links leaving {domain}, grouped by site{groups.length > 0 && ` (${groups.length} ${groups.length === 1 ? "site" : "sites"})`}.</p>

      {links.length === 0 ? (
        <EmptyState title={filtered ? "No matching outbound links" : "No outbound links"}>
          {!filtered && "Every link on this page stays on the site."}
        </EmptyState>
      ) : (
        <div className="site-groups">
          {groups.map(([host, hostLinks]) => (
            <div key={host} className="site-group">
              <div className="site-group-header">
                <Globe size={15} aria-hidden="true" />
                <span className="site-host">{host}</span>
                <span className="count-pill count-pill-out">{hostLinks.length}</span>
              </div>
              <ul className="link-list">
                {hostLinks.map((link) => (
                  <li key={link.url} className="link-row">
                    <a href={link.url} target="_blank" rel="noopener noreferrer" className="link-url" title={link.url}>
                      {shortUrl(link.url, "about:blank")}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
