import { defineConfig } from "vitest/config";

// Pruebas de @flit/ui: contrato de los estilos que exporta el paquete (lectura de archivos, sin DOM).
export default defineConfig({
  test: { environment: "node", include: ["src/**/*.test.{ts,tsx}"] },
});
