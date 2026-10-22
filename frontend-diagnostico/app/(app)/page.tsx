"use client";

import { Suspense } from "react";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";

/**
 * Pantalla base de Diagnóstico (HU #13732): la tarjeta título con la compañía de la sesión. El shell solo la muestra con
 * la sesión cargada; el loader, el error y la 403 los resuelve él. Con la capacidad crearDiagnosticos muestra además el
 * bloque Express (HU #13755); la API vuelve a exigir el permiso al crear. Con verDiagnosticos, debajo, el listado de
 * diagnósticos de la compañía con sus filtros en la URL (HU #13852, decisión Q6), que reemplaza el estado vacío de antes:
 * ahora el vacío lo pinta el listado cuando la compañía no tiene diagnósticos. useSearchParams va bajo Suspense.
 */
export default function Inicio() {
  const { sesion } = useSesion();
  const empresa = sesion?.empresa.nombre;
  const puedeCrear = sesion?.capacidades.crearDiagnosticos ?? false;
  const puedeVer = sesion?.capacidades.verDiagnosticos ?? false;

  return (
    <>
      <section
        aria-labelledby="diagnostico-titulo"
        className="rounded-2xl border border-[var(--color-flit-gray)] bg-white px-6 py-5 shadow-sm dark:border-white/[0.08] dark:bg-white/[0.04]"
      >
        <h1 id="diagnostico-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
          Diagnóstico vehicular
        </h1>
        {empresa && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{empresa}</p>}
      </section>

      {puedeCrear && <CapturaExpress />}

      {puedeVer && (
        <Suspense fallback={<VehicleLoader label="Cargando los diagnósticos…" />}>
          <ListadoDiagnosticos />
        </Suspense>
      )}
    </>
  );
}
