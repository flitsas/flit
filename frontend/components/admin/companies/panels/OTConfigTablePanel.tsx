"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Lock } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { Pagination } from "@/components/atom/Pagination";
import { useToast } from "@/components/admin/Toast";
import { OTConfigTable, type OtOperationalInfo } from "@/components/admin/companies/OTConfigTable";
import { OTConfigModal } from "@/components/admin/companies/OTConfigModal";
import { OtScopeConfirmDialog } from "@/components/admin/companies/OtScopeConfirmDialog";
import { TransitBlocksReadOnlySection } from "@/components/admin/companies/panels/TransitBlocksReadOnlySection";
import {
  addTransitGrant,
  fetchOtBlockingPolicies,
  fetchOtConsultationRestrictions,
  fetchOtPrendaDocumentPolicies,
  fetchTransitAgreements,
  fetchTransitBlocks,
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
import {
  otConfigPanelLegend,
  otConfigPanelReadOnly,
  type OtConfigPanelMode,
} from "@/lib/companies/ot-config-mode";
import type {
  BlockingCriterion,
  ConsultationRestrictionKind,
  OtBlockingPolicy,
  OtConsultationRestriction,
  OtPrendaDocumentPolicy,
  TransitOffice,
} from "@/lib/api/types";

const OT_PAGE_SIZE = 10;

type PendingGrantAction = {
  officeId: string;
  officeName: string;
  enabled: boolean;
};

export function OTConfigTablePanel({
  tenantId,
  mode = "editable",
  grantScopeWarningCount = 0,
  blocksTenantId,
  grantsTenantId,
}: {
  tenantId: string;
  /** HUs #12351 / #12408 — condiciona lectura, leyenda y confirmaciones de alcance. */
  mode?: OtConfigPanelMode;
  /** SuperAdmin en Concesión: hijos vigentes afectados por cambios de grants (AC5). */
  grantScopeWarningCount?: number;
  /** Tenant whose blocks are listed (head, when the ficha is a Marca Blanca child). */
  blocksTenantId?: string;
  /** Tenant whose grants are listed (head, when the ficha is a concession child). */
  grantsTenantId?: string;
}) {
  const { show } = useToast();
  const readOnly = otConfigPanelReadOnly(mode);
  const legend = otConfigPanelLegend(mode);
  const showMarcaBlocks = mode === "readonly-marca-blanca";
  const inheritedOnly = mode === "readonly-concession-inherited";
  const effectiveBlocksTenantId = blocksTenantId ?? tenantId;
  const effectiveGrantsTenantId = grantsTenantId ?? tenantId;

  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [grantedIds, setGrantedIds] = useState<string[]>([]);
  const [blockedIds, setBlockedIds] = useState<string[]>([]);
  const [agreementIds, setAgreementIds] = useState<string[]>([]);
  const [operationalById, setOperationalById] = useState<Record<string, OtOperationalInfo>>({});
  const [policies, setPolicies] = useState<OtBlockingPolicy[]>([]);
  const [restrictions, setRestrictions] = useState<OtConsultationRestriction[]>([]);
  const [prendaOptionalPolicies, setPrendaOptionalPolicies] = useState<OtPrendaDocumentPolicy[]>([]);
  const [configOffice, setConfigOffice] = useState<TransitOffice | null>(null);
  const [page, setPage] = useState(1);
  const [pendingGrant, setPendingGrant] = useState<PendingGrantAction | null>(null);
  const [grantBusy, setGrantBusy] = useState(false);
  const [grantConfirmResolver, setGrantConfirmResolver] = useState<{
    resolve: () => void;
    reject: () => void;
  } | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const needsBlocks = showMarcaBlocks;
        const needsPolicies = !readOnly;

        const [catalog, grants, agreements, opStatus, blockingRows, restrictionRows, prendaRows, blocks] =
          await Promise.all([
            fetchTransitOffices(undefined, signal),
            fetchTransitGrants(effectiveGrantsTenantId, signal),
            needsPolicies
              ? fetchTransitAgreements(tenantId, signal).catch(() => ({ transitOfficeIds: [] }))
              : Promise.resolve({ transitOfficeIds: [] }),
            fetchTransitOfficesOperationalStatus(signal).catch(
              () => [] as TransitOfficeOperationalStatus[],
            ),
            needsPolicies ? fetchOtBlockingPolicies(tenantId, signal) : Promise.resolve([]),
            needsPolicies ? fetchOtConsultationRestrictions(tenantId, signal) : Promise.resolve([]),
            needsPolicies
              ? fetchOtPrendaDocumentPolicies(tenantId, signal).catch(() => [] as OtPrendaDocumentPolicy[])
              : Promise.resolve([]),
            needsBlocks
              ? fetchTransitBlocks(effectiveBlocksTenantId, signal).catch(() => ({ transitOfficeIds: [] }))
              : Promise.resolve({ transitOfficeIds: [] }),
          ]);

        if (signal?.aborted) return;

        setOffices(catalog);
        setGrantedIds(grants.transitOfficeIds);
        setBlockedIds(blocks.transitOfficeIds);
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
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId, readOnly, showMarcaBlocks, effectiveBlocksTenantId, effectiveGrantsTenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
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

  const persistGrantToggle = async (officeId: string, enabled: boolean) => {
    if (enabled) {
      await addTransitGrant(tenantId, officeId);
    } else {
      await removeTransitGrant(tenantId, officeId);
    }
    setGrantedIds((current) =>
      enabled ? Array.from(new Set([...current, officeId])) : current.filter((id) => id !== officeId),
    );
  };

  const requestGrantToggle = async (officeId: string, enabled: boolean) => {
    const office = offices.find((o) => o.id === officeId);
    const officeName = office?.name ?? "organismo";
    if (mode === "superadmin-concession" && grantScopeWarningCount > 0) {
      await new Promise<void>((resolve, reject) => {
        setGrantConfirmResolver({ resolve, reject });
        setPendingGrant({ officeId, officeName, enabled });
      });
      await persistGrantToggle(officeId, enabled);
      return;
    }
    await persistGrantToggle(officeId, enabled);
  };

  const confirmPendingGrant = async () => {
    if (!pendingGrant) return;
    setGrantBusy(true);
    try {
      grantConfirmResolver?.resolve();
    } finally {
      setGrantBusy(false);
      setPendingGrant(null);
      setGrantConfirmResolver(null);
    }
  };

  const cancelPendingGrant = () => {
    if (grantBusy) return;
    grantConfirmResolver?.reject();
    setPendingGrant(null);
    setGrantConfirmResolver(null);
  };

  const visibleOffices = useMemo(() => {
    if (inheritedOnly) {
      return offices.filter((office) => grantedIds.includes(office.id));
    }

    if (showMarcaBlocks) {
      const blocked = new Set(blockedIds);
      return offices.filter((office) => {
        if (blocked.has(office.id)) return false;
        const op = operationalById[office.id];
        return Boolean(op?.hasTenant && op.estadoActivo);
      });
    }

    return offices.filter((office) => {
      if (grantedIds.includes(office.id)) return true;
      const op = operationalById[office.id];
      return Boolean(op?.hasTenant && op.estadoActivo);
    });
  }, [offices, grantedIds, operationalById, inheritedOnly, showMarcaBlocks, blockedIds]);

  const displayGrantedIds = useMemo(() => {
    if (showMarcaBlocks) {
      return visibleOffices.map((o) => o.id);
    }
    if (inheritedOnly) {
      return grantedIds;
    }
    return grantedIds;
  }, [showMarcaBlocks, visibleOffices, inheritedOnly, grantedIds]);

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

  const emptyMessage = inheritedOnly
    ? "Tu Concesión no tiene organismos de tránsito habilitados."
    : showMarcaBlocks
      ? "No hay organismos disponibles (todos están bloqueados o inactivos)."
      : "No hay organismos de tránsito activos.";

  return (
    <>
      {legend && (
        <div
          className="mb-3 flex items-start gap-2 rounded-xl border px-3 py-2 text-xs"
          style={{ borderColor: "#DFE5ED", background: "rgba(85,126,255,0.04)" }}
          role="note"
          aria-live="polite"
        >
          <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0 opacity-70" aria-hidden />
          <span>{legend}</span>
        </div>
      )}

      <UiStateBoundary
        status={status === "ready" && visibleOffices.length === 0 ? "empty" : status}
        onRetry={() => void load()}
        emptyMessage={emptyMessage}
        errorMessage="No se pudieron cargar los organismos de tránsito."
        skeletonRows={4}
      >
        <OTConfigTable
          offices={pageOffices}
          grantedIds={displayGrantedIds}
          agreementIds={readOnly ? [] : agreementIds}
          onToggleAgreement={readOnly ? undefined : handleToggleAgreement}
          operationalById={operationalById}
          onToggleGrant={readOnly ? async () => {} : requestGrantToggle}
          onOpenConfig={readOnly ? () => {} : (office) => setConfigOffice(office)}
          onError={(message) => show(message, "error")}
          readOnly={readOnly}
        />
        <Pagination
          page={safePage}
          pageSize={OT_PAGE_SIZE}
          totalCount={visibleOffices.length}
          onPageChange={setPage}
        />
      </UiStateBoundary>

      {showMarcaBlocks && (
        <div className="mt-4 border-t pt-4">
          <TransitBlocksReadOnlySection tenantId={effectiveBlocksTenantId} />
        </div>
      )}

      {configOffice && !readOnly && (
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

      {pendingGrant && (
        <OtScopeConfirmDialog
          open
          action={pendingGrant.enabled ? "habilitar" : "deshabilitar"}
          officeName={pendingGrant.officeName}
          affectedChildrenCount={grantScopeWarningCount}
          busy={grantBusy}
          onConfirm={() => void confirmPendingGrant()}
          onCancel={cancelPendingGrant}
        />
      )}
    </>
  );
}
