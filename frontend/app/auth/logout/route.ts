import { isOidcSessionMode } from "@flit/auth/request";
import { tramitesAuthRoutes } from "@/lib/auth/oidc.server";

// A-10 (HU #13001): /auth/logout de @flit/auth. Con la sesión antigua (FLIT_SESSION_MODE distinto de oidc) no existe.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> | Response {
  if (!isOidcSessionMode()) return new Response(null, { status: 404 });
  return tramitesAuthRoutes.logout(request);
}
export const POST = GET;
