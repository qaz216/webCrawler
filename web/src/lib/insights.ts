import type { PageStatus, PageTreeNode } from "../api/types";

export function flatten(root: PageTreeNode | null | undefined): PageTreeNode[] {
  if (!root) return [];
  const result: PageTreeNode[] = [];
  const stack = [root];
  while (stack.length > 0) {
    const node = stack.pop()!;
    result.push(node);
    for (let i = node.children.length - 1; i >= 0; i--) stack.push(node.children[i]!);
  }
  return result;
}

/** Most recently finished pages first — the live activity feed. */
export function recentlyFinished(root: PageTreeNode | null | undefined, limit = 8): PageTreeNode[] {
  return flatten(root)
    .filter((page) => page.finishedAt !== null)
    .sort((a, b) => Date.parse(b.finishedAt!) - Date.parse(a.finishedAt!))
    .slice(0, limit);
}

export interface RatioBand {
  label: string;
  min: number;
  max: number;
  count: number;
}

export interface RatioStats {
  /** Pages that have a ratio (crawled HTML pages). */
  measured: number;
  average: number | null;
  bands: RatioBand[];
}

const BANDS: Omit<RatioBand, "count">[] = [
  { label: "0–25%", min: 0, max: 0.25 },
  { label: "25–50%", min: 0.25, max: 0.5 },
  { label: "50–75%", min: 0.5, max: 0.75 },
  { label: "75–100%", min: 0.75, max: 1.0001 },
];

export function ratioStats(root: PageTreeNode | null | undefined): RatioStats {
  const measured = flatten(root).filter((page) => page.domainLinkRatio !== null);
  const ratios = measured.map((page) => page.domainLinkRatio!);

  return {
    measured: measured.length,
    average: ratios.length === 0 ? null : ratios.reduce((sum, r) => sum + r, 0) / ratios.length,
    bands: BANDS.map((band) => ({
      ...band,
      count: ratios.filter((r) => r >= band.min && r < band.max).length,
    })),
  };
}

export interface GraphNode {
  id: string;
  url: string;
  depth: number;
  status: PageStatus;
  ratio: number | null;
  links: number;
  isRoot: boolean;
}

export interface GraphLink {
  source: string;
  target: string;
  kind: "child" | "duplicate";
}

/** Site graph: pages as nodes, "first discovered from" edges, plus dashed duplicate → original edges. */
export function toGraph(root: PageTreeNode | null | undefined): { nodes: GraphNode[]; links: GraphLink[] } {
  const pages = flatten(root);
  const ids = new Set(pages.map((page) => page.pageId));
  const nodes = pages.map<GraphNode>((page) => ({
    id: page.pageId,
    url: page.url,
    depth: page.depth,
    status: page.status,
    ratio: page.domainLinkRatio,
    links: page.outgoingLinkCount ?? 0,
    isRoot: page.pageId === root?.pageId,
  }));

  const links: GraphLink[] = [];
  for (const page of pages) {
    for (const child of page.children) links.push({ source: page.pageId, target: child.pageId, kind: "child" });
    if (page.duplicateOfPageId && ids.has(page.duplicateOfPageId)) {
      links.push({ source: page.pageId, target: page.duplicateOfPageId, kind: "duplicate" });
    }
  }
  return { nodes, links };
}

/**
 * Colour for a Domain Link Ratio: amber (links mostly leave the site) → green → blue (links stay on site).
 * Always paired with the percentage in text, so colour is never the only signal.
 */
export function ratioColor(ratio: number): string {
  const clamped = Math.min(1, Math.max(0, ratio));
  return `hsl(${Math.round(32 + clamped * 178)} 78% 48%)`;
}
