import { History, Moon, Radar, ScanSearch, Sun } from "lucide-react";
import { Link, NavLink, Route, Routes } from "react-router";
import { EmptyState } from "./components/Feedback";
import { useTheme } from "./lib/theme";
import { HistoryPage } from "./pages/HistoryPage";
import { JobDetailsPage } from "./pages/JobDetailsPage";
import { PageLinksPage } from "./pages/PageLinksPage";
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
          <Route path="/jobs/:jobId/pages/:pageId" element={<PageLinksPage />} />
          <Route path="/history" element={<HistoryPage />} />
          <Route path="*" element={
            <EmptyState title="Page not found"><Link to="/">Start a crawl</Link></EmptyState>
          } />
        </Routes>
      </main>
    </>
  );
}

/** Sun in light mode, moon in dark mode; each click switches between the two. */
function ThemeToggle() {
  const { theme, toggleTheme } = useTheme();
  const next = theme === "light" ? "dark" : "light";

  return (
    <button type="button" className="icon-button theme-toggle" onClick={toggleTheme}
      title={`Switch to ${next} mode`} aria-label={`Switch to ${next} mode`}>
      {theme === "light" ? <Sun size={18} aria-hidden="true" /> : <Moon size={18} aria-hidden="true" />}
    </button>
  );
}
