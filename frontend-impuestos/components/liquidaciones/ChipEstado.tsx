import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { ETIQUETA_PUNTO_CLS, StatusBadge, etiquetaKit, type StatusBadgeTone } from "@flit/ui/StatusBadge";
import type { Chip } from "@/lib/liquidaciones/estados";

/**
 * Chip de estado de liquidaciones (spec-visual §1, mapa de lib/liquidaciones/estados.ts). `vivo` (cabecera de una
 * pantalla) usa StatusBadge con role="status"; en tablas y listas va `etiquetaKit` + punto, sin role, para no crear
 * decenas de regiones vivas. EN_PROCESO del lote va con EtiquetaEnProceso. Siempre con texto, nunca solo color. La
 * `ayuda` del chip (p. ej. NO_APLICA, servicio público) va como tooltip nativo (`title`); la razón también está en el
 * aviso del detalle de la placa, así que no depende del puntero.
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
  if (vivo) return <StatusBadge tone={chip.tono} label={chip.texto} title={chip.ayuda} />;
  return (
    <Marca tono={chip.tono} title={chip.ayuda}>
      {chip.texto}
    </Marca>
  );
}

/** Chip pequeño sin role (marcas «Parcial», «Posible duplicado», «Resultado tardío» y estados de tabla). */
export function Marca({ tono, title, children }: { tono: StatusBadgeTone; title?: string; children: string }) {
  const kit = etiquetaKit(tono);
  return (
    <span className={kit.className} style={kit.style} title={title}>
      <span aria-hidden="true" className={ETIQUETA_PUNTO_CLS} />
      {children}
    </span>
  );
}
