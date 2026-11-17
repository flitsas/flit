import { headers } from "next/headers";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { getSession } from "@flit/auth/server";
import { HubShell } from "@/components/HubShell";
import { ProductArt } from "@/components/ui/ProductIcon";
import { StatusChip } from "@/components/ui/StatusChip";
import { HUB_PRODUCT } from "@/lib/auth.server";
import { hubConfig } from "@/lib/config.server";
import { toHubUser } from "@/lib/hub-user";
import { productInfo } from "@/lib/products";
import { CONTACT } from "@/lib/site";
import { resolveTramitesUrl } from "@/lib/tramites-url";
import { buttonClass } from "@flit/ui/Button";

// Apertura de un producto que todavía no está desplegado (Comparendos, Diagnóstico): la tarjeta del inicio y el menú de
// productos llegan aquí en vez de a un host que no responde. La API decide qué productos vienen aquí
// (Suite:Hosts:ComingSoon); esta página los presenta con el catálogo de lib/products.ts.
export const dynamic = "force-dynamic";

export default async function ProximamentePage({ params }: { params: Promise<{ producto: string }> }) {
  const [{ producto }, user] = await Promise.all([params, getSession(HUB_PRODUCT)]);
  const product = productInfo(producto);
  if (!product || product.status !== "soon") notFound();

  const content = (
    <section className="mx-auto max-w-3xl py-10">
      <Link href="/?inicio=1" className="inline-flex items-center gap-1 text-sm font-medium text-muted-foreground transition hover:text-[color:var(--di-action)]">
        <Icono nombre="izquierda" className="h-4 w-4" /> Volver al inicio
      </Link>
      <div className="mt-6 rounded-[20px] border border-border bg-card p-8 shadow-[var(--shadow-card)] md:p-10">
        <div className="flex items-center gap-4">
          <ProductArt code={product.code} icon={product.icon} size="lg" />
          <div>
            <h1 className="text-3xl font-bold text-[color:var(--di-heading)]">{product.name}</h1>
            <div className="mt-1">
              <StatusChip status="soon" />
            </div>
          </div>
        </div>
        <p className="mt-6 text-lg font-medium leading-relaxed">{product.tagline}</p>
        <p className="mt-3 leading-relaxed text-muted-foreground">{product.description}</p>
        <p className="mt-8 text-xs font-semibold uppercase tracking-[0.16em] text-[color:var(--di-action)]">Lo que traerá</p>
        <ul className="mt-4 grid gap-3 sm:grid-cols-2">
          {product.features.map((f) => (
            <li key={f} className="flex gap-3 text-sm leading-relaxed">
              <span className="mt-0.5 grid h-5 w-5 shrink-0 place-items-center rounded-full bg-[var(--badge-success-bg)] text-[var(--flit-success-ink)]">
                <Icono nombre="check" className="h-3 w-3" />
              </span>
              {f}
            </li>
          ))}
        </ul>
        <div className="mt-10 flex flex-wrap items-center gap-3 border-t border-border pt-6">
          <p className="mr-auto text-sm text-muted-foreground">Cuando esté listo aparecerá aquí mismo, con tu misma cuenta.</p>
          <a
            href={`mailto:${CONTACT.email}?subject=${encodeURIComponent(`Quiero saber cuándo llega ${product.name}`)}`}
            className={buttonClass()}
          >
            <Icono nombre="correo" className="h-4 w-4" /> Avísame cuando llegue
          </a>
        </div>
      </div>
    </section>
  );

  if (!user) return <main className="min-h-full bg-flit-bg px-4 text-flit-primary dark:bg-background">{content}</main>;

  return (
    <HubShell tramitesUrl={await resolveTramitesUrl((await headers()).get("host"), hubConfig().tramitesUrl)} user={toHubUser(user)}>
      {content}
    </HubShell>
  );
}
