import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useParams } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { StartCrawlPage } from "./StartCrawlPage";

function JobPageStub() {
  return <p>Job page for {useParams().jobId}</p>;
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<StartCrawlPage />} />
          <Route path="/jobs/:jobId" element={<JobPageStub />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function mockFetch(status: number, body: unknown) {
  return vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }),
  );
}

describe("StartCrawlPage", () => {
  it("submits the URL and depth, then opens the job details page", async () => {
    const fetchMock = mockFetch(202, { jobId: "job-123" });
    renderPage();

    fireEvent.change(screen.getByLabelText("Website URL"), { target: { value: " https://example.com/ " } });
    fireEvent.change(screen.getByLabelText(/Max depth/), { target: { value: "1" } });
    fireEvent.click(screen.getByRole("button", { name: "Start crawl" }));

    expect(await screen.findByText("Job page for job-123")).toBeInTheDocument();
    const [path, init] = fetchMock.mock.calls[0]!;
    expect(path).toBe("/api/jobs");
    expect(JSON.parse(init!.body as string)).toEqual({ url: "https://example.com/", maxDepth: 1 });
  });

  it("shows the API's validation errors next to the fields", async () => {
    mockFetch(400, { title: "One or more validation errors occurred.", errors: { url: ["Must be an absolute http:// or https:// URL."] } });
    renderPage();

    fireEvent.change(screen.getByLabelText("Website URL"), { target: { value: "ftp://nope" } });
    fireEvent.click(screen.getByRole("button", { name: "Start crawl" }));

    expect(await screen.findByText("Must be an absolute http:// or https:// URL.")).toBeInTheDocument();
    expect(screen.getByLabelText("Website URL")).toHaveAttribute("aria-invalid", "true");
  });

  it("shows a clear message when the API is unreachable", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("Failed to fetch"));
    renderPage();

    fireEvent.change(screen.getByLabelText("Website URL"), { target: { value: "https://example.com/" } });
    fireEvent.click(screen.getByRole("button", { name: "Start crawl" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Could not reach the Crawl API");
  });

  it("keeps the submit button disabled until a URL is entered", () => {
    renderPage();
    expect(screen.getByRole("button", { name: "Start crawl" })).toBeDisabled();
  });
});
