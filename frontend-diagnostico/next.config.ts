import path from "node:path";
import type { NextConfig } from "next";

// Raíz del workspace pnpm. `outputFileTracingRoot` y `turbopack.root` DEBEN ser iguales (Next.js 16).
const monorepoRoot = path.resolve(__dirname, "..");

// Diagnóstico (FLIT Suite, producto `diagnostico`). Una sola imagen para DEV, QA y PDN: la configuración por ambiente
// se lee en el servidor al atender cada petición (lib/config.server.ts) y el navegador habla con core-diagnostico por
// el proxy /api/v1/* de este mismo host (app/api/v1/[...path]).
const nextConfig: NextConfig = {
  transpilePackages: ["@flit/ui", "@flit/brand", "@flit/auth", "@flit/shell"],
  outputFileTracingRoot: monorepoRoot,
  turbopack: {
    root: monorepoRoot,
  },
};

export default nextConfig;
