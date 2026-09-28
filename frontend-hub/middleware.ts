import { NextResponse, type NextRequest } from "next/server";
import { legacyTramitesRedirect } from "@/lib/legacy-tramites";

// A-11 (HU #13002): con FLIT_TRAMITES_HOST_ENABLED=true el hub ocupa la raíz del ambiente y las rutas de Trámites se
// redirigen con 308 a TRAMITES_URL (plan maestro §4.3). Se lee en runtime: se enciende y apaga sin reconstruir.
export function middleware(request: NextRequest) {
  const tramitesUrl = process.env.TRAMITES_URL;
  if (process.env.FLIT_TRAMITES_HOST_ENABLED !== "true" || !tramitesUrl) {
    return NextResponse.next();
  }

  const destination = legacyTramitesRedirect(request.nextUrl, tramitesUrl);
  return destination ? NextResponse.redirect(destination, 308) : NextResponse.next();
}

export const config = {
  runtime: "nodejs",
  matcher: ["/((?!_next/static|_next/image).*)"],
};
