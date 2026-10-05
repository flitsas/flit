import { Lock } from "lucide-react";
import { LEYENDA_CONFIGURADO_POR_ORGANISMO } from "@/lib/plataforma/mandatario-permisos";

/**
 * HU #13139 — candado con leyenda bajo el nombre del mandatario que configuró el organismo de
 * tránsito. El estado se lee en el texto y el ícono, no solo en el color gris de la fila.
 */
export function MandatarioCandado() {
  return (
    <span
      className="mt-0.5 flex items-center gap-1 text-[11px] font-medium text-[#59677D] dark:text-white/60"
      data-testid="mandatario-candado"
    >
      <Lock className="h-3 w-3 shrink-0" aria-hidden={true} />
      {LEYENDA_CONFIGURADO_POR_ORGANISMO}
    </span>
  );
}
