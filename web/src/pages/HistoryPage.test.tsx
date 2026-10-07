import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { JobListItem, PagedResult } from "../api/types";
import { HistoryPage } from "./HistoryPage";

const job: JobListItem = {
  jobId: "job-1",
  url: "https://example.com/",
  status: "Completed",
  createdAt: "2026-10-07T10:00:00Z",
  startedAt: "2026-10-07T10:00:01Z",
  completedAt: "2026-10-07T10:00:05Z",
  pagesDiscovered: 12,
  averageDomainLinkRatio: 0.875,
};

const page = (items: JobListItem[]): PagedResult<JobListItem> =>
  ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0 });

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter><HistoryPage /></MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("HistoryPage", () => {
  it("lists jobs with their average link ratio", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json(page([job])));
    renderPage();

    expect(await screen.findByRole("link", { name: "https://example.com/" })).toBeInTheDocument();
    expect(screen.getByText("87.5%")).toBeInTheDocument();
  });

  it("clears all jobs after confirmation", async () => {
    let cleared = false;
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) => {
      if (init?.method === "DELETE") {
        cleared = true;
        return json({ deleted: 1 });
      }
      return json(page(cleared ? [] : [job]));
    });
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderPage();
    await screen.findByRole("link", { name: "https://example.com/" }); // list loaded, button enabled

    fireEvent.click(screen.getByRole("button", { name: "Clear all" }));

    expect(await screen.findByText("No crawls yet")).toBeInTheDocument();
    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("Delete all 1 crawl job"));
    expect(fetchMock).toHaveBeenCalledWith("/api/jobs", expect.objectContaining({ method: "DELETE" }));
  });

  it("does nothing when the confirmation is declined", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(json(page([job])));
    vi.spyOn(window, "confirm").mockReturnValue(false);
    renderPage();
    await screen.findByRole("link", { name: "https://example.com/" });

    fireEvent.click(screen.getByRole("button", { name: "Clear all" }));

    expect(window.confirm).toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalledWith("/api/jobs", expect.objectContaining({ method: "DELETE" }));
    expect(screen.getByRole("link", { name: "https://example.com/" })).toBeInTheDocument();
  });

  it("disables Clear all when there is nothing to clear", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json(page([])));
    renderPage();

    expect(await screen.findByText("No crawls yet")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Clear all" })).toBeDisabled();
  });
});
