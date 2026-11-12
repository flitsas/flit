"use client";

import { etiquetaKit } from "@flit/ui/StatusBadge";
import { useId } from "react";
import { Icono } from "@flit/ui/Icono";

// Chips compartidos de la sección «Permisos por módulo» (filas sueltas y par Ver/Crear).

/** HU #13653 (AC1): texto del tooltip del chip «Solo SA». */
export const SOLO_SA_TOOLTIP = "Permiso exclusivo del Super Admin: un administrador de compañía u OT no puede otorgarlo.";
/** HU #13655 (FD2-3): textos del chip de permisos fuera del tope del administrador. */
export const OUT_OF_CAP_LABEL = "Fuera de tu tope";
export const OUT_OF_CAP_TOOLTIP = "Lo otorgó otro administrador. Puedes quitarlo, pero no volver a otorgarlo.";
export const OUT_OF_CAP_LOCKED_TOOLTIP = "Está fuera de lo que puedes otorgar.";

export function OutOfCapChip({ tooltip, id }: { tooltip: string; id: string }) {
  return (
    <span
      tabIndex={0}
      title={tooltip}
      aria-describedby={id}
      data-testid="out-of-cap-chip"
      {...etiquetaKit("danger", { className: "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand/40" })}
    >
      <Icono nombre="candado" className="h-3 w-3" />
      {OUT_OF_CAP_LABEL}
      <span id={id} role="tooltip" className="sr-only">
        {tooltip}
      </span>
    </span>
  );
}

export function SoloSaChip() {
  const tipId = useId();
  return (
    <span
      tabIndex={0}
      title={SOLO_SA_TOOLTIP}
      aria-describedby={tipId}
      data-testid="solo-sa"
      {...etiquetaKit({ c: "var(--badge-pending-border)", ct: "var(--badge-pending-fg)" })}
    >
      <Icono nombre="candado" className="h-3 w-3" />
      Solo SA
      <span id={tipId} className="sr-only">
        {SOLO_SA_TOOLTIP}
      </span>
    </span>
  );
}
