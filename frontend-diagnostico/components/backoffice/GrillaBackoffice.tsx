"use client";

import { useCallback, useEffect, useRef, useState, type ComponentType } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { PanelModal } from "@flit/ui/PanelModal";
import { AuditoriaDictamen } from "@/components/backoffice/AuditoriaDictamen";
import { useSesion } from "@/components/DiagnosticoShell";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { EstadoGlobal, EstimadoVsConsumido } from "@/components/tablero/EstadoGlobal";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import { PanelDictamen } from "@/components/tablero/PanelDictamen";
import { FiltrosGrilla, VacioFiltros } from "@/components/tablero/FiltrosGrilla";
import { AYUDA_GRILLA } from "@/components/tablero/GrillaLote";
import { MenuAcciones, type MenuAccionesItem } from "@/components/ui/MenuAcciones";
import { FILTROS_GRILLA_VACIOS, aplicarFiltrosGrilla, hayFiltrosGrilla, type FiltrosGrilla as ValorFiltros } from "@/lib/tablero/filtrosTablero";
import { Etiqueta, TEXTO_SECUNDARIO_CLS } from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import {
  aplicarLlenado,
  esLlenable,
  mensajeErrorBackoffice,
  obtenerGrillaBackoffice,
  rutaDetalleBackoffice,
  type CeldaGrillaBackoffice,
  type FilaVehiculoBackoffice,
  type GrillaBackoffice as TGrillaBackoffice,
  type RespuestaLlenado,
} from "@/lib/backoffice/api";
import { nombreFuente } from "@/lib/configuracion";
import { etiquetaSaneamiento } from "@/lib/saneamiento/estados";
import { puedeGestionarSaneamiento, puedeOperarBackoffice } from "@/lib/sesion";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { fusionarCeldas } from "@/lib/tablero/fusionarCeldas";
import { FormularioLlenadoManual } from "./FormularioLlenadoManual";
import { IconoDxManual } from "@/components/dx/iconos/IconoDxManual";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

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

/** «Llenar a mano» compacto en la celda: el CTA primario de pastilla a la escala de 12px de la grilla. */
const BOTON_LLENAR_CLS = BOTON_PRIMARIO_CLS.replace("px-5 py-2 text-sm", "px-3 py-1 text-xs");

/** Ruta de la lista de lotes de la compañía elegida. */
const IconoDictamen = iconoComo("documento") as NonNullable<MenuAccionesItem["icon"]>;
const IconoAuditoria = iconoComo("portapapeles") as NonNullable<MenuAccionesItem["icon"]>;

const placaDe = (v: FilaVehiculo) => v.placa || v.vin || v.identificador;

const rutaLista = (empresa: string) => `/backoffice/diagnosticos?empresa=${encodeURIComponent(empresa)}`;

/**
 * Marca de origen manual (texto e icono, nunca solo color). Solo existe en el backoffice (Q5). HU #14562 · M7: el glifo es
 * IconoDxManual, decorativo junto al texto, a 12 px como el ícono anterior.
 */
export function MarcaManual() {
  return (
    <Etiqueta tono="info">
      <IconoDxManual tamano={16} className="h-3 w-3" />
      Manual
    </Etiqueta>
  );
}

/**
 * Etiqueta del saneamiento de la fila (HU #14577 AC38, spec-ui §7.1). Con gestionarSaneamiento va envuelta en un enlace a la
 * gestión (PS-5) con ?empresa=; sin ella, la etiqueta sola. Un ANULADO no se pinta (D-06: solo PS-4 y PS-5 lo rotulan).
 * El backoffice nunca ofrece la bandera para marcar (C7).
 */
function SaneamientoFila({ vehiculo, empresa, gestionar }: { vehiculo: FilaVehiculo; empresa: string; gestionar: boolean }) {
  const saneamiento = (vehiculo as unknown as FilaVehiculoBackoffice).saneamiento;
  const etiqueta = etiquetaSaneamiento(saneamiento?.estado);
  if (!saneamiento || !etiqueta) return null;
  if (!gestionar || !saneamiento.id) return <EtiquetaSaneamiento estado={saneamiento.estado} />;
  const identificador = vehiculo.placa || vehiculo.vin || vehiculo.identificador;
  return (
    <Link
      href={`/backoffice/saneamientos/${encodeURIComponent(saneamiento.id)}?empresa=${encodeURIComponent(empresa)}`}
      aria-label={`Gestionar saneamiento de ${identificador}: ${etiqueta.texto}`}
      className="rounded-full focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]"
    >
      <EtiquetaSaneamiento estado={saneamiento.estado} />
    </Link>
  );
}

/** Ícono del estado vacío: del kit (`iconoComo`) o de lucide cuando el kit no trae el glifo (iconoComo("buscar")). */
type IconoVacio = ComponentType<{ className?: string; strokeWidth?: number; "aria-hidden"?: boolean | "true" | "false" }>;
const ICONO_VACIO = iconoComo("portapapeles");

