/**
 * Lectura de archivos sueltos y carpetas para el cargue masivo. Vive aparte del componente porque es
 * la parte con reglas de verdad (qué se acepta, qué se ignora, hasta dónde se entra en una carpeta) y
 * conviene poder probarla sin montar la UI.
 *
 * Las carpetas se leen PLANAS a propósito: un operador que arrastra la carpeta del trámite espera que
 * entren sus documentos, no el árbol entero de lo que tenga dentro.
 */

/** Extensiones que el lote sabe leer. WEBP se admite como adjunto pero el modelo de visión no lo lee. */
const EXTENSIONES = ['.pdf', '.jpg', '.jpeg', '.png', '.zip'];

/** true si el archivo tiene una extensión que el lote puede procesar. */
export function esArchivoAdmitido(name: string): boolean {
  const lower = name.toLowerCase();
  return EXTENSIONES.some((ext) => lower.endsWith(ext));
}

/**
 * Descarta lo que ensucia una carga sin aportar nada: archivos ocultos (`.DS_Store`), los metadatos de
 * macOS y las extensiones que no sabemos leer. Se filtra en el cliente para que el operador no gaste
 * una llamada ni vea una lista de errores por basura del sistema de archivos.
 */
export function filtrarUtiles(files: readonly File[]): File[] {
  return files.filter((f) => {
    const nombre = f.name;
    if (!nombre || nombre.startsWith('.')) return false;
    if (nombre.startsWith('__MACOSX')) return false;
    return esArchivoAdmitido(nombre);
  });
}

/**
 * Aplana la selección de un input con `webkitdirectory`: se queda con los archivos del primer nivel de
 * la carpeta elegida y descarta las subcarpetas. El navegador siempre entrega el árbol completo, así
 * que el recorte se hace aquí mirando `webkitRelativePath` (`carpeta/archivo.pdf` = un nivel).
 */
export function soloPrimerNivel(files: readonly File[]): File[] {
  return files.filter((f) => {
    const ruta = (f as File & { webkitRelativePath?: string }).webkitRelativePath;
    if (!ruta) return true; // sin ruta relativa no hay árbol que recortar
    return ruta.split('/').length <= 2;
  });
}

// ── Drag & drop ──────────────────────────────────────────────────────────────

function entryToFile(entry: FileSystemEntry): Promise<File | null> {
  return new Promise((resolve) => {
    const fileEntry = entry as FileSystemFileEntry;
    if (typeof fileEntry.file !== 'function') return resolve(null);
    fileEntry.file(
      (file) => resolve(file),
      () => resolve(null),
    );
  });
}

/**
 * Lee un directorio soltado. `readEntries` devuelve los hijos por tandas, así que hay que llamarlo
 * hasta que entregue una tanda vacía; quedarse con la primera es el error clásico y se lleva por
 * delante las carpetas de más de ~100 archivos.
 */
async function leerDirectorio(entry: FileSystemEntry): Promise<File[]> {
  const dirEntry = entry as FileSystemDirectoryEntry;
  if (typeof dirEntry.createReader !== 'function') return [];
  const reader = dirEntry.createReader();

  const hijos: FileSystemEntry[] = [];
  let tanda: FileSystemEntry[] = [];
  do {
    tanda = await new Promise<FileSystemEntry[]>((resolve) => {
      reader.readEntries(
        (entries) => resolve(entries),
        () => resolve([]),
      );
    });
    hijos.push(...tanda);
  } while (tanda.length > 0);

  // Plano: las subcarpetas se ignoran.
  const archivos = await Promise.all(hijos.filter((h) => h.isFile).map(entryToFile));
  return archivos.filter((f): f is File => f !== null);
}

/**
 * Extrae los archivos de un evento de arrastre, entrando un nivel en las carpetas. Si el navegador no
 * expone la API de entradas (o el arrastre no trae ninguna), cae al `dataTransfer.files` de siempre,
 * que cubre el caso de arrastrar archivos sueltos.
 */
export async function archivosDesdeArrastre(dataTransfer: DataTransfer): Promise<File[]> {
  const items = Array.from(dataTransfer.items ?? []);

  const entries = items
    .map((item) =>
      typeof item.webkitGetAsEntry === 'function' ? item.webkitGetAsEntry() : null,
    )
    .filter((e): e is FileSystemEntry => e !== null);

  if (entries.length === 0) return filtrarUtiles(Array.from(dataTransfer.files ?? []));

  const porEntrada = await Promise.all(
    entries.map(async (entry) => {
      if (entry.isDirectory) return leerDirectorio(entry);
      const file = await entryToFile(entry);
      return file ? [file] : [];
    }),
  );

  return filtrarUtiles(porEntrada.flat());
}
