import { BrandLogo } from "@flit/brand/BrandLogo";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import { isFlitBrand } from "@flit/brand/types";
import { hubConfig } from "@/lib/config.server";

// Portada del hub sin sesión (B-09). El inicio con los productos del usuario y la entrada directa cuando solo
// tiene uno llegan con B-11 (opción 4, «Hub con entrada directa»); el login propio, con A-06.
export const dynamic = "force-dynamic";

export default async function HubHome() {
  const brand = await resolveBrand();
  const { loginUrl } = hubConfig();
  const tagline = isFlitBrand(brand)
    ? "Todos los productos FLIT con una sola cuenta."
    : `Los servicios de ${brand.platformName} con una sola cuenta.`;

  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-8 bg-flit-primary px-6 py-16 text-center">
      <BrandLogo variant="white" className="h-14 w-auto" />
      <p className="max-w-md text-lg text-[color:var(--brand-on-primary,#ffffff)]">{tagline}</p>
      <a
        href={loginUrl}
        className="rounded-full bg-flit-brand px-8 py-3 font-semibold text-white shadow-sm transition hover:opacity-90 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
      >
        Iniciar sesión
      </a>
    </main>
  );
}