function Vacio({ titulo, children, Icono = ICONO_VACIO }: { titulo: string; children?: React.ReactNode; Icono?: IconoVacio }) {
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
 * en las celdas de origen MANUAL, visible solo aquí (Q5). Cada semáforo abre en una pestaña nueva la página del detalle del
 * backoffice (HU #14087, con ?empresa=) y, en una celda en ERROR llenable (no RUNT ni impuesto, no bloqueada por el RUNT) y con
 * las capacidades llenarConsultas y backofficeOperacion (solo FLIT), la celda ofrece «Llenar a mano», que abre FormularioLlenadoManual como modal. Al
 * confirmar, la celda pasa a verde con la marca y el lote se vuelve a leer desde su versión. Cuatro estados: cargando, error
 * (404 no encontrado en la compañía, 403 a la página 403, otro con Reintentar), vacío (sin vehículos) y lleno.
 */
export function GrillaBackoffice({ diagnosticoId, empresa }: GrillaBackofficeProps) {
  const router = useRouter();
  const { sesion } = useSesion();
  // Override y llenado manual son operación FLIT: además de su capacidad exigen `backofficeOperacion`. El SuperAdmin de otra
  // compañía abre la grilla pero no los ve; la API responde 403 de todos modos.
  const opera = puedeOperarBackoffice(sesion?.capacidades);
  const puedeLlenar = opera && (sesion?.capacidades.llenarConsultas ?? false);
  const puedeCorregir = opera && (sesion?.capacidades.corregirDictamen ?? false);
  // AC47: la auditoría de versiones del dictamen (autor, motivo, versiones) es solo de backoffice y exige verAuditoria.
  const puedeAuditar = sesion?.capacidades.verAuditoria ?? false;
  const gestionaSaneamiento = puedeGestionarSaneamiento(sesion?.capacidades);
  // El disparador viaja con la fila: el retorno de foco por defecto del Modal no sobrevive al doble montaje de StrictMode
  // (next dev captura como «previo» el título del propio modal), así que se devuelve aquí, como en PanelDictamen.
  const [auditoria, setAuditoria] = useState<{ vehiculo: FilaVehiculo; disparador: HTMLElement | null } | null>(null);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [llenado, setLlenado] = useState<Llenado | null>(null);
  const [dictamen, setDictamen] = useState<{ vehiculo: FilaVehiculo; disparador: HTMLElement | null } | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [avisoLectura, setAvisoLectura] = useState<string | null>(null);
  const [leyendo, setLeyendo] = useState(false);
  // HU #14584 §6.6 (decisión del orquestador): la misma barra de filtros del tablero, en memoria.
  const [filtros, setFiltros] = useState<ValorFiltros>(FILTROS_GRILLA_VACIOS);
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
      setAvisoLectura(mensajeErrorBackoffice(e, "No se pudo volver a leer la grilla. Intenta de nuevo en un momento."));
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

  const origenDe = (celda: CeldaGrilla) => (celda as CeldaGrillaBackoffice).origen ?? null;

  /** «Llenar a mano» en la celda (antes en el pie del modal de detalle): abre FormularioLlenadoManual como modal. */
  function accionCelda(celda: CeldaGrilla, vehiculo: FilaVehiculo) {
    if (!puedeLlenar || !esLlenable(celda)) return null;
    return (
      <button
        type="button"
        aria-label={`Llenar a mano ${nombreFuente(celda.fuente)} de ${vehiculo.identificador}`}
        className={BOTON_LLENAR_CLS}
        onClick={(e) => {
          setExito(null);
          setLlenado({ celda, vehiculo, disparador: e.currentTarget });
        }}
      >
        <Icono nombre="lapiz" className="h-3.5 w-3.5" />
        Llenar a mano
      </button>
    );
  }

  /**
   * HU #14584 §6.4 (A6.6): Dictamen y Auditoría salen de la celda de la placa y pasan a la columna «Acciones» con el menú de
   * Diagnóstico; misma visibilidad por capacidad (la auditoría solo con verAuditoria). El foco vuelve al disparador al cerrar.
   */
  function accionesFila(v: FilaVehiculo) {
    const placa = placaDe(v);
    const items: MenuAccionesItem[] = [
      {
        key: "dictamen",
        label: "Ver dictamen",
        ariaLabel: `Ver dictamen de ${placa}`,
        icon: IconoDictamen,
        grupo: "dictamen",
        onSelect: ({ disparador }) => setDictamen({ vehiculo: v, disparador }),
      },
    ];
    if (puedeAuditar)
      items.push({
        key: "auditoria",
        label: "Auditoría del dictamen",
        ariaLabel: `Auditoría del dictamen de ${placa}`,
        icon: IconoAuditoria,
        grupo: "dictamen",
        onSelect: ({ disparador }) => setAuditoria({ vehiculo: v, disparador }),
      });
    return <MenuAcciones ariaLabel={`Acciones de la placa ${placa}`} triggerLabel="Acciones" items={items} />;
  }

  let contenido: React.ReactNode;
  let titulo = "Grilla del lote";
  if (carga.tipo === "cargando" || es403) {
    contenido = <VehicleLoader label="Cargando la grilla…" />;
  } else if (carga.tipo === "error") {
    const { error } = carga;
    contenido =
      error instanceof ApiError && error.status === 404 ? (
        <Vacio titulo="No encontramos este lote en la compañía elegida" Icono={iconoComo("buscar")}>
          <p className="max-w-md text-sm text-muted-foreground">Revisa la compañía o vuelve a la lista de sus lotes.</p>
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
          {mensajeErrorBackoffice(error, "Intenta de nuevo en un momento.")}
        </InlineAlert>
      );
  } else {
    const { grilla } = carga;
    titulo = grilla.nombre;
    const filas = grilla.vehiculos as unknown as FilaVehiculo[];
    const visibles = aplicarFiltrosGrilla(filas, filtros);
    const filtrando = hayFiltrosGrilla(filtros);
    contenido =
      grilla.avance.vehiculos === 0 ? (
        <Vacio titulo="Este lote no tiene vehículos">
          <p className="max-w-md text-sm text-muted-foreground">No hay celdas que revisar ni llenar.</p>
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
            <FiltrosGrilla
              grilla={grilla}
              vehiculos={filas}
              valor={filtros}
              onCambiar={setFiltros}
              onLimpiar={() => setFiltros(FILTROS_GRILLA_VACIOS)}
              hayActivos={filtrando}
              resumen={filtrando ? `${visibles.length} de ${filas.length}` : `Mostrando los ${filas.length} vehículos`}
            />
            {filtrando && visibles.length === 0 ? (
              <VacioFiltros onLimpiar={() => setFiltros(FILTROS_GRILLA_VACIOS)} />
            ) : (
            <div ref={contenedor}>
              <GrillaSemaforos
                grilla={grilla as unknown as Grilla}
                vehiculos={visibles}
                puedeReprocesar={false}
                hrefDetalle={(celda, v) => rutaDetalleBackoffice(grilla.id, v.id, celda.fuente, empresa)}
                accionCelda={accionCelda}
                marcaCelda={(celda) => (origenDe(celda) === "MANUAL" ? <MarcaManual /> : null)}
                accionEncabezado={(v) =>
                  v.dictamen_ajustado || (v as unknown as FilaVehiculoBackoffice).saneamiento ? (
                    // Solo marcas de estado bajo la placa (spec-ui §6.1); las acciones van en «Acciones».
                    <span className="flex flex-wrap items-center gap-1.5">
                      {v.dictamen_ajustado && <Etiqueta tono="info">Ajustado por FLIT</Etiqueta>}
                      <SaneamientoFila vehiculo={v} empresa={empresa} gestionar={gestionaSaneamiento} />
                    </span>
                  ) : null
                }
                accionVehiculo={accionesFila}
                tituloAccionVehiculo="Acciones"
              />
            </div>
            )}
            <p className={AYUDA_CLS}>{AYUDA_GRILLA}</p>
            <EstimadoVsConsumido estimado={grilla.estimado_consultas} />
          </section>
        </>
      );
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="grilla-bo-titulo"
        tituloId="grilla-bo-titulo"
        titulo={titulo}
        descripcion={<span className="break-all">{`Backoffice · Compañía ${empresa}`}</span>}
        icono="diagnostico"
        acciones={
          <>
            <Link href={rutaLista(empresa)} className={BOTON_SECUNDARIO_CLS}>
              <Icono nombre="izquierda" className="h-4 w-4" />
              Volver a los lotes
            </Link>
            {carga.tipo === "listo" && (
              <button type="button" onClick={() => void releer()} disabled={leyendo} aria-busy={leyendo} className={BOTON_SECUNDARIO_CLS}>
                <Icono nombre="reintento" className="h-4 w-4" />
                Actualizar
              </button>
            )}
          </>
        }
      />
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
      {auditoria && (
        <PanelModal
          abierto
          onCerrar={() => {
            const { disparador } = auditoria;
            setAuditoria(null);
            if (disparador?.isConnected) disparador.focus();
          }}
          titulo={`Auditoría del dictamen · ${auditoria.vehiculo.placa || auditoria.vehiculo.vin || auditoria.vehiculo.identificador}`}
        >
          <AuditoriaDictamen diagnosticoId={diagnosticoId} vehiculoId={auditoria.vehiculo.id} empresa={empresa} />
        </PanelModal>
      )}
      {dictamen && (
        <PanelDictamen
          diagnosticoId={diagnosticoId}
          vehiculoId={dictamen.vehiculo.id}
          identificador={dictamen.vehiculo.placa || dictamen.vehiculo.vin || dictamen.vehiculo.identificador}
          modo="backoffice"
          empresa={empresa}
          puedeCorregir={puedeCorregir}
          onClose={() => setDictamen(null)}
          devolverFocoA={dictamen.disparador}
        />
      )}
    </>
  );
}
