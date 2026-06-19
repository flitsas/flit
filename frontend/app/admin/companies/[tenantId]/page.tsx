"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { CompanyConfigTabs } from "@/components/admin/companies/CompanyConfigTabs";
import { WhitelistPanel } from "@/components/admin/companies/panels/WhitelistPanel";
import { OTMatrixPanel } from "@/components/admin/companies/panels/OTMatrixPanel";
import { AuditLogPanel } from "@/components/admin/companies/panels/AuditLogPanel";
import { fetchTenantSettings, updateTenantSettings } from "@/lib/api/admin-companies";
import type { TenantSettings, TenantSettingsUpdate } from "@/lib/api/types";

// Consola admin — detalle de compañía (HU #10194, AC2–AC5/AC7). Carga la
// configuración y orquesta las pestañas con guardado atómico + slots de whitelist,
// matriz OT e historial.
export default function AdminCompanyDetailPage() {
  return (
    <ToastProvider>
      <CompanyDetail />
    </ToastProvider>
  );
}

function CompanyDetail() {
  const router = useRouter();
  const params = useParams<{ tenantId: string }>();
  const tenantId = params.tenantId;
  const { show } = useToast();

  const [status, setStatus] = useState<UiStatus>("loading");
  const [settings, setSettings] = useState<TenantSettings | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchTenantSettings(tenantId, signal);
        if (signal?.aborted) {
          return;
        }
        setSettings(data);
        setStatus("ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleSaveSettings = async (update: TenantSettingsUpdate) => {
    // Propaga ApiValidationError (422) para que CompanyConfigTabs marque campos.
    const updated = await updateTenantSettings(tenantId, update);
    setSettings(updated);
  };

  return (
    <main className="app-bg flex min-h-screen flex-col gap-4 px-6 py-6">
      <button
        type="button"
        onClick={() => router.push("/admin/companies")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" /> Volver al listado
      </button>

      <ModuleTitle
        title="Configuración de compañía"
        subtitle="Edita las políticas operativas y revisa el historial de cambios."
      />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60" style={{ borderColor: "#DFE5ED" }}>
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          errorMessage="No se pudo cargar la configuración de la compañía."
        >
          {settings && (
            <CompanyConfigTabs
              settings={settings}
              onSaveSettings={handleSaveSettings}
              onNotify={show}
              whitelistSlot={<WhitelistPanel tenantId={tenantId} />}
              otSlot={<OTMatrixPanel tenantId={tenantId} />}
              auditSlot={<AuditLogPanel tenantId={tenantId} />}
            />
          )}
        </UiStateBoundary>
      </div>
    </main>
  );
}
