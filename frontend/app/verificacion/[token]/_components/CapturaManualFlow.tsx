"use client";

import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { useEffect, useMemo, useReducer, useState } from "react";
import { getManualCaptureClient } from "@/lib/captura-manual/client";
import { initialStepsState, STEPS, stepsReducer } from "@/lib/captura-manual/steps";
import {
  terminalKindOf,
  type LinkTerminalKind,
  type ManualCaptureClient,
  type ManualCaptureView,
} from "@/lib/captura-manual/types";
import { CaptureCard } from "./CaptureCard";
import { LinkTerminal } from "./LinkTerminal";
import { StepBar } from "./StepBar";
import { PasoDatos } from "./PasoDatos";
import { PasoCaptura } from "./PasoCaptura";
import { PasoFirma, type SubmitOutcome } from "./PasoFirma";
import { EnvioExitoso } from "./EnvioExitoso";
import { EnviandoInfo } from "./EnviandoInfo";
import { CAPTURE_STEP_INDEX, STEP_INDEX_DATOS, type Captures } from "@/lib/captura-manual/captures";

type Load =
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "terminal"; terminal: LinkTerminalKind }
  | { kind: "ready"; view: ManualCaptureView };

/**
 * Orquesta la página: carga la sesión por token (4 estados: cargando, error, terminal, lista) y
 * muestra la barra de 5 pasos. `client` es inyectable para pruebas.
 */
export function CapturaManualFlow({ token, client }: { token: string; client?: ManualCaptureClient }) {
  const api = useMemo(() => client ?? getManualCaptureClient(), [client]);
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [steps, dispatch] = useReducer(stepsReducer, initialStepsState);

  const [attempt, setAttempt] = useState(0);
  // Las imágenes (Blob) viven solo en memoria del flujo hasta el envío final; recargar reinicia en Datos.
  const [captures, setCaptures] = useState<Captures>({});
  // El consentimiento ya se registró: volver a Datos no lo re-envía ni desmarca la casilla.
  const [consented, setConsented] = useState(false);
  // Aviso al volver a un paso por un error del envío (413/415 o consentimiento requerido).
  const [notice, setNotice] = useState<string | null>(null);
  // Mientras se envía el POST final: barra con los 5 pasos completados + spinner (sin botones).
  const [sending, setSending] = useState(false);

  useEffect(() => {
    let cancelled = false;
    api
      .getManualCapture(token)
      .then((view) => !cancelled && setLoad({ kind: "ready", view }))
      .catch((e: unknown) => {
        if (cancelled) return;
        const terminal = terminalKindOf(e);
        setLoad(terminal ? { kind: "terminal", terminal } : { kind: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [api, token, attempt]);

  function onOutcome(o: SubmitOutcome) {
    if (o.kind === "success") {
      setNotice(null);
      dispatch({ type: "next" }); // completa el paso 5: finished
    } else if (o.kind === "terminal") {
      setLoad({ kind: "terminal", terminal: o.terminal });
    } else if (o.kind === "consent") {
      setConsented(false);
      setNotice("Necesitamos tu autorización para continuar. Márcala de nuevo y vuelve a enviar.");
      dispatch({ type: "goto", index: STEP_INDEX_DATOS });
    } else {
      setNotice("Una de tus imágenes es demasiado grande o su formato no es válido. Repite esa captura.");
      dispatch({ type: "goto", index: CAPTURE_STEP_INDEX[o.capture] });
    }
  }

  const retry = () => {
    setLoad({ kind: "loading" });
    setAttempt((n) => n + 1);
  };

  if (load.kind === "loading") {
    return (
      <CaptureCard>
        <div role="status" aria-live="polite" className="py-10 text-center text-base text-muted-foreground">
          Cargando…
        </div>
      </CaptureCard>
    );
  }
  if (load.kind === "error") {
    return (
      <CaptureCard>
        <div role="alert" className="flex flex-col items-center gap-3 py-6 text-center">
          <p className="text-base text-flit-primary">No pudimos cargar la página. Revisa tu conexión e inténtalo de nuevo.</p>
          <button
            type="button"
            onClick={retry}
            className={`min-h-11 rounded-xl px-6 ${BRAND_BTN} focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand`}
          >
            Reintentar
          </button>
        </div>
      </CaptureCard>
    );
  }
  if (load.kind === "terminal") {
    return (
      <CaptureCard>
        <LinkTerminal kind={load.terminal} />
      </CaptureCard>
    );
  }

  const step = STEPS[steps.current];
  return (
    <CaptureCard productName={load.view.productName}>
      <StepBar state={sending ? { current: STEPS.length - 1, completed: STEPS.map((_, i) => i), finished: false } : steps} />
      {sending ? <EnviandoInfo /> : null}
      {notice && !steps.finished && !sending ? (
        <p role="alert" className="mt-4 rounded-xl bg-red-50 p-3 text-sm text-red-900">
          {notice}
        </p>
      ) : null}
      {steps.finished ? (
        <EnvioExitoso />
      ) : step.id === "datos" ? (
        <PasoDatos
          token={token}
          view={load.view}
          client={api}
          consentRegistered={consented}
          onConsentRegistered={() => setConsented(true)}
          onTerminal={(terminal) => setLoad({ kind: "terminal", terminal })}
          onDone={() => {
            setNotice(null);
            dispatch({ type: "next" });
          }}
        />
      ) : step.id === "firma" ? (
        <PasoFirma
          token={token}
          client={api}
          captures={captures}
          onSignature={(png) => setCaptures((c) => ({ ...c, firma: png ?? undefined }))}
          onOutcome={onOutcome}
          onSendingChange={setSending}
        />
      ) : (
        <PasoCaptura
          key={step.id}
          kind={step.id}
          blob={captures[step.id]}
          onCaptured={(blob) => {
            setNotice(null);
            setCaptures((c) => ({ ...c, [step.id]: blob }));
            dispatch({ type: "next" });
          }}
        />
      )}
    </CaptureCard>
  );
}
