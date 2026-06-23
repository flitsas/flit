import path from "node:path";
import type { NextConfig } from "next";

// Raíz del workspace pnpm. `outputFileTracingRoot` y `turbopack.root` DEBEN ser iguales (Next.js 16).
const monorepoRoot = path.resolve(__dirname, "..");

const nextConfig: NextConfig = {
  outputFileTracingRoot: monorepoRoot,
  turbopack: {
    root: monorepoRoot,
  },
  // El proxy de rewrites corta conexiones largas (~5s por defecto) → "socket hang up".
  // El pre-vuelo hace consultas RUNT/SIMIT reales (~8-10s); subimos el timeout del proxy.
  experimental: {
    proxyTimeout: 120_000,
  },
  // Proxy SuperAdmin API in dev — avoids browser CORS to :4003.
  // Requires: pnpm run dev:core-api + pnpm run dev:frontend (leave NEXT_PUBLIC_API_URL unset).
  async rewrites() {
    const apiOrigin = process.env.CORE_API_ORIGIN ?? 'http://localhost:4003';
    return [
      {
        source: '/api/v1/:path*',
        destination: `${apiOrigin}/api/v1/:path*`,
      },
    ];
  },
};

export default nextConfig;
