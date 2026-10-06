"use client";

import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { useRef, useState } from "react";
import { SignaturePad } from "./SignaturePad";
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
  onBack,
}: {
  token: string;
  client: ManualCaptureClient;
  captures: Captures;
  onSignature: (png: Blob | null) => void;
  onOutcome: (o: SubmitOutcome) => void;
  onBack: () => void;
}) {
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef(false); // evita el doble envío aunque el segundo clic llegue antes del re-render

  async function send() {
    if (inFlight.current || !isComplete(captures)) return;
    inFlight.current = true;
    setSending(true);
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
        if (e.status === 413 || e.status === 415) {
          const capture = offendingCapture(e);
          if (capture && capture !== "firma") return onOutcome({ kind: "repeat", capture });
          const what = e.status === 413 ? "Una imagen es demasiado grande" : "El formato de una imagen no es válido";
          setError(
            `${what}. Repite esa captura: borra la firma y trázala de nuevo, o vuelve con «Atrás» para repetir una foto.`,
          );
          return;
        }
      }
      // Error de red o del servidor: las 4 capturas se conservan y se reintenta con el mismo botón.
      setError("No pudimos enviar tu información. Revisa tu conexión e inténtalo de nuevo; no tienes que repetir nada.");
    } finally {
      inFlight.current = false;
      setSending(false);
    }
  }

  const btn =
    "min-h-11 rounded-xl px-4 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <section aria-labelledby="paso-firma-titulo" className="mt-6 flex flex-col gap-4">
      <div>
        <h1 id="paso-firma-titulo" className="text-xl font-bold text-flit-primary">
          Tu firma
        </h1>
        {/* Aspecto del paso: SUPUESTO, no se vio en Kyverum. */}
        <p className="mt-1 text-base text-muted-foreground">
          Traza tu firma con el dedo o el mouse dentro del recuadro, como la haces en tu documento.
        </p>
      </div>
      <SignaturePad initialBlob={captures.firma} onChange={onSignature} disabled={sending} />
      <p className="text-sm text-muted-foreground">
        La firma se traza en pantalla y no tiene alternativa de teclado. Si no puedes trazarla, pídele ayuda a FLIT 2.0.
      </p>
      {error ? (
        <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-red-900">
          {error}
        </p>
      ) : null}
      <div className="flex gap-3">
        <button type="button" onClick={onBack} disabled={sending} className={`${btn} border border-flit-brand-ink text-base font-semibold text-flit-brand-ink`}>
          Atrás
        </button>
        <button
          type="button"
          onClick={() => void send()}
          disabled={!captures.firma || sending}
          className={`${btn} flex-1 ${BRAND_BTN}`}
        >
          {sending ? "Enviando…" : "Finalizar"}
        </button>
      </div>
    </section>
  );
}
