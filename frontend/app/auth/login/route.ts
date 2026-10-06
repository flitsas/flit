import { isOidcSessionMode } from "@flit/auth/request";
import { safeReturnTo } from "@flit/auth/server";
import { tramitesAuthRoutes } from "@/lib/auth/oidc.server";

// A-10 (HU #13001): login por el hub. Tras el callback se pasa por /auth/listo, que deja en el navegador los claims
// sin firma para que menús y permisos se dibujen desde el primer render, y de ahí a la ruta pedida.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> | Response {
  if (!isOidcSessionMode()) return new Response(null, { status: 404 });
  const url = new URL(request.url);
  const to = safeReturnTo(url.searchParams.get("returnTo"));
  url.searchParams.set("returnTo", `/auth/listo?to=${encodeURIComponent(to)}`);
  return tramitesAuthRoutes.login(new Request(url, request));
}
