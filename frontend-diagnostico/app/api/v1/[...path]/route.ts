import { apiProxy } from "@/lib/auth.server";

// /api/v1/* del navegador: @flit/auth lo reenvía al gateway con el Bearer de la sesión de Diagnóstico (token con
// aud=diagnostico) y sella el dominio con el host real. Sin sesión responde 401 SESSION_EXPIRED sin llamar a la API.
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  const { path } = await context.params;
  return apiProxy(request, path);
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;
