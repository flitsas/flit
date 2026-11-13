import { etiquetaKit, ETIQUETA_PUNTO_CLS } from "./StatusBadge";

/** Etiqueta «En proceso» con punto pulsante (HU #14562 · UI-1, CF-04): el punto queda quieto con menos movimiento. */
export function EtiquetaEnProceso({ etiqueta = "En proceso", className }: { etiqueta?: string; className?: string }) {
  const kit = etiquetaKit("info", { className });
  return (
    <span
      role="status"
      aria-label={`Estado: ${etiqueta}`}
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
