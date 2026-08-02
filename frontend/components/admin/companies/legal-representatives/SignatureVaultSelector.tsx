"use client";

// HU #11180 — AC1 y AC3: selector de firma del baúl filtrado por documento de la persona.
// Los 4 estados obligatorios: sin-documento (vacío), cargando, error y lleno (lista de firmas).
// AC3 se cubre con un estado especial "lleno pero sin firmas vigentes" dentro del estado lleno.

import { useCallback, useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { SignatureCapture } from "@/components/admin/companies/signature-vault/SignatureCapture";
import type { SignatureVaultItem } from "@/lib/api/admin-signature-vault";
import {
  createSignatureVaultEntry,
  fetchSignatureVaultByDocument,
} from "@/lib/api/admin-signature-vault";
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
  /**
   * HU #11193 (AC2) — datos de la PERSONA que se toman del representante para dar de alta la firma
   * sin volver a pedirlos. Sin nombre completo no se ofrece la captura: es obligatorio en el baúl.
   */
  fullName?: string;
  nitEmpresa?: string | null;
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
  fullName,
  nitEmpresa,
}: SignatureVaultSelectorProps) {
  const [items, setItems] = useState<SignatureVaultItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const [fetched, setFetched] = useState(false);

  // HU #11193 — captura de firma dentro del mismo formulario.
  const [capturing, setCapturing] = useState(false);
  const [artefacto, setArtefacto] = useState<string | null>(null);
  const [codigoHash, setCodigoHash] = useState("");
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const hasDoc = documentType.trim() !== "" && documentNumber.trim().length >= 3;

  const load = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(false);
      setFetched(false);
      return fetchSignatureVaultByDocument(tenantId, documentType, documentNumber, true, signal)
        .then((list) => {
          if (signal?.aborted) return;
          setItems(list);
          setFetched(true);
          setLoading(false);
        })
        .catch(() => {
          if (!signal?.aborted) {
            setError(true);
            setLoading(false);
          }
        });
    },
    [tenantId, documentType, documentNumber],
  );

  useEffect(() => {
    if (!hasDoc) {
      // Sin documento no hay a quién buscarle firmas: conservar las de la persona anterior
      // ofrecería la firma equivocada.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setItems([]);
      setFetched(false);
      setError(false);
      return;
    }

    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [hasDoc, load]);

  const cerrarCaptura = () => {
    setCapturing(false);
    setArtefacto(null);
    setCodigoHash("");
    setDesde("");
    setHasta("");
    setSaveError(null);
  };

  const guardarFirma = async () => {
    if (!artefacto || desde === "" || hasta === "" || !fullName) return;
    setSaving(true);
    setSaveError(null);
    try {
      // El código hash es el que se estampa como "Hash:" en el sello de la firma del baúl de TODOS
      // los documentos (FlitFirmaBaulSello). Sin él la línea se omite, así que una firma capturada
      // desde aquí salía sin trazabilidad verificable mientras la del baúl sí la llevaba.
      const codigo = codigoHash.trim();
      const creada = await createSignatureVaultEntry(tenantId, {
        documentType,
        documentNumber,
        nitEmpresa: nitEmpresa ?? null,
        fullName,
        codigoHash: codigo === "" ? undefined : codigo,
        vigenciaDesde: desde,
        vigenciaHasta: hasta,
        artefactoFirmaBase64: artefacto,
      });
      await load();
      // AC3 — la firma recién capturada queda elegida; el guardado del representante la persiste.
      onChange(creada.id);
      cerrarCaptura();
    } catch {
      setSaveError("No se pudo registrar la firma. Revisa la vigencia y vuelve a intentarlo.");
    } finally {
      setSaving(false);
    }
  };

  /** AC1 y AC2 — captura inline con el resto de datos tomados del representante. */
  const bloqueCaptura = (
    <div
      className="mt-2 space-y-2 rounded-xl border px-3 py-3"
      style={{ borderColor: "var(--flit-border, #DFE5ED)" }}
      data-testid="sig-capture-block"
    >
      <p className="text-[11px] opacity-70">
        La firma se registra a nombre de <b>{fullName}</b> con el documento {documentType}{" "}
        {documentNumber} del representante.
      </p>
      <SignatureCapture value={artefacto} onChange={setArtefacto} disabled={saving} />
      <label className="block text-[11px] font-semibold">
        Código hash <span className="font-normal opacity-60">(opcional)</span>
        <input
          className={`mt-1 ${OT_INPUT_CLS}`}
          value={codigoHash}
          onChange={(e) => setCodigoHash(e.target.value)}
          disabled={saving}
          placeholder="Código alfanumérico"
          aria-label="Código hash"
        />
      </label>
      <div className="flex gap-2">
        <label className="flex-1 text-[11px] font-semibold">
          Vigencia desde
          <input
            type="date"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={desde}
            onChange={(e) => setDesde(e.target.value)}
            disabled={saving}
            aria-label="Vigencia desde"
          />
        </label>
        <label className="flex-1 text-[11px] font-semibold">
          Vigencia hasta
          <input
            type="date"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={hasta}
            onChange={(e) => setHasta(e.target.value)}
            disabled={saving}
            aria-label="Vigencia hasta"
          />
        </label>
      </div>
      {saveError && (
        <p role="alert" className="text-[11px] font-medium" style={{ color: "#FF4E00" }}>
          {saveError}
        </p>
      )}
      <div className="flex justify-end gap-2">
        <button
          type="button"
          className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold"
          onClick={cerrarCaptura}
          disabled={saving}
        >
          Cancelar
        </button>
        <button
          type="button"
          className="rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-60"
          style={{ background: "#557EFF" }}
          onClick={() => void guardarFirma()}
          disabled={saving || !artefacto || desde === "" || hasta === ""}
        >
          {saving ? "Guardando…" : "Guardar firma"}
        </button>
      </div>
    </div>
  );

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

  // Sin firmas vigentes (AC3 de la HU #11180). Desde la HU #11193 la salida ya no es «ve al baúl»:
  // se captura aquí mismo, con los datos de la persona que ya están en el formulario.
  if (fetched && items.length === 0) {
    return (
      <div data-testid="sig-selector-empty" aria-live="polite">
        <p className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-[11px] opacity-80">
          Esta persona no tiene firmas vigentes en el baúl.
        </p>
        {!readOnly && fullName && !capturing && (
          <button
            type="button"
            className="mt-2 rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white"
            style={{ background: "#557EFF" }}
            onClick={() => setCapturing(true)}
            data-testid="sig-capture-open"
          >
            Capturar firma
          </button>
        )}
        {capturing && bloqueCaptura}
      </div>
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
