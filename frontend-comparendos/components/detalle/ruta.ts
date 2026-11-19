import type { SeccionDetalle } from "@/components/detalle/Timeline";

/** Adónde vuelve el detalle si no le dicen de dónde vino. */
export const VOLVER_POR_DEFECTO = "/bandeja";

/**
 * Ruta del detalle del comparendo (rediseño S1). `volver` es la pantalla de origen con su query, para regresar igual;
 * `seccion` abre y ubica esa sección al llegar.
 */
export function rutaDetalle(id: string, opciones: { volver?: string | null; seccion?: SeccionDetalle | null } = {}): string {
  const p = new URLSearchParams();
  if (opciones.seccion) p.set("seccion", opciones.seccion);
  if (opciones.volver) p.set("volver", opciones.volver);
  const q = p.toString();
  return `/comparendos/${encodeURIComponent(id)}${q ? `?${q}` : ""}`;
}

/** La ruta actual del navegador con su query, para usarla como `volver`. */
export function rutaActual(): string {
  return `${window.location.pathname}${window.location.search}`;
}

/**
 * Solo se vuelve a una ruta de esta misma app: empieza por «/», no por «//» ni «/\» (otro dominio), sin esquema ni
 * saltos de línea. Lo demás se ignora en silencio y se usa la bandeja.
 */
export function volverSeguro(valor: string | null | undefined): string {
  if (!valor || !valor.startsWith("/") || valor.startsWith("//") || valor.startsWith("/\\")) return VOLVER_POR_DEFECTO;
  if (/[\u0000-\u001f\\]/.test(valor) || /^\/[a-z][a-z0-9+.-]*:/i.test(valor)) return VOLVER_POR_DEFECTO;
  return valor;
}

const ORIGENES: ReadonlyArray<[RegExp, string]> = [
  [/^\/bandeja/, "Bandeja"],
  [/^\/mensajes/, "Centro de mensajes"],
  [/^\/ocr\/paquetes\//, "Paquete OCR"],
  [/^\/ocr\/documentos\//, "Documento OCR"],
  [/^\/reportes\/productividad/, "Productividad"],
];

/** El nombre de la pantalla de origen para la miga («Bandeja › 1100100000012345»). */
export function nombreOrigen(volver: string): string {
  return ORIGENES.find(([re]) => re.test(volver))?.[1] ?? "Volver";
}

/**
 * Capas que atienden Escape antes que la página: diálogos del kit (Modal, PanelLateral, la hoja del dock en celular),
 * menús, los menús de cuenta y productos de la barra y el grupo abierto del dock.
 */
export const SELECTOR_CAPAS =
  '[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"], #flit-suite-account, #flit-suite-products, ' +
  'nav[aria-label="Navegación principal"] [aria-expanded="true"]';

/** ¿Hay una capa abierta? Se pregunta en la fase de CAPTURA, antes de que la capa se cierre con ese mismo Escape. */
export function hayCapaAbierta(doc: Pick<Document, "querySelector">): boolean {
  return doc.querySelector(SELECTOR_CAPAS) !== null;
}

/**
 * Escape vuelve solo si al pulsarlo no había ninguna capa abierta (la capa se cierra sola y la página se queda) y no se
 * está escribiendo en un campo. `habiaCapa` se mide en la captura: en el burbujeo la capa ya puede haberse desmontado.
 */
export function escapeVuelve(e: Pick<KeyboardEvent, "key" | "defaultPrevented" | "target">, habiaCapa: boolean): boolean {
  if (e.key !== "Escape" || e.defaultPrevented || habiaCapa) return false;
  const t = e.target as HTMLElement | null;
  const etiqueta = t?.tagName;
  if (etiqueta === "INPUT" || etiqueta === "TEXTAREA" || etiqueta === "SELECT" || t?.isContentEditable) return false;
  return true;
}
