import { proxyToApi } from "@/lib/api-proxy.server";
import { hubConfig } from "@/lib/config.server";

// El navegador del hub llama a /api/v1/* en su mismo host; esto lo reenvía al gateway (lib/api-proxy.server.ts).
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  const { path } = await context.params;
  return proxyToApi(request, path, hubConfig());
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;
