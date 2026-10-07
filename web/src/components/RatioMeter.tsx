import { formatRatio } from "../lib/format";

/** Domain Link Ratio as a percentage with a small bar (share of links staying on the starting domain). */
export function RatioMeter({ ratio, label = "Domain Link Ratio" }: { ratio: number | null; label?: string }) {
  if (ratio === null) return <span className="ratio ratio-empty" title={`No ${label}`}>—</span>;

  return (
    <span className="ratio" title={`${label}: ${formatRatio(ratio)} of outgoing links stay on the starting domain`}>
      <span className="ratio-bar" aria-hidden="true">
        <span className="ratio-fill" style={{ width: `${ratio * 100}%` }} />
      </span>
      <span className="ratio-value">{formatRatio(ratio)}</span>
    </span>
  );
}
