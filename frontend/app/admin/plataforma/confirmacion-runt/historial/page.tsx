"use client";

import { Suspense } from "react";
import { ConfirmacionRuntHistorialPanel } from "@/components/admin/plataforma/confirmacion-runt/ConfirmacionRuntHistorialPanel";
import { ConfirmacionRuntPage } from "@/components/admin/plataforma/confirmacion-runt/ConfirmacionRuntPage";

/**
 * Plataforma → Confirmación RUNT → Historial (HU #12313 arma la pestaña; HU #12311 el panel).
 * `Suspense` porque el panel lee `useSearchParams` (filtros en la URL) y Next lo exige en build.
 * Requiere `runt_confirmation.history.read` (o SuperAdmin).
 */
export default function AdminConfirmacionRuntHistorialPage() {
  return (
    <ConfirmacionRuntPage
      tab="historial"
      subtitle="Corridas e intentos de confirmación por trámite: qué se consultó, qué respondió el RUNT y por qué se marcó SÍ o NO. Uso interno."
    >
      <Suspense fallback={null}>
        <ConfirmacionRuntHistorialPanel />
      </Suspense>
    </ConfirmacionRuntPage>
  );
}
