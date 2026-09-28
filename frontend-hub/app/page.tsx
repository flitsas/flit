import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { ArrowUpRight } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import { isFlitBrand } from "@flit/brand/types";
import { getSession } from "@flit/auth/server";
import { HubHome } from "@/components/HubHome";
import { HubShell } from "@/components/HubShell";
import { HUB_PRODUCT } from "@/lib/auth.server";
import { hubConfig } from "@/lib/config.server";
import { decideHome } from "@/lib/home";
import { fetchMyAppsOnServer } from "@/lib/me-apps.server";

// Raíz del hub, opción 4 «Hub con entrada directa» (B-11, provisional mientras el CTO y el líder deciden; plan maestro
// §4.6). Sin sesión: portada con la marca del host. Con un solo producto: directo a él. Con dos o más: el inicio.
export const dynamic = "force-dynamic";

/** Productos que se presentan en la portada de FLIT (no en la de una red de Marca Blanca). */
const FLIT_PRODUCTS = [
  { name: "Trámites", description: "Matrículas, traspasos y demás trámites vehiculares." },
  { name: "Comparendos", description: "Gestión de comparendos de tránsito." },
  { name: "Diagnóstico", description: "Diagnóstico de flotas y vehículos." },
];

export default async function HubRoot({ searchParams }: { searchParams: Promise<{ inicio?: string }> }) {
  const [brand, user, { inicio }] = await Promise.all([resolveBrand(), getSession(HUB_PRODUCT), searchParams]);
  const config = hubConfig();

  if (user) {
    const apps = await fetchMyAppsOnServer(config, (await headers()).get("host"));
    if (apps) {
      const decision = decideHome(apps, inicio === "1");
      if (decision.kind === "direct") redirect(decision.url);
    }

    const hubUser = {
      email: user.email,
      tenantName: user.tenant.name,
      roleLabel: user.roles.map((r) => r.code).join(", ") || undefined,
      permissions: user.permissions,
      roles: user.roles.map((r) => r.code),
      isSuperAdmin: user.isSuperAdmin,
    };
    return (
      <HubShell tramitesUrl={config.tramitesUrl} user={hubUser}>
        <HubHome user={hubUser} tramitesUrl={config.tramitesUrl} apps={apps} />
      </HubShell>
    );
  }

  const flit = isFlitBrand(brand);
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-8 bg-flit-primary px-6 py-16 text-center">
      <BrandLogo variant="white" className="h-14 w-auto" />
      <p className="max-w-md text-lg text-[color:var(--brand-on-primary,#ffffff)]">
        {flit ? "Todos los productos FLIT con una sola cuenta." : `Los servicios de ${brand.platformName} con una sola cuenta.`}
      </p>
      <a
        href={config.loginUrl}
        className="rounded-full bg-flit-brand px-8 py-3 font-semibold text-white shadow-sm transition hover:opacity-90 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
      >
        Iniciar sesión
      </a>
      {/* En Marca Blanca solo la marca de la red: los productos de FLIT no se promocionan en su dominio. */}
      {flit && (
        <ul className="mt-4 grid w-full max-w-3xl gap-3 text-left sm:grid-cols-3">
          {FLIT_PRODUCTS.map((product) => (
            <li key={product.name} className="rounded-2xl border border-white/15 bg-white/5 p-4">
              <p className="font-semibold text-white">{product.name}</p>
              <p className="mt-1 text-sm text-white/70">{product.description}</p>
              <a
                href="https://flitsas.com"
                target="_blank"
                rel="noopener"
                className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-white underline-offset-4 hover:underline"
              >
                Conocer más <ArrowUpRight className="h-4 w-4" aria-hidden="true" />
              </a>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
