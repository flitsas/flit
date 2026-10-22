import { createApiProxy } from "@flit/auth/server";
import { COMPARENDOS_PRODUCT } from "@/lib/auth.server";

// Rutas públicas de Comparendos, sin sesión: el píxel de apertura de los correos (/publico/t/{id}.gif) y el portal del
// conductor. Se reenvían a /api/v1/comparendos/publico/* de la API, que decide (la credencial es el enlace). El proxy
// con sesión de /api/v1/* responde 401 sin sesión, por eso este va aparte.
export const dynamic = "force-dynamic";

const proxyPublico = createApiProxy({ productCode: COMPARENDOS_PRODUCT, allowAnonymous: true });

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  const { path } = await context.params;
  return proxyPublico(request, ["comparendos", "publico", ...path]);
}

export const GET = handle;
export const POST = handle;
