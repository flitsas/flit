"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pestanas } from "@flit/ui/Pestanas";
import { useSesion } from "@/components/DiagnosticoShell";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";
import { ListadoVehiculos } from "@/components/vehiculos/ListadoVehiculos";
import { puedeSanear as tieneSanear } from "@/lib/sesion";
import { URL_PLANTILLA } from "@/lib/carga/api";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

const TITULO_TARJETA_CLS = "text-lg font-semibold text-flit-primary";

type Vista = "diagnosticos" | "vehiculos";
const vistaDe = (params: URLSearchParams): Vista => (params.get("vista") === "vehiculos" ? "vehiculos" : "diagnosticos");

/**
 * Pestañas del inicio (HU #14577 AC20, spec-ui §4.1): «Diagnósticos» (el listado de siempre) y «Vehículos» (grilla de
 * placas, PS-3). La activa va en la URL (`?vista=vehiculos`; sin parámetro, «Diagnósticos»). Cambiar de pestaña hace
 * `router.replace` conservando solo la vista: los filtros de cada pestaña son suyos. Solo se monta el panel activo, para no
 * pedir dos listados.
 */
function VistasInicio({ puedeCrear, puedeSanear, puedeDescargar }: { puedeCrear: boolean; puedeSanear: boolean; puedeDescargar: boolean }) {
  const router = useRouter();
  const params = useSearchParams();
  const deUrl = vistaDe(params);
  const [activa, setActiva] = useState<Vista>(deUrl);
  const [vistaUrl, setVistaUrl] = useState<Vista>(deUrl);
  // La URL cambió desde fuera (atrás/adelante, enlace compartido): la pestaña la sigue.
  if (deUrl !== vistaUrl) {
    setVistaUrl(deUrl);
    setActiva(deUrl);
  }

  function cambiar(vista: Vista) {
    if (vista === activa) return;
    setActiva(vista);
    router.replace(vista === "vehiculos" ? "/diagnosticos?vista=vehiculos" : "/diagnosticos", { scroll: false });
  }

  return (
    <Pestanas<Vista>
      ariaLabel="Vista del inicio"
      active={activa}
      onChange={cambiar}
      tabs={[
        { id: "diagnosticos", label: "Diagnósticos", content: activa === "diagnosticos" ? <ListadoDiagnosticos puedeCrear={puedeCrear} /> : null },
        {
          id: "vehiculos",
          label: "Vehículos",
          content: activa === "vehiculos" ? <ListadoVehiculos puedeCrear={puedeCrear} puedeSanear={puedeSanear} puedeDescargar={puedeDescargar} /> : null,
        },
      ]}
    />
  );
}

/**
 * Diagnósticos (/diagnosticos; antes la raíz, que desde HU #14580 es el Dashboard): HU #13732, rediseño del kit 2026 (HU #14562 AC13). La tarjeta título con la compañía de la
 * sesión y, con la capacidad crearDiagnosticos, dos tarjetas: Express («Placa o VIN» + «Diagnosticar», que abre
 * /diagnosticos/nuevo en Express con la placa precargada) y Masivo («Nuevo diagnóstico masivo» y «Descargar
 * plantilla»). La API vuelve a exigir el permiso al crear. Con verDiagnosticos, debajo, las pestañas «Diagnósticos» y
 * «Vehículos» (HU #14577 AC20) con sus filtros en la URL; el vacío, la carga y el error los pinta cada listado.
 * useSearchParams va bajo Suspense.
 */
export default function Inicio() {
  const { sesion } = useSesion();
  const empresa = sesion?.empresa.nombre;
  const puedeCrear = sesion?.capacidades.crearDiagnosticos ?? false;
  const puedeVer = sesion?.capacidades.verDiagnosticos ?? false;
  const puedeSanear = tieneSanear(sesion?.capacidades);
  const puedeDescargar = sesion?.capacidades.descargarReportes ?? false;

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
          <VistasInicio puedeCrear={puedeCrear} puedeSanear={puedeSanear} puedeDescargar={puedeDescargar} />
        </Suspense>
      )}
    </>
  );
}
