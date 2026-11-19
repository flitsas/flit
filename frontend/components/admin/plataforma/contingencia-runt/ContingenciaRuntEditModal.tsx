"use client";

import { useEffect, useId, useRef, useState } from "react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";
import {
  RUNT_CONTINGENCY_KIND_LABEL,
  RUNT_CONTINGENCY_PROVIDERS_BY_KIND,
  RUNT_CONTINGENCY_PROVIDER_LABEL,
  RUNT_CONTINGENCY_REASON_MAX,
  putRuntContingency,
  runtContingencyErrorMessage,
  type RuntContingencyItem,
  type RuntContingencyProvider,
} from "@/lib/api/admin-runt-contingency";

const campoCls =
  "w-full rounded-xl border bg-card px-3 py-2 text-sm text-flit-primary outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50";
const botonPrimarioCls =
  "rounded-full bg-[image:var(--gradient-action)] px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-50";
const botonSecundarioCls =
  "rounded-full border border-border px-4 py-2 text-xs font-semibold text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50";

/**
 * Modal normal (no lateral) para activar, cambiar o apagar la contingencia de un tipo (HU #14649).
 * Dos pasos dentro del mismo modal: formulario → confirmación corta → PUT. Si el PUT falla, el modal
 * vuelve al formulario con el motivo exacto en un `role="alert"` y se puede reintentar.
 */
export function ContingenciaRuntEditModal({
  item,
  onClose,
  onSaved,
}: {
  /** Fila que se edita; `null` = cerrado. */
  item: RuntContingencyItem | null;
  onClose: () => void;
  onSaved: (saved: RuntContingencyItem) => void;
}) {
  const uid = useId();
  const [provider, setProvider] = useState<RuntContingencyProvider | "">("");
  const [reason, setReason] = useState("");
  const [tocado, setTocado] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const confirmRef = useRef<HTMLDivElement>(null);

  // Cada apertura parte del estado actual de la fila y de un motivo vacío (el motivo es de CADA cambio).
  const kind = item?.kind;
  useEffect(() => {
    /* eslint-disable react-hooks/set-state-in-effect -- reinicio del borrador al abrir el modal */
    setProvider(item?.provider ?? "");
    setReason("");
    setTocado(false);
    setConfirmando(false);
    setGuardando(false);
    setError(null);
    /* eslint-enable react-hooks/set-state-in-effect */
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo al abrir o cambiar de fila
  }, [kind]);

  useEffect(() => {
    if (confirmando) confirmRef.current?.focus();
  }, [confirmando]);

  if (!item) return null;

  const motivo = reason.trim();
  const sinMotivo = motivo === "";
  const providerId = `${uid}-provider`;
  const reasonId = `${uid}-reason`;
  const tipo = RUNT_CONTINGENCY_KIND_LABEL[item.kind];
  const destino = provider === "" ? "Sin contingencia" : RUNT_CONTINGENCY_PROVIDER_LABEL[provider];

  const guardar = async () => {
    setGuardando(true);
    setError(null);
    try {
      const saved = await putRuntContingency(item.kind, {
        provider: provider === "" ? null : provider,
        reason: motivo,
      });
      onSaved(saved);
    } catch (err) {
      setError(runtContingencyErrorMessage(err, "No se pudo guardar el cambio."));
      setConfirmando(false);
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={guardando}
      icono="ajustes"
      title={`Contingencia · ${tipo}`}
      description={confirmando ? undefined : "Elige con qué proveedor consultar y explica por qué."}
      size="md"
    >
      {confirmando ? (
        <div className="flex flex-col gap-4">
          <div
            ref={confirmRef}
            tabIndex={-1}
            className="text-sm text-flit-primary outline-none"
            data-testid="contingencia-runt-confirmar"
          >
            {provider === ""
              ? `¿Apagar la contingencia de ${tipo}? Volverá a usarse la configuración de cada empresa.`
              : `¿Consultar ${tipo} con ${destino}? Aplica a toda la plataforma.`}
          </div>
          <div className="flex flex-wrap justify-end gap-2">
            <button type="button" onClick={() => setConfirmando(false)} disabled={guardando} className={botonSecundarioCls}>
              Volver
            </button>
            <button type="button" onClick={() => void guardar()} disabled={guardando} className={botonPrimarioCls}>
              {guardando ? "Guardando…" : "Confirmar"}
            </button>
          </div>
        </div>
      ) : (
        <form
          className="flex flex-col gap-4"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            setTocado(true);
            if (!sinMotivo) setConfirmando(true);
          }}
        >
          {error ? (
            // InlineAlert de error ya se anuncia como role="alert".
            <InlineAlert tone="error" title="No se pudo guardar">
              {error}
            </InlineAlert>
          ) : null}

          <div className="flex flex-col gap-1">
            <label htmlFor={providerId} className="text-xs font-semibold text-flit-primary">
              Proveedor
            </label>
            <select
              id={providerId}
              value={provider}
              onChange={(e) => setProvider(e.target.value as RuntContingencyProvider | "")}
              className={`${campoCls} border-input`}
            >
              <option value="">Sin contingencia</option>
              {RUNT_CONTINGENCY_PROVIDERS_BY_KIND[item.kind].map((p) => (
                <option key={p} value={p}>
                  {RUNT_CONTINGENCY_PROVIDER_LABEL[p]}
                </option>
              ))}
            </select>
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor={reasonId} className="text-xs font-semibold text-flit-primary">
              Motivo (obligatorio)
            </label>
            <textarea
              id={reasonId}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              onBlur={() => setTocado(true)}
              maxLength={RUNT_CONTINGENCY_REASON_MAX}
              rows={3}
              required
              aria-required="true"
              aria-invalid={tocado && sinMotivo ? true : undefined}
              aria-describedby={`${reasonId}-hint`}
              className={`${campoCls} ${tocado && sinMotivo ? "border-destructive" : "border-border"}`}
            />
            <p
              id={`${reasonId}-hint`}
              className={`flex justify-between gap-2 text-[11px] leading-snug ${
                tocado && sinMotivo ? "font-semibold text-destructive" : "text-muted-foreground"
              }`}
            >
              <span>{sinMotivo ? "El motivo es obligatorio." : "Queda registrado con tu nombre y la fecha."}</span>
              <span>
                {reason.length}/{RUNT_CONTINGENCY_REASON_MAX}
              </span>
            </p>
          </div>

          <div className="flex flex-wrap justify-end gap-2">
            <button type="button" onClick={onClose} className={botonSecundarioCls}>
              Cancelar
            </button>
            <button type="submit" disabled={sinMotivo} className={botonPrimarioCls}>
              Guardar
            </button>
          </div>
        </form>
      )}
    </Modal>
  );
}
