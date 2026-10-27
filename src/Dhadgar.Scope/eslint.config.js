import js from "@eslint/js";
import globals from "globals";
import tseslint from "@typescript-eslint/eslint-plugin";
import tsparser from "@typescript-eslint/parser";
import eslintReact from "@eslint-react/eslint-plugin";
import reactHooks from "eslint-plugin-react-hooks";
import jsxA11y from "eslint-plugin-jsx-a11y-x";
import security from "eslint-plugin-security";
import astroPlugin from "eslint-plugin-astro";

// @eslint-react replaces eslint-plugin-react, which does not support ESLint 10
// (jsx-eslint/eslint-plugin-react#3977). Its recommended set also ships hook
// rules; eslint-plugin-react-hooks stays the source of truth for those, so the
// duplicates are switched off here instead of the react-hooks ones.
const eslintReactRecommended = eslintReact.configs["recommended-typescript"];
const eslintReactHookDuplicates = Object.fromEntries(
  [
    "error-boundaries",
    "exhaustive-deps",
    "globals",
    "immutability",
    "purity",
    "refs",
    "rules-of-hooks",
    "set-state-in-effect",
    "set-state-in-render",
    "static-components",
    "unsupported-syntax",
    "use-memo",
  ].map((rule) => [`@eslint-react/${rule}`, "off"])
);

export default [
  js.configs.recommended,
  // Astro files - use Astro plugin's recommended config
  ...astroPlugin.configs.recommended,
  // TypeScript/React files
  {
    files: ["**/*.{ts,tsx}"],
    languageOptions: {
      parser: tsparser,
      parserOptions: {
        ecmaVersion: "latest",
        sourceType: "module",
        ecmaFeatures: {
          jsx: true,
        },
      },
      globals: {
        ...globals.browser,
        ...globals.es2021,
        ...globals.node,
      },
    },
    plugins: {
      "@typescript-eslint": tseslint,
      ...eslintReactRecommended.plugins,
      "react-hooks": reactHooks,
      "jsx-a11y-x": jsxA11y,
      security: security,
    },
    rules: {
      ...tseslint.configs.recommended.rules,
      ...eslintReactRecommended.rules,
      ...eslintReactHookDuplicates,
      // Rules eslint-plugin-react's recommended set enforced that
      // @eslint-react leaves out of recommended or only warns on.
      "@eslint-react/no-missing-component-display-name": "error",
      "@eslint-react/jsx-no-comment-textnodes": "error",
      "@eslint-react/jsx-no-children-prop": "error",
      "@eslint-react/dom-no-unsafe-target-blank": "error",
      "@eslint-react/dom-no-unknown-property": "error",
      ...reactHooks.configs.recommended.rules,
      ...jsxA11y.configs.recommended.rules,
      ...security.configs.recommended.rules,
      "@typescript-eslint/no-unused-vars": [
        "error",
        {
          argsIgnorePattern: "^_",
        },
      ],
    },
  },
  {
    ignores: ["node_modules/**", "dist/**", "_swa_publish/**", ".astro/**"],
  },
];
