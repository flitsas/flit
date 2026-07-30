"use client";

import { useState } from "react";
import { Info, Loader2 } from "lucide-react";
import { DocumentTypeSelect } from "@/components/admin/documents/DocumentTypeSelect";
import { ScopeBadge } from "@/components/admin/documents/panels/ScopeBadge";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import type { DocumentType, OverrideScope } from "@/lib/api/types-documents";
import { digitsOnly } from "@/lib/format/currency";

// Formulario de alta de override de orden por OT o Cliente (HU #10198, AC3/AC4).
// Muestra el badge del scope (OT/CLIENTE) y, para CLIENTE, la nota de precedencia
// "CLIENTE > OT > Default". Valida documento + orden antes de delegar a `onSubmit`.
// `documents` ya viene acotado a los documentos asociados al trámite (solo esos
// admiten override); `excludeIds` oculta los que ya tienen override.
export interface OrderOverrideFormProps {
  scope: OverrideScope;
  documents: DocumentType[];
  /** Documentos ya con override en este scope: se ocultan para evitar el duplicado. */
  excludeIds?: string[];
  /** Persiste el override. Debe lanzar para que el formulario muestre el error. */
  onSubmit: (documentTypeId: string, orden: number) => Promise<void>;
  disabled?: boolean;
}

export function OrderOverrideForm({ scope, documents, excludeIds, onSubmit, disabled }: OrderOverrideFormProps) {
  const [documentTypeId, setDocumentTypeId] = useState("");
  const [orden, setOrden] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const submit = async () => {
    setError(null);
    if (!documentTypeId) {
      setError("Selecciona un documento.");
      return;
    }
    const ordenValue = Number(orden);
    if (orden.trim() === "" || Number.isNaN(ordenValue) || ordenValue < 0) {
      setError("Ingresa un orden válido (entero ≥ 0).");
      return;
    }

    setSubmitting(true);
    try {
      await onSubmit(documentTypeId, ordenValue);
      setDocumentTypeId("");
      setOrden("");
    } catch (err) {
      // Surface el motivo real del backend (p. ej. documento no asociado al trámite)
      // en lugar de un genérico, para que el usuario sepa qué corregir.
      setError(resolveErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="rounded-2xl border p-4">
      <div className="mb-3 flex items-center gap-2">
        <ScopeBadge scope={scope} />
        <h3 className="text-xs font-semibold">Nuevo override de orden</h3>
      </div>

      {scope === "CLIENTE" && (
        <p
          className="mb-3 flex items-start gap-1.5 rounded-xl border px-3 py-2 text-[10px] font-medium"
          style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
          role="note"
        >
          <Info className="mt-px h-3.5 w-3.5 shrink-0" />
          Precedencia: CLIENTE &gt; OT &gt; Default. Este orden prevalece sobre el de OT y el default.
        </p>
      )}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <div className="flex-1">
          <DocumentTypeSelect
            id={`override-doc-${scope}`}
            value={documentTypeId}
            onChange={setDocumentTypeId}
            options={documents}
            excludeIds={excludeIds}
            disabled={disabled}
          />
        </div>
        <div className="w-full sm:w-28">
          <label htmlFor={`override-orden-${scope}`} className="mb-1 block text-xs font-semibold">
            Orden
          </label>
          <input
            id={`override-orden-${scope}`}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={orden}
            onChange={(e) => setOrden(digitsOnly(e.target.value))}
            disabled={disabled}
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: error ? "#FF4E00" : "#DFE5ED" }}
          />
        </div>
        <button
          type="button"
          onClick={submit}
          disabled={disabled || submitting}
          className="inline-flex items-center justify-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          Agregar
        </button>
      </div>

      {error && (
        <p className="mt-2 text-[10px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

/** Traduce el error de la API al mensaje que ve el usuario, priorizando el detalle 422. */
function resolveErrorMessage(err: unknown): string {
  if (err instanceof ApiValidationError) {
    return err.errors[0]?.message ?? "Revisa los datos del override.";
  }
  if (err instanceof ApiError) {
    return err.status === 404
      ? "El documento o el ámbito ya no existe. Actualiza la página."
      : "No se pudo crear el override. Intenta de nuevo.";
  }
  return "No se pudo crear el override. Intenta de nuevo.";
}
