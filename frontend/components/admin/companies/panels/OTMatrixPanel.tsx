"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OTMatrix, type OtOperationalInfo } from "@/components/admin/companies/OTMatrix";
import {
  addTransitGrant,
  fetchTransitGrants,
  fetchTransitOffices,
  removeTransitGrant,
} from "@/lib/api/admin-companies";
import {
  fetchTransitOfficesOperationalStatus,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import type { TransitOffice } from "@/lib/api/types";

// Slot de matriz OT (HU #10194, AC4). Carga el catálogo completo y los grants del
// tenant; el buscador y la UI optimista viven en OTMatrix.
export function OTMatrixPanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [grantedIds, setGrantedIds] = useState<string[]>([]);
  const [operationalById, setOperationalById] = useState<Record<string, OtOperationalInfo>>({});

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, grants, opStatus] = await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitGrants(tenantId, signal),
          // HU #10518 — estado operativo por OT para bloquear habilitación. Best-effort:
          // si falla (p. ej. permisos), la matriz sigue y el backend hace de árbitro (422).
          fetchTransitOfficesOperationalStatus(signal).catch(
            () => [] as TransitOfficeOperationalStatus[],
          ),
        ]);
        if (signal?.aborted) {
          return;
        }
        setOffices(catalog);
        setGrantedIds(grants.transitOfficeIds);
        setOperationalById(
          Object.fromEntries(
            opStatus.map((s) => [s.id, { hasTenant: s.hasTenant, estadoActivo: s.estadoActivo }]),
          ),
        );
        setStatus(catalog.length === 0 ? "empty" : "ready");
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

  const handleToggle = async (officeId: string, enabled: boolean) => {
    if (enabled) {
      await addTransitGrant(tenantId, officeId);
    } else {
      await removeTransitGrant(tenantId, officeId);
    }
  };

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      emptyMessage="No hay organismos de tránsito en el catálogo."
      errorMessage="No se pudieron cargar los organismos de tránsito."
      skeletonRows={4}
    >
      <OTMatrix
        offices={offices}
        grantedIds={grantedIds}
        operationalById={operationalById}
        onToggle={handleToggle}
        onError={(message) => show(message, "error")}
      />
    </UiStateBoundary>
  );
}
