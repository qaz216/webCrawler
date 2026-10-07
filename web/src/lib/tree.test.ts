import { describe, expect, it } from "vitest";
import { sampleTree } from "../test/fixtures";
import { ancestorIds, countNodes, expandableIds } from "./tree";

describe("tree helpers", () => {
  it("counts every node", () => {
    expect(countNodes(sampleTree)).toBe(5);
    expect(countNodes(null)).toBe(0);
  });

  it("lists nodes with children, optionally limited by depth", () => {
    expect(expandableIds(sampleTree)).toEqual(["root", "about"]);
    expect(expandableIds(sampleTree, 1)).toEqual(["root"]);
  });

  it("finds the ancestors of a node, root first", () => {
    expect(ancestorIds(sampleTree, "team")).toEqual(["root", "about"]);
    expect(ancestorIds(sampleTree, "root")).toEqual([]);
    expect(ancestorIds(sampleTree, "nope")).toBeNull();
  });
});
