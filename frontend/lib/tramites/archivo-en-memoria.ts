/**
 * El selector del navegador entrega un `File` que sigue apuntando al disco. OCR, hash y subida
 * lo leían cada uno por su cuenta. En Windows esa segunda o tercera lectura falla a veces
 * (`NotReadableError`) si el antivirus, OneDrive o un temporal cerraron el archivo después de
 * elegirlo. La copia en memoria se lee una sola vez y el resto del cargue ya no toca el disco.
 */

export const MENSAJE_ARCHIVO_NO_LEGIBLE =
  'No se pudo leer el archivo. Ciérralo si lo tienes abierto en otro programa, cópialo a una carpeta local (por ejemplo el Escritorio, no una carpeta de OneDrive) y vuelve a adjuntarlo. Si sigue fallando, prueba con otro navegador o desactiva el antivirus un momento y reintenta.';

/** `DOMException` NotReadableError, o la misma frase cuando llega como `Error` plano. */
export function esLecturaDeArchivoDenegada(error: unknown): boolean {
  const name =
    error && typeof error === 'object' && 'name' in error
      ? String((error as { name?: unknown }).name)
      : '';
  const message = error instanceof Error ? error.message : '';
  return name === 'NotReadableError' || /could not be read/i.test(message);
}

/**
 * Bytes del archivo en un `File` nuevo, desconectado del disco.
 * Si la lectura falla, lanza un `Error` con {@link MENSAJE_ARCHIVO_NO_LEGIBLE}.
 */
export async function copiarArchivoEnMemoria(file: File): Promise<File> {
  try {
    const bytes = await file.arrayBuffer();
    return new File([bytes], file.name || 'archivo', {
      type: file.type,
      lastModified: file.lastModified,
    });
  } catch {
    throw new Error(MENSAJE_ARCHIVO_NO_LEGIBLE);
  }
}
