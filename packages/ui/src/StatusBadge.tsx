import type { CSSProperties, ReactNode } from "react";

/** Tones semánticos del estado (HU #10844). Fuente única de color en globals.css. */
export type StatusTone = "success" | "warning" | "danger" | "info" | "neutral";

/**
 * Tonos que acepta el chip: los cinco semánticos más `purple` (integraciones/RUNT) y `tinta`, que llegan con el
 * UI kit «Diseño Inteligente» (HU #14476). Van aparte de {@link StatusTone} para no romper a quien la usa como
 * enumeración completa (`Record<StatusTone, …>`).
 */
export type StatusBadgeTone = StatusTone | "purple" | "tinta";

export interface StatusBadgeStyle {
  /** @deprecated Usa `tone`. Fondo tintado crudo (rgba translúcido). */
  bg?: string;
  /** @deprecated Usa `tone`. Color del texto crudo. */
  color?: string;
  /** @deprecated Usa `tone`. Color del borde crudo. */
  border?: string;
}

export interface StatusBadgeProps extends StatusBadgeStyle {
  label: ReactNode;
  /** Tone semántico: aplica la paleta unificada (globals.css), theme-aware. */
  tone?: StatusBadgeTone;
  /** Nombre accesible; por defecto usa `label` si es texto. */
  ariaLabel?: string;
  className?: string;
  /**
   * Red de seguridad para celdas angostas: el chip nunca excede su contenedor (`max-w-full min-w-0`) y,
   * si el texto no cabe, se parte en varias líneas centradas en vez de montarse sobre la columna vecina.
   * Por defecto el chip sigue en una sola línea (`whitespace-nowrap`).
   */
  wrap?: boolean;
  /** Tooltip nativo con el texto completo (útil cuando la tabla muestra una etiqueta corta). */
  title?: string;
}

/**
 * Marca que va antes del texto, como el `.badge::before` del prototipo «Diseño Inteligente» (rama `propuesta`):
 * completo lleva un check azul, pendiente un punto naranja, error una equis naranja y el resto un punto del color
 * del texto. Es lo que hace que el estado no dependa solo del color (los tres primeros comparten el fondo cielo).
 */
function Marca({ tone }: { tone?: StatusBadgeTone }) {
  if (tone === "success") {
    return (
      <svg aria-hidden="true" data-marca="check" viewBox="0 0 10 8" className="h-2 w-2.5 shrink-0" fill="none">
        <path d="M1 4.2 3.7 6.8 9 1.2" stroke="var(--di-success-dot)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }
  if (tone === "danger") {
    return (
      <svg aria-hidden="true" data-marca="equis" viewBox="0 0 8 8" className="h-2 w-2 shrink-0" fill="none">
        <path d="M1 1 7 7M7 1 1 7" stroke="var(--di-alerta-punto)" strokeWidth="1.8" strokeLinecap="round" />
      </svg>
    );
  }
  return (
    <span
      aria-hidden="true"
      data-marca="punto"
      className="h-1.5 w-1.5 shrink-0 rounded-full"
      style={{ background: tone === "warning" ? "var(--di-alerta-punto)" : "currentColor" }}
    />
  );
}

/**
 * Chip de estado unificado (HU #10494, tones HU #10844), con el aspecto del `.badge` de la propuesta (HU #14476):
 * píldora sin borde de color (el borde de cada tono coincide con su fondo, salvo el aro naranja de `danger`), texto
 * tinta y una marca antes del texto (ver {@link Marca}). El COLOR se resuelve por `tone` desde los `--badge-*` de
 * `tokens.css`, consistentes en claro y oscuro.
 *
 * La API cruda (`bg`/`color`/`border`) queda `@deprecated` solo para la migración; los
 * nuevos usos deben pasar `tone`.
 */
export function StatusBadge({
  label,
  tone,
  bg,
  color,
  border,
  ariaLabel,
  className = "",
  wrap = false,
  title,
}: StatusBadgeProps) {
  const style: CSSProperties = tone
    ? {
        background: `var(--badge-${tone}-bg)`,
        color: `var(--badge-${tone}-fg)`,
        borderColor: `var(--badge-${tone}-border)`,
      }
    : { background: bg, color, borderColor: border ?? color };
  const aria = ariaLabel ?? (typeof label === "string" ? `Estado: ${label}` : undefined);
  return (
    <span
      role="status"
      aria-label={aria}
      // 12px es el piso tipográfico del sistema; estaba en 11 y es texto que el gestor lee para
      // saber en qué estado va cada fila.
      title={title}
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-[3px] text-xs font-semibold ${
        wrap ? "min-w-0 max-w-full justify-center whitespace-normal text-center leading-tight" : "whitespace-nowrap"
      } ${className}`}
      style={style}
    >
      <Marca tone={tone} />
      {label}
    </span>
  );
}
