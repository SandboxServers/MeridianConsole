import { defineConfig } from 'astro/config';
import react from '@astrojs/react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  integrations: [react()],
  vite: {
    plugins: [tailwindcss()]
  },
  output: 'static',
  // Preserve Astro 5 whitespace behavior (v7 default is 'jsx')
  compressHTML: true,
  build: {
    assets: '_assets'
  },
  outDir: '_swa_publish/wwwroot'
});
