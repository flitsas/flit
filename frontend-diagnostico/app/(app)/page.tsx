"use client";

import { Suspense } from "react";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { TARJETA_CLS } from "@/components/configuracion/estilos";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";

/**
 * Pantalla base de Diagnóstico (HU #13732): la tarjeta título con la compañía de la sesión. El shell solo la muestra con
 * la sesión cargada; el loader, el error y la 403 los resuelve él. Con la capacidad crearDiagnosticos la tarjeta trae
 * además «Placa o VIN» + «Diagnosticar» y «Carga masiva» (HU #14085), que abren la pantalla única /diagnosticos/nuevo en
 * Express (con la placa precargada) o en Masivo; la API vuelve a exigir el permiso al crear. Con verDiagnosticos, debajo,
 * el listado de diagnósticos de la compañía con sus filtros en la URL (HU #13852, decisión Q6); el vacío lo pinta el
 * listado cuando la compañía no tiene diagnósticos. useSearchParams va bajo Suspense.
 */
export default function Inicio() {
  const { sesion } = useSesion();
  const empresa = sesion?.empresa.nombre;
  const puedeCrear = sesion?.capacidades.crearDiagnosticos ?? false;
  const puedeVer = sesion?.capacidades.verDiagnosticos ?? false;

  return (
    <>
      <section aria-labelledby="diagnostico-titulo" className={`${TARJETA_CLS} flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between`}>
        <div>
          <h1 id="diagnostico-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
            Diagnóstico vehicular
          </h1>
          {empresa && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{empresa}</p>}
        </div>
        {puedeCrear && <CapturaExpress />}
      </section>

      {puedeVer && (
        <Suspense fallback={<VehicleLoader label="Cargando los diagnósticos…" />}>
          <ListadoDiagnosticos />
        </Suspense>
      )}
    </>
  );
}
