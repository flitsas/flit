"use client";

import { Suspense } from "react";
import Link from "next/link";
import { FileSpreadsheet } from "lucide-react";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";

/**
 * Pantalla base de Diagnóstico (HU #13732): la tarjeta título con la compañía de la sesión. El shell solo la muestra con
 * la sesión cargada; el loader, el error y la 403 los resuelve él. Con la capacidad crearDiagnosticos muestra además el
 * bloque Express (HU #13755); la API vuelve a exigir el permiso al crear. Con verDiagnosticos, debajo, el listado de
 * diagnósticos de la compañía con sus filtros en la URL (HU #13852, decisión Q6), que reemplaza el estado vacío de antes:
 * ahora el vacío lo pinta el listado cuando la compañía no tiene diagnósticos. useSearchParams va bajo Suspense. Con la
 * misma capacidad, junto a Express, la entrada a la carga masiva (HU #13754): /diagnosticos/nuevo crea la cabecera del
 * lote y lleva a /diagnosticos/{id}/carga con la plantilla Excel.
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

      {puedeCrear && (
        <section aria-labelledby="diagnostico-lote-titulo" className={`${TARJETA_CLS} flex flex-col gap-3`}>
          <div className="flex items-center gap-2">
            <FileSpreadsheet className="h-5 w-5 text-flit-brand" aria-hidden="true" />
            <h2 id="diagnostico-lote-titulo" className="text-lg font-semibold text-flit-primary dark:text-white">
              Diagnóstico por lote
            </h2>
          </div>
          <p className={AYUDA_CLS}>Para cargar varias placas o VIN a la vez desde la plantilla Excel.</p>
          <div>
            <Link href="/diagnosticos/nuevo" className={BOTON_PRIMARIO_CLS}>
              Nuevo diagnóstico (lote)
            </Link>
          </div>
        </section>
      )}

      {puedeVer && (
        <Suspense fallback={<VehicleLoader label="Cargando los diagnósticos…" />}>
          <ListadoDiagnosticos />
        </Suspense>
      )}
    </>
  );
}
