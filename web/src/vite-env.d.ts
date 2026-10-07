/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Optional absolute API origin; empty means same origin (proxied /api). */
  readonly VITE_API_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
