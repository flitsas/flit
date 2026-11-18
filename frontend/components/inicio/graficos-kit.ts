/**
 * UI Kit 2026 — estilos de las gráficas recharts del Inicio (los mismos que «Seguimiento operativo» usaba en
 * `Dashboard.tsx`): guías horizontales punteadas 3·4 en --kit-linea, ejes de 11 px en gris y el tooltip oscuro.
 */
export const EJE_TICK = { fontSize: 11, fill: "var(--di-text-tertiary)" } as const;

export const REJILLA = { strokeDasharray: "3 4", stroke: "var(--kit-linea)" } as const;

export const TOOLTIP_CONTENIDO = {
  background: "rgba(22,39,68,0.95)",
  border: "none",
  borderRadius: 10,
  color: "white",
  fontSize: 11,
} as const;

export const TOOLTIP_ETIQUETA = { color: "var(--color-flit-tech)", fontWeight: 600 } as const;

export const TOOLTIP_CURSOR = { fill: "color-mix(in srgb, var(--color-flit-brand) 8%, transparent)" } as const;

/** Serie de una gráfica: clave del dato, nombre visible y color (un token del kit). */
export interface SerieGrafica {
  clave: string;
  etiqueta: string;
  color: string;
}
