// Nombre por defecto de las descargas de la deuda de un vehículo (N-3 / AP-3). Solo se usa si la respuesta no trae
// Content-Disposition (`nombreArchivo` en lib/reportes/api.ts); por eso replica al servidor (PdfDeuda.PlacaSegura):
// la placa en mayúsculas con solo [A-Z0-9] y, si no queda nada, "sin-placa". Nunca la etiqueta visible «Sin placa».

/** Lo que va en el nombre cuando el vehículo no tiene placa utilizable; igual que el servidor. */
export const SIN_PLACA_ARCHIVO = "sin-placa";

/** `deuda-{PLACA}.{extension}` con la placa reducida a [A-Z0-9]; `deuda-sin-placa.{extension}` si no queda nada. */
export function nombreDescargaDeuda(placa: string | null | undefined, extension: "pdf" | "xlsx"): string {
  const segura = (placa ?? "").toUpperCase().replace(/[^A-Z0-9]/g, "");
  return `deuda-${segura || SIN_PLACA_ARCHIVO}.${extension}`;
}
