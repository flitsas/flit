"use client";

import { ActionsMenu } from "@/components/atom/ActionsMenu";
import type { RowAction } from "@/components/atom/RowActions";

/**
 * Acciones de una fila en UN solo botón «Acciones» con menú, como en el resto de los módulos (bandeja de
 * trámites, organismos…). Recibe las mismas acciones que <c>RowActions</c>; el texto visible de cada ítem es la
 * etiqueta sin el nombre de la fila («Editar mandatario Ana» → «Editar») y el nombre accesible sigue completo.
 */
export function RowActionsMenu({
  actions,
  ariaLabel,
  subject,
  className = "",
}: {
  actions: RowAction[];
  /** Nombre accesible del botón «Acciones» (p. ej. «Acciones de Ana Flujo»). */
  ariaLabel: string;
  /** Nombre de la fila: se quita de la etiqueta para el texto visible. */
  subject?: string;
  className?: string;
}) {
  if (actions.length === 0) return null;
  const corto = (label: string): string => {
    if (!subject) return label;
    const sin = label
      .replace(` mandatario de ${subject}`, " mandatario")
      .replace(` mandatario ${subject}`, "")
      .replace(` a ${subject}`, "")
      .replace(` de ${subject}`, "")
      .replace(` ${subject}`, "")
      .trim();
    return sin.length > 0 ? sin : label;
  };
  return (
    <div className={`flex justify-end ${className}`}>
      <ActionsMenu
        ariaLabel={ariaLabel}
        className="w-28"
        items={actions.map((a) => ({
          key: a.label,
          label: corto(a.label),
          ariaLabel: a.label,
          icon: a.icon,
          onSelect: a.onClick,
          disabled: a.disabled,
          disabledReason: a.disabledTitle,
          tone: a.tone === "danger" ? ("danger" as const) : ("default" as const),
        }))}
      />
    </div>
  );
}
