"use client";

import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { useRef, useState } from "react";
import { SignaturePad, type SignaturePadHandle } from "./SignaturePad";
import { AUTORIZACION_FIRMA_INSTRUCTION, AUTORIZACION_FIRMA_TEXT, AUTORIZACION_FIRMA_TITLE } from "@/lib/captura-manual/consent";
import { isComplete, offendingCapture, type CaptureKey, type Captures } from "@/lib/captura-manual/captures";
import {
  ManualCaptureError,
  terminalKindOf,
  type LinkTerminalKind,
  type ManualCaptureClient,
} from "@/lib/captura-manual/types";

export type SubmitOutcome =
  | { kind: "success" }
  | { kind: "terminal"; terminal: LinkTerminalKind }
  | { kind: "consent" }
  | { kind: "repeat"; capture: CaptureKey };

/** Paso 5 — Firma y envío final (HU #13295): un solo POST multipart con las 4 imágenes. */
export function PasoFirma({
  token,
  client,
  captures,
  onSignature,
  onOutcome,
  onSendingChange,
}: {
  token: string;
  client: ManualCaptureClient;
  captures: Captures;
  onSignature: (png: Blob | null) => void;
  onOutcome: (o: SubmitOutcome) => void;
  /** El Flow oculta este paso y muestra la pantalla «Enviando…» mientras dura el POST final. */
  onSendingChange?: (sending: boolean) => void;
}) {
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const padRef = useRef<SignaturePadHandle>(null);
  const inFlight = useRef(false); // evita el doble envío aunque el segundo clic llegue antes del re-render

  async function send() {
    if (inFlight.current || !isComplete(captures)) return;
    inFlight.current = true;
    setSending(true);
    onSendingChange?.(true);
    setError(null);
    try {
      await client.submit(token, captures);
      onOutcome({ kind: "success" });
      return;
    } catch (e) {
      if (e instanceof ManualCaptureError) {
        if (e.status === 409 && e.code === "consentimiento_requerido") return onOutcome({ kind: "consent" });
        const terminal = terminalKindOf(e);
        if (terminal) return onOutcome({ kind: "terminal", terminal });
        if (e.status === 422) {
          // archivo_requerido / firma_requerida: el servidor no recibió alguna de las 4 piezas.
          setError(
            e.code === "firma_requerida"
              ? "Falta tu firma. Trázala de nuevo y envía otra vez."
              : "Falta alguna de las imágenes. Recarga la página para repetir las fotos y envía otra vez.",
          );
          return;
        }
        if (e.status === 413 || e.status === 415) {
          const capture = offendingCapture(e);
          if (capture && capture !== "firma") return onOutcome({ kind: "repeat", capture });
          const what = e.status === 413 ? "Una imagen es demasiado grande" : "El formato de una imagen no es válido";
          setError(
            `${what}. Repite esa captura: borra la firma y trázala de nuevo, o recarga la página para repetir una foto.`,
          );
          return;
        }
      }
      // Error de red o del servidor: las 4 capturas se conservan y se reintenta con el mismo botón.
      setError("No pudimos enviar tu información. Revisa tu conexión e inténtalo de nuevo; no tienes que repetir nada.");
    } finally {
      inFlight.current = false;
      setSending(false);
      onSendingChange?.(false);
    }
  }

  const btn =
    "min-h-12 min-w-0 rounded-xl px-3 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <section aria-labelledby="paso-firma-titulo" hidden={sending} className="mt-6 flex flex-col gap-4">
      <h1 id="paso-firma-titulo" className="text-2xl font-bold text-flit-primary">
        {AUTORIZACION_FIRMA_TITLE}
      </h1>
      {/* Cuadro con altura limitada y scroll interno; enfocable para poder leerlo con el teclado. */}
      <div
        role="region"
        aria-label="Texto de autorización"
        tabIndex={0}
        data-testid="autorizacion-firma"
        className="max-h-40 overflow-y-auto rounded-2xl border border-flit-gray bg-slate-50 p-3 text-xs leading-snug text-muted-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
      >
        {AUTORIZACION_FIRMA_TEXT}
      </div>
      <p className="text-base text-muted-foreground">{AUTORIZACION_FIRMA_INSTRUCTION}</p>
      <SignaturePad ref={padRef} initialBlob={captures.firma} onChange={onSignature} disabled={sending} />
      {/* Alternativa de firma (quien no puede dibujar) pendiente de definición del PO: no se muestra enlace. */}
      <p className="text-sm text-muted-foreground">
        La firma se traza en pantalla y no tiene alternativa de teclado. Si no puedes trazarla, pídele ayuda a FLIT 2.0.
      </p>
      {error ? (
        <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-red-900">
          {error}
        </p>
      ) : null}
      <div className="flex gap-3">
        <button
          type="button"
          onClick={() => padRef.current?.clear()}
          disabled={sending}
          className={`${btn} flex-[2] border-2 border-flit-brand text-base font-semibold text-flit-brand`}
        >
          Borrar
        </button>
        <button type="button" onClick={() => void send()} disabled={!captures.firma || sending} className={`${btn} flex-[3] whitespace-nowrap ${BRAND_BTN}`}>
          Firmar y autorizar
        </button>
      </div>
    </section>
  );
}
