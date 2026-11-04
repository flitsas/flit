/**
 * Dirección pública del canal OT (HU #14217): `{origen}/o/{codigo}`.
 * El origen es el del portal Persona Natural, no el de la consola admin.
 * Placa y documento no entran en la dirección.
 */

/** Origen público del canal, sin barra final. Vacío si no hay cómo saberlo. */
export function origenPublicoCanal(): string {
  const configurado = (process.env.NEXT_PUBLIC_CANAL_OT_ORIGEN ?? "").trim().replace(/\/+$/, "");
  if (configurado) return configurado;
  if (typeof window === "undefined") return "";
  const { protocol, hostname } = window.location;
  // En local el portal no comparte origen con el admin: escucha en el puerto 4031.
  if (hostname === "localhost" || hostname === "127.0.0.1") {
    return `${protocol}//${hostname}:4031`;
  }
  return "";
}

/** Misma cadena para el texto, el botón Copiar y el QR. */
export function direccionCanalPunto(codigo: string, origen: string): string {
  const base = origen.trim().replace(/\/+$/, "");
  const segmento = encodeURIComponent(codigo.trim());
  return base ? `${base}/o/${segmento}` : `/o/${segmento}`;
}
