import type { PageTreeNode } from "../api/types";

export function node(
  pageId: string,
  path: string,
  depth: number,
  overrides: Partial<PageTreeNode> = {},
  children: PageTreeNode[] = [],
): PageTreeNode {
  return {
    pageId,
    url: `https://site.test${path}`,
    depth,
    status: "Completed",
    httpStatus: 200,
    error: null,
    domainLinkRatio: 0.5,
    outgoingLinkCount: 4,
    duplicateOfPageId: null,
    duplicateOfUrl: null,
    children,
    ...overrides,
  };
}

/** / → (/about → /team, /index.html [duplicate of /]), /missing.html [404] */
export const sampleTree: PageTreeNode = node("root", "/", 0, { domainLinkRatio: 0.875, outgoingLinkCount: 8 }, [
  node("about", "/about.html", 1, { domainLinkRatio: 0.75 }, [
    node("team", "/team.html", 2, { domainLinkRatio: 0, outgoingLinkCount: 0 }),
    node("alias", "/index.html", 2, {
      status: "Duplicate",
      domainLinkRatio: null,
      outgoingLinkCount: null,
      duplicateOfPageId: "root",
      duplicateOfUrl: "https://site.test/",
    }),
  ]),
  node("missing", "/missing.html", 1, {
    status: "Failed",
    httpStatus: 404,
    error: "HTTP 404",
    domainLinkRatio: null,
    outgoingLinkCount: null,
  }),
]);
