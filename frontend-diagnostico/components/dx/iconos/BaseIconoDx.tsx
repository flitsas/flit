import type { ReactNode } from "react";

/**
 * Base de la familia IconoDx (HU #14562 · UI-3, D2 = c): SVG en línea con rejilla de 1024, trazo de 72 en currentColor,
 * puntas y uniones redondas. Los glifos de estado del sistema (copiados de @ant-design/icons-svg, licencia MIT) llevan
 * relleno en currentColor y viewBox "64 64 896 896".
 *
 * Accesibilidad (AC4): sin `titulo` el ícono acompaña texto y es decorativo (aria-hidden, focusable=false); con `titulo`
 * va solo y se anuncia como imagen (role="img" + aria-label).
 */
export interface IconoDxProps {
  /** Nombre accesible; solo cuando el ícono va sin texto al lado. */
  titulo?: string;
  className?: string;
  /** Lado en px: 16 (en línea), 20 (por defecto) o 24 (sección). */
  tamano?: 16 | 20 | 24;
  /** viewBox del glifo; por defecto la rejilla de 1024. */
  viewBox?: string;
  children: ReactNode;
}

export function BaseIconoDx({ titulo, className, tamano = 20, viewBox = "0 0 1024 1024", children }: IconoDxProps) {
  const a11y = titulo ? { role: "img" as const, "aria-label": titulo } : { "aria-hidden": true as const };
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox={viewBox}
      width={tamano}
      height={tamano}
      fill="none"
      stroke="currentColor"
      strokeWidth={72}
      strokeLinecap="round"
      strokeLinejoin="round"
      focusable="false"
      className={className}
      {...a11y}
    >
      {children}
    </svg>
  );
}
