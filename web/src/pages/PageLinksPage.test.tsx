import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { PageLinks } from "../api/types";
import { PageLinksPage } from "./PageLinksPage";

const page: PageLinks = {
  jobId: "job-1",
  pageId: "about",
  url: "https://site.test/about.html",
  status: "Completed",
  httpStatus: 200,
  depth: 1,
  domainLinkRatio: 0.6,
  error: null,
  parentUrl: "https://site.test/",
  startingDomain: "site.test",
  internalLinks: [
    { url: "https://site.test/", pageId: "root", status: "Completed", domainLinkRatio: 0.875, outgoingLinkCount: 8 },
    { url: "https://blog.site.test/post", pageId: "post", status: "Completed", domainLinkRatio: 1, outgoingLinkCount: 2 },
    { url: "https://site.test/deep.html", pageId: null, status: null, domainLinkRatio: null, outgoingLinkCount: null },
  ],
  externalLinks: [
    { url: "https://facebook.com/site", host: "facebook.com" },
    { url: "https://twitter.com/site", host: "twitter.com" },
    { url: "https://twitter.com/site/status/1", host: "twitter.com" },
  ],
};

function renderPage(body: unknown = page, status = 200) {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={["/jobs/job-1/pages/about"]}>
        <Routes><Route path="/jobs/:jobId/pages/:pageId" element={<PageLinksPage />} /></Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("PageLinksPage", () => {
  it("shows the page, then its in-domain links, then its outbound links grouped by site", async () => {
    renderPage();

    expect(await screen.findByRole("heading", { name: /about\.html/ })).toBeInTheDocument();
    expect(screen.getByText("60%")).toBeInTheDocument();

    const inDomain = screen.getByRole("region", { name: "In-domain links" });
    const outbound = screen.getByRole("region", { name: "Outbound links" });
    expect(inDomain.compareDocumentPosition(outbound) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // Crawled targets with links open their own page; undiscovered ones say so.
    expect(within(inDomain).getByRole("link", { name: "/" })).toHaveAttribute("href", "/jobs/job-1/pages/root");
    expect(within(inDomain).getByRole("link", { name: "blog.site.test/post" })).toHaveAttribute("href", "/jobs/job-1/pages/post");
    expect(within(inDomain).getByText("Not crawled")).toBeInTheDocument();

    // Outbound grouped by site, busiest first.
    const hosts = within(outbound).getAllByText(/^(facebook|twitter)\.com$/).map((el) => el.textContent);
    expect(hosts).toEqual(["twitter.com", "facebook.com"]);
    expect(within(outbound).getByText("(2 sites)", { exact: false })).toBeInTheDocument();
  });

  it("filters both lists", async () => {
    renderPage();
    await screen.findByRole("heading", { name: /about\.html/ });

    fireEvent.change(screen.getByRole("searchbox", { name: "Filter links by URL" }), { target: { value: "twitter" } });

    expect(screen.getByText("No matching in-domain links")).toBeInTheDocument();
    expect(screen.queryByText("facebook.com")).not.toBeInTheDocument();
    expect(screen.getByText("twitter.com/site/status/1")).toBeInTheDocument();
  });

  it("explains when a page has no outbound links", async () => {
    renderPage({ ...page, externalLinks: [] });

    expect(await screen.findByText("Every link on this page stays on the site.")).toBeInTheDocument();
  });

  it("shows not found for a page outside the crawl", async () => {
    renderPage({ title: "Page not found" }, 404);

    expect(await screen.findByText("Page not found")).toBeInTheDocument();
  });
});
