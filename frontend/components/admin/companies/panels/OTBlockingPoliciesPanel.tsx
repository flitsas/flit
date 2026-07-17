"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OTBlockingPoliciesMatrix } from "@/components/admin/companies/OTBlockingPoliciesMatrix";
import {
  fetchOtBlockingPolicies,
  fetchTransitGrants,
  fetchTransitOffices,
  setOtBlockingPolicy,
} from "@/lib/api/admin-companies";
import type { BlockingCriterion, OtBlockingPolicy, TransitOffice } from "@/lib/api/types";

// Slot de políticas de bloqueo de preflight por OT (FEATURE 05). Complementa la matriz de
// consultas ("qué se consulta") con "y qué bloquea". Endpoint propio, fuera del PUT atómico de
// settings (matriz de longitud variable, se guarda por toggle).
export function OTBlockingPoliciesPanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [grantedIds, setGrantedIds] = useState<string[]>([]);
  const [policies, setPolicies] = useState<OtBlockingPolicy[]>([]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, grants, rows] = await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitGrants(tenantId, signal),
          fetchOtBlockingPolicies(tenantId, signal),
        ]);
        if (signal?.aborted) {
          return;
        }
        setOffices(catalog);
        setGrantedIds(grants.transitOfficeIds);
        setPolicies(rows);
        // Sin grants no hay nada que configurar: el backend rechaza (422) configurar un OT sin
        // habilitar, así que se invita a habilitarlos primero.
        setStatus(grants.transitOfficeIds.length === 0 ? "empty" : "ready");
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

  // Solo los OT habilitados son configurables: espeja la validación 422 del backend.
  const configurableOffices = useMemo(() => {
    const granted = new Set(grantedIds);
    return offices.filter((o) => granted.has(o.id));
  }, [offices, grantedIds]);

  const handleToggle = async (
    transitOfficeId: string,
    criterion: BlockingCriterion,
    blocks: boolean,
  ) => {
    await setOtBlockingPolicy(tenantId, transitOfficeId, criterion, blocks);
  };

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      emptyMessage="Esta compañía aún no tiene organismos de tránsito habilitados. Habilita al menos uno en la matriz de organismos para definir qué criterios bloquean en cada uno."
      errorMessage="No se pudieron cargar los criterios de bloqueo."
      skeletonRows={3}
    >
      <OTBlockingPoliciesMatrix
        offices={configurableOffices}
        policies={policies}
        onToggle={handleToggle}
        onError={(message) => show(message, "error")}
      />
    </UiStateBoundary>
  );
}
