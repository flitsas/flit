/**
 * El nombre del archivo lleva el nombre de la consulta cuando lo tiene. Media docena de
 * `consulta-2026-07-01-a-2026-07-31.xlsx` en Descargas son indistinguibles entre sí.
 */
export function queryFileName(
  prefix: string,
  nombre: string | null,
  from: string,
  to: string,
  ext: "csv" | "xlsx",
  parte?: { numero: number; total: number },
): string {
  const base = nombre
    ? nombre
        .toLowerCase()
        .normalize("NFD")
        .replace(/[̀-ͯ]/g, "")
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
    : "";

  const sufijoParte = parte && parte.total > 1 ? `-parte-${parte.numero}-de-${parte.total}` : "";

  return `${base || prefix}-${from}-a-${to}${sufijoParte}.${ext}`;
}

/** Descarga de un archivo generado. Se revoca la URL para no dejar el blob retenido toda la sesión. */
export function download(content: BlobPart, fileName: string, mime: string): void {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

/**
 * Filas por archivo al exportar. No es un límite de lo que el usuario puede ver, sino el tamaño
 * cómodo de UN archivo de Excel: por encima de esto, abrirlo empieza a doler.
 */
export const EXPORT_BATCH_SIZE = 5000;

/**
 * Recorre TODO el resultado —no la página a la vista— y lo reparte en archivos de hasta
 * {@link EXPORT_BATCH_SIZE} filas.
 *
 * Exportar solo lo visible sería una trampa: el archivo parecería completo y nadie lo
 * comprobaría. Y truncar a un tope obligaría al usuario a re-acotar la búsqueda para ver las
 * últimas filas. Por eso se reparte, y cada archivo se dispara apenas se arma —dentro de la misma
 * interacción del clic— en vez de esperar a que el usuario pida «el resto».
 *
 * @param total Filas que cumplen los filtros, según la primera respuesta.
 * @param traerPagina Devuelve una página; lista vacía corta el recorrido y evita un bucle infinito
 *   si el total y las páginas dejan de cuadrar.
 * @param volcar Recibe cada lote junto con su número de archivo y el total de archivos.
 * @returns Cuántas filas se llegaron a exportar y en cuántos archivos.
 */
export async function exportarPorLotes<T>(opciones: {
  total: number;
  pageSize: number;
  batchSize?: number;
  traerPagina: (page: number, pageSize: number) => Promise<readonly T[]>;
  volcar: (lote: T[], parte: { numero: number; total: number }) => void;
}): Promise<{ exportadas: number; archivos: number }> {
  const { total, pageSize, traerPagina, volcar } = opciones;
  const batchSize = opciones.batchSize ?? EXPORT_BATCH_SIZE;
  const totalArchivos = Math.max(1, Math.ceil(total / batchSize));

  let lote: T[] = [];
  let numeroArchivo = 1;
  let exportadas = 0;
  let pagina = 1;

  const volcarLote = () => {
    if (lote.length === 0) return;
    volcar(lote, { numero: numeroArchivo, total: totalArchivos });
    exportadas += lote.length;
    numeroArchivo += 1;
    lote = [];
  };

  while (exportadas + lote.length < total) {
    const filas = await traerPagina(pagina, pageSize);
    if (filas.length === 0) break;
    lote.push(...filas);
    pagina += 1;

    if (lote.length >= batchSize) {
      volcarLote();
    }
  }
  volcarLote();

  return { exportadas, archivos: numeroArchivo - 1 };
}
