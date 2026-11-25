import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { ETIQUETA_PUNTO_CLS, StatusBadge, etiquetaKit, type StatusBadgeTone } from "@flit/ui/StatusBadge";
import type { Chip } from "@/lib/liquidaciones/estados";

/**
 * Chip de estado de liquidaciones (spec-visual §1, mapa de lib/liquidaciones/estados.ts). `vivo` (cabecera de una
 * pantalla) usa StatusBadge con role="status"; en tablas y listas va `etiquetaKit` + punto, sin role, para no crear
 * decenas de regiones vivas. EN_PROCESO del lote va con EtiquetaEnProceso. Siempre con texto, nunca solo color.
 */
export function ChipEstado({ chip, vivo = false }: { chip: Chip; vivo?: boolean }) {
  if (chip.enProceso) {
    if (vivo) return <EtiquetaEnProceso etiqueta={chip.texto} />;
    const kit = etiquetaKit("info");
    return (
      <span className={kit.className} style={kit.style} data-estado="en-proceso">
        <span aria-hidden="true" className={`${ETIQUETA_PUNTO_CLS} motion-safe:animate-pulse`} />
        {chip.texto}
      </span>
    );
  }
  if (vivo) return <StatusBadge tone={chip.tono} label={chip.texto} />;
  return <Marca tono={chip.tono}>{chip.texto}</Marca>;
}

/** Chip pequeño sin role (marcas «Parcial», «Posible duplicado», «Resultado tardío» y estados de tabla). */
export function Marca({ tono, children }: { tono: StatusBadgeTone; children: string }) {
  const kit = etiquetaKit(tono);
  return (
    <span className={kit.className} style={kit.style}>
      <span aria-hidden="true" className={ETIQUETA_PUNTO_CLS} />
      {children}
    </span>
  );
}
