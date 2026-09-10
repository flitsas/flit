"use client";

import { ConfirmacionRuntPage } from "@/components/admin/plataforma/confirmacion-runt/ConfirmacionRuntPage";

/**
 * Plataforma → Confirmación RUNT → Configuración (HU #12313 arma; HU #12279 rellena).
 * Requiere `runt_confirmation.settings.manage` (o SuperAdmin).
 */
export default function AdminConfirmacionRuntConfiguracionPage() {
  return (
    <ConfirmacionRuntPage
      tab="configuracion"
      subtitle="Consulta periódica al RUNT para confirmar que los trámites aprobados en FLIT quedaron registrados. Configuración global de la plataforma."
    >
      <p className="text-sm text-[#59677D] dark:text-white/65">
        La configuración del proceso se habilita en la HU #12279.
      </p>
    </ConfirmacionRuntPage>
  );
}
