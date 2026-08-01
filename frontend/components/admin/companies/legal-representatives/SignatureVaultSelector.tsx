"use client";

// HU #11180 — AC1 y AC3: selector de firma del baúl filtrado por documento de la persona.
// Los 4 estados obligatorios: sin-documento (vacío), cargando, error y lleno (lista de firmas).
// AC3 se cubre con un estado especial "lleno pero sin firmas vigentes" dentro del estado lleno.

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import type { SignatureVaultItem } from "@/lib/api/admin-signature-vault";
import { fetchSignatureVaultByDocument } from "@/lib/api/admin-signature-vault";
import { formatFecha } from "@/lib/format/date";

export interface SignatureVaultSelectorProps {
  tenantId: string;
  documentType: string;
  documentNumber: string;
  /** ID de la firma seleccionada (null = sin selección). */
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /** Solo lectura (modo view): muestra la firma seleccionada como texto plano. */
  readOnly?: boolean;
}

/**
 * Selector de firma del baúl filtrado por documento de la persona (AC1).
 * Consulta `GET .../signature-vault?documentType=&documentNumber=&soloVigentes=true`
 * cuando el documento está completo. AC3: si no hay firmas vigentes muestra aviso con
 * indicación para ir al baúl de firmas.
 */
export function SignatureVaultSelector({
  tenantId,
  documentType,
  documentNumber,
  value,
  onChange,
  readOnly,
}: SignatureVaultSelectorProps) {
  const [items, setItems] = useState<SignatureVaultItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const [fetched, setFetched] = useState(false);

  const hasDoc = documentType.trim() !== "" && documentNumber.trim().length >= 3;

  useEffect(() => {
    if (!hasDoc) {
      setItems([]);
      setFetched(false);
      setError(false);
      return;
    }

    setLoading(true);
    setError(false);
    setFetched(false);
    const controller = new AbortController();

    fetchSignatureVaultByDocument(tenantId, documentType, documentNumber, true, controller.signal)
      .then((list) => {
        if (controller.signal.aborted) return;
        setItems(list);
        setFetched(true);
        setLoading(false);
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setError(true);
          setLoading(false);
        }
      });

    return () => controller.abort();
  }, [tenantId, documentType, documentNumber, hasDoc]);

  // Estado vacío — sin documento diligenciado
  if (!hasDoc) {
    return (
      <p
        className="text-[11px] opacity-60"
        data-testid="sig-selector-no-doc"
        aria-live="polite"
      >
        Ingresa el tipo y número de documento para ver las firmas disponibles.
      </p>
    );
  }

  // Estado cargando
  if (loading) {
    return (
      <div
        className="flex items-center gap-2 text-[11px] opacity-60"
        aria-live="polite"
        data-testid="sig-selector-loading"
      >
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        Buscando firmas vigentes…
      </div>
    );
  }

  // Estado error
  if (error) {
    return (
      <p
        role="alert"
        className="text-[11px] font-medium"
        style={{ color: "#FF4E00" }}
        data-testid="sig-selector-error"
      >
        No se pudo consultar el baúl de firmas. Intenta de nuevo.
      </p>
    );
  }

  // AC3 — sin firmas vigentes
  if (fetched && items.length === 0) {
    return (
      <p
        className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-[11px] opacity-80"
        data-testid="sig-selector-empty"
        aria-live="polite"
      >
        Esta persona no tiene firmas vigentes en el baúl.{" "}
        <span className="font-semibold">
          Ve al Baúl de firmas para registrar una.
        </span>
      </p>
    );
  }

  // Modo solo lectura (vista de consulta)
  if (readOnly) {
    const selected = items.find((i) => i.id === value);
    return (
      <p className="text-xs" data-testid="sig-selector-readonly">
        {selected
          ? `${selected.fullName} — vigente hasta ${formatFecha(selected.vigenciaHasta)}`
          : "Sin firma seleccionada"}
      </p>
    );
  }

  // AC1 — lista de firmas vigentes
  return (
    <select
      id="lr-sig-vault"
      aria-label="Firma del baúl"
      value={value ?? ""}
      onChange={(e) => onChange(e.target.value || null)}
      className={OT_INPUT_CLS}
      data-testid="sig-selector-select"
    >
      <option value="">Sin firma seleccionada</option>
      {items.map((sig) => (
        <option key={sig.id} value={sig.id}>
          {sig.fullName} — vigente hasta {formatFecha(sig.vigenciaHasta)}
        </option>
      ))}
    </select>
  );
}
