// Exportación de la bandeja por partes (HU #13871), como la de Consultas de Trámites: lo filtrado se reparte en archivos de
// hasta 5.000 filas que se descargan en el mismo clic, cada uno con solo las columnas visibles.

/** Filas por archivo: el tamaño cómodo de un Excel, no un tope de lo que se puede exportar (igual que en el servidor). */
export const FILAS_POR_PARTE = 5000;

/** Por encima de estos archivos se pide confirmar antes de empezar, para que nadie dispare 20 descargas por error. */
export const PARTES_SIN_CONFIRMAR = 5;

export const partesDe = (total: number) => Math.max(1, Math.ceil(total / FILAS_POR_PARTE));

/** «comparendos-2026-10-23.xlsx», o «…-parte-1-de-2.xlsx» cuando hay más de un archivo. */
export function nombreParte(hoy: string, parte: number, partes: number): string {
  return `comparendos-${hoy}${partes > 1 ? `-parte-${parte}-de-${partes}` : ""}.xlsx`;
}

const plural = (n: number, uno: string, varios: string) => `${n.toLocaleString("es-CO")} ${n === 1 ? uno : varios}`;

/** Lo que se dice al terminar: cuántos comparendos, en cuántos archivos y con cuántas columnas. */
export function mensajeExportacion(total: number, partes: number, columnas: number): string {
  const archivos = partes > 1 ? ` en ${partes} archivos de hasta ${FILAS_POR_PARTE.toLocaleString("es-CO")} filas cada uno,` : "";
  return `Se ${total === 1 ? "exportó" : "exportaron"} ${plural(total, "comparendo", "comparendos")}${archivos} con ${plural(columnas, "columna visible", "columnas visibles")}.`;
}
