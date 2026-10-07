/** 0.875 → "87.5%", 1 → "100%", null → "—". */
export function formatRatio(ratio: number | null | undefined): string {
  if (ratio === null || ratio === undefined) return "—";
  const percent = Math.round(ratio * 1000) / 10;
  return `${percent}%`;
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "medium",
  });
}

/** Elapsed time between two instants: "850 ms", "35.7 s", "4m 05s", "1h 02m". End defaults to now (still running). */
export function formatDuration(
  startIso: string | null | undefined,
  endIso: string | null | undefined,
  now: number = Date.now(),
): string {
  if (!startIso) return "—";
  const ms = Math.max(0, (endIso ? Date.parse(endIso) : now) - Date.parse(startIso));

  if (ms < 1000) return `${ms} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;

  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (n: number) => n.toString().padStart(2, "0");

  return hours > 0 ? `${hours}h ${pad(minutes)}m` : `${minutes}m ${pad(seconds)}s`;
}

/**
 * Compact label for a page URL: same-host URLs show path + query ("/about?x=1"),
 * other hosts keep the full URL. The full URL is always available as a tooltip/link.
 */
export function displayUrl(url: string, rootUrl: string): string {
  try {
    const target = new URL(url);
    const root = new URL(rootUrl);
    return target.host === root.host ? `${target.pathname}${target.search}` : url;
  } catch {
    return url;
  }
}
