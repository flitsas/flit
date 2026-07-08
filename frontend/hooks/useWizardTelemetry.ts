"use client";

import { useEffect, useRef } from "react";
import { flushTelemetry, trackEvent } from "@/lib/telemetry";

/**
 * Telemetría del wizard de trámites (Reportes 2.0 · HU-A, contrato §7).
 * Observa el paso activo (server-driven) y expone callbacks para que la shell
 * del wizard emita los eventos SIN reordenar su lógica:
 *   · wizard_step_view      — al cambiar `activeStep?.key` (aquí, vía effect).
 *   · wizard_step_complete  — cuando handleContinue avanza con éxito (duración de permanencia).
 *   · wizard_step_exit      — al navegar hacia atrás/saltar con goToStep (duración).
 *   · wizard_abandon        — salida explícita (Cancelar/Volver) sin radicar.
 *   · wizard_complete       — trámite radicado (duración total desde el montaje).
 * Todo fire-and-forget: la telemetría nunca lanza ni afecta al wizard.
 */
export function useWizardTelemetry(
  instanceId: string | null,
  activeStepKey: string | undefined,
) {
  const instanceRef = useRef<string | null>(instanceId);
  const currentStepRef = useRef<string | undefined>(undefined);
  const stepEnteredAtRef = useRef<number>(0);
  const wizardStartedAtRef = useRef<number>(0);
  const completedRef = useRef(false);

  useEffect(() => {
    instanceRef.current = instanceId;
  }, [instanceId]);

  useEffect(() => {
    const now = Date.now();
    stepEnteredAtRef.current = now;
    wizardStartedAtRef.current = now;
  }, []);

  // wizard_step_view al cambiar el paso activo (mismo patrón de detección que el
  // effect de preflight del wizard: reacciona a activeStep?.key).
  useEffect(() => {
    if (!activeStepKey || activeStepKey === currentStepRef.current) return;
    currentStepRef.current = activeStepKey;
    stepEnteredAtRef.current = Date.now();
    trackEvent({
      eventType: "wizard_step_view",
      module: "tramites",
      stepKey: activeStepKey,
      procedureInstanceId: instanceRef.current,
    });
  }, [activeStepKey]);

  const stepDuration = () => Math.max(0, Date.now() - stepEnteredAtRef.current);

  return {
    /** Paso completado (avance con éxito): duración de permanencia en el paso. */
    trackStepComplete(): void {
      if (!currentStepRef.current) return;
      trackEvent({
        eventType: "wizard_step_complete",
        module: "tramites",
        stepKey: currentStepRef.current,
        procedureInstanceId: instanceRef.current,
        durationMs: stepDuration(),
      });
    },

    /** Salida del paso sin completarlo (retroceso/salto): duración de permanencia. */
    trackStepExit(): void {
      if (!currentStepRef.current) return;
      trackEvent({
        eventType: "wizard_step_exit",
        module: "tramites",
        stepKey: currentStepRef.current,
        procedureInstanceId: instanceRef.current,
        durationMs: stepDuration(),
      });
    },

    /** Salida/cancelación explícita del wizard sin radicar (último paso visto). */
    trackAbandon(): void {
      if (completedRef.current) return;
      trackEvent({
        eventType: "wizard_abandon",
        module: "tramites",
        stepKey: currentStepRef.current,
        procedureInstanceId: instanceRef.current,
      });
      void flushTelemetry();
    },

    /** Trámite radicado desde el wizard: duración total desde el montaje. */
    trackComplete(): void {
      completedRef.current = true;
      trackEvent({
        eventType: "wizard_complete",
        module: "tramites",
        procedureInstanceId: instanceRef.current,
        durationMs: Math.max(0, Date.now() - wizardStartedAtRef.current),
      });
      void flushTelemetry();
    },
  };
}
