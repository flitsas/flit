import { BrandLogo } from "@flit/brand/BrandLogo";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import { isFlitBrand } from "@flit/brand/types";
import { getSession } from "@flit/auth/server";
import { HUB_PRODUCT } from "@/lib/auth.server";
import { hubConfig } from "@/lib/config.server";
import { HubShell } from "@/components/HubShell";

// Raíz del hub. Sin sesión: portada con la marca del host (B-09). Con sesión: la barra común de la suite (B-10); el
// inicio con los productos y la entrada directa cuando solo tiene uno llegan con B-11 (opción 4).
export const dynamic = "force-dynamic";

export default async function HubHome() {
  const [brand, user] = await Promise.all([resolveBrand(), getSession(HUB_PRODUCT)]);
  const { loginUrl, tramitesUrl } = hubConfig();
  const tagline = isFlitBrand(brand)
    ? "Todos los productos FLIT con una sola cuenta."
    : `Los servicios de ${brand.platformName} con una sola cuenta.`;

  if (user) {
    // Con sesión: la barra común de la suite (B-10). El inicio con las tarjetas de productos llega con B-11.
    return (
      <HubShell
        tramitesUrl={tramitesUrl}
        user={{
          email: user.email,
          tenantName: user.tenant.name,
          roleLabel: user.roles.map((r) => r.code).join(", ") || undefined,
          permissions: user.permissions,
          roles: user.roles.map((r) => r.code),
          isSuperAdmin: user.isSuperAdmin,
        }}
      >
        <h1 className="text-2xl font-semibold text-[var(--nav-texto-fuerte)]">Hola, {user.email}</h1>
      </HubShell>
    );
  }

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
