import { NextResponse, type NextRequest } from "next/server";
import { evaluateAdminAccess } from "@/lib/auth/guard";
import { TOKEN_COOKIE } from "@/lib/auth/jwt";

// Gate SuperAdmin para /admin/* (HU #10194, AC6). Se ejecuta en el borde antes de
// renderizar cualquier dato. La API valida la firma del JWT; aquí solo se lee el
// claim de rol para no exponer la consola a usuarios no autorizados.
export function middleware(request: NextRequest) {
  const token = request.cookies.get(TOKEN_COOKIE)?.value;
  const { allowed, redirectTo } = evaluateAdminAccess(token);

  if (allowed) {
    return NextResponse.next();
  }

  return NextResponse.redirect(new URL(redirectTo ?? "/403", request.url));
}

export const config = {
  matcher: ["/admin/:path*"],
};
