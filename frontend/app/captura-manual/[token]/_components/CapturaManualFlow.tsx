"use client";

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
import { StepPlaceholder } from "./StepPlaceholder";

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
            className="min-h-11 rounded-xl bg-flit-brand px-6 text-base font-semibold text-flit-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand"
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
      <StepBar state={steps} />
      {steps.finished ? (
        <p className="mt-6 text-base text-flit-primary" role="status">
          Flujo completado.
        </p>
      ) : steps.current === 0 ? (
        <PasoDatos token={token} view={load.view} client={api} onDone={() => dispatch({ type: "next" })} />
      ) : (
        <StepPlaceholder
          label={step.label}
          onNext={() => dispatch({ type: "next" })}
          onBack={() => dispatch({ type: "back" })}
        />
      )}
    </CaptureCard>
  );
}
