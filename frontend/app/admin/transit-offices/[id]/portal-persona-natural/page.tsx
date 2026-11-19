"use client";

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useMemo } from "react";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import {
  OtCajaTab,
  rolDeCaja,
  subDesdeQuery,
  subHaciaQuery,
  type CajaSubId,
} from "@/components/admin/transit-offices/portal-pn/OtCajaTab";
import { OtCobroTab } from "@/components/admin/transit-offices/portal-pn/OtCobroTab";
import { OtPortalDomainSection } from "@/components/admin/transit-offices/portal-pn/OtPortalDomainSection";
import { OtReportesSection } from "@/components/admin/transit-offices/portal-pn/OtReportesSection";
import { PortalPnAccess } from "@/components/admin/transit-offices/portal-pn/PortalPnAccess";
import { PortalPnTabs, type PortalPnTabId } from "@/components/admin/transit-offices/portal-pn/PortalPnTabs";

const PESTANAS: readonly PortalPnTabId[] = ["reportes", "cobro", "caja", "diseno"];

/**
 * Portal Persona Natural del organismo (Épica #14297, Feature #14374): Reportes, Cobro, Caja y Diseño.
 * SuperAdmin y administrador del organismo, con el mismo componente anclado al organismo de la ruta. La persona de caja
 * (solo `ot_caja`) entra a la misma página de SU organismo y ve únicamente la pestaña Caja (Cobrar y Cobros del día).
 * La pestaña y la subpestaña viajan en la URL: `?tab=caja&sub=cobrar`.
 */
export default function OtPortalPersonaNaturalPage() {
  return (
    <ToastProvider>
      <Suspense fallback={null}>
        <OtPortalPersonaNaturalPageInner />
      </Suspense>
    </ToastProvider>
  );
}

function OtPortalPersonaNaturalPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="portal-persona-natural"
      moduleTitle="Administración OT — Portal Persona Natural"
      surface="panel"
    >
      <PortalPnAccess>
        <PortalPnContenido transitOfficeId={params.id} />
      </PortalPnAccess>
    </OtHubLayout>
  );
}

function PortalPnContenido({ transitOfficeId }: { transitOfficeId: string }) {
  const search = useSearchParams();
  const router = useRouter();
  // Solo se monta tras PortalPnAccess (efecto de cliente): el token ya se puede leer.
  const rol = useMemo(() => rolDeCaja(), []);
  const soloCaja = rol === "cajero";

  // La sección sale de la URL (`?tab=`), que cambia el submenú «Portal Persona Natural» del menú de abajo. La persona de caja
  // solo tiene Caja: cualquier otro valor la lleva a ella. Sin valor, el administrador abre Reportes.
  const tabParam = search.get("tab") ?? "";
  const pedida = (PESTANAS as readonly string[]).includes(tabParam) ? (tabParam as PortalPnTabId) : "reportes";
  // Admin FLIT y admin del organismo: las cuatro pestañas, la activa sale de `?tab=`. La persona de caja solo tiene Caja.
  const activa: PortalPnTabId = soloCaja ? "caja" : pedida;
  const initialSub = subDesdeQuery(search.get("sub"), rol);

  /** Pone la pestaña en la URL (`?tab=`) sin apilar historial; si no es Caja, quita la subpestaña. */
  const tabAUrl = useCallback(
    (id: PortalPnTabId) => {
      const url = new URL(window.location.href);
      url.searchParams.set("tab", id);
      if (id !== "caja") url.searchParams.delete("sub");
      router.replace(`${url.pathname}${url.search}`, { scroll: false });
    },
    [router],
  );

  /** Refleja la subpestaña de Caja en la URL sin recargar ni apilar historial. */
  const subAUrl = useCallback((id: CajaSubId) => {
    const url = new URL(window.location.href);
    url.searchParams.set("tab", "caja");
    url.searchParams.set("sub", subHaciaQuery(id));
    window.history.replaceState(window.history.state, "", url);
  }, []);

  return (
    <>
      <nav aria-label="Miga de pan" className="mb-3 text-xs" style={{ color: "var(--color-flit-primary)" }}>
        <Link href="/" className="hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
          Inicio
        </Link>{" "}
        &gt; <span aria-current="page" className="font-semibold">Portal Persona Natural</span>
      </nav>
      <PortalPnTabs
        transitOfficeId={transitOfficeId}
        activeTab={activa}
        onTabChange={tabAUrl}
        reportes={soloCaja ? undefined : <OtReportesSection transitOfficeId={transitOfficeId} />}
        cobro={soloCaja ? undefined : <OtCobroTab transitOfficeId={transitOfficeId} />}
        caja={<OtCajaTab transitOfficeId={transitOfficeId} rol={rol} initialSub={initialSub} onSubChange={subAUrl} />}
        domain={soloCaja ? undefined : <OtPortalDomainSection transitOfficeId={transitOfficeId} />}
      />
    </>
  );
}
