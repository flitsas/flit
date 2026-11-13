"use client";

import { etiquetaKit, ETIQUETA_PUNTO_CLS } from "./StatusBadge";
import { useRegionVivaLista } from "./useRegionViva";

/**
 * Etiqueta «En proceso» con punto pulsante (HU #14562 · UI-1, CF-04): el punto queda quieto con menos movimiento.
 * HU #14562 · M11: se monta como región `aria-live` vacía y recibe el texto un instante después, para que se anuncie.
 */
export function EtiquetaEnProceso({ etiqueta = "En proceso", className }: { etiqueta?: string; className?: string }) {
  const kit = etiquetaKit("info", { className });
  const lista = useRegionVivaLista();
  if (!lista) return <span aria-live="polite" aria-atomic="true" data-tono="info" data-estado="en-proceso" />;
  return (
    <span
      role="status"
      aria-live="polite"
      aria-atomic="true"
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
