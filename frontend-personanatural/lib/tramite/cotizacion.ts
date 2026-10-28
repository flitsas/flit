// Clave de la caché en memoria de la cotización de un trámite: el «Valor del trámite» de la barra lateral y el «Valor total
// liquidado» del paso Requisitos comparten el valor base, así el ciudadano no resuelve el captcha dos veces para lo mismo.
export const claveCotizacion = (organismoId: string, tipoCodigo: string, adicionales: readonly string[]) =>
  ["pn", "cotizacion", organismoId, tipoCodigo, [...adicionales].sort().join(",")] as const;
