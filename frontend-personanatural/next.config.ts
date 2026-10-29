import path from "node:path";
import type { NextConfig } from "next";
import { cabecerasDeSeguridad } from "./lib/seguridad/cabeceras";

// Raíz del workspace pnpm. `outputFileTracingRoot` y `turbopack.root` DEBEN ser iguales (Next.js 16).
const monorepoRoot = path.resolve(__dirname, "..");

// Portal Persona Natural. Una sola imagen para DEV, QA y PDN: la configuración por ambiente se lee en el servidor al
// atender cada petición (lib/config.server.ts) y el navegador habla con el BFF por el proxy /api/v1/persona-natural/*
// de este mismo host (app/api/v1/persona-natural/[...path]), que custodia la sesión del trámite en una cookie HttpOnly.
const nextConfig: NextConfig = {
  outputFileTracingRoot: monorepoRoot,
  async headers() {
    return [{ source: "/:path*", headers: cabecerasDeSeguridad(process.env.NODE_ENV === "development") }];
  },
  turbopack: {
    root: monorepoRoot,
  },
};

export default nextConfig;
