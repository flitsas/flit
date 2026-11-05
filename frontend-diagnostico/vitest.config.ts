import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "."),
      // `server-only` lanza fuera de un Server Component; en las pruebas los módulos de servidor se importan directo.
      "server-only": path.resolve(__dirname, "__tests__/server-only.ts"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    env: { TZ: "America/Bogota" },
    setupFiles: ["./vitest.setup.ts"],
    include: ["**/*.test.{ts,tsx}"],
    exclude: ["node_modules", ".next", "e2e"],
  },
});
