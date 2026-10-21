import { Car } from "lucide-react";

/**
 * Loader de marca de la suite. Sus clases (`.flit-loader`, `.flit-vehicle`) viven en `@flit/ui/loader.css`:
 * la app que lo use debe importar esa hoja desde su CSS global (`@import "@flit/ui/loader.css";`).
 */
export function VehicleLoader({ label = "Procesando…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-6">
      <div className="flit-loader" role="status" aria-label={label}>
        <div className="flit-vehicle"><Car className="h-8 w-8" /></div>
      </div>
      <p className="text-xs font-mono uppercase tracking-wider text-flit-primary/70 dark:text-white/60">{label}</p>
    </div>
  );
}