// Evidencia del saneamiento (spec-ui §6.3.2, plan §6.4, SP-16 y SP-17; HU #14577 AC32): comprobación en el cliente antes
// de subir. Un solo archivo, de hasta 10 MB, con la firma `%PDF-` en el byte 0 y `%%EOF` en los últimos 1024 bytes. Es
// ayuda de interfaz: el servidor vuelve a validar todo (tipo, firma, fin y tamaño) y manda.
import { MAXIMO_EVIDENCIA_BYTES } from "@/lib/backoffice/saneamientos";

export const MENSAJE_NO_ES_PDF = "El archivo no es un PDF válido.";
export const MENSAJE_UN_SOLO_ARCHIVO = "Elige un solo archivo PDF.";
export const MENSAJE_MUY_GRANDE = "El archivo supera 10 MB.";

const FIRMA = [0x25, 0x50, 0x44, 0x46, 0x2d]; // %PDF-
const FIN = [0x25, 0x25, 0x45, 0x4f, 0x46]; // %%EOF

function leer(blob: Blob): Promise<Uint8Array> {
  return new Promise((resolver, rechazar) => {
    const lector = new FileReader();
    lector.onload = () => resolver(new Uint8Array(lector.result as ArrayBuffer));
    lector.onerror = () => rechazar(lector.error);
    lector.readAsArrayBuffer(blob);
  });
}

function contiene(bytes: Uint8Array, patron: number[]): boolean {
  for (let i = 0; i + patron.length <= bytes.length; i++) if (patron.every((b, j) => bytes[i + j] === b)) return true;
  return false;
}

/** null si el archivo puede subirse; si no, el mensaje fijo del problema (nunca el nombre del archivo). */
export async function validarEvidenciaPdf(archivo: File): Promise<string | null> {
  if (!archivo.name.toLowerCase().endsWith(".pdf")) return MENSAJE_NO_ES_PDF;
  if (archivo.size === 0) return MENSAJE_NO_ES_PDF;
  if (archivo.size > MAXIMO_EVIDENCIA_BYTES) return MENSAJE_MUY_GRANDE;
  try {
    const inicio = await leer(archivo.slice(0, FIRMA.length));
    if (!FIRMA.every((b, i) => inicio[i] === b)) return MENSAJE_NO_ES_PDF;
    const final = await leer(archivo.slice(Math.max(0, archivo.size - 1024)));
    return contiene(final, FIN) ? null : MENSAJE_NO_ES_PDF;
  } catch {
    return MENSAJE_NO_ES_PDF;
  }
}
