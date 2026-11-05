import type { CSSProperties, ReactNode } from "react";

// Piezas comunes de las vistas del detalle de celda (HU #13801), también para la grilla de backoffice (F06-H5). Todo valor
// visual sale de los tokens de @flit/ui (tokens.css): cabecera de tabla gris de marca, borde de fila y tintas AA.

/** Cabecera de tabla del sistema: barra gris de marca (--table-header-*), versalita de 10px (misma que la grilla). */
export const CABECERA_TABLA_CLS = "px-3 py-2 text-left text-[10px] font-semibold uppercase tracking-wider";
export const CABECERA_TABLA_ESTILO: CSSProperties = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" };
export const CELDA_TABLA_CLS = "px-3 py-2 align-top text-xs text-flit-primary dark:text-white";
export const FILA_TABLA_CLS = "border-t border-[var(--table-row-border)]";
export const TEXTO_SECUNDARIO_CLS = "text-xs text-[var(--nav-texto)] dark:text-slate-300";

/** Tarjeta interna del detalle (borde gris de marca, radio 12px). */
export const TARJETA_DETALLE_CLS = "rounded-xl border border-[var(--color-flit-gray)] p-4 dark:border-white/10";

/** Lista de datos rótulo/valor en dos columnas. */
export function ListaDatos({ children }: { children: ReactNode }) {
  return <dl className="grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2">{children}</dl>;
}

export function Dato({ rotulo, children }: { rotulo: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className={TEXTO_SECUNDARIO_CLS}>{rotulo}</dt>
      <dd className="break-all text-sm font-medium text-flit-primary dark:text-white">{children}</dd>
    </div>
  );
}

export type TonoEtiqueta = "success" | "warning" | "danger" | "info" | "neutral";

/**
 * Etiqueta tintada con los tokens de badge de @flit/ui. Sin role="status": es contenido estático del detalle, no un aviso
 * vivo. Lleva siempre texto (y, si el estado lo amerita, icono), nunca solo color.
 */
export function Etiqueta({ tono, children }: { tono: TonoEtiqueta; children: ReactNode }) {
  // El rojo usa la tinta --badge-pending-fg (AA sobre el fondo tintado), como el semáforo de la grilla.
  const color = tono === "danger" ? "var(--badge-pending-fg)" : `var(--badge-${tono}-fg)`;
  return (
    <span
      className="inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-semibold"
      style={{ background: `var(--badge-${tono}-bg)`, color, borderColor: `var(--badge-${tono}-border)` }}
    >
      {children}
    </span>
  );
}

/** Mensaje de estado vacío dentro del detalle. */
export function Vacio({ children }: { children: ReactNode }) {
  return <p className={`${TARJETA_DETALLE_CLS} text-sm text-[var(--nav-texto)] dark:text-slate-300`}>{children}</p>;
}
