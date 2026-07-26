// Tailwind CSS 3 via PostCSS. Replaces the deprecated @astrojs/tailwind
// integration, which does not support Astro >= 6.
export default {
  plugins: {
    tailwindcss: {},
    autoprefixer: {}
  }
};
