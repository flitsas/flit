"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OTConsultationRestrictionsMatrix } from "@/components/admin/companies/OTConsultationRestrictionsMatrix";
import {
  fetchOtConsultationRestrictions,
  fetchTransitGrants,
  fetchTransitOffices,
  setOtConsultationRestriction,
} from "@/lib/api/admin-companies";
import type {
  ConsultationRestrictionKind,
  OtConsultationRestriction,
  TransitOffice,
} from "@/lib/api/types";

// Slot de restricciones de consulta por OT (HU #10761, backend #10759). Refina la matriz
// OT: "qué OTs" → "y qué consultamos en cada uno". Endpoint propio, fuera del PUT atómico
// de settings (matriz de longitud variable, se guarda por toggle).
export function OTConsultationRestrictionsPanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [grantedIds, setGrantedIds] = useState<string[]>([]);
  const [restrictions, setRestrictions] = useState<OtConsultationRestriction[]>([]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, grants, rows] = await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitGrants(tenantId, signal),
          fetchOtConsultationRestrictions(tenantId, signal),
        ]);
        if (signal?.aborted) {
          return;
        }
        setOffices(catalog);
        setGrantedIds(grants.transitOfficeIds);
        setRestrictions(rows);
        // Sin grants no hay nada que restringir: el backend rechaza (422) restringir un
        // OT sin habilitar, así que se invita a habilitarlos primero (AC3).
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

  // Solo los OT habilitados son restringibles (AC2): espeja la validación 422 del backend.
  const restrictableOffices = useMemo(() => {
    const granted = new Set(grantedIds);
    return offices.filter((o) => granted.has(o.id));
  }, [offices, grantedIds]);

  const handleToggle = async (
    transitOfficeId: string,
    kind: ConsultationRestrictionKind,
    enabled: boolean,
  ) => {
    await setOtConsultationRestriction(tenantId, transitOfficeId, kind, enabled);
  };

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      emptyMessage="Esta compañía aún no tiene organismos de tránsito habilitados. Habilita al menos uno en la matriz de organismos para definir qué se consulta en cada uno."
      errorMessage="No se pudieron cargar las restricciones de consulta."
      skeletonRows={3}
    >
      <OTConsultationRestrictionsMatrix
        offices={restrictableOffices}
        restrictions={restrictions}
        onToggle={handleToggle}
        onError={(message) => show(message, "error")}
      />
    </UiStateBoundary>
  );
}
