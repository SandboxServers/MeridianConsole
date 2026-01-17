/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{astro,html,js,jsx,ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        // ShoppingCart uses magenta as primary accent (vs Panel's cyan)
        'cyber-cyan': '#00D4FF',
        'cyber-magenta': '#FF00AA',
        'cyber-amber': '#FFB000',
        'cyber-green': '#00FF88',
        'space-dark': '#0A0E17',
        'panel-dark': '#111827',
        'panel-darker': '#0D1117',
        'glow-line': '#1E3A5F',
        'text-primary': '#E8F0FF',
        'text-secondary': '#6B7B8F',
        'text-muted': '#4A5568',
        // Brand accent - magenta tint for shop
        'brand-primary': '#FF00AA',
        'brand-glow': 'rgba(255, 0, 170, 0.3)',
      },
      fontFamily: {
        display: ['Orbitron', 'system-ui', 'sans-serif'],
        body: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['JetBrains Mono', 'Consolas', 'monospace'],
      },
      boxShadow: {
        'glow-cyan': '0 0 20px rgba(0, 212, 255, 0.3)',
        'glow-magenta': '0 0 20px rgba(255, 0, 170, 0.3)',
        'glow-magenta-lg': '0 0 40px rgba(255, 0, 170, 0.4)',
        'glow-amber': '0 0 20px rgba(255, 176, 0, 0.3)',
        'glow-green': '0 0 20px rgba(0, 255, 136, 0.3)',
        'inner-glow': 'inset 0 0 20px rgba(255, 0, 170, 0.1)',
      },
      animation: {
        'pulse-glow': 'pulse-glow 2s ease-in-out infinite',
        'fade-in': 'fade-in 0.3s ease-out',
        'slide-up': 'slide-up 0.3s ease-out',
      },
      keyframes: {
        'pulse-glow': {
          '0%, 100%': { boxShadow: '0 0 20px rgba(255, 0, 170, 0.3)' },
          '50%': { boxShadow: '0 0 30px rgba(255, 0, 170, 0.5)' },
        },
        'fade-in': {
          '0%': { opacity: '0' },
          '100%': { opacity: '1' },
        },
        'slide-up': {
          '0%': { opacity: '0', transform: 'translateY(10px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
      },
      backgroundImage: {
        'grid-pattern': 'linear-gradient(rgba(30, 58, 95, 0.3) 1px, transparent 1px), linear-gradient(90deg, rgba(30, 58, 95, 0.3) 1px, transparent 1px)',
      },
      backgroundSize: {
        'grid': '50px 50px',
      },
    },
  },
  plugins: [],
};
