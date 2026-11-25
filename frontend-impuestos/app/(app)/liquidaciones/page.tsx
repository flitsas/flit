"use client";

import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { useSesion } from "@/components/ImpuestosShell";
import { GridLiquidaciones } from "@/components/liquidaciones/GridLiquidaciones";
import { bloqueoCreacion } from "@/lib/liquidaciones/estados";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

/**
 * /liquidaciones (plan §6.1, spec-visual §3.2): grid mixto de liquidaciones por placa y masivas con las tarjetas de
 * creación. El Super Admin trabaja sobre la compañía de ?empresa (el backend 1.1.0 la exige en GET /liquidaciones):
 * sin ella, el grid le pide elegir una y no consulta. No hay vista global: siempre se lee una sola compañía.
 */
export default function PaginaLiquidaciones() {
  const { sesion } = useSesion();
  const esSuperAdmin = sesion?.esSuperAdmin === true;
  const descripcion = esSuperAdmin ? undefined : (sesion?.empresa.nombre ?? undefined);
  return (
    <>
      <CabeceraModulo titulo="Liquidaciones" icono="pesos" descripcion={descripcion} />
      <GridLiquidaciones
        puedeCrear={tienePermiso(sesion, PERMISOS.liquidacionesCrear)}
        bloqueo={bloqueoCreacion(sesion)}
        esSuperAdmin={esSuperAdmin}
      />
    </>
  );
}
