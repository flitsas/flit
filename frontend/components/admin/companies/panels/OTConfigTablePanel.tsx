"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { Pagination } from "@/components/atom/Pagination";
import { useToast } from "@/components/admin/Toast";
import { OTConfigTable, type OtOperationalInfo } from "@/components/admin/companies/OTConfigTable";
import { OTConfigModal } from "@/components/admin/companies/OTConfigModal";
import {
  addTransitGrant,
  fetchOtBlockingPolicies,
  fetchOtConsultationRestrictions,
  fetchOtPrendaDocumentPolicies,
  fetchTransitAgreements,
  fetchTransitGrants,
  fetchTransitOffices,
  removeTransitGrant,
  setTransitAgreement,
  setOtBlockingPolicy,
  setOtConsultationRestriction,
  setOtPrendaDocumentPolicy,
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
  OtPrendaDocumentPolicy,
  TransitOffice,
} from "@/lib/api/types";

// Panel único de configuración de Organismos de Tránsito (HU #10194 — consolidación).
// Reemplaza los 3 slots que antes se apilaban en "Configuración Empresa" (matriz de
// grants, restricciones de consulta y políticas de bloqueo) por UNA tabla con switch de
// habilitación y un menú "⋯ Acciones" → "Configurar" que abre, por OT, UN solo modal con
// las dos secciones (bloqueos + restricciones de consulta). Carga todo lo que antes
// cargaban los 3 paneles (catálogo, grants, estado operativo, políticas de bloqueo y
// restricciones de consulta) para poder abrir el modal sin una petición adicional por fila.
/** Filas por página del listado de OT. */
const OT_PAGE_SIZE = 10;

export function OTConfigTablePanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [grantedIds, setGrantedIds] = useState<string[]>([]);
  // Convenio comercial: distinto del grant. Se carga aparte porque no depende de él.
  const [agreementIds, setAgreementIds] = useState<string[]>([]);
  const [operationalById, setOperationalById] = useState<Record<string, OtOperationalInfo>>({});
  const [policies, setPolicies] = useState<OtBlockingPolicy[]>([]);
  const [restrictions, setRestrictions] = useState<OtConsultationRestriction[]>([]);
  const [prendaOptionalPolicies, setPrendaOptionalPolicies] = useState<OtPrendaDocumentPolicy[]>([]);
  const [configOffice, setConfigOffice] = useState<TransitOffice | null>(null);
  const [page, setPage] = useState(1);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, grants, agreements, opStatus, blockingRows, restrictionRows, prendaRows] =
          await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitGrants(tenantId, signal),
          // Best-effort como el estado operativo: si falla, la tabla sigue funcionando sin la
          // columna poblada en vez de dejar al gestor sin pantalla.
          fetchTransitAgreements(tenantId, signal).catch(() => ({ transitOfficeIds: [] })),
          // HU #10518 — estado operativo por OT para bloquear habilitación. Best-effort:
          // si falla (p. ej. permisos), la tabla sigue y el backend hace de árbitro (422).
          fetchTransitOfficesOperationalStatus(signal).catch(
            () => [] as TransitOfficeOperationalStatus[],
          ),
          fetchOtBlockingPolicies(tenantId, signal),
          fetchOtConsultationRestrictions(tenantId, signal),
          fetchOtPrendaDocumentPolicies(tenantId, signal).catch(() => [] as OtPrendaDocumentPolicy[]),
        ]);
        if (signal?.aborted) {
          return;
        }
        setOffices(catalog);
        setGrantedIds(grants.transitOfficeIds);
        setAgreementIds(agreements.transitOfficeIds);
        setOperationalById(
          Object.fromEntries(
            opStatus.map((s) => [s.id, { hasTenant: s.hasTenant, estadoActivo: s.estadoActivo }]),
          ),
        );
        setPolicies(blockingRows);
        setRestrictions(restrictionRows);
        setPrendaOptionalPolicies(prendaRows);
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

  const handleToggleAgreement = async (officeId: string, active: boolean) => {
    await setTransitAgreement(tenantId, officeId, active);
    setAgreementIds((current) =>
      active ? Array.from(new Set([...current, officeId])) : current.filter((id) => id !== officeId),
    );
  };

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

  /**
   * Solo los OT dados de alta y activos. El catálogo trae también los que nunca se dieron de alta y
   * los desactivados, que no se pueden habilitar y solo alargaban la lista.
   *
   * <p>Un OT ya habilitado para la compañía se sigue mostrando aunque haya dejado de estar activo:
   * si se ocultara, su grant quedaría vigente sin forma de revocarlo desde la consola.</p>
   */
  const visibleOffices = useMemo(
    () =>
      offices.filter((office) => {
        if (grantedIds.includes(office.id)) return true;
        const op = operationalById[office.id];
        return Boolean(op?.hasTenant && op.estadoActivo);
      }),
    [offices, grantedIds, operationalById],
  );

  // La página se acota al render en vez de corregirse con setState: si al revocar un grant la lista
  // se encoge, un índice guardado dejaría la tabla en blanco en una página que ya no existe.
  const lastPage = Math.max(1, Math.ceil(visibleOffices.length / OT_PAGE_SIZE));
  const safePage = Math.min(page, lastPage);

  const pageOffices = useMemo(
    () => visibleOffices.slice((safePage - 1) * OT_PAGE_SIZE, safePage * OT_PAGE_SIZE),
    [visibleOffices, safePage],
  );

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

  const handleTogglePrendaOptional = async (transitOfficeId: string, documentOptional: boolean) => {
    await setOtPrendaDocumentPolicy(tenantId, transitOfficeId, documentOptional);
    setPrendaOptionalPolicies((current) => {
      const without = current.filter((p) => p.transitOfficeId !== transitOfficeId);
      return documentOptional ? [...without, { transitOfficeId, documentOptional: true }] : without;
    });
  };

  return (
    <>
      <UiStateBoundary
        status={status === "ready" && visibleOffices.length === 0 ? "empty" : status}
        onRetry={() => void load()}
        emptyMessage="No hay organismos de tránsito activos."
        errorMessage="No se pudieron cargar los organismos de tránsito."
        skeletonRows={4}
      >
        <OTConfigTable
          offices={pageOffices}
          grantedIds={grantedIds}
          agreementIds={agreementIds}
          onToggleAgreement={handleToggleAgreement}
          operationalById={operationalById}
          onToggleGrant={handleToggleGrant}
          onOpenConfig={(office) => setConfigOffice(office)}
          onError={(message) => show(message, "error")}
        />
        <Pagination
          page={safePage}
          pageSize={OT_PAGE_SIZE}
          totalCount={visibleOffices.length}
          onPageChange={setPage}
        />
      </UiStateBoundary>

      {configOffice && (
        <OTConfigModal
          office={configOffice}
          policies={policies}
          restrictions={restrictions}
          prendaOptionalPolicies={prendaOptionalPolicies}
          onToggleBlocking={handleToggleBlocking}
          onToggleRestriction={handleToggleRestriction}
          onTogglePrendaOptional={handleTogglePrendaOptional}
          onClose={() => setConfigOffice(null)}
          onError={(message) => show(message, "error")}
        />
      )}
    </>
  );
}
