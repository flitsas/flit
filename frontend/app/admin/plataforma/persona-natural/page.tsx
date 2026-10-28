"use client";

import { ArrowLeft, ShieldAlert } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { ADMIN_BACK_LINK_CLS } from "@/components/admin/admin-ui-styles";
import { PersonaNaturalConsole } from "@/components/admin/plataforma/persona-natural/PersonaNaturalConsole";
import { usePermissions } from "@/hooks/usePermissions";

/**
 * SuperAdmin — Plataforma → Persona Natural (HU #14074, Feature #14070, Épica #14021): tarifas, organismos y tipos,
 * parámetros y contenido del portal público de Persona Natural. Exclusivo del Super Admin FLIT: el borde (middleware,
 * `evaluateAdminAccess` con la regla de /admin) ya lo corta antes de llegar aquí y la API exige el rol más el permiso
 * `platform.persona_natural.manage`; se repite la comprobación como defensa en profundidad.
 */
export default function AdminPersonaNaturalPage() {
  const { isSuperAdmin } = usePermissions();

  if (!isSuperAdmin) return <NoAccess />;

  return (
    <ToastProvider>
      <PersonaNaturalPage />
    </ToastProvider>
  );
}

function NoAccess() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 px-4 text-center">
      <ShieldAlert className="h-8 w-8" style={{ color: "#FF4E00" }} aria-hidden="true" />
      <p className="text-sm font-medium" style={{ color: "var(--color-flit-primary)" }}>
        No tienes permiso para administrar el portal de Persona Natural.
      </p>
    </div>
  );
}

function PersonaNaturalPage() {
  const router = useRouter();
  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
      <button type="button" onClick={() => router.push("/")} className={ADMIN_BACK_LINK_CLS}>
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" /> Volver al inicio
      </button>

      <ModuleTitle
        title="Persona Natural"
        subtitle="Configura las tarifas, los organismos y tipos habilitados, los parámetros y el contenido del portal de Persona Natural."
      />

      <PersonaNaturalConsole />
    </div>
  );
}
