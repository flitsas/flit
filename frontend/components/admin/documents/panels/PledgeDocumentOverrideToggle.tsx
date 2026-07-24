"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { fetchProcedureDocumentRequirements } from "@/lib/api/admin-procedure-documents";
import {
  fetchDocumentRequirementOverrides,
  setDocumentRequirementOverride,
} from "@/lib/api/admin-document-requirement-overrides";
import { ApiError } from "@/lib/api/types";

// Código del documento de inscripción/registro de prenda en el catálogo (seed HU #10520).
const PLEDGE_DOCUMENT_CODE = "inscripcion_prenda";

const DEFAULT_LOAD_ERROR = "No se pudo cargar la obligatoriedad del documento de prenda.";
const DEFAULT_SAVE_ERROR = "No se pudo actualizar la obligatoriedad del documento de prenda.";
const FORBIDDEN_OT_ERROR =
  "No tienes permisos para gestionar el documento de prenda de este Organismo de Tránsito.";

/**
 * Traduce el 403 `TRANSIT_OFFICE_FORBIDDEN` (backend HU #10887: `ot_admin` solo puede
 * gestionar overrides de su propia OT) a un mensaje entendible; el resto cae al genérico.
 */
function describePledgeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 403) {
    return FORBIDDEN_OT_ERROR;
  }
  return fallback;
}

export interface PledgeDocumentOverrideToggleProps {
  procedureTypeId: string;
  transitOfficeId: string;
}

/**
 * Toggle dedicado "Documento de prenda obligatorio" (HU #10887).
 *
 * Componente autocontenido: se reutiliza tal cual desde dos puntos de entrada — la consola
 * documental global (`OtOverridesTab`, con selector de trámite y OT) y el detalle del propio
 * Organismo de Tránsito (`DocumentsSection` en `/admin/transit-offices/[id]/documents`, donde
 * el `transitOfficeId` viene del hub). Ambos pegan al mismo endpoint y quedan sincronizados.
 *
 * AC1 — al activarlo, hace upsert (PUT) del override de obligatoriedad del documento
 * `inscripcion_prenda` a REQUIRED para el (tipo de trámite, OT) en curso, vía el endpoint
 * granular de obligatoriedad por OT (HU #10198). Al desactivarlo, limpia el override
 * (estado=DEFAULT) y el documento vuelve a heredar la obligatoriedad por defecto del trámite.
 *
 * AC2 (bloqueo de radicación sin el documento) ya lo garantiza el backend en runtime
 * (HU #10881, `prenda_documento_requerido` / `canSubmit=false`): este control solo persiste
 * el override, no reimplementa el gate.
 *
 * 4 estados UI: cargando, error, vacío (el trámite no tiene el documento de prenda asociado
 * — no admite override) y lleno (el switch con el valor vigente).
 */
export function PledgeDocumentOverrideToggle({
  procedureTypeId,
  transitOfficeId,
}: PledgeDocumentOverrideToggleProps) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [documentTypeId, setDocumentTypeId] = useState<string | null>(null);
  const [required, setRequired] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState(DEFAULT_LOAD_ERROR);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [requirements, overrides] = await Promise.all([
          fetchProcedureDocumentRequirements(procedureTypeId, signal),
          fetchDocumentRequirementOverrides(procedureTypeId, transitOfficeId, signal),
        ]);
        if (signal?.aborted) return;

        const pledge = requirements.find((r) => r.documento.codigo === PLEDGE_DOCUMENT_CODE);
        if (!pledge) {
          setDocumentTypeId(null);
          setRequired(false);
          setStatus("empty");
          return;
        }

        setDocumentTypeId(pledge.documentTypeId);
        const override = overrides.find((o) => o.documentTypeId === pledge.documentTypeId);
        setRequired(override?.estado === "REQUIRED");
        setStatus("ready");
      } catch (error) {
        if (!signal?.aborted) {
          setLoadError(describePledgeError(error, DEFAULT_LOAD_ERROR));
          setStatus("error");
        }
      }
    },
    [procedureTypeId, transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleToggle = async (next: boolean) => {
    if (!documentTypeId) return;
    const previous = required;
    setRequired(next);
    setSaving(true);
    try {
      await setDocumentRequirementOverride({
        procedureTypeId,
        documentTypeId,
        transitOfficeId,
        estado: next ? "REQUIRED" : "DEFAULT",
      });
      show(
        next
          ? "Documento de prenda marcado como obligatorio para este OT."
          : "El documento de prenda vuelve a heredar la obligatoriedad del trámite.",
        "success",
      );
    } catch (error) {
      setRequired(previous);
      show(describePledgeError(error, DEFAULT_SAVE_ERROR), "error");
    } finally {
      setSaving(false);
    }
  };

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      emptyMessage="Este trámite no tiene asociado el documento de inscripción/registro de prenda; no admite override de obligatoriedad."
      errorMessage={loadError}
      skeletonRows={1}
    >
      <div className="flex items-start justify-between gap-4 rounded-2xl border bg-white px-4 py-3 dark:bg-[#0B0F14]">
        <div className="min-w-0">
          <label htmlFor="pledge-doc-toggle" className="block text-xs font-semibold">
            Documento de prenda obligatorio
          </label>
          <p className="mt-1 text-[11px] opacity-70">
            Exige la inscripción/registro de prenda para radicar este trámite en el Organismo
            de Tránsito seleccionado. Con el override activo, la radicación queda bloqueada si
            falta el documento.
          </p>
        </div>
        <button
          id="pledge-doc-toggle"
          type="button"
          role="switch"
          aria-checked={required}
          aria-label="Documento de prenda obligatorio"
          disabled={saving}
          onClick={() => void handleToggle(!required)}
          className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full bg-muted transition-colors disabled:opacity-50"
          style={{ background: required ? "#557EFF" : undefined }}
        >
          <span
            className="inline-block h-5 w-5 transform rounded-full bg-white shadow transition-transform"
            style={{ transform: required ? "translateX(22px)" : "translateX(2px)" }}
          />
        </button>
      </div>
    </UiStateBoundary>
  );
}
