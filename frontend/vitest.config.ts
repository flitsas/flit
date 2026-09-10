import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Configuración de pruebas unitarias de la consola admin (HU #10194).
// jsdom + Testing Library; alias "@/" alineado con tsconfig (paths).
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "."),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    // El producto vive en Bogotá y varios cálculos de fecha dependen del huso. Sin fijarlo, cada
    // quien corre las pruebas en el suyo: en UTC un fallo de «hoy corrido un día» no se manifiesta
    // —UTC y la hora local coinciden— y la prueba que debía cazarlo pasa sin haber comprobado nada.
    env: { TZ: "America/Bogota" },
    setupFiles: ["./vitest.setup.ts"],
    include: ["**/*.test.{ts,tsx}"],
    exclude: ["node_modules", ".next"],
  },
});
