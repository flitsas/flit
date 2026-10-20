import { isOidcSessionMode } from "@flit/auth/request";
import { tramitesApiProxy } from "@/lib/auth/oidc.server";

// A-10 (HU #13001): con la sesión de @flit/auth, el middleware reescribe /api/v1/* aquí. Se reenvía al gateway con el
// Bearer de la sesión (renovado si hace falta); lo que mande el navegador en Authorization se descarta. Con la sesión
// antigua esta ruta no existe y /api/v1/* sigue por los rewrites de next.config.ts, como siempre.
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  if (!isOidcSessionMode()) return new Response(null, { status: 404 });
  const { path } = await context.params;
  return tramitesApiProxy(request, path);
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;
