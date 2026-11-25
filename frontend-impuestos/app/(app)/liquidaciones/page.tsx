"use client";

import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { useSesion } from "@/components/ImpuestosShell";
import { GridLiquidaciones } from "@/components/liquidaciones/GridLiquidaciones";
import { useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { bloqueoCreacion } from "@/lib/liquidaciones/estados";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

/**
 * /liquidaciones (plan §6.1, spec-visual §3.2): grid mixto de liquidaciones por placa y masivas con las tarjetas de
 * creación. El Super Admin en vista global ve «Todas las compañías» y la columna Compañía.
 */
export default function PaginaLiquidaciones() {
  const { sesion } = useSesion();
  const { empresa } = useEmpresaSeleccionada();
  const esSuperAdmin = sesion?.esSuperAdmin === true;
  const descripcion = esSuperAdmin ? (empresa ? undefined : "Todas las compañías") : (sesion?.empresa.nombre ?? undefined);
  return (
    <>
      <CabeceraModulo titulo="Liquidaciones" icono="pesos" descripcion={descripcion} />
      <GridLiquidaciones
        puedeCrear={tienePermiso(sesion, PERMISOS.liquidacionesCrear)}
        mostrarCompania={esSuperAdmin && !empresa}
        bloqueo={bloqueoCreacion(sesion)}
      />
    </>
  );
}
