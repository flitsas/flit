"use client";

import type { ReactNode } from "react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Requiere } from "@/components/Pantalla";
import { capacidades } from "@/lib/sesion";

/** Los reportes solo existen si la empresa tiene `has_reportes_module` (RN-185); además piden el permiso de la pantalla. */
export function RequiereReportes({ permiso, children }: { permiso: string; children: ReactNode }) {
  const { sesion, cargando } = useSesion();
  if (!cargando && sesion && !capacidades(sesion).reportes)
    return (
      <InlineAlert tone="warning" title="Reportes no habilitados">
        Tu empresa no tiene el módulo de reportes. Pídeselo a FLIT.
      </InlineAlert>
    );
  return <Requiere permiso={permiso}>{children}</Requiere>;
}
