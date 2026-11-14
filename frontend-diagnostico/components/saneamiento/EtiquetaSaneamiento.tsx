import { ETIQUETA_PUNTO_CLS, etiquetaKit } from "@flit/ui/StatusBadge";
import type { EstadoSaneamiento } from "@/lib/saneamiento/api";
import { etiquetaSaneamiento } from "@/lib/saneamiento/estados";

export interface EtiquetaSaneamientoProps {
  estado: EstadoSaneamiento | null | undefined;
  /** Solo el backoffice (PS-4, PS-5) rotula los anulados (D-06). */
  backoffice?: boolean;
}

/**
 * Etiqueta del estado del saneamiento (spec-ui §1.1, HU #14577): punto + texto completo con la forma de la etiqueta del kit
 * (`etiquetaKit`) y los tokens `--badge-{tono}-*`. Sin `role="status"`: en una tabla de 100 filas serían 100 regiones vivas.
 * Texto y tono salen de `lib/saneamiento/estados.ts`. Sin saneamiento, un ANULADO fuera del backoffice o un estado
 * desconocido no pintan nada (nunca se muestra el código).
 */
export function EtiquetaSaneamiento({ estado, backoffice = false }: EtiquetaSaneamientoProps) {
  const etiqueta = etiquetaSaneamiento(estado, { backoffice });
  if (!etiqueta) return null;
  const { tono, texto } = etiqueta;
  return (
    <span {...etiquetaKit({ c: `var(--badge-${tono}-border)`, ct: `var(--badge-${tono}-fg)` })}>
      <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
      {texto}
    </span>
  );
}
