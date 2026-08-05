"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import {
  fetchOtPrendaDocumentPoliciesForOffice,
  setOtPrendaDocumentPolicyForOffice,
} from "@/lib/api/admin-ot-prenda-document-policies";
import type { OtPrendaDocumentPolicyCompany } from "@/lib/api/types";
import { ApiError } from "@/lib/api/types";

const DEFAULT_LOAD_ERROR = "No se pudo cargar la configuración de prenda por compañía.";
const DEFAULT_SAVE_ERROR = "No se pudo actualizar la obligatoriedad de prenda.";
const FORBIDDEN_OT_ERROR =
  "No tienes permisos para gestionar el documento de prenda de este Organismo de Tránsito.";

export interface PledgeDocumentOverrideToggleProps {
  transitOfficeId: string;
  /** @deprecated Ya no se usa: la config es por compañía+OT, no por tipo de trámite. */
  procedureTypeId?: string;
}

/**
 * Hub OT: por cada compañía habilitada, check "Prenda opcional".
 * Default = obligatorio; check ON = deja de exigir el documento.
 */
export function PledgeDocumentOverrideToggle({ transitOfficeId }: PledgeDocumentOverrideToggleProps) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<OtPrendaDocumentPolicyCompany[]>([]);
  const [pendingTenantId, setPendingTenantId] = useState<string | null>(null);
  const [loadError, setLoadError] = useState(DEFAULT_LOAD_ERROR);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchOtPrendaDocumentPoliciesForOffice(transitOfficeId, signal);
        if (signal?.aborted) return;
        setRows(data);
        setStatus(data.length === 0 ? "empty" : "ready");
      } catch (error) {
        if (signal?.aborted) return;
        setLoadError(
          error instanceof ApiError && error.status === 403 ? FORBIDDEN_OT_ERROR : DEFAULT_LOAD_ERROR,
        );
        setStatus("error");
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleToggle = async (tenantId: string, next: boolean) => {
    const previous = rows;
    setRows((current) =>
      current.map((r) => (r.tenantId === tenantId ? { ...r, documentOptional: next } : r)),
    );
    setPendingTenantId(tenantId);
    try {
      await setOtPrendaDocumentPolicyForOffice(transitOfficeId, tenantId, next);
      show(
        next
          ? "Documento de prenda ahora es opcional para esa compañía."
          : "Documento de prenda vuelve a ser obligatorio para esa compañía.",
        "success",
      );
    } catch (error) {
      setRows(previous);
      show(
        error instanceof ApiError && error.status === 403 ? FORBIDDEN_OT_ERROR : DEFAULT_SAVE_ERROR,
        "error",
      );
    } finally {
      setPendingTenantId(null);
    }
  };

  return (
    <UiStateBoundary
      status={status}
      errorMessage={loadError}
      emptyMessage="No hay compañías habilitadas en este Organismo de Tránsito."
      onRetry={() => void load()}
      skeletonRows={3}
    >
      <div className="space-y-3" data-testid="ot-prenda-optional-by-company">
        <p className="text-[11px] opacity-70">
          Por defecto el documento de prenda es obligatorio. Activa el check para que deje de
          exigirse en esa compañía.
        </p>
        <ul className="space-y-2">
          {rows.map((row) => (
            <li key={row.tenantId} className="rounded-xl border px-3 py-2">
              <ToggleSwitch
                id={`prenda-optional-${row.tenantId}`}
                label={`${row.tenantName} — Prenda opcional`}
                checked={row.documentOptional}
                disabled={pendingTenantId === row.tenantId}
                onChange={(checked) => void handleToggle(row.tenantId, checked)}
              />
            </li>
          ))}
        </ul>
      </div>
    </UiStateBoundary>
  );
}
