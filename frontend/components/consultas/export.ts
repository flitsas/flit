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
