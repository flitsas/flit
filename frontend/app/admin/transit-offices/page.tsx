"use client";

import { useEffect, useState } from "react";
import { ArrowLeft, Landmark } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { TransitOfficesList } from "@/components/admin/transit-offices/TransitOfficesList";
import { CreateTransitOfficeTenantDialog } from "@/components/admin/transit-offices/CreateTransitOfficeTenantDialog";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isOtAdmin } from "@/lib/auth/jwt";
import { otHubModulePath } from "@/components/admin/transit-offices/ot-nav";

// Consola OT — listado de organismos de tránsito (HU #10236 AC1) + alta de tenants OT
// (refactor adminOT, SuperAdmin). Un usuario ot_admin salta directo a su propio hub
// (igual que AdminCompany entra directo a /empresa/usuarios) en vez de ver el catálogo.
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
  const [createOpen, setCreateOpen] = useState(false);
  const [createOfficeId, setCreateOfficeId] = useState<string | undefined>(undefined);
  const [listVersion, setListVersion] = useState(0);

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

  // ot_admin nunca ve el catálogo completo: mientras se resuelve su propio tenant
  // (o se decide el rol) se muestra un estado de carga neutro.
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
          subtitle="Selecciona un OT para configurar trámites, integraciones, reglas y documentos."
        />
        <CreateButton
          label="Dar de alta Organismo de Tránsito"
          icon={Landmark}
          onClick={() => {
            setCreateOfficeId(undefined);
            setCreateOpen(true);
          }}
        />
      </div>

      <div
        className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
      >
        <TransitOfficesList
          key={listVersion}
          onCreateTenant={(office) => {
            setCreateOfficeId(office.id);
            setCreateOpen(true);
          }}
        />
      </div>

      <CreateTransitOfficeTenantDialog
        open={createOpen}
        initialOfficeId={createOfficeId}
        onClose={() => setCreateOpen(false)}
        onCreated={(tenant) => {
          setCreateOpen(false);
          show(`Organismo de tránsito «${tenant.legalName}» creado.`, "success");
          setListVersion((v) => v + 1);
          router.push(otHubModulePath(tenant.transitOfficeId, "client-procedures"));
        }}
      />
    </div>
  );
}
