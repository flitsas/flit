"use client";

import { useCallback, useEffect, useState } from "react";
import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { TransitOfficesList } from "@/components/admin/transit-offices/TransitOfficesList";
import {
  createTransitOfficeTenant,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isOtAdmin } from "@/lib/auth/jwt";
import { otHubModulePath } from "@/components/admin/transit-offices/ot-nav";
import { ApiError, ApiValidationError } from "@/lib/api/types";

// Consola OT — listado (HU #10236) + activación one-shot sin modal (HU #11224).
// ot_admin salta a su hub; SuperAdmin activa desde la fila del catálogo.
export default function AdminTransitOfficesPage() {
  return (
    <ToastProvider>
      <AdminTransitOfficesPageInner />
    </ToastProvider>
  );
}

type RoleResolution = "checking" | "ot_admin" | "other";

function AdminTransitOfficesPageInner() {
  const router = useRouter();
  const { show } = useToast();
  const [role, setRole] = useState<RoleResolution>("checking");
  const [listVersion, setListVersion] = useState(0);
  const [activatingId, setActivatingId] = useState<string | null>(null);

  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setRole(payload && isOtAdmin(payload) ? "ot_admin" : "other");
  }, []);

  useEffect(() => {
    if (role !== "ot_admin") {
      return;
    }
    const controller = new AbortController();
    void fetchOtProfile(controller.signal)
      .then((profile) => {
        if (controller.signal.aborted) {
          return;
        }
        router.replace(otHubModulePath(profile.transitOfficeId, "client-procedures"));
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [role, router]);

  const activateOffice = useCallback(
    async (office: TransitOfficeOperationalStatus) => {
      if (activatingId) {
        return;
      }
      setActivatingId(office.id);
      try {
        const tenant = await createTransitOfficeTenant({
          transitOfficeId: office.id,
          legalName: office.name,
          taxId: office.code,
          code: office.code,
        });
        show(`Organismo de tránsito «${tenant.legalName}» activado.`, "success");
        setListVersion((v) => v + 1);
        router.push(otHubModulePath(tenant.transitOfficeId, "client-procedures"));
      } catch (err) {
        const message =
          err instanceof ApiValidationError
            ? err.message || "No se pudo activar el organismo de tránsito."
            : err instanceof ApiError
              ? err.message
              : "No se pudo activar el organismo de tránsito.";
        show(message, "error");
      } finally {
        setActivatingId(null);
      }
    },
    [activatingId, router, show],
  );

  if (role === "checking" || role === "ot_admin") {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-3 px-6 pt-6 pb-10" role="status" aria-busy="true" aria-live="polite">
        <span className="sr-only">Cargando…</span>
        <div
          className="h-10 w-10 animate-spin rounded-full border-2 border-t-transparent"
          style={{ borderColor: "#557EFF", borderTopColor: "transparent" }}
          aria-hidden="true"
        />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al inicio
      </button>

      <div className="flex flex-wrap items-start justify-between gap-3">
        <ModuleTitle
          title="Administración de organismos de tránsito"
          subtitle="Selecciona un OT para configurar trámites, integraciones, reglas y documentos. Activa un organismo desde la fila del catálogo."
        />
      </div>

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <TransitOfficesList key={listVersion} onCreateTenant={(office) => void activateOffice(office)} />
      </div>
    </div>
  );
}
