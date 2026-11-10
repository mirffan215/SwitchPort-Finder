import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev: Vite proxies /api to the ASP.NET Core API. Production: `npm run build` writes into the API's wwwroot,
// and Kestrel serves the files itself (single process, no CDN).
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': 'http://localhost:5000' } },
  // assetsInlineLimit 0: never inline fonts/images as data: URIs - the production CSP (default-src 'self') blocks data: fonts.
  build: { outDir: '../SwitchPortFinder.Api/wwwroot', emptyOutDir: true, assetsInlineLimit: 0 },
});
