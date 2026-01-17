/// <reference types="astro/client" />

interface ImportMetaEnv {
  readonly PUBLIC_GATEWAY_URL: string;
  readonly PUBLIC_APP_NAME: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
