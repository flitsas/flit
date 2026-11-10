import { NextResponse, type NextRequest } from "next/server";
import { claimsTokenFromCookieHeader } from "@flit/auth/request";
import { COOKIES_SESION_CAJA, sesionDeCajaVencida } from "@/lib/caja/sesion";

// La caja del organismo es lo único del portal que exige login. Sin sesión del producto `caja` (o con la de más de 12 h),
// `/o/{codigo}/caja` lleva al login del hub (PKCE) y vuelve a donde iba. El resto del portal queda fuera (matcher).
export async function middleware(request: NextRequest) {
  const claims = await claimsTokenFromCookieHeader(request.headers.get("cookie"), "caja");
  if (claims && !sesionDeCajaVencida(claims)) return NextResponse.next();

  const login = new URL("/auth/login", request.url);
  login.searchParams.set("returnTo", `${request.nextUrl.pathname}${request.nextUrl.search}`);
  const respuesta = NextResponse.redirect(login);
  // Sesión vencida o ilegible (fail-closed): se borra la de la caja para no dejar viva una cookie de 14 días.
  if (claims) for (const nombre of COOKIES_SESION_CAJA) respuesta.cookies.set(nombre, "", { path: "/", maxAge: 0, httpOnly: true, sameSite: "lax" });
  return respuesta;
}

export const config = {
  runtime: "nodejs",
  matcher: ["/o/:codigo/caja", "/o/:codigo/caja/:path*"],
};
