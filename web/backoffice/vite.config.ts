import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// O navegador só fala com o BFF (porta 5180). Em desenvolvimento, o BFF repassa ao Vite, inclusive o HMR.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    hmr: { clientPort: 5180 },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.{ts,tsx}"],
    setupFiles: ["src/test/setup.ts"],
  },
});
