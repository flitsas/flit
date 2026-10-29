import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "."),
      // `server-only` lanza fuera del servidor de Next; en las pruebas se sustituye por un módulo vacío.
      "server-only": path.resolve(__dirname, "__tests__/server-only-vacio.ts"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    env: { TZ: "America/Bogota" },
    setupFiles: ["./vitest.setup.ts"],
    include: ["**/*.test.{ts,tsx}"],
    exclude: ["node_modules", ".next"],
  },
});
