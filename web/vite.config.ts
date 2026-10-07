/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development, /api is proxied to the Crawl API so the browser sees a single origin.
// In Docker, nginx does the same (see nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": process.env.API_URL ?? "http://localhost:8080",
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: false,
  },
});
