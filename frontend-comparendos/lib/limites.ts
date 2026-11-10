// Tope de tamaño de un paquete OCR (GDC punto 10). El gateway, core-comparendos y el proxy de Next aceptan 100 MB de
// archivos por petición (más un margen para el sobre del formulario): la pantalla muestra el total y bloquea el envío
// por encima, en vez de dejar que el servidor corte la subida a mitad de camino.
import { ApiError } from "@/lib/api";

const MB = 1024 * 1024;
export const LIMITE_PAQUETE_MB = 100;
export const LIMITE_PAQUETE_BYTES = LIMITE_PAQUETE_MB * MB;

/** Lo mínimo de un archivo elegido que importa aquí (los rechazados no se envían, así que no cuentan). */
interface Elegido {
  archivo: Pick<File, "size">;
  error: string | null;
}

/** Bytes que se van a subir: solo los archivos válidos (los rechazados no viajan en el paquete). */
export function totalPaquete(elegidos: Elegido[]): number {
  return elegidos.reduce((suma, a) => (a.error ? suma : suma + a.archivo.size), 0);
}

/** Megas con un decimal y coma decimal, como el resto de la pantalla en español: 1536 KB → «1,5». */
export function megas(bytes: number): string {
  return (bytes / MB).toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

/** «12,3 de 100 MB». */
export function textoTotal(bytes: number): string {
  return `${megas(bytes)} de ${LIMITE_PAQUETE_MB} MB`;
}

/** null si el paquete cabe; si no, qué hacer. */
export function errorTamanoPaquete(bytes: number): string | null {
  if (bytes <= LIMITE_PAQUETE_BYTES) return null;
  return `Los archivos suman ${megas(bytes)} MB y un paquete admite hasta ${LIMITE_PAQUETE_MB} MB. Quita archivos o pártelos en dos paquetes y cárgalos por separado.`;
}

/**
 * Mensaje de un error al crear el paquete. Un 413 puede llegar sin cuerpo (lo corta un servidor intermedio antes de la
 * API): se explica igual que el bloqueo de la pantalla.
 */
export function mensajeErrorCarga(e: unknown): string {
  if (e instanceof ApiError && e.status === 413)
    return `El paquete supera el tamaño permitido (${LIMITE_PAQUETE_MB} MB). Pártelo en dos paquetes y cárgalos por separado.`;
  return e instanceof Error ? e.message : "No se pudo crear el paquete.";
}
