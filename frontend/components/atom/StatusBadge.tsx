import type { CSSProperties, ReactNode } from "react";

/** Tones semánticos del estado (HU #10844). Fuente única de color en globals.css. */
export type StatusTone = "success" | "warning" | "danger" | "info" | "neutral";

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
  tone?: StatusTone;
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
 * Chip de estado unificado (HU #10494, tones HU #10844). Convención TINTADA: fondo
 * translúcido + texto de color + borde. La forma, el tamaño y la tipografía son idénticos
 * en TODAS las tablas. El COLOR se resuelve por `tone` semántico (success/warning/danger/
 * info/neutral) desde la paleta única de `globals.css`, consistente en claro y oscuro.
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
      className={`inline-flex items-center rounded-full border px-2.5 py-1 text-xs font-semibold ${
        wrap ? "min-w-0 max-w-full justify-center whitespace-normal text-center leading-tight" : "whitespace-nowrap"
      } ${className}`}
      style={style}
    >
      {label}
    </span>
  );
}
