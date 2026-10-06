"use client";

// HU #13248 (F9 #13245) — ficha del mandatario con el estado de SU validación de identidad y el botón
// «Reenviar validación». Solo cuenta la validación lanzada para este mandatario (no la del comprador
// ni la del módulo Identidad). Aparece únicamente con Persona natural y forma de firma «Validación de
// identidad»; con Baúl de firmas, Persona jurídica o Formato en blanco no se muestra nada.

import { useState } from "react";
import { Send } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import type { MandateSigner, MandateSignerIdentityResend } from "@/lib/api/admin-mandate-signers";
import {
  mensajeErrorReenvio,
  mensajeReenvio,
  presentarValidacion,
  puedeReenviarValidacion,
  requiereValidacionPropia,
} from "@/lib/plataforma/mandatario-validacion";

export function MandatarioIdentidadBlock({
  signer,
  onResend,
}: {
  signer: MandateSigner;
  /** Reenvía la validación (compañía u hub OT). Sin esta prop no se ofrece el botón. */
  onResend?: () => Promise<MandateSignerIdentityResend>;
}) {
  const [sending, setSending] = useState(false);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Tras un reenvío correcto la validación queda «en curso» hasta que la lista se recargue.
  const [enviada, setEnviada] = useState(false);

  if (!requiereValidacionPropia(signer)) return null;

  const estado = enviada && signer.identityStatus !== "valid" ? "pending" : signer.identityStatus;
  const vista = presentarValidacion(estado);
  const mostrarBoton = onResend != null && puedeReenviarValidacion(signer);

  const reenviar = async () => {
    if (!onResend || sending) return;
    setSending(true);
    setMensaje(null);
    setError(null);
    try {
      const result = await onResend();
      setEnviada(true);
      setMensaje(mensajeReenvio(result, signer.email));
    } catch (err) {
      setError(mensajeErrorReenvio(err));
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="rounded-xl border p-3" data-testid="mandatario-identidad">
      <p className="mb-1.5 text-xs font-semibold">Validación de identidad</p>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span data-testid="mandatario-validacion-estado" data-estado={vista.estado}>
          <StatusBadge tone={vista.tone} label={vista.texto} ariaLabel={`Validación: ${vista.texto}`} />
        </span>
        {mostrarBoton && (
          <button
            type="button"
            onClick={() => void reenviar()}
            disabled={sending}
            className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:opacity-50"
          >
            <Send className="h-3.5 w-3.5" aria-hidden="true" />
            {sending ? "Enviando…" : "Reenviar validación"}
          </button>
        )}
      </div>
      <p className="mt-1 text-[11px] leading-tight opacity-70" data-testid="mandatario-validacion-detalle">
        {vista.detalle}
      </p>
      {mensaje && (
        <p className="mt-1 text-[11px] leading-tight" style={{ color: "#3f7a15" }} role="status" data-testid="mandatario-validacion-mensaje">
          {mensaje}
        </p>
      )}
      {error && (
        <p className="mt-1 text-[11px] leading-tight" style={{ color: "#E5484D" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
