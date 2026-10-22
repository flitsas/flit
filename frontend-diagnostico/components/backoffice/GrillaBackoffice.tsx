"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, ClipboardList, PencilLine, RefreshCw, SearchX } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { EstadoGlobal, EstimadoVsConsumido } from "@/components/tablero/EstadoGlobal";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import { Etiqueta, TEXTO_SECUNDARIO_CLS } from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import {
  aplicarLlenado,
  esLlenable,
  obtenerGrillaBackoffice,
  type CeldaGrillaBackoffice,
  type GrillaBackoffice as TGrillaBackoffice,
  type RespuestaLlenado,
} from "@/lib/backoffice/api";
import { nombreFuente } from "@/lib/configuracion";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import type { CargadorDetalle, DetalleCelda } from "@/lib/tablero/detalle";
import { fusionarCeldas } from "@/lib/tablero/fusionarCeldas";
import { FormularioLlenadoManual } from "./FormularioLlenadoManual";

export interface GrillaBackofficeProps {
  diagnosticoId: string;
  /** Compañía elegida en el selector (viaja como ?empresa= en cada llamada). */
  empresa: string;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; grilla: TGrillaBackoffice };

interface Llenado {
  celda: CeldaGrilla;
  vehiculo: FilaVehiculo;
  /** Semáforo de la celda: recibe el foco al cerrar el formulario. */
  disparador: HTMLElement | null;
}

/** Ruta de la lista de lotes de la compañía elegida. */
const rutaLista = (empresa: string) => `/backoffice/diagnosticos?empresa=${encodeURIComponent(empresa)}`;

/** Marca de origen manual (texto e icono, nunca solo color). Solo existe en esta pantalla (Q5). */
function MarcaManual() {
  return (
    <Etiqueta tono="info">
      <PencilLine className="h-3 w-3" aria-hidden="true" />
      Manual
    </Etiqueta>
  );
}

function Vacio({ titulo, children, Icono = ClipboardList }: { titulo: string; children?: React.ReactNode; Icono?: typeof ClipboardList }) {
  return (
    <section aria-labelledby="grilla-bo-vacio" className={`${TARJETA_CLS} flex flex-col items-center gap-3 py-12 text-center`}>
      <Icono className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
      <h2 id="grilla-bo-vacio" className="text-lg font-semibold">
        {titulo}
      </h2>
      {children}
    </section>
  );
}

/**
 * Grilla de un lote para el backoffice FLIT (HU #13802, F06-H5; F06-C6..C9, C14) en /backoffice/diagnosticos/{id}?empresa=.
 * Lee GET /backoffice/diagnosticos/{id}/grilla?empresa=&desde= y reutiliza GrillaSemaforos (HU #13748) con la marca «Manual»
 * en las celdas de origen MANUAL, visible solo aquí (Q5). Cada semáforo abre DetalleCelda (HU #13801); el backoffice no tiene
 * ruta de detalle propia, así que el detalle se arma con lo que trae la grilla (estado, error, fecha) y, en una celda en ERROR
 * llenable (no RUNT ni impuesto, no bloqueada por el RUNT) y con la capacidad llenarConsultas, ofrece «Llenar a mano». Al
 * confirmar, la celda pasa a verde con la marca y el lote se vuelve a leer desde su versión. Cuatro estados: cargando, error
 * (404 no encontrado en la compañía, 403 a la página 403, otro con Reintentar), vacío (sin vehículos) y lleno.
 */
