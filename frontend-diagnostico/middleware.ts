import { NextResponse, type NextRequest } from "next/server";
import { claimsTokenFromCookieHeader } from "@flit/auth/request";

// Sin sesión de Diagnóstico, toda página lleva al login del hub (OIDC con PKCE, client_id = aud = diagnostico) y vuelve
// a donde iba. La API, el login, la salud, la página 403 y los recursos estáticos quedan fuera (matcher): así una
// suscripción suspendida o un usuario sin permisos ven la 403 sin volver a pasar por el login (sin bucles).
export async function middleware(request: NextRequest) {
  if (await claimsTokenFromCookieHeader(request.headers.get("cookie"), "diagnostico")) {
    return NextResponse.next();
  }

  const returnTo = `${request.nextUrl.pathname}${request.nextUrl.search}`;
  const login = new URL("/auth/login", request.url);
  login.searchParams.set("returnTo", returnTo);
  return NextResponse.redirect(login);
}

export const config = {
  runtime: "nodejs",
  matcher: ["/((?!auth/|api/|healthz|403|_next/|assets/|icon.svg|favicon).*)"],
};
