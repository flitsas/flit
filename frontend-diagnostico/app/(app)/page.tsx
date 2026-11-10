"use client";

import { Suspense } from "react";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { rutaIlustracion } from "@flit/ui/ilustraciones";

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
      <CabeceraModulo
        as="section"
        aria-labelledby="diagnostico-titulo"
        tituloId="diagnostico-titulo"
        titulo="Diagnóstico vehicular"
        descripcion={empresa}
        ilustracion={rutaIlustracion("diagnostico")}
        acciones={puedeCrear && <CapturaExpress />}
      />

      {puedeVer && (
        <Suspense fallback={<VehicleLoader label="Cargando los diagnósticos…" />}>
          <ListadoDiagnosticos />
        </Suspense>
      )}
    </>
  );
}
