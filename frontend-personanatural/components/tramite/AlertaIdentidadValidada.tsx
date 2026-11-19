import { Check } from "lucide-react";
import type { ReactNode } from "react";

const COLOR_OK = "#8CC63F";

/** «ANA MARIA RUIZ» → «Ana Maria Ruiz». Sin nombre devuelve cadena vacía. */
export function nombrePropio(nombre: string | null | undefined): string {
  return (nombre ?? "")
    .trim()
    .toLocaleLowerCase("es-CO")
    .split(/\s+/)
    .filter(Boolean)
    .map((p) => (["de", "del", "la", "las", "los", "y"].includes(p) ? p : p.charAt(0).toLocaleUpperCase("es-CO") + p.slice(1)))
    .join(" ");
}

/**
 * Alerta verde de identidad validada. El título lleva `role="status"` para que el lector de pantalla lo anuncie
 * cuando la validación cambia a aprobada mientras la persona está en el paso.
 */
export function AlertaIdentidadValidada({ id, children }: { id?: string; children: ReactNode }) {
  return (
    <div className="flex gap-3 rounded-[9px] p-4" style={{ backgroundColor: `${COLOR_OK}18` }} id={id}>
      <Check aria-hidden className="mt-0.5 h-5 w-5 shrink-0" style={{ color: COLOR_OK }} />
      <div className="space-y-1 text-sm">
        <p role="status" className="font-semibold" style={{ color: COLOR_OK }}>
          ¡Identidad validada!
        </p>
        <p className="text-muted-foreground">{children}</p>
      </div>
    </div>
  );
}
