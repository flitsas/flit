import { Check } from "lucide-react";
import { STEPS, progressText, statusOf, type StepsState } from "@/lib/captura-manual/steps";

/**
 * Barra de 5 pasos de la captura manual (HU #13291). Completado = ✓, activo = resaltado,
 * pendiente = gris. El progreso se anuncia con aria-live: «Paso N de 5».
 */
export function StepBar({ state }: { state: StepsState }) {
  return (
    <nav aria-label="Progreso de la verificación" className="w-full">
      <p className="sr-only" role="status" aria-live="polite">
        {progressText(state)}
      </p>
      <ol className="flex w-full items-start justify-between gap-1">
        {STEPS.map((step, i) => {
          const status = statusOf(state, i);
          return (
            <li
              key={step.id}
              className="flex min-w-0 flex-1 flex-col items-center gap-1"
              aria-current={status === "active" ? "step" : undefined}
            >
              <span
                className={
                  "flex size-8 items-center justify-center rounded-full text-sm font-semibold " +
                  (status === "done"
                    ? "bg-flit-primary text-white"
                    : status === "active"
                      ? "bg-flit-brand text-white ring-2 ring-flit-brand ring-offset-2"
                      : "bg-flit-gray text-flit-primary")
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
                  (status === "pending" ? "text-muted-foreground" : "font-semibold text-flit-primary")
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
