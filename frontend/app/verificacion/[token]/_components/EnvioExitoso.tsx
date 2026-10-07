import { CheckCircle2 } from "lucide-react";

/**
 * AlertCard verde de éxito (patrón de feedback FLIT; no existe un componente compartido con ese nombre,
 * se compone con los tokens badge-success). No promete borrado de imágenes (riesgo Habeas Data) ni pide pasos; solo confirma la recepción.
 */
export function EnvioExitoso() {
  return (
    <div
      role="status"
      data-testid="alert-card-exito"
      className="mt-6 flex flex-col items-center gap-2 rounded-2xl border border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] p-6 text-center"
    >
      <CheckCircle2 aria-hidden="true" className="size-10 text-[var(--badge-success-fg)]" />
      <h1 className="text-xl font-bold text-[var(--badge-success-fg)]">Recibimos tu información</h1>
    </div>
  );
}
