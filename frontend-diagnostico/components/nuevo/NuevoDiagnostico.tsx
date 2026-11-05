"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { FilePlus2 } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { obtenerConfiguracion, type Configuracion } from "@/lib/configuracion";
import { accesoNegadoExpress } from "@/lib/express/api";
import { limpiarIdentificador } from "@/lib/validation/fieldRules";
import { ZonaArchivo } from "@/components/carga/SubidaArchivo";
import { BOTON_REINTENTAR_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { useParametrizacion } from "@/components/parametrizacion/useParametrizacion";
import type { BorradorExpress, BorradorMasivo, ModoCreacion } from "./borradores";
import { CamposExpress } from "./CamposExpress";
import { CreacionExpress } from "./CreacionExpress";
import { CreacionMasiva } from "./CreacionMasiva";
import { DosZonas } from "./DosZonas";
import { SelectorModo } from "./SelectorModo";

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; configuracion: Configuracion };

export const EXPLICACION_CABECERA_CREADA =
  "Ya se creó la cabecera del lote: termina aquí la carga o retómala desde el listado. Para un Express, inicia un diagnóstico nuevo.";

const NOOP = () => {};

/**
 * Pantalla única de creación de diagnósticos (HU #14085): tarjeta título con el selector Express | Masivo (en la URL
 * como ?modo=express|masivo; ?placa= precarga la placa del Express desde el inicio) y debajo las zonas Vehículos y
 * Parámetros. Lee GET /configuracion para ofrecer las fuentes contratadas; mientras llega, Vehículos se ve deshabilitado
 * y Parámetros muestra el loader o el error con Reintentar. Lo digitado en cada modo, el alcance y el modo de análisis
 * se conservan al cambiar de modo; con la cabecera del lote creada el selector se bloquea. Sin la capacidad
 * crearDiagnosticos no se pide nada y se explica el porqué; la API vuelve a exigir el permiso al crear.
 */
export function NuevoDiagnostico() {
  const router = useRouter();
  const params = useSearchParams();
  const { sesion } = useSesion();
  const puedeCrear = sesion?.capacidades.crearDiagnosticos ?? false;
  const [modo, setModo] = useState<ModoCreacion>(() => (params.get("modo") === "masivo" ? "masivo" : "express"));
  const [express, setExpress] = useState<BorradorExpress>(() => ({
    placa: limpiarIdentificador(params.get("placa") ?? ""),
    tipoDocumento: "",
    documento: "",
    nombreManual: null,
  }));
  const [masivo, setMasivo] = useState<BorradorMasivo>({ nombre: "", archivo: null });
  const [cabeceraId, setCabeceraId] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [intento, setIntento] = useState(0);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });

  useEffect(() => {
    if (!puedeCrear) return;
    const controller = new AbortController();
    obtenerConfiguracion(controller.signal)
      .then((configuracion) => setCarga({ tipo: "listo", configuracion }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        const code = accesoNegadoExpress(e);
        if (code) return router.replace(`/403?code=${encodeURIComponent(code)}`);
        setCarga({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo cargar la configuración." });
      });
    return () => controller.abort();
  }, [puedeCrear, intento, router]);

  const reintentar = useCallback(() => {
    setCarga({ tipo: "cargando" });
    setIntento((i) => i + 1);
  }, []);

  const bloqueado = cabeceraId !== null || ocupado;

  function cambiarModo(nuevo: ModoCreacion) {
    if (bloqueado || nuevo === modo) return;
    setModo(nuevo);
    // Solo refleja el modo en la URL (sin navegar): lo digitado sigue en memoria.
    window.history.replaceState(null, "", `?modo=${nuevo}`);
  }

  const cambiarExpress = useCallback((c: Partial<BorradorExpress>) => setExpress((a) => ({ ...a, ...c })), []);
  const cambiarMasivo = useCallback((c: Partial<BorradorMasivo>) => setMasivo((a) => ({ ...a, ...c })), []);

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
  } else if (carga.tipo !== "listo") {
    contenido = (
      <DosZonas
        vehiculos={modo === "express" ? <CamposExpress borrador={express} onCambio={cambiarExpress} disabled /> : <ZonaArchivo archivo={masivo.archivo} onElegir={NOOP} disabled />}
        parametros={
          carga.tipo === "cargando" ? (
            <VehicleLoader label="Cargando la parametrización…" />
          ) : (
            <InlineAlert
              tone="error"
              title="No se pudo cargar la parametrización"
              action={
                <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
                  Reintentar
                </button>
              }
            >
              {carga.mensaje}
            </InlineAlert>
          )
        }
      />
    );
  } else {
    contenido = (
      <Creacion
        configuracion={carga.configuracion}
        modo={modo}
        express={express}
        onExpress={cambiarExpress}
        masivo={masivo}
        onMasivo={cambiarMasivo}
        onOcupado={setOcupado}
        onCabecera={setCabeceraId}
      />
    );
  }

  return (
    <>
      <section aria-labelledby="nuevo-titulo" className={`${TARJETA_CLS} flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between`}>
        <div>
          <h1 id="nuevo-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
            Nuevo diagnóstico
          </h1>
          {sesion?.empresa.nombre && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{sesion.empresa.nombre}</p>}
        </div>
        {puedeCrear && (
          <SelectorModo modo={modo} onModo={cambiarModo} bloqueado={bloqueado} explicacion={cabeceraId ? EXPLICACION_CABECERA_CREADA : null} />
        )}
      </section>
      {contenido}
    </>
  );
}

interface CreacionProps {
  configuracion: Configuracion;
  modo: ModoCreacion;
  express: BorradorExpress;
  onExpress: (c: Partial<BorradorExpress>) => void;
  masivo: BorradorMasivo;
  onMasivo: (c: Partial<BorradorMasivo>) => void;
  onOcupado: (ocupado: boolean) => void;
  onCabecera: (id: string) => void;
}

/** Dueña del estado de parámetros: queda montada al cambiar de modo, así alcance, modo y tipo se conservan. */
function Creacion({ configuracion, modo, express, onExpress, masivo, onMasivo, onOcupado, onCabecera }: CreacionProps) {
  const p = useParametrizacion(configuracion);
  return modo === "express" ? (
    <CreacionExpress p={p} configuracion={configuracion} borrador={express} onBorrador={onExpress} onOcupado={onOcupado} />
  ) : (
    <CreacionMasiva p={p} borrador={masivo} onBorrador={onMasivo} onOcupado={onOcupado} onCabecera={onCabecera} />
  );
}
