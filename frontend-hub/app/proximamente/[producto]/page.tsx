import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, Check, Mail } from "lucide-react";
import { getSession } from "@flit/auth/server";
import { HubShell } from "@/components/HubShell";
import { ProductIcon } from "@/components/ui/ProductIcon";
import { StatusChip } from "@/components/ui/StatusChip";
import { HUB_PRODUCT } from "@/lib/auth.server";
import { hubConfig } from "@/lib/config.server";
import { toHubUser } from "@/lib/hub-user";
import { productInfo } from "@/lib/products";
import { CONTACT } from "@/lib/site";

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
      <Link href="/?inicio=1" className="inline-flex items-center gap-1 text-sm font-medium text-[#59677d] transition hover:text-flit-brand dark:text-white/60">
        <ArrowLeft className="h-4 w-4" aria-hidden="true" /> Volver al inicio
      </Link>
      <div className="mt-6 rounded-3xl border border-[var(--color-flit-gray)] bg-white p-8 shadow-[var(--nav-sombra-dock)] md:p-10 dark:border-white/10 dark:bg-[#0B0F14]">
        <div className="flex items-center gap-4">
          <ProductIcon icon={product.icon} size="lg" />
          <div>
            <h1 className="text-3xl font-semibold">{product.name}</h1>
            <div className="mt-1">
              <StatusChip status="soon" />
            </div>
          </div>
        </div>
        <p className="mt-6 text-lg font-medium leading-relaxed">{product.tagline}</p>
        <p className="mt-3 leading-relaxed text-[#59677d] dark:text-white/65">{product.description}</p>
        <p className="mt-8 text-xs font-semibold uppercase tracking-[0.16em] text-flit-brand">Lo que traerá</p>
        <ul className="mt-4 grid gap-3 sm:grid-cols-2">
          {product.features.map((f) => (
            <li key={f} className="flex gap-3 text-sm leading-relaxed">
              <span className="mt-0.5 grid h-5 w-5 shrink-0 place-items-center rounded-full bg-flit-tech/15 text-[#00a8a3] dark:text-flit-tech">
                <Check className="h-3 w-3" aria-hidden="true" />
              </span>
              {f}
            </li>
          ))}
        </ul>
        <div className="mt-10 flex flex-wrap items-center gap-3 border-t border-[var(--color-flit-gray)] pt-6 dark:border-white/10">
          <p className="mr-auto text-sm text-[#59677d] dark:text-white/65">Cuando esté listo aparecerá aquí mismo, con tu misma cuenta.</p>
          <a
            href={`mailto:${CONTACT.email}?subject=${encodeURIComponent(`Quiero saber cuándo llega ${product.name}`)}`}
            className="inline-flex items-center gap-2 rounded-full bg-flit-brand px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90"
          >
            <Mail className="h-4 w-4" aria-hidden="true" /> Avísame cuando llegue
          </a>
        </div>
      </div>
    </section>
  );

  if (!user) return <main className="min-h-full bg-[var(--color-flit-bg)] px-4 text-flit-primary dark:bg-[#05060A] dark:text-white">{content}</main>;

  return (
    <HubShell tramitesUrl={hubConfig().tramitesUrl} user={toHubUser(user)}>
      {content}
    </HubShell>
  );
}
