import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
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

async function openClearDialog() {
  await screen.findByRole("link", { name: "https://example.com/" }); // list loaded, button enabled
  fireEvent.click(screen.getByRole("button", { name: "Clear all" }));
  return screen.getByRole("dialog", { name: "Clear crawl history?" });
}

describe("HistoryPage", () => {
  it("lists jobs with their average link ratio", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json(page([job])));
    renderPage();

    expect(await screen.findByRole("link", { name: "https://example.com/" })).toBeInTheDocument();
    expect(screen.getByText("87.5%")).toBeInTheDocument();
  });

  it("asks for confirmation in a dialog, then clears all jobs", async () => {
    let cleared = false;
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) => {
      if (init?.method === "DELETE") {
        cleared = true;
        return json({ deleted: 1 });
      }
      return json(page(cleared ? [] : [job]));
    });
    renderPage();

    const dialog = await openClearDialog();
    expect(dialog).toHaveTextContent("This permanently deletes 1 crawl job");
    expect(fetchMock).not.toHaveBeenCalledWith("/api/jobs", expect.objectContaining({ method: "DELETE" }));

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete 1 job" }));

    expect(await screen.findByText("No crawls yet")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/jobs", expect.objectContaining({ method: "DELETE" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("keeps everything when the dialog is cancelled", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(json(page([job])));
    renderPage();

    const dialog = await openClearDialog();
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalledWith("/api/jobs", expect.objectContaining({ method: "DELETE" }));
    expect(screen.getByRole("link", { name: "https://example.com/" })).toBeInTheDocument();
  });

  it("shows the error inside the dialog when clearing fails", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) =>
      init?.method === "DELETE" ? json({ title: "Server error" }, 500) : json(page([job])));
    renderPage();

    const dialog = await openClearDialog();
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete 1 job" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Could not clear the history: Server error");
  });

  it("disables Clear all when there is nothing to clear", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json(page([])));
    renderPage();

    expect(await screen.findByText("No crawls yet")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Clear all" })).toBeDisabled();
  });
});
