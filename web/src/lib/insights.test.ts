import { describe, expect, it } from "vitest";
import { node, sampleTree } from "../test/fixtures";
import { flatten, ratioColor, ratioStats, recentlyFinished, toGraph } from "./insights";

describe("flatten", () => {
  it("lists every page in depth-first order", () => {
    expect(flatten(sampleTree).map((p) => p.pageId)).toEqual(["root", "about", "team", "alias", "missing"]);
  });
});

describe("recentlyFinished", () => {
  it("returns finished pages, newest first, limited", () => {
    const tree = node("root", "/", 0, { finishedAt: "2026-10-07T10:00:01Z" }, [
      node("a", "/a", 1, { finishedAt: "2026-10-07T10:00:05Z" }),
      node("b", "/b", 1, { finishedAt: null, status: "Pending" }),
      node("c", "/c", 1, { finishedAt: "2026-10-07T10:00:03Z" }),
    ]);

    expect(recentlyFinished(tree, 2).map((p) => p.pageId)).toEqual(["a", "c"]);
  });
});

describe("ratioStats", () => {
  it("averages measured pages and buckets them into bands", () => {
    const stats = ratioStats(sampleTree);

    // Measured: root 0.875, about 0.75, team 0 (duplicate and failed pages have no ratio).
    expect(stats.measured).toBe(3);
    expect(stats.average).toBeCloseTo((0.875 + 0.75 + 0) / 3);
    expect(stats.bands.map((b) => b.count)).toEqual([1, 0, 0, 2]);
  });

  it("handles a tree with nothing measured yet", () => {
    expect(ratioStats(null)).toMatchObject({ measured: 0, average: null });
  });
});

describe("toGraph", () => {
  it("builds nodes, parent → child edges and duplicate → original edges", () => {
    const graph = toGraph(sampleTree);

    expect(graph.nodes).toHaveLength(5);
    expect(graph.nodes.find((n) => n.id === "root")?.isRoot).toBe(true);
    expect(graph.links.filter((l) => l.kind === "child")).toHaveLength(4);
    expect(graph.links.filter((l) => l.kind === "duplicate")).toEqual([
      { source: "alias", target: "root", kind: "duplicate" },
    ]);
  });
});

describe("ratioColor", () => {
  it("moves from amber (off-site) to blue (on-site)", () => {
    expect(ratioColor(0)).toBe("hsl(32 78% 48%)");
    expect(ratioColor(1)).toBe("hsl(210 78% 48%)");
    expect(ratioColor(5)).toBe(ratioColor(1));
  });
});
