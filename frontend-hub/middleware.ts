import { NextResponse, type NextRequest } from "next/server";
import { legacyTramitesRedirect } from "@/lib/legacy-tramites";
import { resolveTramitesUrl } from "@/lib/tramites-url";

// A-11 (HU #13002): con FLIT_TRAMITES_HOST_ENABLED=true el hub ocupa la raíz del ambiente y las rutas de Trámites se
// redirigen con 308 a TRAMITES_URL (plan maestro §4.3). Se lee en runtime: se enciende y apaga sin reconstruir.
// En el dominio de una red de Marca Blanca van al Trámites de esa red: en el de FLIT su usuario no puede entrar.
export async function middleware(request: NextRequest) {
  const tramitesUrl = process.env.TRAMITES_URL;
  if (process.env.FLIT_TRAMITES_HOST_ENABLED !== "true" || !tramitesUrl) {
    return NextResponse.next();
  }

  // Primero el reparto, que es puro: lo que atiende el hub (todas sus rutas y estáticos) no consulta la red.
  if (!legacyTramitesRedirect(request.nextUrl, tramitesUrl)) return NextResponse.next();

  const target = await resolveTramitesUrl(request.headers.get("host"), tramitesUrl);
  return NextResponse.redirect(legacyTramitesRedirect(request.nextUrl, target)!, 308);
}

export const config = {
  runtime: "nodejs",
  matcher: ["/((?!_next/static|_next/image).*)"],
};
