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
  /** Ruta del disco de estado del kit (p. ej. `/assets/estados/aprobado.svg`); sin él va el punto del tono. */
  icono?: string;
}

/** Color pleno (`--c`) y de texto (`--ct`) de cada tono: los de la etiqueta del Manual UI Kit 2026. */
const TONO: Record<StatusBadgeTone, { c: string; ct: string }> = {
  success: { c: "var(--badge-success-border)", ct: "var(--badge-success-fg)" },
  warning: { c: "var(--badge-warning-border)", ct: "var(--badge-warning-fg)" },
  danger: { c: "var(--badge-danger-border)", ct: "var(--badge-danger-fg)" },
  info: { c: "var(--badge-info-border)", ct: "var(--badge-info-fg)" },
  neutral: { c: "var(--badge-neutral-border)", ct: "var(--badge-neutral-fg)" },
  purple: { c: "var(--badge-purple-border)", ct: "var(--badge-purple-fg)" },
  tinta: { c: "var(--badge-tinta-border)", ct: "var(--badge-tinta-fg)" },
};

/**
 * Chip de estado unificado (HU #10494, tones HU #10844) con la forma del Manual UI Kit 2026 (propuesta-2, `.badge`):
 * etiqueta con punta (lado izquierdo redondo, derecho en punta), contorno de 1 px en el color pleno del tono, relleno
 * al 10 % y texto oscurecido a AA. Antes del texto va el ícono de estado del kit (`icono`, 20 px) o, sin él, un punto
 * de 8 px del color del tono: el estado nunca depende solo del color. La forma vive en `.kit-etiqueta` (tokens.css).
 *
 * La API cruda (`bg`/`color`/`border`) queda `@deprecated`: el borde es el color pleno y `color` el del texto.
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
  icono,
}: StatusBadgeProps) {
  const t = tone ? TONO[tone] : { c: border ?? color ?? "var(--badge-neutral-border)", ct: color ?? "var(--badge-neutral-fg)" };
  // El color del tono sigue en el estilo en línea (contrato de HU #10844: todo color sale de los --badge-*); la forma
  // del kit la dibujan dos capas de `.kit-etiqueta`: el contorno con punta en --c y el relleno al 10 % encima.
  const style = {
    "--c": t.c,
    "--ct": t.ct,
    ...(tone
      ? { background: `var(--badge-${tone}-bg)`, color: `var(--badge-${tone}-fg)`, borderColor: `var(--badge-${tone}-border)` }
      : { background: bg, color, borderColor: border ?? color }),
  } as CSSProperties;
  const aria = ariaLabel ?? (typeof label === "string" ? `Estado: ${label}` : undefined);
  return (
    <span
      role="status"
      aria-label={aria}
      title={title}
      data-tono={tone ?? "propio"}
      className={`kit-etiqueta${icono ? " kit-etiqueta--icono" : ""} ${
        wrap ? "min-w-0 max-w-full justify-center whitespace-normal text-center leading-tight" : "whitespace-nowrap"
      } ${className}`}
      style={style}
    >
      {icono ? (
        // eslint-disable-next-line @next/next/no-img-element -- disco estático del kit
        <img src={icono} alt="" aria-hidden="true" width={20} height={20} className="kit-etiqueta__icono" />
      ) : (
        <span aria-hidden="true" data-marca="punto" className="kit-etiqueta__punto" />
      )}
      {label}
    </span>
  );
}

/**
 * Forma de la etiqueta del kit para un elemento PROPIO que ya existe (un `<span>` de estado, un chip de una tabla o una
 * tarjeta) sin cambiar su DOM ni su accesibilidad: StatusBadge agrega role="status" y aria-label; esto no. Devuelve la
 * clase y las variables de color; el contenido (punto, ícono o texto) lo pone quien lo usa.
 *
 *   <span {...etiquetaKit("success")}><span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />Activo</span>
 *   <span {...etiquetaKit({ c: estilo.border, ct: estilo.color })}>…</span>
 */
export function etiquetaKit(
  tono: StatusBadgeTone | { c: string; ct?: string },
  opciones: { icono?: boolean; className?: string } = {},
): { className: string; style: CSSProperties } {
  const t = typeof tono === "string" ? TONO[tono] : { c: tono.c, ct: tono.ct ?? tono.c };
  return {
    className: `kit-etiqueta${opciones.icono ? " kit-etiqueta--icono" : ""} whitespace-nowrap${opciones.className ? ` ${opciones.className}` : ""}`,
    style: { "--c": t.c, "--ct": t.ct } as CSSProperties,
  };
}

/** Punto de 8 px del color del tono, para usar dentro de `etiquetaKit` cuando no hay ícono de estado. */
export const ETIQUETA_PUNTO_CLS = "kit-etiqueta__punto";
/** Ícono de 20 px dentro de `etiquetaKit({…}, { icono: true })` (p. ej. `<img src={ESTADO_ICONO[e]} className={…}>`). */
export const ETIQUETA_ICONO_CLS = "kit-etiqueta__icono";
