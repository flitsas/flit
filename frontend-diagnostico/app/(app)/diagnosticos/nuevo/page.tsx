"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowRight, FilePlus2 } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { FormularioParametrizacion } from "@/components/parametrizacion/FormularioParametrizacion";
import { obtenerConfiguracion, type Configuracion } from "@/lib/configuracion";

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; configuracion: Configuracion };

/**
 * Nuevo diagnóstico (HU #13752): la tarjeta título y el formulario de parametrización. Lee GET /configuracion para
 * ofrecer solo las fuentes contratadas y premarcar las por defecto. Sin la capacidad crearDiagnosticos no se pide nada y
 * se explica el porqué; la API vuelve a exigir el permiso al crear.
 */
export default function NuevoDiagnostico() {
  const { sesion } = useSesion();
  const puedeCrear = sesion?.capacidades.crearDiagnosticos ?? false;
  const [intento, setIntento] = useState(0);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  // Id de la cabecera creada: habilita el paso de carga de vehículos (HU #13754).
  const [creadoId, setCreadoId] = useState<string | null>(null);

  useEffect(() => {
    if (!puedeCrear) return;
    const controller = new AbortController();
    obtenerConfiguracion(controller.signal)
      .then((configuracion) => setCarga({ tipo: "listo", configuracion }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setCarga({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo cargar la configuración." });
      });
    return () => controller.abort();
  }, [puedeCrear, intento]);

  const reintentar = useCallback(() => {
    setCarga({ tipo: "cargando" });
    setIntento((i) => i + 1);
  }, []);

  let contenido;
  if (!puedeCrear) {
    contenido = (
      <section aria-labelledby="nuevo-vacio" className={`${TARJETA_CLS} flex flex-col items-center gap-3 py-12 text-center`}>
        <FilePlus2 className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
        <h2 id="nuevo-vacio" className="text-lg font-semibold">
          No tienes permiso para crear diagnósticos
        </h2>
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Pídele acceso al administrador de tu compañía.</p>
      </section>
    );
  } else if (carga.tipo === "cargando") {
    contenido = <VehicleLoader label="Cargando el formulario…" />;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo cargar el formulario"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  } else {
    contenido = (
      <>
        <FormularioParametrizacion configuracion={carga.configuracion} onGuardado={(d) => setCreadoId(d.id)} />
        {creadoId && (
          <div className="flex justify-end">
            <Link href={`/diagnosticos/${encodeURIComponent(creadoId)}/carga`} className={BOTON_PRIMARIO_CLS}>
              Cargar vehículos
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Link>
          </div>
        )}
      </>
    );
  }

  return (
    <>
      <section aria-labelledby="nuevo-titulo" className={TARJETA_CLS}>
        <h1 id="nuevo-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
          Nuevo diagnóstico
        </h1>
        {sesion?.empresa.nombre && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{sesion.empresa.nombre}</p>}
      </section>
      {contenido}
    </>
  );
}
