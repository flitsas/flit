import { Check } from "lucide-react";
import { STEPS, progressText, statusOf, type StepsState } from "@/lib/captura-manual/steps";

/**
 * Barra de 5 pasos de la captura manual (HU #13291), con el aspecto del flujo real de Kyverum:
 * círculos conectados por una línea. Completado = círculo azul con ✓ blanco (y línea azul hacia el
 * siguiente), activo = círculo blanco con borde y anillo azules y número azul, pendiente = gris claro.
 * El progreso se anuncia con aria-live: «Paso N de 5».
 */
export function StepBar({ state }: { state: StepsState }) {
  return (
    <nav aria-label="Progreso de la verificación" className="w-full">
      <p className="sr-only" role="status" aria-live="polite">
        {progressText(state)}
      </p>
      <ol className="flex w-full items-start">
        {STEPS.map((step, i) => {
          const status = statusOf(state, i);
          return (
            <li
              key={step.id}
              className="relative flex min-w-0 flex-1 flex-col items-center gap-1"
              aria-current={status === "active" ? "step" : undefined}
            >
              {i < STEPS.length - 1 ? (
                <span
                  aria-hidden="true"
                  data-testid="step-line"
                  className={"absolute left-1/2 top-4 h-0.5 w-full -translate-y-1/2 " + (status === "done" ? "bg-flit-brand" : "bg-flit-gray")}
                />
              ) : null}
              <span
                className={
                  "relative z-10 flex size-8 items-center justify-center rounded-full text-sm font-semibold " +
                  (status === "done"
                    ? "bg-flit-brand text-white"
                    : status === "active"
                      ? "border-2 border-flit-brand bg-white text-flit-brand ring-4 ring-flit-brand/20"
                      : "bg-flit-gray text-muted-foreground")
                }
              >
                {status === "done" ? (
                  <>
                    <Check aria-hidden="true" className="size-4" />
                    <span className="sr-only">completado, </span>
                  </>
                ) : (
                  <span aria-hidden="true">{i + 1}</span>
                )}
              </span>
              <span
                className={
                  "max-w-full truncate text-xs " +
                  (status === "active" ? "font-bold text-flit-brand" : "text-muted-foreground")
                }
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
