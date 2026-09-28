import { hubApiProxy } from "@/lib/auth.server";

// /api/v1/* del navegador del hub: @flit/auth lo reenvía al gateway con el Bearer de la sesión del hub (A-09) y
// sella el dominio con el host real. Sin sesión pasa sin token: la API decide (recuperación, activación).
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  const { path } = await context.params;
  return hubApiProxy(request, path);
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;
