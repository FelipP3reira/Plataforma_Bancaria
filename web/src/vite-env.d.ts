/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BANCO?: string;
  readonly VITE_API_CREDITO?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
