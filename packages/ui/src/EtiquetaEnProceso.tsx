import { etiquetaKit, ETIQUETA_PUNTO_CLS } from "./StatusBadge";

/**
 * Etiqueta «En proceso» con punto pulsante (HU #14562 · UI-1, CF-04): el punto queda quieto con menos movimiento.
 * HU #14562: `nombreAccesible` reemplaza el nombre por defecto («Estado: {etiqueta}»), p. ej. «Estado del lote: En
 * proceso» en el tablero. Sin la prop, el marcado es el de siempre. El chip se pinta en el primer render: si el cambio
 * de estado debe anunciarse, la región viva estable va en la pantalla que lo contiene, no en el chip que se reemplaza.
 */
export function EtiquetaEnProceso({
  etiqueta = "En proceso",
  nombreAccesible,
  className,
}: {
  etiqueta?: string;
  nombreAccesible?: string;
  className?: string;
}) {
  const kit = etiquetaKit("info", { className });
  return (
    <span
      role="status"
      aria-label={nombreAccesible ?? `Estado: ${etiqueta}`}
      data-tono="info"
      data-estado="en-proceso"
      className={kit.className}
      style={kit.style}
    >
      <span aria-hidden="true" data-marca="punto" className={`${ETIQUETA_PUNTO_CLS} motion-safe:animate-pulse`} />
      {etiqueta}
    </span>
  );
}
