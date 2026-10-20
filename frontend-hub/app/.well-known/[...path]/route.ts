import { proxyToApi } from "@/lib/api-proxy.server";
import { hubConfig } from "@/lib/config.server";

// Servidor OIDC del hub (A-05): vive en core-api y se sirve en el host del hub. El borde puede enviarlo directo al
// gateway; si llega aquí, se reenvía igual que /api/v1/* (lib/api-proxy.server.ts).
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  const { path } = await context.params;
  return proxyToApi(request, "/.well-known", path, hubConfig());
}

export const GET = handle;
export const POST = handle;
