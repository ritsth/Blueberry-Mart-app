import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Vite dev server runs on 5173 by default, which the API's CORS policy allows
// (Cors:PortalOrigins in appsettings.json).
export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
});
