"use client";

import { ConfirmacionRuntConfigPanel } from "@/components/admin/plataforma/confirmacion-runt/ConfirmacionRuntConfigPanel";
import { ConfirmacionRuntPage } from "@/components/admin/plataforma/confirmacion-runt/ConfirmacionRuntPage";

/**
 * Plataforma → Confirmación RUNT → Configuración (HU #12313 arma la pestaña; HU #12279 el panel).
 * Requiere `runt_confirmation.settings.manage` (o SuperAdmin).
 */
export default function AdminConfirmacionRuntConfiguracionPage() {
  return (
    <ConfirmacionRuntPage
      tab="configuracion"
      subtitle="Consulta periódica al RUNT para confirmar que los trámites aprobados en FLIT quedaron registrados. Configuración global de la plataforma."
    >
      <ConfirmacionRuntConfigPanel />
    </ConfirmacionRuntPage>
  );
}
