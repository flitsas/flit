import { isOidcSessionMode } from "@flit/auth/request";
import { tramitesAuthRoutes } from "@/lib/auth/oidc.server";

// HU #13004: /auth/frontchannel-logout de @flit/auth. Al cerrar sesión en el hub, su página de cierre lo abre en segundo
// plano y Trámites borra su sesión al instante. Con la sesión antigua no existe.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> | Response {
  if (!isOidcSessionMode()) return new Response(null, { status: 404 });
  return tramitesAuthRoutes.frontchannelLogout(request);
}
