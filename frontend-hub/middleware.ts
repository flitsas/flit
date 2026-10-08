import { NextResponse, type NextRequest } from "next/server";
import { legacyTramitesRedirect } from "@/lib/legacy-tramites";
import { tramitesUrlFor } from "@/lib/tramites-url";

// A-11 (HU #13002): con FLIT_TRAMITES_HOST_ENABLED=true el hub ocupa la raíz del ambiente y las rutas de Trámites se
// redirigen con 308 a TRAMITES_URL (plan maestro §4.3). Se lee en runtime: se enciende y apaga sin reconstruir.
// Con raíces alternativas (TRAMITES_URLS), a la Trámites de la raíz por la que se entró: app.flitsas.com → tramites.flitsas.com.
export function middleware(request: NextRequest) {
  if (process.env.FLIT_TRAMITES_HOST_ENABLED !== "true" || !process.env.TRAMITES_URL) {
    return NextResponse.next();
  }

  const tramitesUrl = tramitesUrlFor(request.headers.get("x-forwarded-host") ?? request.headers.get("host"));
  const destination = legacyTramitesRedirect(request.nextUrl, tramitesUrl);
  return destination ? NextResponse.redirect(destination, 308) : NextResponse.next();
}

export const config = {
  runtime: "nodejs",
  matcher: ["/((?!_next/static|_next/image).*)"],
};
