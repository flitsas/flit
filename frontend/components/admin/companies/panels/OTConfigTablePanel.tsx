"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OTConfigTable, type OtOperationalInfo } from "@/components/admin/companies/OTConfigTable";
import { OTConfigModal } from "@/components/admin/companies/OTConfigModal";
import {
  addTransitGrant,
  fetchOtBlockingPolicies,
  fetchOtConsultationRestrictions,
  fetchTransitGrants,
  fetchTransitOffices,
  removeTransitGrant,
  setOtBlockingPolicy,
  setOtConsultationRestriction,
} from "@/lib/api/admin-companies";
import {
  fetchTransitOfficesOperationalStatus,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import type {
  BlockingCriterion,
  ConsultationRestrictionKind,
  OtBlockingPolicy,
  OtConsultationRestriction,
  TransitOffice,
} from "@/lib/api/types";

// Panel único de configuración de Organismos de Tránsito (HU #10194 — consolidación).
// Reemplaza los 3 slots que antes se apilaban en "Configuración Empresa" (matriz de
// grants, restricciones de consulta y políticas de bloqueo) por UNA tabla con switch de
// habilitación y un menú "⋯ Acciones" → "Configurar" que abre, por OT, UN solo modal con
// las dos secciones (bloqueos + restricciones de consulta). Carga todo lo que antes
// cargaban los 3 paneles (catálogo, grants, estado operativo, políticas de bloqueo y
// restricciones de consulta) para poder abrir el modal sin una petición adicional por fila.
export function OTConfigTablePanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [grantedIds, setGrantedIds] = useState<string[]>([]);
  const [operationalById, setOperationalById] = useState<Record<string, OtOperationalInfo>>({});
  const [policies, setPolicies] = useState<OtBlockingPolicy[]>([]);
  const [restrictions, setRestrictions] = useState<OtConsultationRestriction[]>([]);
  const [configOffice, setConfigOffice] = useState<TransitOffice | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, grants, opStatus, blockingRows, restrictionRows] = await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitGrants(tenantId, signal),
          // HU #10518 — estado operativo por OT para bloquear habilitación. Best-effort:
          // si falla (p. ej. permisos), la tabla sigue y el backend hace de árbitro (422).
          fetchTransitOfficesOperationalStatus(signal).catch(
            () => [] as TransitOfficeOperationalStatus[],
          ),
          fetchOtBlockingPolicies(tenantId, signal),
          fetchOtConsultationRestrictions(tenantId, signal),
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
        setPolicies(blockingRows);
        setRestrictions(restrictionRows);
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

  const handleToggleGrant = async (officeId: string, enabled: boolean) => {
    if (enabled) {
      await addTransitGrant(tenantId, officeId);
    } else {
      await removeTransitGrant(tenantId, officeId);
    }
    setGrantedIds((current) =>
      enabled ? Array.from(new Set([...current, officeId])) : current.filter((id) => id !== officeId),
    );
  };

  const handleToggleBlocking = async (
    transitOfficeId: string,
    criterion: BlockingCriterion,
    blocks: boolean,
  ) => {
    await setOtBlockingPolicy(tenantId, transitOfficeId, criterion, blocks);
    // Mantiene la línea base actualizada: si se reabre el modal, refleja el último valor.
    setPolicies((current) => [
      ...current.filter((p) => !(p.transitOfficeId === transitOfficeId && p.criterion === criterion)),
      { transitOfficeId, criterion, blocks },
    ]);
  };

  const handleToggleRestriction = async (
    transitOfficeId: string,
    kind: ConsultationRestrictionKind,
    enabled: boolean,
  ) => {
    await setOtConsultationRestriction(tenantId, transitOfficeId, kind, enabled);
    setRestrictions((current) => [
      ...current.filter((r) => !(r.transitOfficeId === transitOfficeId && r.consultationKind === kind)),
      { transitOfficeId, consultationKind: kind, enabled },
    ]);
  };

  return (
    <>
      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        emptyMessage="No hay organismos de tránsito en el catálogo."
        errorMessage="No se pudieron cargar los organismos de tránsito."
        skeletonRows={4}
      >
        <OTConfigTable
          offices={offices}
          grantedIds={grantedIds}
          operationalById={operationalById}
          onToggleGrant={handleToggleGrant}
          onOpenConfig={(office) => setConfigOffice(office)}
          onError={(message) => show(message, "error")}
        />
      </UiStateBoundary>

      {configOffice && (
        <OTConfigModal
          office={configOffice}
          policies={policies}
          restrictions={restrictions}
          onToggleBlocking={handleToggleBlocking}
          onToggleRestriction={handleToggleRestriction}
          onClose={() => setConfigOffice(null)}
          onError={(message) => show(message, "error")}
        />
      )}
    </>
  );
}
