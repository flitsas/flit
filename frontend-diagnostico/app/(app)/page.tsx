"use client";

import { Suspense, useEffect } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { Dashboard } from "@/components/estadisticas/Dashboard";
import { puedeVerReportes, tieneVistaGlobal, type CapacidadesDiagnostico } from "@/lib/sesion";

/**
 * Raíz de Diagnóstico = Dashboard (HU #14580, U7, diseño §c C1): entrar al producto (desde el hub o por /) abre los
 * indicadores para todos los roles. Con la capacidad verEstadisticasGlobales, la vista global con selector de compañía
 * (Q4); si no, la compañía de la sesión. El listado de siempre vive en /diagnosticos (los enlaces viejos con su query
 * los redirige next.config.ts). Sin verDiagnosticos ni vista global, la primera ruta que el rol sí puede abrir
 * (`destinoSinDashboard`); la 403 solo si no tiene ninguna. useSearchParams va bajo Suspense.
 */
/**
 * Adónde va quien no puede ver el Dashboard (CR-FE-3, U7): /diagnosticos con ver o crear (con solo crear, esa página
 * muestra las tarjetas Express y Masivo); si no, /reportes con verReportes o la vista global; sin ninguna, la 403.
 */
function destinoSinDashboard(capacidades: CapacidadesDiagnostico | undefined): string {
  if (capacidades?.verDiagnosticos === true || capacidades?.crearDiagnosticos === true) return "/diagnosticos";
  if (puedeVerReportes(capacidades)) return "/reportes";
  return "/403?code=SIN_PERMISOS_DIAGNOSTICO";
}

export default function Raiz() {
  const router = useRouter();
  const { sesion } = useSesion();
  const capacidades = sesion?.capacidades;
  const vistaGlobal = tieneVistaGlobal(capacidades);
  const puedeVer = vistaGlobal || capacidades?.verDiagnosticos === true;
  const destino = sesion !== null && !puedeVer ? destinoSinDashboard(capacidades) : null;

  useEffect(() => {
    if (destino) router.replace(destino);
  }, [destino, router]);

  if (!sesion || !puedeVer) return null;
  return (
    <Suspense fallback={<VehicleLoader label="Cargando el resumen…" />}>
      <Dashboard vistaGlobal={vistaGlobal} puedeCrear={capacidades?.crearDiagnosticos === true} empresaNombre={sesion.empresa.nombre} />
    </Suspense>
  );
}
