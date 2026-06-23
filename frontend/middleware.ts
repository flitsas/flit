import { NextResponse, type NextRequest } from "next/server";
import { evaluateAdminAccess, evaluateLoginAccess } from "@/lib/auth/guard";
import { TOKEN_COOKIE } from "@/lib/auth/jwt";

// Gates en el borde, antes de renderizar:
// - /login → si ya hay sesión activa, redirige al dashboard (no se muestra el login).
// - /admin/* → gate SuperAdmin (HU #10194, AC6). La API valida la firma del JWT;
//   aquí solo se leen claims para no exponer vistas a quien no corresponde.
export function middleware(request: NextRequest) {
  const token = request.cookies.get(TOKEN_COOKIE)?.value;

  if (request.nextUrl.pathname === "/login") {
    const { redirect, redirectTo } = evaluateLoginAccess(token);
    return redirect
      ? NextResponse.redirect(new URL(redirectTo ?? "/", request.url))
      : NextResponse.next();
  }

  const { allowed, redirectTo } = evaluateAdminAccess(token, request.nextUrl.pathname);
  return allowed
    ? NextResponse.next()
    : NextResponse.redirect(new URL(redirectTo ?? "/403", request.url));
}

export const config = {
  matcher: ["/admin/:path*", "/login"],
};
