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

const TONE: Record<NonNullable<RowAction["tone"]>, string> = {
  default: "#162744",
  primary: "#557EFF",
  danger: "#FF4E00",
};

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
            className="inline-flex items-center justify-center rounded-lg p-1.5 transition hover:bg-[#557EFF]/10 disabled:cursor-not-allowed disabled:opacity-40"
            style={{ color: TONE[a.tone ?? "default"] }}
          >
            <Icon className="h-4 w-4" />
          </button>
        );
      })}
    </div>
  );
}
