import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const require = createRequire(new URL('../../../../MProjectFrontend/package.json', import.meta.url));
export default {
  root: fileURLToPath(new URL('../../../../', import.meta.url)),
  resolve: { alias: [
    { find: /^react$/, replacement: require.resolve('react') },
    { find: /^antd$/, replacement: require.resolve('antd') },
    { find: /^react-i18next$/, replacement: require.resolve('react-i18next') },
    { find: /^react\/jsx-runtime$/, replacement: require.resolve('react/jsx-runtime') },
    { find: /^react\/jsx-dev-runtime$/, replacement: require.resolve('react/jsx-dev-runtime') },
    { find: /^@testing-library\/react$/, replacement: require.resolve('@testing-library/react') },
    { find: /^vitest$/, replacement: fileURLToPath(new URL('../../../../MProjectFrontend/node_modules/vitest/dist/index.js', import.meta.url)) },
  ] },
  esbuild: { jsx: 'automatic' },
  test: { environment: 'jsdom', include: ['docs/code-review/reports/08-probes/monitor.test.tsx'] },
};
