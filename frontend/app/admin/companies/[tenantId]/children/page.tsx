"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { NetworkChildrenPanel } from "@/components/admin/companies/NetworkChildrenPanel";
import { fetchCompany } from "@/lib/api/admin-companies";
import { isHeadTenantType } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";

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
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden />
        Volver a configuración
      </button>

      <ModuleTitle title="Panel de red" subtitle="Clientes hijos de tu cabeza de grupo." />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
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
    </main>
  );
}
