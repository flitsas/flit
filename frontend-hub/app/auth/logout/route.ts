import { hubAuthRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/logout del hub como cliente `plataforma`.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return hubAuthRoutes.logout(request);
}
export const POST = GET;
