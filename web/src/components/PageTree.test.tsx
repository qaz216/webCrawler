import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { sampleTree } from "../test/fixtures";
import { PageTree } from "./PageTree";

describe("PageTree", () => {
  it("shows the root and its children, with ratio, status and link count", () => {
    render(<PageTree root={sampleTree} />);

    const rootRow = screen.getByRole("link", { name: "/" }).closest(".tree-row") as HTMLElement;
    expect(within(rootRow).getByText("87.5%")).toBeInTheDocument();
    expect(within(rootRow).getByText("8 links")).toBeInTheDocument();
    expect(within(rootRow).getByText("Completed")).toBeInTheDocument();

    expect(screen.getByRole("link", { name: "/about.html" })).toBeInTheDocument();
    expect(screen.getByText("HTTP 404")).toBeInTheDocument();
    expect(screen.getByText("5 pages")).toBeInTheDocument();
  });

  it("starts collapsed below the first level and expands on demand", () => {
    render(<PageTree root={sampleTree} />);
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
    const { rerender } = render(<PageTree root={{ ...sampleTree, children: [] }} />);
    expect(screen.queryByRole("link", { name: "/about.html" })).not.toBeInTheDocument();

    // Next poll: children were discovered — the top level opens, deeper levels stay closed.
    rerender(<PageTree root={sampleTree} />);
    expect(screen.getByRole("link", { name: "/about.html" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "/team.html" })).not.toBeInTheDocument();
  });

  it("remembers that the user collapsed the top level", () => {
    const { rerender } = render(<PageTree root={sampleTree} />);

    fireEvent.click(screen.getByRole("button", { name: "Collapse https://site.test/" }));
    rerender(<PageTree root={{ ...sampleTree }} />); // a later poll

    expect(screen.queryByRole("link", { name: "/about.html" })).not.toBeInTheDocument();
  });

  it("labels duplicates and links them to their original", () => {
    render(<PageTree root={sampleTree} />);
    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));

    const aliasRow = screen.getByRole("link", { name: "/index.html" }).closest(".tree-row") as HTMLElement;
    expect(within(aliasRow).getByText("Duplicate")).toBeInTheDocument();
    expect(aliasRow).toHaveTextContent("Same content as /, not crawled again");

    fireEvent.click(within(aliasRow).getByRole("button", { name: "/" }));
    const rootRow = screen.getByRole("link", { name: "/" }).closest(".tree-row");
    expect(rootRow).toHaveClass("tree-row-highlight");
  });
});
