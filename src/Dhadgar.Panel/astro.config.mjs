import { defineConfig } from 'astro/config';
import react from '@astrojs/react';
import node from '@astrojs/node';

export default defineConfig({
  integrations: [react()],
  output: 'server',
  adapter: node({
    mode: 'standalone'
  }),
  // Preserve Astro 5 whitespace behavior (v7 default is 'jsx')
  compressHTML: true,
  server: {
    port: 4321,
    host: true
  }
});
