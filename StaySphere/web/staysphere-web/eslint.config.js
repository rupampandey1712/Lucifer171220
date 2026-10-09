import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'playwright-report', 'src/api/schema.d.ts'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { 'react-hooks': reactHooks },
    rules: {
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      // An expression-bodied effect returns its value to React as the cleanup. Browsers now return Promises from
      // scrollTo/scrollIntoView, which crashed the app on unmount. Effects must use a block body (or return a cleanup arrow).
      'no-restricted-syntax': ['error', {
        selector: "CallExpression[callee.name=/^use(Layout|Insertion)?Effect$/] > ArrowFunctionExpression[expression=true]:not([body.type='ArrowFunctionExpression'])",
        message: 'Use a block body in effects: `useEffect(() => { doThing(); }, deps)`. Returned values become the cleanup.',
      }],
    },
  },
);
