import { Icono } from "@flit/ui/Icono";
import { STEPS, progressText, statusOf, type StepsState } from "@/lib/captura-manual/steps";

/**
 * Barra de 5 pasos de la captura manual (HU #13291), con el aspecto del flujo real de Kyverum:
 * círculos conectados por una línea. Completado = círculo azul con ✓ blanco (y línea azul hacia el
 * siguiente), activo = círculo blanco con borde y anillo azules y número azul, pendiente = gris claro.
 * El progreso se anuncia con aria-live: «Paso N de 5».
 */
/** Estado de la captura → `data-estado` de `.kit-paso` (Manual UI Kit 2026, avance horizontal). */
const ESTADO_KIT = { done: "hecho", active: "actual", pending: "pendiente" } as const;

export function StepBar({ state }: { state: StepsState }) {
  return (
    <nav aria-label="Progreso de la verificación" className="w-full">
      <p className="sr-only" role="status" aria-live="polite">
        {progressText(state)}
      </p>
      <ol className="kit-pasos w-full items-start px-0">
        {STEPS.map((step, i) => {
          const status = statusOf(state, i);
          return (
            <li
              key={step.id}
              className="kit-paso flex min-w-0 flex-col items-center gap-1.5"
              data-estado={ESTADO_KIT[status]}
              aria-current={status === "active" ? "step" : undefined}
            >
              {i < STEPS.length - 1 ? (
                <span
                  aria-hidden="true"
                  data-testid="step-line"
                  className="hidden"
                />
              ) : null}
              <span
                className="kit-paso__num relative z-10"
              >
                {status === "done" ? (
                  <>
                    <Icono nombre="check" className="size-4" />
                    <span className="sr-only">completado, </span>
                  </>
                ) : (
                  <span aria-hidden="true">{i + 1}</span>
                )}
              </span>
              <span
                className="kit-paso__etiqueta max-w-full truncate text-xs"
              >
                <span className="sr-only">{status === "pending" ? `${i + 1}, ` : ""}</span>
                {step.label}
              </span>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
