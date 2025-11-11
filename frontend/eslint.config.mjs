import { defineConfig, globalIgnores } from 'eslint/config';
import nextVitals from 'eslint-config-next/core-web-vitals';
import nextTs from 'eslint-config-next/typescript';
import pluginPrettier from 'eslint-plugin-prettier';

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    // Allow the `any` type explicitly in TypeScript code.
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
    },
  },
  // Run Prettier as an ESLint rule so formatting issues show up in linting.
  {
    plugins: { prettier: pluginPrettier },
    rules: {
      'prettier/prettier': [
        'error',
        {
          endOfLine: 'lf',
        },
      ],
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    '.next/**',
    'out/**',
    'build/**',
    'next-env.d.ts',
    'lib/api/generated/**',
  ]),
]);

export default eslintConfig;
