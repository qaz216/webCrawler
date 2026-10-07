import { History, Monitor, Moon, Radar, ScanSearch, Sun } from "lucide-react";
import { Link, NavLink, Route, Routes } from "react-router";
import { EmptyState } from "./components/Feedback";
import { useTheme, type ThemePreference } from "./lib/theme";
import { HistoryPage } from "./pages/HistoryPage";
import { JobDetailsPage } from "./pages/JobDetailsPage";
import { StartCrawlPage } from "./pages/StartCrawlPage";

export function App() {
  return (
    <>
      <header className="topbar">
        <div className="topbar-inner">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden="true"><Radar size={18} strokeWidth={2.5} /></span>
            Web Crawler
          </Link>
          <nav className="nav" aria-label="Main">
            <NavLink to="/" end><ScanSearch size={16} aria-hidden="true" /> New crawl</NavLink>
            <NavLink to="/history"><History size={16} aria-hidden="true" /> History</NavLink>
          </nav>
          <ThemeToggle />
        </div>
      </header>

      <main className="container">
        <Routes>
          <Route path="/" element={<StartCrawlPage />} />
          <Route path="/jobs/:jobId" element={<JobDetailsPage />} />
          <Route path="/history" element={<HistoryPage />} />
          <Route path="*" element={
            <EmptyState title="Page not found"><Link to="/">Start a crawl</Link></EmptyState>
          } />
        </Routes>
      </main>
    </>
  );
}

const NEXT: Record<ThemePreference, ThemePreference> = { system: "light", light: "dark", dark: "system" };
const LABEL: Record<ThemePreference, string> = { system: "System theme", light: "Light theme", dark: "Dark theme" };

function ThemeToggle() {
  const { preference, setPreference } = useTheme();
  const Icon = preference === "light" ? Sun : preference === "dark" ? Moon : Monitor;

  return (
    <button type="button" className="icon-button theme-toggle" onClick={() => setPreference(NEXT[preference])}
      title={`${LABEL[preference]} (click to change)`} aria-label={`${LABEL[preference]}. Switch to ${LABEL[NEXT[preference]].toLowerCase()}`}>
      <Icon size={18} aria-hidden="true" />
    </button>
  );
}
