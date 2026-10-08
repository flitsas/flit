import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import { isFlitBrand } from "@flit/brand/types";
import { getSession } from "@flit/auth/server";
import { HubHome } from "@/components/HubHome";
import { HubShell } from "@/components/HubShell";
import { Landing } from "@/components/landing/Landing";
import { HUB_PRODUCT } from "@/lib/auth.server";
import { hubConfig } from "@/lib/config.server";
import { toHubUser } from "@/lib/hub-user";
import { decideHome } from "@/lib/home";
import { fetchMyAppsOnServer } from "@/lib/me-apps.server";

// Raíz del hub, opción 4 «Hub con entrada directa» (B-11; plan maestro §4.6). Sin sesión: el landing de FLIT (o la
// portada de la red, en Marca Blanca). Con un solo producto: directo a él. Con dos o más: el inicio.
//
// El hub tiene su propia cookie de sesión, aparte de la del servidor de login. Si alguien ya inició sesión (por
// ejemplo, al entrar a Trámites), el hub intenta una vez entrar en silencio (`prompt=none`) antes de mostrar la
// portada; si no hay sesión, vuelve con `sso=0` y muestra la portada sin volver a intentar.
export const dynamic = "force-dynamic";

export default async function HubRoot({ searchParams }: { searchParams: Promise<{ inicio?: string; sso?: string }> }) {
  const [brand, user, { inicio, sso }] = await Promise.all([resolveBrand(), getSession(HUB_PRODUCT), searchParams]);
  const host = (await headers()).get("host");
  const config = hubConfig(process.env, host);

  const silentLogin = `/auth/login?prompt=none&returnTo=${encodeURIComponent(inicio === "1" ? "/?inicio=1" : "/")}`;
  if (!user && sso !== "0") redirect(silentLogin);

  if (user) {
    const fetched = await fetchMyAppsOnServer(config, host);
    // La API rechazó el token: la sesión se cerró en otro producto. El intento silencioso la renueva si el servidor de
    // login sigue con sesión o, si no, la borra y vuelve a la portada (sin bucle: sin cookie ya no hay `user`).
    if (fetched === "unauthorized") redirect(silentLogin);
    const apps = fetched;
    if (apps) {
      const decision = decideHome(apps, inicio === "1");
      if (decision.kind === "direct") redirect(decision.url);
    }

    const hubUser = toHubUser(user);
    return (
      <HubShell tramitesUrl={config.tramitesUrl} user={hubUser}>
        <HubHome user={hubUser} tramitesUrl={config.tramitesUrl} apps={apps} />
      </HubShell>
    );
  }

  // En FLIT, el landing con los productos; en el dominio de una red de Marca Blanca solo su marca y el acceso: los
  // productos de FLIT no se promocionan allí.
  if (isFlitBrand(brand)) return <Landing loginUrl={config.loginUrl} />;

  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-8 bg-flit-primary px-6 py-16 text-center">
      <BrandLogo variant="white" className="h-14 w-auto" />
      <p className="max-w-md text-lg text-[color:var(--brand-on-primary,#ffffff)]">{`Los servicios de ${brand.platformName} con una sola cuenta.`}</p>
      <a
        href={config.loginUrl}
        className="rounded-full bg-flit-brand px-8 py-3 font-semibold text-white shadow-sm transition hover:opacity-90 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
      >
        Iniciar sesión
      </a>
    </main>
  );
}
