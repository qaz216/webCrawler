import { Link, NavLink, Route, Routes } from "react-router";
import { EmptyState } from "./components/Feedback";
import { HistoryPage } from "./pages/HistoryPage";
import { JobDetailsPage } from "./pages/JobDetailsPage";
import { StartCrawlPage } from "./pages/StartCrawlPage";

export function App() {
  return (
    <>
      <header className="topbar">
        <div className="topbar-inner">
          <Link to="/" className="brand">Web Crawler</Link>
          <nav className="nav" aria-label="Main">
            <NavLink to="/" end>New crawl</NavLink>
            <NavLink to="/history">History</NavLink>
          </nav>
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
