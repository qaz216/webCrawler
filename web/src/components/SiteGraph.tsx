import { useEffect, useMemo, useRef, useState } from "react";
import ForceGraph2D, { type ForceGraphMethods, type LinkObject, type NodeObject } from "react-force-graph-2d";
import type { PageTreeNode } from "../api/types";
import { displayUrl, formatRatio } from "../lib/format";
import { ratioColor, toGraph, type GraphLink, type GraphNode } from "../lib/insights";
import { useTheme } from "../lib/theme";

type Node = NodeObject<GraphNode>;
type Link = LinkObject<GraphNode, GraphLink>;

interface SiteGraphProps {
  root: PageTreeNode;
  live: boolean;
  onSelectPage: (pageId: string) => void;
}

const HEIGHT = 480;

/**
 * Force-directed map of the crawl: one node per page, coloured by Domain Link Ratio (amber = links
 * leave the site, blue = links stay), sized by link count. Solid edges are "first discovered from";
 * dashed edges point a duplicate at its original. Click a node to open it in the tree.
 */
export default function SiteGraph({ root, live, onSelectPage }: SiteGraphProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<ForceGraphMethods<Node, Link> | undefined>(undefined);
  const nodeCache = useRef(new Map<string, Node>());
  const fitted = useRef(false);
  const [width, setWidth] = useState(600);
  const { theme } = useTheme();

  // Keep the container width in sync (the canvas needs explicit pixel sizes).
  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(280, Math.floor(entry!.contentRect.width))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  // Reuse node objects between polls so the simulation keeps positions instead of re-exploding.
  const data = useMemo(() => {
    const graph = toGraph(root);
    const nodes = graph.nodes.map((fresh) => {
      const existing = nodeCache.current.get(fresh.id);
      if (existing) return Object.assign(existing, fresh);
      const created: Node = { ...fresh };
      nodeCache.current.set(fresh.id, created);
      return created;
    });
    return { nodes, links: graph.links.map((link) => ({ ...link })) as Link[] };
  }, [root]);

  const colors = useMemo(() => readColors(), [theme]); // re-read CSS variables when the theme changes

  return (
    <div className="graph" ref={containerRef}>
      <ForceGraph2D<GraphNode, GraphLink>
        ref={graphRef}
        width={width}
        height={HEIGHT}
        graphData={data}
        backgroundColor="rgba(0,0,0,0)"
        nodeRelSize={4}
        nodeVal={(node) => (node.isRoot ? 14 : 2 + Math.sqrt(node.links))}
        nodeLabel={(node) => tooltip(node, root.url)}
        nodeCanvasObjectMode={() => "replace"}
        nodeCanvasObject={(node, ctx, scale) => drawNode(node, ctx, scale, colors)}
        linkColor={(link) => (link.kind === "duplicate" ? colors.duplicate : colors.edge)}
        linkLineDash={(link) => (link.kind === "duplicate" ? [3, 3] : null)}
        linkWidth={(link) => (link.kind === "duplicate" ? 1 : 0.8)}
        linkDirectionalParticles={(link) => (live && link.kind === "child" ? 1 : 0)}
        linkDirectionalParticleWidth={2}
        linkDirectionalParticleColor={() => colors.accent}
        cooldownTicks={120}
        onEngineStop={() => {
          if (!fitted.current) {
            graphRef.current?.zoomToFit(400, 40);
            fitted.current = true;
          }
        }}
        onNodeClick={(node) => onSelectPage(String(node.id))}
      />

      <div className="graph-legend" aria-hidden="true">
        <span className="legend-scale">
          <span>Links leave site</span>
          <span className="legend-gradient" />
          <span>Links stay on site</span>
        </span>
        <span className="legend-item"><span className="legend-dot" style={{ background: colors.inactive }} /> Failed / skipped / pending</span>
        <span className="legend-item"><span className="legend-dash" /> Duplicate of</span>
      </div>
      <p className="sr-only">
        The graph is a visual summary; the same pages, with all details, are listed in the tree view.
      </p>
    </div>
  );
}

interface Palette {
  edge: string;
  duplicate: string;
  accent: string;
  inactive: string;
  ring: string;
  label: string;
}

function readColors(): Palette {
  const css = getComputedStyle(document.documentElement);
  const v = (name: string, fallback: string) => css.getPropertyValue(name).trim() || fallback;
  return {
    edge: v("--graph-edge", "rgba(120,130,150,0.35)"),
    duplicate: v("--s-purple-fg", "#5b3cc4"),
    accent: v("--accent", "#2563eb"),
    inactive: v("--graph-inactive", "#9aa4b2"),
    ring: v("--text", "#1b1f24"),
    label: v("--muted", "#5f6b7a"),
  };
}

function drawNode(node: Node, ctx: CanvasRenderingContext2D, scale: number, colors: Palette) {
  const radius = Math.sqrt(node.isRoot ? 14 : 2 + Math.sqrt(node.links)) * 4;
  const x = node.x ?? 0;
  const y = node.y ?? 0;

  ctx.beginPath();
  ctx.arc(x, y, radius, 0, 2 * Math.PI);
  ctx.fillStyle =
    node.status === "Duplicate" ? colors.duplicate
    : node.ratio === null ? colors.inactive
    : ratioColor(node.ratio);
  ctx.globalAlpha = node.status === "Pending" || node.status === "Processing" ? 0.45 : 1;
  ctx.fill();
  ctx.globalAlpha = 1;

  if (node.isRoot) {
    ctx.lineWidth = 2 / scale;
    ctx.strokeStyle = colors.ring;
    ctx.beginPath();
    ctx.arc(x, y, radius + 3 / scale, 0, 2 * Math.PI);
    ctx.stroke();
  }

  // Labels only when zoomed in enough to read them (and always for the start page).
  if (node.isRoot || scale > 2.2) {
    const label = node.isRoot ? "start" : lastSegment(node.url);
    ctx.font = `${11 / scale}px Inter, system-ui, sans-serif`;
    ctx.fillStyle = colors.label;
    ctx.textAlign = "center";
    ctx.textBaseline = "top";
    ctx.fillText(label, x, y + radius + 2 / scale);
  }
}

function lastSegment(url: string): string {
  try {
    const path = new URL(url).pathname.replace(/\/index\.html?$/, "/");
    const parts = path.split("/").filter(Boolean);
    return parts.at(-1) ?? "/";
  } catch {
    return url;
  }
}

/** Tooltip HTML. Crawled URLs are untrusted input, so everything is escaped. */
function tooltip(node: Node, rootUrl: string): string {
  const ratio = node.ratio === null ? "no ratio" : `ratio ${formatRatio(node.ratio)}`;
  return `<div class="graph-tip"><strong>${escapeHtml(displayUrl(node.url, rootUrl))}</strong><br/>`
    + `${escapeHtml(node.status)} · ${ratio} · ${node.links} links · depth ${node.depth}</div>`;
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
}
