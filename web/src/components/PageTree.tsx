import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";
import { Link } from "react-router";
import type { PageTreeNode } from "../api/types";
import { flatten } from "../lib/insights";
import { displayUrl } from "../lib/format";
import { ancestorIds, countNodes, expandableIds } from "../lib/tree";
import { RatioMeter } from "./RatioMeter";
import { StatusBadge } from "./StatusBadge";

interface PageTreeProps {
  /** The crawl job, for links to each page's detail view. */
  jobId: string;
  root: PageTreeNode;
  /** Levels expanded initially (root = depth 0). */
  initialDepth?: number;
  /** Reveal and highlight this page (e.g. clicked in the graph). The nonce allows re-focusing the same page. */
  focus?: { pageId: string; nonce: number } | null;
}

/**
 * Collapsible tree of crawled pages. Each page shows its status, HTTP code, Domain Link Ratio and
 * link count; duplicates link to their original, expanding the tree to reveal it. Pages that
 * appear while the crawl runs are briefly highlighted.
 */
export function PageTree({ jobId, root, initialDepth = 1, focus }: PageTreeProps) {
  // Expansion = a default by depth (top level open, the rest closed) plus the user's explicit choices.
  // Keeping only the overrides means pages that appear later (while the crawl runs, or when the
  // tree first loads before the start page has children) still get the default.
  const [overrides, setOverrides] = useState<ReadonlyMap<string, boolean>>(() => new Map());
  const [highlighted, setHighlighted] = useState<string | null>(null);
  const total = useMemo(() => countNodes(root), [root]);
  const fresh = useFreshIds(root);

  const isExpanded = useCallback(
    (node: PageTreeNode) => overrides.get(node.pageId) ?? node.depth < initialDepth,
    [overrides, initialDepth],
  );

  const setMany = useCallback((ids: string[], open: boolean) => {
    setOverrides((current) => {
      const next = new Map(current);
      for (const id of ids) next.set(id, open);
      return next;
    });
  }, []);

  const toggle = useCallback(
    (node: PageTreeNode) => setMany([node.pageId], !isExpanded(node)),
    [isExpanded, setMany],
  );

  const reveal = useCallback(
    (pageId: string) => {
      const path = ancestorIds(root, pageId);
      if (!path) return;
      setMany(path, true);
      setHighlighted(pageId);
    },
    [root, setMany],
  );

  useEffect(() => {
    if (focus) reveal(focus.pageId);
  }, [focus, reveal]);

  return (
    <div className="tree">
      <div className="tree-toolbar">
        <span className="muted small">{total === 1 ? "1 page" : `${total} pages`}</span>
        <div className="tree-actions">
          <button type="button" className="button button-small button-secondary"
            onClick={() => setMany(expandableIds(root), true)}>
            Expand all
          </button>
          <button type="button" className="button button-small button-secondary"
            onClick={() => setMany(expandableIds(root), false)}>
            Collapse all
          </button>
        </div>
      </div>

      <ul className="tree-list" role="tree" aria-label="Crawled pages">
        <TreeItem jobId={jobId} node={root} rootUrl={root.url} isExpanded={isExpanded} highlighted={highlighted}
          fresh={fresh} onToggle={toggle} onReveal={reveal} />
      </ul>
    </div>
  );
}

/**
 * Ids of pages that weren't in the tree on the previous render — i.e. just discovered by a running
 * crawl. Everything present on first render counts as already seen, so nothing flashes on page load.
 */
function useFreshIds(root: PageTreeNode): ReadonlySet<string> {
  const seen = useRef<Set<string> | null>(null);
  const ids = flatten(root).map((page) => page.pageId);

  if (seen.current === null) seen.current = new Set(ids);
  const fresh = new Set(ids.filter((id) => !seen.current!.has(id)));

  useEffect(() => {
    for (const id of fresh) seen.current!.add(id);
  });

  return fresh;
}

interface TreeItemProps {
  jobId: string;
  node: PageTreeNode;
  rootUrl: string;
  isExpanded: (node: PageTreeNode) => boolean;
  highlighted: string | null;
  fresh: ReadonlySet<string>;
  onToggle: (node: PageTreeNode) => void;
  onReveal: (pageId: string) => void;
}

function TreeItem({ jobId, node, rootUrl, isExpanded: isNodeExpanded, highlighted, fresh, onToggle, onReveal }: TreeItemProps) {
  const hasChildren = node.children.length > 0;
  const isExpanded = isNodeExpanded(node);
  const isHighlighted = highlighted === node.pageId;
  const isFresh = fresh.has(node.pageId);
  const rowRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (isHighlighted) rowRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
  }, [isHighlighted]);

  return (
    <li role="treeitem" aria-level={node.depth + 1} aria-expanded={hasChildren ? isExpanded : undefined}>
      <div ref={rowRef} id={`page-${node.pageId}`}
        className={`tree-row status-${node.status.toLowerCase()} ${isHighlighted ? "tree-row-highlight" : ""} ${isFresh ? "tree-row-new" : ""}`}>
        {hasChildren ? (
          <button type="button" className="tree-toggle" onClick={() => onToggle(node)}
            aria-label={`${isExpanded ? "Collapse" : "Expand"} ${node.url}`}>
            <ChevronRight size={15} className={isExpanded ? "chevron chevron-open" : "chevron"} aria-hidden="true" />
          </button>
        ) : (
          <span className="tree-toggle tree-toggle-leaf" aria-hidden="true">•</span>
        )}

        <div className="tree-main">
          <a href={node.url} target="_blank" rel="noopener noreferrer" className="tree-url" title={node.url}>
            {displayUrl(node.url, rootUrl)}
          </a>
          <PageNote node={node} rootUrl={rootUrl} onReveal={onReveal} />
        </div>

        <div className="tree-meta">
          <span className="status-cell"><StatusBadge status={node.status} /></span>
          <span className="http-code" title={node.httpStatus === null ? undefined : "HTTP status"}>{node.httpStatus ?? ""}</span>
          <RatioMeter ratio={node.domainLinkRatio} />
          {node.outgoingLinkCount ? (
            <Link to={`/jobs/${jobId}/pages/${node.pageId}`} className="link-count link-count-action"
              title="See this page's in-domain and outbound links">
              {node.outgoingLinkCount === 1 ? "1 link" : `${node.outgoingLinkCount} links`}
            </Link>
          ) : (
            <span className="link-count" title="Distinct outgoing links">
              {node.outgoingLinkCount === null ? "" : "0 links"}
            </span>
          )}
        </div>
      </div>

      {hasChildren && isExpanded && (
        <ul role="group" className="tree-list">
          {node.children.map((child) => (
            <TreeItem key={child.pageId} jobId={jobId} node={child} rootUrl={rootUrl} isExpanded={isNodeExpanded}
              highlighted={highlighted} fresh={fresh} onToggle={onToggle} onReveal={onReveal} />
          ))}
        </ul>
      )}
    </li>
  );
}

function PageNote({ node, rootUrl, onReveal }: { node: PageTreeNode; rootUrl: string; onReveal: (id: string) => void }) {
  if (node.status === "Duplicate" && node.duplicateOfPageId && node.duplicateOfUrl) {
    return (
      <span className="tree-note">
        Same content as{" "}
        <button type="button" className="link-button" onClick={() => onReveal(node.duplicateOfPageId!)}>
          {displayUrl(node.duplicateOfUrl, rootUrl)}
        </button>
        , not crawled again
      </span>
    );
  }

  if (node.error && (node.status === "Failed" || node.status === "Skipped")) {
    return <span className="tree-note">{node.error}</span>;
  }

  return null;
}
