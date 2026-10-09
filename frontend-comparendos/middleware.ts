import { NextResponse, type NextRequest } from "next/server";
import { claimsTokenFromCookieHeader } from "@flit/auth/request";

// Sin sesión de Comparendos, toda página lleva al login del hub (PKCE) y vuelve a donde iba. La API, el login, la
// salud, el portal público del conductor y los recursos estáticos quedan fuera (matcher).
export async function middleware(request: NextRequest) {
  if (await claimsTokenFromCookieHeader(request.headers.get("cookie"), "comparendos")) {
    return NextResponse.next();
  }

  const returnTo = `${request.nextUrl.pathname}${request.nextUrl.search}`;
  const login = new URL("/auth/login", request.url);
  login.searchParams.set("returnTo", returnTo);
  return NextResponse.redirect(login);
}

export const config = {
  runtime: "nodejs",
  matcher: ["/((?!auth/|api/|healthz|403|portal/|_next/|assets/|icon.svg|favicon).*)"],
};
