import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import type { PageTreeNode } from "../api/types";
import { sampleTree } from "../test/fixtures";
import { PageTree } from "./PageTree";

const tree = (root: PageTreeNode) => (
  <MemoryRouter>
    <PageTree jobId="job-1" root={root} />
  </MemoryRouter>
);

describe("PageTree", () => {
  it("shows the root and its children, with ratio, status and link count", () => {
    render(tree(sampleTree));

    const rootRow = screen.getByRole("link", { name: "/" }).closest(".tree-row") as HTMLElement;
    expect(within(rootRow).getByText("87.5%")).toBeInTheDocument();
    expect(within(rootRow).getByText("8 links")).toBeInTheDocument();
    expect(within(rootRow).getByText("Completed")).toBeInTheDocument();

    expect(screen.getByRole("link", { name: "/about.html" })).toBeInTheDocument();
    expect(screen.getByText("HTTP 404")).toBeInTheDocument();
    expect(screen.getByText("5 pages")).toBeInTheDocument();
  });

  it("links each page's link count to its in-domain/outbound links page", () => {
    render(tree(sampleTree));

    expect(screen.getByRole("link", { name: "8 links" })).toHaveAttribute("href", "/jobs/job-1/pages/root");
  });

  it("starts collapsed below the first level and expands on demand", () => {
    render(tree(sampleTree));
    expect(screen.queryByRole("link", { name: "/team.html" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Expand https://site.test/about.html" }));
    expect(screen.getByRole("link", { name: "/team.html" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Collapse all" }));
    expect(screen.queryByRole("link", { name: "/about.html" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));
    expect(screen.getByRole("link", { name: "/team.html" })).toBeInTheDocument();
  });

  it("expands the top level by default even when its children arrive later", () => {
    // First render: the crawl has only the start page so far.
    const { rerender } = render(tree({ ...sampleTree, children: [] }));
    expect(screen.queryByRole("link", { name: "/about.html" })).not.toBeInTheDocument();

    // Next poll: children were discovered — the top level opens, deeper levels stay closed.
    rerender(tree(sampleTree));
    expect(screen.getByRole("link", { name: "/about.html" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "/team.html" })).not.toBeInTheDocument();
  });

  it("remembers that the user collapsed the top level", () => {
    const { rerender } = render(tree(sampleTree));

    fireEvent.click(screen.getByRole("button", { name: "Collapse https://site.test/" }));
    rerender(tree({ ...sampleTree })); // a later poll

    expect(screen.queryByRole("link", { name: "/about.html" })).not.toBeInTheDocument();
  });

  it("labels duplicates and links them to their original", () => {
    render(tree(sampleTree));
    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));

    const aliasRow = screen.getByRole("link", { name: "/index.html" }).closest(".tree-row") as HTMLElement;
    expect(within(aliasRow).getByText("Duplicate")).toBeInTheDocument();
    expect(aliasRow).toHaveTextContent("Same content as /, not crawled again");

    fireEvent.click(within(aliasRow).getByRole("button", { name: "/" }));
    const rootRow = screen.getByRole("link", { name: "/" }).closest(".tree-row");
    expect(rootRow).toHaveClass("tree-row-highlight");
  });
});
