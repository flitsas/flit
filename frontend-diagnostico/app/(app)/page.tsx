"use client";

import { Suspense } from "react";
import Link from "next/link";
import { Icono } from "@flit/ui/Icono";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";
import { URL_PLANTILLA } from "@/lib/carga/api";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

const TITULO_TARJETA_CLS = "text-lg font-semibold text-flit-primary";

/**
 * Inicio de Diagnóstico (HU #13732; rediseño del kit 2026, HU #14562 AC13): la tarjeta título con la compañía de la
 * sesión y, con la capacidad crearDiagnosticos, dos tarjetas: Express («Placa o VIN» + «Diagnosticar», que abre
 * /diagnosticos/nuevo en Express con la placa precargada) y Masivo («Nuevo diagnóstico masivo» y «Descargar
 * plantilla»). La API vuelve a exigir el permiso al crear. Con verDiagnosticos, debajo, el listado con sus filtros en la
 * URL; el vacío, la carga y el error los pinta el listado. useSearchParams va bajo Suspense.
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
        icono="diagnostico"
      />

      {puedeCrear && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <section aria-labelledby="inicio-express" className={`${TARJETA_CLS} flex flex-col gap-3`}>
            <div className="flex flex-col gap-1">
              <h2 id="inicio-express" className={TITULO_TARJETA_CLS}>
                Diagnóstico Express
              </h2>
              <p className={AYUDA_CLS}>Un vehículo, sin crear un lote.</p>
            </div>
            <CapturaExpress />
          </section>
          <section aria-labelledby="inicio-masivo" className={`${TARJETA_CLS} flex flex-col gap-3`}>
            <div className="flex flex-col gap-1">
              <h2 id="inicio-masivo" className={TITULO_TARJETA_CLS}>
                Diagnóstico masivo
              </h2>
              <p className={AYUDA_CLS}>Muchos vehículos desde un archivo.</p>
            </div>
            <p className={AYUDA_CLS}>Archivo .xlsx o .csv con la columna Identificador_Vehiculo y, opcional, Documento_Locatario.</p>
            <div className="mt-auto flex flex-wrap gap-2">
              <Link href="/diagnosticos/nuevo?modo=masivo" className={BOTON_PRIMARIO_CLS}>
                <Icono nombre="reporte-detallado" className="h-4 w-4" />
                Nuevo diagnóstico masivo
              </Link>
              <a href={URL_PLANTILLA} download className={BOTON_SECUNDARIO_CLS}>
                <Icono nombre="nube-bajar" className="h-4 w-4" />
                Descargar plantilla
              </a>
            </div>
          </section>
        </div>
      )}

      {puedeVer && (
        <Suspense fallback={<VehicleLoader label="Cargando diagnósticos…" />}>
          <ListadoDiagnosticos puedeCrear={puedeCrear} />
        </Suspense>
      )}
    </>
  );
}
