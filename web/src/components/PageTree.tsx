import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";
import type { PageTreeNode } from "../api/types";
import { flatten } from "../lib/insights";
import { displayUrl } from "../lib/format";
import { ancestorIds, countNodes, expandableIds } from "../lib/tree";
import { RatioMeter } from "./RatioMeter";
import { StatusBadge } from "./StatusBadge";

interface PageTreeProps {
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
export function PageTree({ root, initialDepth = 1, focus }: PageTreeProps) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(expandableIds(root, initialDepth)));
  const [highlighted, setHighlighted] = useState<string | null>(null);
  const total = useMemo(() => countNodes(root), [root]);
  const fresh = useFreshIds(root);

  const toggle = useCallback((pageId: string) => {
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(pageId)) next.delete(pageId);
      else next.add(pageId);
      return next;
    });
  }, []);

  const reveal = useCallback(
    (pageId: string) => {
      const path = ancestorIds(root, pageId);
      if (!path) return;
      setExpanded((current) => new Set([...current, ...path]));
      setHighlighted(pageId);
    },
    [root],
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
            onClick={() => setExpanded(new Set(expandableIds(root)))}>
            Expand all
          </button>
          <button type="button" className="button button-small button-secondary"
            onClick={() => setExpanded(new Set())}>
            Collapse all
          </button>
        </div>
      </div>

      <ul className="tree-list" role="tree" aria-label="Crawled pages">
        <TreeItem node={root} rootUrl={root.url} expanded={expanded} highlighted={highlighted}
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
  node: PageTreeNode;
  rootUrl: string;
  expanded: Set<string>;
  highlighted: string | null;
  fresh: ReadonlySet<string>;
  onToggle: (pageId: string) => void;
  onReveal: (pageId: string) => void;
}

function TreeItem({ node, rootUrl, expanded, highlighted, fresh, onToggle, onReveal }: TreeItemProps) {
  const hasChildren = node.children.length > 0;
  const isExpanded = expanded.has(node.pageId);
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
          <button type="button" className="tree-toggle" onClick={() => onToggle(node.pageId)}
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
          <StatusBadge status={node.status} />
          {node.httpStatus !== null && <span className="http-code" title="HTTP status">{node.httpStatus}</span>}
          <RatioMeter ratio={node.domainLinkRatio} />
          <span className="link-count" title="Distinct outgoing links">
            {node.outgoingLinkCount === null ? "" : `${node.outgoingLinkCount} links`}
          </span>
        </div>
      </div>

      {hasChildren && isExpanded && (
        <ul role="group" className="tree-list">
          {node.children.map((child) => (
            <TreeItem key={child.pageId} node={child} rootUrl={rootUrl} expanded={expanded}
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
