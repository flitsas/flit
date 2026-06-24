import { NextResponse, type NextRequest } from "next/server";
import { evaluateAdminAccess, evaluateEmpresaAccess, evaluateLoginAccess } from "@/lib/auth/guard";
import { TOKEN_COOKIE } from "@/lib/auth/jwt";

// Gates en el borde, antes de renderizar:
// - /login → si ya hay sesión activa, redirige al dashboard.
// - /admin/* → gate SuperAdmin (HU #10194, AC6). Solo SuperAdmin accede.
// - /empresa/* → gate AdminCompany. AdminCompany y SuperAdmin acceden.
export function middleware(request: NextRequest) {
  const token = request.cookies.get(TOKEN_COOKIE)?.value;
  const { pathname } = request.nextUrl;

  if (pathname === "/login") {
    const { redirect, redirectTo } = evaluateLoginAccess(token);
    return redirect
      ? NextResponse.redirect(new URL(redirectTo ?? "/", request.url))
      : NextResponse.next();
  }

  if (pathname.startsWith("/empresa")) {
    const { allowed, redirectTo } = evaluateEmpresaAccess(token);
    return allowed
      ? NextResponse.next()
      : NextResponse.redirect(new URL(redirectTo ?? "/403", request.url));
  }

  // /admin/* — solo SuperAdmin
  const { allowed, redirectTo } = evaluateAdminAccess(token);
  return allowed
    ? NextResponse.next()
    : NextResponse.redirect(new URL(redirectTo ?? "/403", request.url));
}

export const config = {
  matcher: ["/admin/:path*", "/empresa/:path*", "/login"],
};
