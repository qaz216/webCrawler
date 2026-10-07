import { describe, expect, it } from "vitest";
import { displayUrl, formatDuration, formatRatio } from "./format";

describe("formatRatio", () => {
  it.each([
    [1, "100%"],
    [0, "0%"],
    [0.875, "87.5%"],
    [2 / 3, "66.7%"],
    [null, "—"],
  ])("formats %s as %s", (ratio, expected) => {
    expect(formatRatio(ratio)).toBe(expected);
  });
});

describe("formatDuration", () => {
  const start = "2026-10-07T10:00:00.000Z";

  it.each([
    ["2026-10-07T10:00:00.850Z", "850 ms"],
    ["2026-10-07T10:00:35.700Z", "35.7 s"],
    ["2026-10-07T10:04:05.000Z", "4m 05s"],
    ["2026-10-07T11:02:00.000Z", "1h 02m"],
  ])("from start to %s is %s", (end, expected) => {
    expect(formatDuration(start, end)).toBe(expected);
  });

  it("counts up to now while still running", () => {
    expect(formatDuration(start, null, Date.parse("2026-10-07T10:00:12.000Z"))).toBe("12.0 s");
  });

  it("is a dash before the job has started", () => {
    expect(formatDuration(null, null)).toBe("—");
  });
});

describe("displayUrl", () => {
  const root = "https://site.test/";

  it("shows path and query for the starting host", () => {
    expect(displayUrl("https://site.test/about?x=1", root)).toBe("/about?x=1");
  });

  it("keeps the full URL for other hosts", () => {
    expect(displayUrl("https://other.test/a", root)).toBe("https://other.test/a");
  });
});
