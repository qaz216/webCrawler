import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { PageTreeNode } from "../api/types";
import { displayUrl, formatRatio } from "../lib/format";
import { ancestorIds, countNodes, expandableIds } from "../lib/tree";
import { StatusBadge } from "./StatusBadge";

interface PageTreeProps {
  root: PageTreeNode;
  /** Levels expanded initially (root = depth 0). */
  initialDepth?: number;
}

/**
 * Collapsible tree of crawled pages. Each page shows its status, HTTP code, Domain Link Ratio and
 * link count; duplicates link to their original, expanding the tree to reveal it.
 */
export function PageTree({ root, initialDepth = 1 }: PageTreeProps) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(expandableIds(root, initialDepth)));
  const [highlighted, setHighlighted] = useState<string | null>(null);
  const total = useMemo(() => countNodes(root), [root]);

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
          onToggle={toggle} onReveal={reveal} />
      </ul>
    </div>
  );
}

interface TreeItemProps {
  node: PageTreeNode;
  rootUrl: string;
  expanded: Set<string>;
  highlighted: string | null;
  onToggle: (pageId: string) => void;
  onReveal: (pageId: string) => void;
}

function TreeItem({ node, rootUrl, expanded, highlighted, onToggle, onReveal }: TreeItemProps) {
  const hasChildren = node.children.length > 0;
  const isExpanded = expanded.has(node.pageId);
  const isHighlighted = highlighted === node.pageId;
  const rowRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (isHighlighted) rowRef.current?.scrollIntoView?.({ behavior: "smooth", block: "center" });
  }, [isHighlighted]);

  return (
    <li role="treeitem" aria-level={node.depth + 1} aria-expanded={hasChildren ? isExpanded : undefined}>
      <div ref={rowRef} id={`page-${node.pageId}`}
        className={`tree-row status-${node.status.toLowerCase()} ${isHighlighted ? "tree-row-highlight" : ""}`}>
        {hasChildren ? (
          <button type="button" className="tree-toggle" onClick={() => onToggle(node.pageId)}
            aria-label={`${isExpanded ? "Collapse" : "Expand"} ${node.url}`}>
            <span aria-hidden="true">{isExpanded ? "▾" : "▸"}</span>
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
              highlighted={highlighted} onToggle={onToggle} onReveal={onReveal} />
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

/** Domain Link Ratio as a percentage with a small bar (share of links staying on the starting domain). */
function RatioMeter({ ratio }: { ratio: number | null }) {
  if (ratio === null) return <span className="ratio ratio-empty" title="No Domain Link Ratio">—</span>;

  return (
    <span className="ratio" title={`Domain Link Ratio: ${formatRatio(ratio)} of outgoing links stay on the starting domain`}>
      <span className="ratio-bar" aria-hidden="true">
        <span className="ratio-fill" style={{ width: `${ratio * 100}%` }} />
      </span>
      <span className="ratio-value">{formatRatio(ratio)}</span>
    </span>
  );
}
