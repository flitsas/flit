"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { addProcedureGrant, fetchProcedureGrants, removeProcedureGrant } from "@/lib/api/admin-companies";
import { superadminClient } from "@/lib/api/superadmin-client";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";

/**
 * FEATURE-08 — pestaña "Trámites habilitados" del detalle de compañía (SuperAdmin). Lista los tipos
 * de trámite PUBLICADOS y permite habilitarlos/deshabilitarlos por compañía (grant model). Solo los
 * habilitados aparecen en el selector del operador al crear un trámite. UI optimista con rollback,
 * calcada de OTMatrixPanel (self-contained, persistencia por-celda).
 */
export function EnabledProceduresPanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [types, setTypes] = useState<ProcedureTypeSummary[]>([]);
  const [enabledIds, setEnabledIds] = useState<Set<string>>(new Set());
  const [pendingIds, setPendingIds] = useState<Set<string>>(new Set());

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [all, grants] = await Promise.all([
          superadminClient.listProcedureTypes(),
          fetchProcedureGrants(tenantId, signal),
        ]);
        if (signal?.aborted) return;
        const published = all.filter((t) => t.publicationStatus === "published");
        setTypes(published);
        setEnabledIds(new Set(grants.procedureTypeIds));
        setStatus(published.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleToggle = async (procedureTypeId: string, enabled: boolean) => {
    // Optimista: refleja el cambio y marca la fila como pendiente (deshabilita el switch).
    setEnabledIds((prev) => {
      const next = new Set(prev);
      if (enabled) next.add(procedureTypeId);
      else next.delete(procedureTypeId);
      return next;
    });
    setPendingIds((prev) => new Set(prev).add(procedureTypeId));

    try {
      if (enabled) await addProcedureGrant(tenantId, procedureTypeId);
      else await removeProcedureGrant(tenantId, procedureTypeId);
    } catch {
      // Rollback al valor previo.
      setEnabledIds((prev) => {
        const next = new Set(prev);
        if (enabled) next.delete(procedureTypeId);
        else next.add(procedureTypeId);
        return next;
      });
      show("No se pudo actualizar la habilitación del trámite.", "error");
    } finally {
      setPendingIds((prev) => {
        const next = new Set(prev);
        next.delete(procedureTypeId);
        return next;
      });
    }
  };

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      emptyMessage="No hay tipos de trámite publicados para habilitar. Publica un tipo en el Configurador."
      errorMessage="No se pudieron cargar los tipos de trámite."
      skeletonRows={4}
    >
      <div className="space-y-2">
        <p className="text-[11px] opacity-60">
          Solo los tipos habilitados aquí aparecen en el selector al crear un trámite para esta compañía.
        </p>
        <ul className="space-y-2" aria-label="Tipos de trámite habilitables">
          {types.map((t) => (
            <li key={t.id}>
              <ToggleSwitch
                id={`procedure-grant-${t.id}`}
                label={t.name}
                description={t.code}
                checked={enabledIds.has(t.id)}
                disabled={pendingIds.has(t.id)}
                onChange={(checked) => void handleToggle(t.id, checked)}
              />
            </li>
          ))}
        </ul>
      </div>
    </UiStateBoundary>
  );
}
