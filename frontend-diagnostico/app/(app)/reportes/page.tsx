"use client";

import { Suspense, useEffect } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { ModuloReportes } from "@/components/reportes/ModuloReportes";
import { puedeVerReportes, tieneVistaGlobal } from "@/lib/sesion";

/**
 * /reportes (HU #14580, spec-ui §5): con verReportes, los reportes de la compañía; con verEstadisticasGlobales, la vista
 * global del backoffice con selector de compañía (Q4). Exportar exige además descargarReportes. Sin ninguna, la 403. La
 * API vuelve a exigir cada permiso y registra cada consulta y descarga. useSearchParams va bajo Suspense.
 */
export default function Reportes() {
  const router = useRouter();
  const { sesion } = useSesion();
  const capacidades = sesion?.capacidades;
  const puede = puedeVerReportes(capacidades);
  const sinAcceso = sesion !== null && !puede;

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  if (!sesion || !puede) return null;
  return (
    <Suspense fallback={<VehicleLoader label="Cargando reportes…" />}>
      <ModuloReportes vistaGlobal={tieneVistaGlobal(capacidades)} puedeDescargar={capacidades?.descargarReportes === true} />
    </Suspense>
  );
}
