import { NextResponse, type NextRequest } from "next/server";
import { claimsTokenFromCookieHeader, isOidcSessionMode } from "@flit/auth/request";
import { evaluateAdminAccess, evaluateEmpresaAccess, evaluateLoginAccess } from "@/lib/auth/guard";
import { TOKEN_COOKIE } from "@/lib/auth/jwt";

// Gates en el borde, antes de renderizar:
// - /login → si ya hay sesión activa, redirige al dashboard.
// - /admin/* → gate SuperAdmin (HU #10194, AC6). Solo SuperAdmin accede.
// - /empresa/* → gate AdminCompany. AdminCompany y SuperAdmin acceden.
//
// A-10 (HU #13001) — con FLIT_SESSION_MODE=oidc la sesión es la de @flit/auth: /api/v1/* va al BFF (Bearer desde el
// servidor), /login lleva al login del hub y los gates leen los claims de la sesión cifrada. Con la sesión antigua
// (por defecto) todo sigue exactamente igual.
export async function middleware(request: NextRequest) {
  const { pathname, search } = request.nextUrl;

  if (isOidcSessionMode()) {
    if (pathname.startsWith("/api/v1/")) {
      return NextResponse.rewrite(new URL(`/bff${pathname}${search}`, request.url));
    }
    if (pathname === "/login") {
      const returnTo = request.nextUrl.searchParams.get("returnUrl") ?? "/";
      return NextResponse.redirect(new URL(`/auth/login?returnTo=${encodeURIComponent(returnTo)}`, request.url));
    }
    return gate(pathname, await claimsTokenFromCookieHeader(request.headers.get("cookie"), "tramites"), request);
  }

  if (pathname.startsWith("/api/v1/")) {
    return NextResponse.next();
  }

  const token = request.cookies.get(TOKEN_COOKIE)?.value;
  if (pathname === "/login") {
    const { redirect, redirectTo } = evaluateLoginAccess(token);
    return redirect
      ? NextResponse.redirect(new URL(redirectTo ?? "/", request.url))
      : NextResponse.next();
  }

  return gate(pathname, token, request);
}

function gate(pathname: string, token: string | null | undefined, request: NextRequest) {
  if (pathname.startsWith("/empresa")) {
    const { allowed, redirectTo } = evaluateEmpresaAccess(token);
    return allowed
      ? NextResponse.next()
      : NextResponse.redirect(new URL(redirectTo ?? "/403", request.url));
  }

  if (pathname.startsWith("/admin")) {
    const { allowed, redirectTo } = evaluateAdminAccess(token, pathname);
    return allowed
      ? NextResponse.next()
      : NextResponse.redirect(new URL(redirectTo ?? "/403", request.url));
  }

  return NextResponse.next();
}

export const config = {
  // Node y no Edge: FLIT_SESSION_MODE y FLIT_SESSION_SECRET se leen en runtime (una imagen para los tres ambientes).
  runtime: "nodejs",
  matcher: ["/admin/:path*", "/empresa/:path*", "/login", "/api/v1/:path*"],
};
