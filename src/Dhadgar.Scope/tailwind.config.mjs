/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{astro,html,js,jsx,md,mdx,svelte,ts,tsx,vue}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        slate: {
          950: '#020617'
        }
      },
      fontFamily: {
        sans: ['Roboto', 'system-ui', 'sans-serif']
      }
    }
  },
  plugins: []
};
