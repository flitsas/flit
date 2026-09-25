"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { NetworkChildrenPanel } from "@/components/admin/companies/NetworkChildrenPanel";
import { BrandingConfigurator } from "@/components/admin/branding/BrandingConfigurator";
import { DomainStatusPanel } from "@/components/admin/domain/DomainStatusPanel";
import { fetchCompany } from "@/lib/api/admin-companies";
import { isHeadTenantType } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";
import { ADMIN_BACK_LINK_CLS, ADMIN_CONTENT_SURFACE_CLS } from "@/components/admin/admin-ui-styles";

/** HU #12356 — panel de red (solo cabeza de grupo). */
export default function NetworkChildrenPage() {
  const router = useRouter();
  const params = useParams<{ tenantId: string }>();
  const headTenantId = params.tenantId;
  const { isSuperAdmin, isGroupParent, isAdminCompany, tenantId: callerTenantId } = usePermissions();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [head, setHead] = useState<CompanyListItem | null>(null);

  useEffect(() => {
    if (isAdminCompany && !isSuperAdmin) {
      if (!isGroupParent || callerTenantId !== headTenantId) {
        router.replace(callerTenantId ? `/admin/companies/${callerTenantId}` : "/403");
      }
    }
  }, [isAdminCompany, isSuperAdmin, isGroupParent, callerTenantId, headTenantId, router]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setStatus("loading");
    void fetchCompany(headTenantId, controller.signal)
      .then((company) => {
        if (controller.signal.aborted) return;
        if (!company || (!isHeadTenantType(company.tenantType) && !company.isGroupParent)) {
          router.replace(`/admin/companies/${headTenantId}`);
          return;
        }
        setHead(company);
        setStatus("ready");
      })
      .catch(() => {
        if (!controller.signal.aborted) setStatus("error");
      });
    return () => controller.abort();
  }, [headTenantId, router]);

  return (
    <main className="app-bg flex min-h-screen flex-col gap-4 px-6 py-6">
      <button
        type="button"
        onClick={() => router.push(`/admin/companies/${headTenantId}`)}
        className={ADMIN_BACK_LINK_CLS}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a configuración
      </button>

      <ModuleTitle title="Panel de red" subtitle="Clientes hijos de tu cabeza de grupo." />

      <div className={ADMIN_CONTENT_SURFACE_CLS}>
        <UiStateBoundary
          status={status}
          onRetry={() => window.location.reload()}
          errorMessage="No se pudo cargar la cabeza de grupo."
        >
          {head && (
            <NetworkChildrenPanel headTenantId={headTenantId} headTenantType={head.tenantType} />
          )}
        </UiStateBoundary>
      </div>

      {/* HU #12414 AC1/AC6 — la cabeza de red Marca Blanca autogestiona su identidad de marca
          desde su propio panel; el SuperAdmin la gestiona embebida en la ficha de compañía
          (app/admin/companies/[tenantId]/page.tsx), no aquí. */}
      {head && !isSuperAdmin && head.tenantType === "MARCA_BLANCA" && (
        <BrandingConfigurator source="company" />
      )}

      {/* HU #12427 AC1/AC2 — la cabeza ve el estado de su dominio y puede comprobarlo; registrar,
          cambiar o retirar es exclusivo del SuperAdmin (ficha de compañía, AC3). */}
      {head && !isSuperAdmin && head.tenantType === "MARCA_BLANCA" && (
        <section
          className="rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
          aria-labelledby="domain-company-title"
        >
          <div className="mb-3">
            <h2 id="domain-company-title" className="text-sm font-bold" style={{ color: "#162744" }}>
              Dominio de la red
            </h2>
            <p className="text-[11px] opacity-60">
              Dominio propio para el acceso y las comunicaciones de tu red.
            </p>
          </div>
          <DomainStatusPanel mode="company" />
        </section>
      )}
    </main>
  );
}
