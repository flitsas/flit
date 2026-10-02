import { createElement } from "react";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getSession } from "@flit/auth/server";
import { appIcon } from "@flit/shell/apps";
import { HubShell } from "@/components/HubShell";
import { HUB_PRODUCT } from "@/lib/auth.server";
import { comingSoonProduct } from "@/lib/coming-soon";
import { hubConfig } from "@/lib/config.server";
import { toHubUser } from "@/lib/hub-user";

// Apertura de un producto que todavía no está desplegado (Comparendos, Diagnóstico): la tarjeta del inicio y el menú de
// productos llegan aquí en vez de a un host que no responde. La API decide qué productos vienen aquí
// (Suite:Hosts:ComingSoon); esta página solo los presenta.
export const dynamic = "force-dynamic";

export default async function ProximamentePage({ params }: { params: Promise<{ producto: string }> }) {
  const [{ producto }, user] = await Promise.all([params, getSession(HUB_PRODUCT)]);
  const product = comingSoonProduct(producto);
  if (!product) notFound();

  const content = (
    <section className="mx-auto flex max-w-xl flex-col items-center gap-5 py-16 text-center">
      <span className="flex h-16 w-16 items-center justify-center rounded-2xl bg-white text-flit-brand shadow-[var(--nav-sombra-dock)]">
        {createElement(appIcon(product.icon), { className: "h-8 w-8", "aria-hidden": true })}
      </span>
      <p className="rounded-full bg-flit-brand/10 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-flit-brand">Próximamente</p>
      <h1 className="text-3xl font-semibold text-[var(--nav-texto-fuerte)]">{product.name}</h1>
      <p className="text-base text-[var(--nav-texto)]">{product.description}</p>
      <p className="text-sm text-[var(--nav-texto)]">
        Estamos preparando este producto. Cuando esté listo aparecerá aquí mismo, con tu misma cuenta.
      </p>
      <Link
        href="/?inicio=1"
        className="mt-2 rounded-full bg-flit-brand px-6 py-2.5 text-sm font-semibold text-white transition hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]"
      >
        Volver al inicio
      </Link>
    </section>
  );

  if (!user) return <main className="min-h-full bg-[var(--nav-app-bg)] px-4">{content}</main>;

  const hubUser = toHubUser(user);
  return (
    <HubShell tramitesUrl={hubConfig().tramitesUrl} user={hubUser}>
      {content}
    </HubShell>
  );
}
