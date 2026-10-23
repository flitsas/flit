import { hubAuthRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/login del hub como cliente `plataforma`.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return hubAuthRoutes.login(request);
}
