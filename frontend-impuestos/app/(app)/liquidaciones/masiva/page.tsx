"use client";

import { useSesion } from "@/components/ImpuestosShell";
import { CargaMasiva } from "@/components/liquidaciones/CargaMasiva";
import { bloqueoCreacion } from "@/lib/liquidaciones/estados";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

/**
 * /liquidaciones/masiva (plan §6.1, spec-visual §3.4): carga masiva con análisis previo y tope (F3-CF1/CF2). El Super Admin
 * sin ?empresa (abierta por URL) ve «Elige una compañía.» con las acciones deshabilitadas: nunca analiza ni envía.
 */
export default function PaginaCargaMasiva() {
  const { sesion } = useSesion();
  if (!tienePermiso(sesion, PERMISOS.liquidacionesCrear)) return <p className="text-sm text-muted-foreground">No tienes permiso para crear liquidaciones.</p>;
  return <CargaMasiva bloqueo={bloqueoCreacion(sesion)} esSuperAdmin={sesion?.esSuperAdmin === true} />;
}
