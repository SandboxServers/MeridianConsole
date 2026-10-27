import { defineConfig } from 'astro/config';
import react from '@astrojs/react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  integrations: [react()],
  // Tailwind CSS 4 via its Vite plugin; theme lives in src/styles/global.css
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
