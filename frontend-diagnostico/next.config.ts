import path from "node:path";
import type { NextConfig } from "next";

// Raíz del workspace pnpm. `outputFileTracingRoot` y `turbopack.root` DEBEN ser iguales (Next.js 16).
const monorepoRoot = path.resolve(__dirname, "..");

// Diagnóstico (FLIT Suite, producto `diagnostico`). Una sola imagen para DEV, QA y PDN: la configuración por ambiente
// se lee en el servidor al atender cada petición (lib/config.server.ts) y el navegador habla con core-diagnostico por
// el proxy /api/v1/* de este mismo host (app/api/v1/[...path]).
// Claves de query que solo usa el listado (/diagnosticos). La raíz pasó a ser el Dashboard (HU #14580, diseño §c C1): un
// enlace viejo con alguna de ellas (marcadores, la campana, /?vista=vehiculos) llega al listado con la misma query, que
// Next.js conserva al redirigir. /?desde=&hasta= sin otra clave abre el Dashboard con ese rango.
export const CLAVES_DEL_LISTADO = ["vista", "modo", "estado", "nombre", "pagina", "tamano", "placa", "veredicto", "saneamiento", "saneamiento_estado", "con_tramite"] as const;

const nextConfig: NextConfig = {
  async redirects() {
    return CLAVES_DEL_LISTADO.map((clave) => ({ source: "/", has: [{ type: "query" as const, key: clave }], destination: "/diagnosticos", permanent: false }));
  },
  transpilePackages: ["@flit/ui", "@flit/brand", "@flit/auth", "@flit/shell"],
  outputFileTracingRoot: monorepoRoot,
  turbopack: {
    root: monorepoRoot,
  },
};

export default nextConfig;