export function GrillaBackoffice({ diagnosticoId, empresa }: GrillaBackofficeProps) {
  const router = useRouter();
  const { sesion } = useSesion();
  const puedeLlenar = sesion?.capacidades.llenarConsultas ?? false;
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [llenado, setLlenado] = useState<Llenado | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [avisoLectura, setAvisoLectura] = useState<string | null>(null);
  const [leyendo, setLeyendo] = useState(false);
  const grillaRef = useRef<TGrillaBackoffice | null>(null);
  const contenedor = useRef<HTMLDivElement>(null);

  const poner = useCallback((grilla: TGrillaBackoffice) => {
    grillaRef.current = grilla;
    setCarga({ tipo: "listo", grilla });
  }, []);

  useEffect(() => {
    const control = new AbortController();
    grillaRef.current = null;
    obtenerGrillaBackoffice(diagnosticoId, empresa, 0, control.signal).then(
      (grilla) => {
        if (!control.signal.aborted) poner({ ...grilla, vehiculos: [...grilla.vehiculos].sort((a, b) => a.orden - b.orden) });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [diagnosticoId, empresa, intento, poner]);

  const es403 = carga.tipo === "error" && carga.error instanceof ApiError && carga.error.status === 403;
  useEffect(() => {
    if (es403) router.replace("/403?code=SIN_PERMISO_BACKOFFICE");
  }, [es403, router]);

  /** Lee una vez los cambios desde la versión que se tiene y los fusiona (tras un llenado o un 409). */
  const releer = useCallback(async () => {
    const actual = grillaRef.current;
    if (!actual) return;
    setLeyendo(true);
    setAvisoLectura(null);
    try {
      const cambios = await obtenerGrillaBackoffice(diagnosticoId, empresa, actual.version);
      const vigente = grillaRef.current ?? actual;
      poner(fusionarCeldas(vigente as unknown as Grilla, cambios as unknown as Grilla) as unknown as TGrillaBackoffice);
    } catch (e) {
      setAvisoLectura(e instanceof Error ? e.message : "No se pudo volver a leer la grilla.");
    } finally {
      setLeyendo(false);
    }
  }, [diagnosticoId, empresa, poner]);

  function alLlenar(respuesta: RespuestaLlenado) {
    const actual = grillaRef.current;
    const seleccion = llenado;
    setLlenado(null);
    if (actual) poner(aplicarLlenado(actual, respuesta));
    if (seleccion) setExito(`Se llenó a mano ${nombreFuente(seleccion.celda.fuente)} de ${seleccion.vehiculo.identificador}.`);
    // La versión del lote en memoria sigue siendo la previa: la relectura desde ella trae el estado recalculado y el avance.
    if (actual) void releer();
  }

  /** El backoffice no tiene ruta de detalle de celda: el detalle se arma con la celda de la grilla, sin resultado. */
  const cargarDetalle: CargadorDetalle = useCallback(async (_lote, vehiculoId, fuente) => {
    const v = grillaRef.current?.vehiculos.find((x) => x.id === vehiculoId);
    const c = v?.celdas.find((x) => x.fuente === fuente);
    if (!v || !c) throw new ApiError(404, "No se encontró la celda.", "NO_ENCONTRADO");
    const detalle: DetalleCelda = {
      id: c.id,
      vehiculo_id: v.id,
      fuente: c.fuente,
      estado: c.estado,
      motivo_na: c.motivo_na,
      tipo_error: c.tipo_error,
      dato_obtenido_en: c.dato_obtenido_en,
      reutilizada: c.reutilizada,
      resultado: null,
      personas: null,
    };
    return detalle;
  }, []);

  const origenDe = (celda: CeldaGrilla) => (celda as CeldaGrillaBackoffice).origen ?? null;

  function accionesDetalle(celda: CeldaGrilla, vehiculo: FilaVehiculo, cerrar: () => void) {
    const manual = origenDe(celda) === "MANUAL";
    const llenar = puedeLlenar && esLlenable(celda);
    if (!manual && !llenar && celda.estado !== "COMPLETADO") return undefined;
    return (
      <>
        {celda.estado === "COMPLETADO" && (
          <span className={`${TEXTO_SECUNDARIO_CLS} mr-auto self-center`}>
            {manual ? "Dato llenado a mano desde el backoffice." : "El backoffice no tiene todavía una ruta para ver el resultado de esta consulta."}
          </span>
        )}
        {llenar && (
          <button
            type="button"
            className={BOTON_PRIMARIO_CLS}
            onClick={() => {
              cerrar();
              setExito(null);
              const disparador = contenedor.current?.querySelector<HTMLElement>(`[data-celda="${celda.id}"] button`) ?? null;
              setLlenado({ celda, vehiculo, disparador });
            }}
          >
            <PencilLine className="h-4 w-4" aria-hidden="true" />
            Llenar a mano
          </button>
        )}
      </>
    );
  }

  let contenido: React.ReactNode;
  let titulo = "Grilla del lote";
  if (carga.tipo === "cargando" || es403) {
    contenido = <VehicleLoader label="Cargando la grilla…" />;
  } else if (carga.tipo === "error") {
    const { error } = carga;
    contenido =
      error instanceof ApiError && error.status === 404 ? (
        <Vacio titulo="No encontramos este lote en la compañía elegida" Icono={SearchX}>
          <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Revisa la compañía o vuelve a la lista de sus lotes.</p>
          <Link href={rutaLista(empresa)} className={BOTON_SECUNDARIO_CLS}>
            Volver a los lotes
          </Link>
        </Vacio>
      ) : (
        <InlineAlert
          tone="error"
          title="No se pudo cargar la grilla"
          action={
            <button
              type="button"
              onClick={() => {
                setCarga({ tipo: "cargando" });
                setIntento((n) => n + 1);
              }}
              className={BOTON_REINTENTAR_CLS}
            >
              Reintentar
            </button>
          }
        >
          {error instanceof Error ? error.message : "Intenta de nuevo en un momento."}
        </InlineAlert>
      );
  } else {
    const { grilla } = carga;
    titulo = grilla.nombre;
    contenido =
      grilla.avance.vehiculos === 0 ? (
        <Vacio titulo="Este lote no tiene vehículos">
          <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">No hay celdas que revisar ni llenar.</p>
        </Vacio>
      ) : (
        <>
          <section aria-label="Avance del lote" className={TARJETA_CLS}>
            <EstadoGlobal grilla={grilla as unknown as Grilla} sondeando={false} />
          </section>
          {exito && (
            <InlineAlert tone="success" title="Celda llenada">
              {exito}
            </InlineAlert>
          )}
          {avisoLectura && (
            <InlineAlert tone="warning" title="No se pudo volver a leer la grilla">
              {avisoLectura}
            </InlineAlert>
          )}
          <section aria-label="Grilla del lote" className={`${TARJETA_CLS} flex flex-col gap-4`}>
            <p className={TEXTO_SECUNDARIO_CLS}>
              Las celdas con la marca «Manual» se llenaron a mano desde el backoffice. La compañía ve el mismo dato, sin la marca.
            </p>
            <div ref={contenedor}>
              <GrillaSemaforos
                grilla={grilla as unknown as Grilla}
                puedeReprocesar={false}
                cargarDetalle={cargarDetalle}
                accionesDetalle={accionesDetalle}
                marcaCelda={(celda) => (origenDe(celda) === "MANUAL" ? <MarcaManual /> : null)}
              />
            </div>
            <EstimadoVsConsumido estimado={grilla.estimado_consultas} />
          </section>
        </>
      );
  }

  return (
    <>
      <section aria-labelledby="grilla-bo-titulo" className={`${TARJETA_CLS} flex flex-wrap items-start justify-between gap-3`}>
        <div className="min-w-0">
          <h1 id="grilla-bo-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
            {titulo}
          </h1>
          <p className="mt-1 break-all text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{`Backoffice · Compañía ${empresa}`}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link href={rutaLista(empresa)} className={BOTON_SECUNDARIO_CLS}>
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            Volver a los lotes
          </Link>
          {carga.tipo === "listo" && (
            <button type="button" onClick={() => void releer()} disabled={leyendo} aria-busy={leyendo} className={BOTON_SECUNDARIO_CLS}>
              <RefreshCw className="h-4 w-4" aria-hidden="true" />
              Actualizar
            </button>
          )}
        </div>
      </section>
      {contenido}
      {llenado && (
        <FormularioLlenadoManual
          consultaId={llenado.celda.id}
          empresa={empresa}
          nombreFuente={nombreFuente(llenado.celda.fuente)}
          identificador={llenado.vehiculo.identificador}
          onClose={() => setLlenado(null)}
          onLlenada={alLlenar}
          onCeldaCambiada={() => void releer()}
          devolverFocoA={llenado.disparador}
        />
      )}
    </>
  );
}
