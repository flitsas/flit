// Clave de la caché en memoria de la cotización de un trámite: el «Valor del trámite» de la barra lateral y el «Valor total
// liquidado» del paso Requisitos comparten la misma consulta, así no se pide dos veces.
export const claveCotizacion = (organismoId: string, tipoCodigo: string, adicionales: readonly string[]) =>
  ["pn", "cotizacion", organismoId, tipoCodigo, [...adicionales].sort().join(",")] as const;

/** Transformaciones que el paso Requisitos tiene activas. La barra lateral cotiza con este mismo conjunto. */
export const claveAdicionalesActivos = ["pn", "cotizacion-adicionales"] as const;
