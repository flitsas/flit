import type { ComponentType } from "react";

export interface RowAction {
  icon: ComponentType<{ className?: string }>;
  /** Nombre accesible + tooltip. Obligatorio (D2: iconos sin texto). */
  label: string;
  onClick: () => void;
  tone?: "default" | "primary" | "danger";
  disabled?: boolean;
  /** Tooltip alternativo cuando está deshabilitada (si no, usa `label`). */
  disabledTitle?: string;
}

// Color por tono como clase Tailwind (no `style` inline) para poder dar variante dark:
// el `default` en claro es un azul marino casi negro (#162744) que sobre fondo oscuro
// desaparecía; en dark pasa a un neutro claro. `primary`/`danger` son colores de marca que
// ya contrastan en ambos temas.
const TONE: Record<NonNullable<RowAction["tone"]>, string> = {
  default: "text-[#162744] dark:text-slate-200",
  primary: "text-[#557EFF]",
  danger: "text-[#FF4E00]",
};

/**
 * Área clickeable mínima de un botón de solo icono (HU19 · ajuste de usabilidad).
 *
 * El cursor de FLIT es un SVG de 22×22 px con el hotspot en la esquina (`globals.css`),
 * así que el punto que realmente recibe el clic queda hasta ~20 px arriba y a la izquierda
 * del cuerpo visible de la flecha. Con el objetivo anterior (16 px de icono + `p-1.5` = 28 px)
 * el usuario veía el puntero sobre el icono mientras el clic caía fuera del botón. Con 40 px
 * el desfase del cursor cae dentro del área activa, y además se cumple el mínimo de
 * accesibilidad para objetivos táctiles (WCAG 2.5.8). El icono conserva su tamaño: lo que
 * crece es la superficie sensible, no el dibujo.
 */
export const ICON_BUTTON_HIT_AREA =
  "inline-flex items-center justify-center min-h-[40px] min-w-[40px]";

/**
 * Columna de acciones unificada (HU #10495 · decisión D2: iconos sin texto).
 * Cada acción es un botón de solo icono con `aria-label` + `title` (tooltip),
 * de modo que el nombre accesible se conserva (los lectores de pantalla y los
 * tests que consultan por nombre siguen funcionando).
 */
export function RowActions({
  actions,
  className = "",
}: {
  actions: RowAction[];
  className?: string;
}) {
  return (
    <div className={`flex items-center justify-end gap-1 ${className}`}>
      {actions.map((a) => {
        const Icon = a.icon;
        return (
          <button
            key={a.label}
            type="button"
            onClick={a.onClick}
            disabled={a.disabled}
            aria-label={a.label}
            aria-disabled={a.disabled || undefined}
            title={a.disabled ? (a.disabledTitle ?? a.label) : a.label}
            className={`${ICON_BUTTON_HIT_AREA} rounded-lg p-1.5 transition hover:bg-[#557EFF]/10 disabled:cursor-not-allowed disabled:opacity-40 ${TONE[a.tone ?? "default"]}`}
          >
            <Icon className="h-4 w-4" />
          </button>
        );
      })}
    </div>
  );
}
