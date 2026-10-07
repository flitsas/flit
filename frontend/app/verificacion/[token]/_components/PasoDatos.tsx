"use client";

import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { useId, useState } from "react";
import {
  CAPTURE_TIPS,
  CONSENT_PRIVACY_LABEL,
  CONSENT_TEXT_BODY,
  RENDERED_CONSENT_TEXT_VERSION,
  CONTACT_EMAIL,
  LEGAL_FOOTER_PREFIX,
} from "@/lib/captura-manual/consent";
import {
  ManualCaptureError,
  terminalKindOf,
  type LinkTerminalKind,
  type ManualCaptureClient,
  type ManualCaptureView,
} from "@/lib/captura-manual/types";

/** Paso 1 — Datos (HU #13292): datos de solo lectura, consejos y consentimiento biométrico. */
export function PasoDatos({
  token,
  view,
  client,
  consentRegistered = false,
  onConsentRegistered,
  onDone,
  onTerminal,
}: {
  token: string;
  view: ManualCaptureView;
  client: ManualCaptureClient;
  /** El consentimiento ya se registró en esta sesión (el cliente volvió a Datos con «Atrás»). */
  consentRegistered?: boolean;
  onConsentRegistered?: () => void;
  onDone: () => void;
  /** El enlace dejó de servir (404/410/409 estado_invalido) al registrar el consentimiento. */
  onTerminal?: (kind: LinkTerminalKind) => void;
}) {
  const checkId = useId();
  const [accepted, setAccepted] = useState(consentRegistered);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(false);
  // El backend vigente usa otra versión del texto que la que este front muestra: no se avanza en silencio.
  const [staleText, setStaleText] = useState(false);
  const versionMismatch = staleText || view.consentTextVersion !== RENDERED_CONSENT_TEXT_VERSION;

  async function start() {
    if (versionMismatch) return;
    setSending(true);
    setError(false);
    try {
      // Si ya se registró (volvió con «Atrás»), no se vuelve a enviar: solo se avanza.
      if (!consentRegistered) {
        await client.postConsent(token, { accepted: true, textVersion: view.consentTextVersion });
        onConsentRegistered?.();
      }
      onDone();
    } catch (e) {
      const terminal = terminalKindOf(e);
      if (terminal && onTerminal) {
        onTerminal(terminal);
        return;
      }
      // La casilla se conserva: el cliente reintenta sin volver a marcarla.
      setStaleText(e instanceof ManualCaptureError && e.code === "version_texto_invalida");
      setError(true);
    } finally {
      setSending(false);
    }
  }

  return (
    <section aria-labelledby="paso-datos-titulo" className="mt-6 flex flex-col gap-4">
      <div>
        <h1 id="paso-datos-titulo" className="text-xl font-bold text-flit-primary">
          Hola {view.fullName}
        </h1>
        <p className="mt-1 text-base text-muted-foreground">
          {view.productName?.trim() || "FLIT 2.0"} necesita verificar tu identidad para continuar con tu trámite.
        </p>
      </div>

      <table className="w-full border-collapse text-base">
        <caption className="sr-only">Tus datos</caption>
        <tbody>
          <tr className="border-b border-flit-gray">
            <th scope="row" className="py-2 pr-3 text-left text-sm font-medium text-muted-foreground">
              Nombre
            </th>
            <td className="py-2 text-right font-semibold text-flit-primary">{view.fullName}</td>
          </tr>
          <tr className="border-b border-flit-gray">
            <th scope="row" className="py-2 pr-3 text-left text-sm font-medium text-muted-foreground">
              Documento
            </th>
            <td className="py-2 text-right font-semibold text-flit-primary">
              {view.documentType} {view.documentNumber}
            </td>
          </tr>
        </tbody>
      </table>

      <p className="text-base text-muted-foreground">
        Estos datos ya los tiene quien te envió el enlace. No tienes que escribirlos otra vez. Haremos una
        verificación facial, de tu documento (anverso y reverso) y tu firma. Toma menos de un minuto.
      </p>

      <aside aria-labelledby="consejos-titulo" className="rounded-xl bg-flit-bg p-4">
        <h2 id="consejos-titulo" className="text-base font-semibold text-flit-primary">
          Para que salga bien
        </h2>
        <ul className="mt-2 list-disc space-y-1 pl-5 text-base text-flit-primary">
          {CAPTURE_TIPS.map((tip) => (
            <li key={tip}>{tip}</li>
          ))}
        </ul>
      </aside>

      <div className="flex items-start gap-3">
        <input
          id={checkId}
          type="checkbox"
          checked={accepted}
          disabled={consentRegistered}
          onChange={(e) => setAccepted(e.target.checked)}
          className="mt-1 size-5 shrink-0 accent-flit-brand focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
        />
        <label htmlFor={checkId} className="min-h-11 text-sm text-flit-primary">
          {CONSENT_TEXT_BODY}{" "}
          <span className="underline">{CONSENT_PRIVACY_LABEL}</span>.
        </label>
      </div>

      {versionMismatch ? (
        <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-red-900">
          El texto de consentimiento cambió; recarga la página.
        </p>
      ) : null}

      {error && !staleText ? (
        <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-red-900">
          No pudimos registrar tu autorización. Inténtalo de nuevo.
        </p>
      ) : null}

      <button
        type="button"
        disabled={!accepted || sending || versionMismatch}
        onClick={() => void start()}
        className={`min-h-12 w-full rounded-xl px-4 ${BRAND_BTN} focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand disabled:cursor-not-allowed disabled:opacity-50`}
      >
        {sending ? "Registrando…" : "Iniciar verificación"}
      </button>

      <p className="text-xs text-muted-foreground">
        {LEGAL_FOOTER_PREFIX}{" "}
        <a
          href={`mailto:${CONTACT_EMAIL}`}
          className="underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
        >
          {CONTACT_EMAIL}
        </a>
        .
      </p>
    </section>
  );
}
