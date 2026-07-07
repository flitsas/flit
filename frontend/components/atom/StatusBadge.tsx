import type { CSSProperties, ReactNode } from "react";

export interface StatusBadgeStyle {
  /** Fondo tintado (rgba translúcido). */
  bg: string;
  /** Color del texto (tono legible del estado). */
  color: string;
  /** Color del borde (rgba). Si se omite, se usa el color del texto. */
  border?: string;
}

export interface StatusBadgeProps extends StatusBadgeStyle {
  label: ReactNode;
  /** Nombre accesible; por defecto usa `label` si es texto. */
  ariaLabel?: string;
  className?: string;
}

/**
 * Chip de estado unificado (HU #10494). Convención TINTADA (decisión D1 del plan de
 * ajustes UX/UI): fondo translúcido + texto de color + borde. La forma, el tamaño y la
 * tipografía son idénticos en TODAS las tablas; cada dominio conserva su propio
 * vocabulario y sus colores (trámites ADR-0022, validaciones, usuarios, compañías…).
 */
export function StatusBadge({
  label,
  bg,
  color,
  border,
  ariaLabel,
  className = "",
}: StatusBadgeProps) {
  const style: CSSProperties = { background: bg, color, borderColor: border ?? color };
  const aria = ariaLabel ?? (typeof label === "string" ? `Estado: ${label}` : undefined);
  return (
    <span
      role="status"
      aria-label={aria}
      className={`inline-flex items-center whitespace-nowrap rounded-full border px-2.5 py-1 text-[11px] font-semibold ${className}`}
      style={style}
    >
      {label}
    </span>
  );
}
