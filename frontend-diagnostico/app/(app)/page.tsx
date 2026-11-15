"use client";

import { Suspense, useEffect } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { Dashboard } from "@/components/estadisticas/Dashboard";
import { tieneVistaGlobal } from "@/lib/sesion";

/**
 * Raíz de Diagnóstico = Dashboard (HU #14580, U7, diseño §c C1): entrar al producto (desde el hub o por /) abre los
 * indicadores para todos los roles. Con la capacidad verEstadisticasGlobales, la vista global con selector de compañía
 * (Q4); si no, la compañía de la sesión. El listado de siempre vive en /diagnosticos (los enlaces viejos con su query
 * los redirige next.config.ts). Sin verDiagnosticos ni vista global, la 403. useSearchParams va bajo Suspense.
 */
export default function Raiz() {
  const router = useRouter();
  const { sesion } = useSesion();
  const capacidades = sesion?.capacidades;
  const vistaGlobal = tieneVistaGlobal(capacidades);
  const puedeVer = vistaGlobal || capacidades?.verDiagnosticos === true;
  const sinAcceso = sesion !== null && !puedeVer;

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISOS_DIAGNOSTICO");
  }, [sinAcceso, router]);

  if (!sesion || !puedeVer) return null;
  return (
    <Suspense fallback={<VehicleLoader label="Cargando el resumen…" />}>
      <Dashboard vistaGlobal={vistaGlobal} puedeCrear={capacidades?.crearDiagnosticos === true} empresaNombre={sesion.empresa.nombre} />
    </Suspense>
  );
}
