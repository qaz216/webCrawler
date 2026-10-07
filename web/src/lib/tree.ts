import type { PageTreeNode } from "../api/types";

export function countNodes(node: PageTreeNode | null | undefined): number {
  if (!node) return 0;
  return 1 + node.children.reduce((sum, child) => sum + countNodes(child), 0);
}

/** Ids of every node that has children (what "expand all" opens). */
export function expandableIds(node: PageTreeNode | null | undefined, maxDepth = Infinity): string[] {
  if (!node || node.children.length === 0 || node.depth >= maxDepth) return [];
  return [node.pageId, ...node.children.flatMap((child) => expandableIds(child, maxDepth))];
}

/**
 * Ids of the ancestors of `targetId`, root first, or null if it's not in the tree.
 * Used to reveal a node (e.g. the original of a duplicate) by expanding its parents.
 */
export function ancestorIds(root: PageTreeNode, targetId: string): string[] | null {
  if (root.pageId === targetId) return [];
  for (const child of root.children) {
    const path = ancestorIds(child, targetId);
    if (path) return [root.pageId, ...path];
  }
  return null;
}
