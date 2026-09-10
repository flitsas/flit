"use client";

import { ConfirmacionRuntPage } from "@/components/admin/plataforma/confirmacion-runt/ConfirmacionRuntPage";

/**
 * Plataforma → Confirmación RUNT → Historial (HU #12313 arma; HU #12311 rellena).
 * Requiere `runt_confirmation.history.read` (o SuperAdmin).
 */
export default function AdminConfirmacionRuntHistorialPage() {
  return (
    <ConfirmacionRuntPage
      tab="historial"
      subtitle="Corridas e intentos de confirmación por trámite: qué se consultó, qué respondió el RUNT y por qué se marcó SÍ o NO. Uso interno."
    >
      <p className="text-sm text-[#59677D] dark:text-white/65">
        El historial se habilita en la HU #12311.
      </p>
    </ConfirmacionRuntPage>
  );
}
