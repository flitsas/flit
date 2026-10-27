"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Info } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Modal } from "@flit/ui/Modal";
import { DocumentoDeclarado, enmascararDocumento } from "@/components/comun/DocumentoDeclarado";
import { BOTON_REINTENTAR_CLS } from "@/components/configuracion/estilos";
import type { Fuente } from "@/lib/configuracion";
import type { EstadoCelda } from "@/lib/tablero/api";
import {
  FUENTES_PERSONA,
  obtenerDetalleCelda,
  textoMotivoNa,
  textoTipoError,
  traducirErrorDetalle,
  type CargadorDetalle,
  type DetalleCelda as Detalle,
  type ErrorDetalle,
  type EstadoPersona,
  type PersonaDetalle,
} from "@/lib/tablero/detalle";
import { ChipEstadoCelda, fechaDelDato } from "./CeldaSemaforo";
import { RespuestaFuente } from "./RespuestaFuente";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  FILA_TABLA_CLS,
  TARJETA_DETALLE_CLS,
  Vacio,
} from "./vistasDetalle/comun";

export interface DetalleCeldaProps {
  loteId: string;
  vehiculoId: string;
  fuente: Fuente;
  nombreFuente: string;
  identificador: string;
  /** Cierre pedido por la X, Escape o el clic fuera del panel. */
  onClose: () => void;
  /** Cargador del detalle; por defecto GET de la compañía. El backoffice (F06-H5) pasa el suyo con `?empresa=`. */
  cargar?: CargadorDetalle;
  /** Acciones al pie (p. ej. «Llenar a mano» en el backoffice). */
  acciones?: ReactNode;
  /** Elemento que recibe el foco al cerrar (el semáforo); si falta, el que tenía el foco al abrir. */
  devolverFocoA?: HTMLElement | null;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: ErrorDetalle } | { tipo: "listo"; detalle: Detalle };

const ENFOCABLES = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

/**
 * Atrapa el foco dentro del diálogo que contiene `ancla`, lo pone en su primer control y lo devuelve al cerrar. También lo usa
 * el formulario del llenado manual del backoffice (HU #13802).
 */
export function useFocoAtrapado(ancla: React.RefObject<HTMLElement | null>, devolverFocoA?: HTMLElement | null) {
  useEffect(() => {
    const previo = devolverFocoA ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    const dialogo = ancla.current?.closest<HTMLElement>('[role="dialog"]');
    if (!dialogo) return;
    const enfocables = () => Array.from(dialogo.querySelectorAll<HTMLElement>(ENFOCABLES));
    enfocables()[0]?.focus();

    const alTeclear = (e: KeyboardEvent) => {
      if (e.key !== "Tab") return;
      const lista = enfocables();
      if (lista.length === 0) return;
      const primero = lista[0];
      const ultimo = lista[lista.length - 1];
      const activo = document.activeElement;
      if (!dialogo.contains(activo)) {
        e.preventDefault();
        primero.focus();
      } else if (e.shiftKey && activo === primero) {
        e.preventDefault();
        ultimo.focus();
      } else if (!e.shiftKey && activo === ultimo) {
        e.preventDefault();
        primero.focus();
      }
    };
    document.addEventListener("keydown", alTeclear);
    return () => {
      document.removeEventListener("keydown", alTeclear);
      if (previo?.isConnected) previo.focus();
    };
  }, [ancla, devolverFocoA]);
}

const TITULO_SECCION_CLS = "text-sm font-semibold text-flit-primary dark:text-white";
const ROTULO_CLS = "text-xs text-[var(--nav-texto)] dark:text-slate-300";
const ROLES: Record<PersonaDetalle["rol"], string> = { PROPIETARIO: "Propietario", LOCATARIO: "Locatario" };

/** Estado de una persona con el mismo chip de la tabla: OK → Ejecutado (OK), ERROR → Error, SIN_RESULTADO → Novedad. */
const ESTADO_PERSONA: Record<EstadoPersona, EstadoCelda> = { OK: "COMPLETADO", ERROR: "ERROR", SIN_RESULTADO: "NA" };

const fechaOGuion = (iso: string | null) => (iso ? fechaDelDato(iso) : "—");

/** Sección del detalle con su título h3 (región con nombre accesible). */
function Seccion({ titulo, children }: { titulo: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`${TARJETA_DETALLE_CLS} flex flex-col gap-3`}>
      <h3 id={id} className={TITULO_SECCION_CLS}>
        {titulo}
      </h3>
      {children}
    </section>
  );
}

function DocumentoPersona({ persona }: { persona: PersonaDetalle }) {
  if (persona.declarado_por_cliente) return <DocumentoDeclarado tipo={persona.tipo_documento} documento={persona.documento_enmascarado} />;
  return (
    <span className="font-mono text-xs font-medium text-flit-primary dark:text-white">{`${persona.tipo_documento} ${enmascararDocumento(persona.documento_enmascarado)}`}</span>
  );
}

function EstadoDePersona({ persona }: { persona: PersonaDetalle }) {
  return (
    <span className="flex flex-col items-start gap-1">
      <ChipEstadoCelda estado={ESTADO_PERSONA[persona.estado]} />
      {persona.estado === "SIN_RESULTADO" && <span className={ROTULO_CLS}>Sin resultado</span>}
      {persona.estado === "ERROR" && <span className={ROTULO_CLS}>{textoTipoError(persona.tipo_error)}</span>}
    </span>
  );
}

/** Auditoría por persona: rol, documento enmascarado, consultado_en y estado mapeado (HU #14087 AC7). */
function TablaPersonas({ personas }: { personas: PersonaDetalle[] }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
      <table aria-label="Personas consultadas" className="w-full border-collapse text-xs">
        <thead>
          <tr>
            {["Persona", "Rol", "Documento", "Consultado en", "Estado"].map((t) => (
              <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                {t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {personas.map((p) => (
            <tr key={p.orden} className={FILA_TABLA_CLS}>
              <td className={`${CELDA_TABLA_CLS} tabular-nums`}>{p.orden}</td>
              <td className={CELDA_TABLA_CLS}>{ROLES[p.rol] ?? p.rol}</td>
              <td className={CELDA_TABLA_CLS}>
                <DocumentoPersona persona={p} />
              </td>
              <td className={`${CELDA_TABLA_CLS} whitespace-nowrap tabular-nums`}>{fechaOGuion(p.consultado_en)}</td>
              <td className={CELDA_TABLA_CLS}>
                <EstadoDePersona persona={p} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** «Auditoría de consulta»: fecha y hora en Colombia (la del dato original si es reutilizada) y estado; por persona, su tabla. */
function Auditoria({ detalle }: { detalle: Detalle }) {
  return (
    <Seccion titulo="Auditoría de consulta">
      <dl className="grid grid-cols-1 gap-x-6 gap-y-3 sm:grid-cols-2">
        <div className="flex flex-col items-start gap-1">
          <dt className={ROTULO_CLS}>Estado</dt>
          <dd className="flex flex-col items-start gap-1">
            <ChipEstadoCelda estado={detalle.estado} />
            {detalle.estado === "NA" && <span className={ROTULO_CLS}>{textoMotivoNa(detalle.motivo_na)}</span>}
          </dd>
        </div>
        <div className="flex flex-col gap-1">
          <dt className={ROTULO_CLS}>Fecha y hora de la consulta</dt>
          <dd className="flex flex-col gap-1">
            <span className="text-sm font-medium tabular-nums text-flit-primary dark:text-white">{fechaOGuion(detalle.dato_obtenido_en)}</span>
            {detalle.reutilizada && (
              <span className={`inline-flex items-center gap-1 ${ROTULO_CLS}`}>
                <Info className="h-3.5 w-3.5" aria-hidden="true" />
                Fecha del dato original: reutilizado de una consulta del mismo día
              </span>
            )}
          </dd>
        </div>
      </dl>
      {detalle.personas && detalle.personas.length > 0 && <TablaPersonas personas={detalle.personas} />}
    </Seccion>
  );
}

/** Respuesta de una persona: todos los campos de su resultado, o que la fuente no respondió nada para ella. */
function RespuestaPersona({ persona }: { persona: PersonaDetalle }) {
  const id = useId();
  const titulo = `${ROLES[persona.rol] ?? persona.rol} ${persona.orden}`;
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h4 id={id} className={TITULO_SECCION_CLS}>
        {titulo}
      </h4>
      {persona.resultado ? (
        <RespuestaFuente datos={persona.resultado} etiqueta={`Respuesta de la fuente · ${titulo}`} nivel={5} />
      ) : (
        <Vacio>Sin respuesta de la fuente para esta persona.</Vacio>
      )}
    </section>
  );
}

/** «Respuesta de la fuente»: lo que devuelve el API de detalle, verbatim (RespuestaFuente); sin resultado, solo el mensaje. */
function Respuesta({ detalle }: { detalle: Detalle }) {
  const porPersona = FUENTES_PERSONA.has(detalle.fuente) || detalle.personas !== null;
  const personas = detalle.personas ?? [];
  let cuerpo: ReactNode;
  if (detalle.estado === "PENDIENTE" || detalle.estado === "EN_CURSO") {
    cuerpo = <Vacio>La consulta todavía no se ha ejecutado: no hay respuesta de la fuente. Vuelve a abrir el detalle en un momento.</Vacio>;
  } else if (detalle.estado === "NA") {
    cuerpo = <Vacio>{`${textoMotivoNa(detalle.motivo_na)} Sin respuesta de la fuente.`}</Vacio>;
  } else if (detalle.estado === "ERROR") {
    cuerpo = (
      <>
        <InlineAlert tone="error" title="La consulta terminó en error">
          {textoTipoError(detalle.tipo_error)}
        </InlineAlert>
        {personas.map((p) => (
          <RespuestaPersona key={p.orden} persona={p} />
        ))}
      </>
    );
  } else if (porPersona && personas.length === 0) {
    cuerpo = <Vacio>{`El vehículo no tiene ${detalle.fuente.endsWith("LOCATARIO") ? "locatarios" : "propietarios"} para esta consulta.`}</Vacio>;
  } else if (porPersona) {
    cuerpo = personas.map((p) => <RespuestaPersona key={p.orden} persona={p} />);
  } else if (detalle.resultado) {
    cuerpo = <RespuestaFuente datos={detalle.resultado} etiqueta="Respuesta de la fuente" />;
  } else {
    cuerpo = <Vacio>Esta consulta no trae campos de respuesta para mostrar.</Vacio>;
  }
  return <Seccion titulo="Respuesta de la fuente">{cuerpo}</Seccion>;
}

/**
 * Cuerpo del detalle ya cargado (HU #14087): «Auditoría de consulta» y «Respuesta de la fuente». Solo lee las claves del
 * contrato (DTO cerrado, R-20): las claves de más que llegaran fuera de `resultado` no se pintan.
 */
export function VistaDetalleCelda({ detalle }: { detalle: Detalle }) {
  return (
    <div className="flex flex-col gap-4">
      <Auditoria detalle={detalle} />
      <Respuesta detalle={detalle} />
    </div>
  );
}

/**
 * Detalle de una celda del tablero (HU #13801, F06-H4; F06-C1..C4/C12/C13). Diálogo de @flit/ui (overlay con blur, título
 * por aria-labelledby, X y Escape cierran) con el foco atrapado y devuelto al semáforo al cerrar. Cuatro estados: cargando
 * (loader de @flit/ui), error (404 «no disponible» sin datos, 403 sin permiso, otro con Reintentar), vacío (sin personas o
 * sin resultado) y lleno (auditoría de consulta y respuesta de la fuente verbatim, HU #14087). La vista de la compañía no
 * tiene ningún indicador de origen manual (Q5).
 */
export function DetalleCelda({ loteId, vehiculoId, fuente, nombreFuente, identificador, onClose, cargar = obtenerDetalleCelda, acciones, devolverFocoA }: DetalleCeldaProps) {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const ancla = useRef<HTMLDivElement>(null);
  const cargarRef = useRef(cargar);
  useEffect(() => {
    cargarRef.current = cargar;
  }, [cargar]);
  useFocoAtrapado(ancla, devolverFocoA);

  useEffect(() => {
    const control = new AbortController();
    cargarRef.current(loteId, vehiculoId, fuente, control.signal).then(
      (detalle) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", detalle });
      },
      (e: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error: traducirErrorDetalle(e) });
      },
    );
    return () => control.abort();
  }, [loteId, vehiculoId, fuente, intento]);

  function reintentar() {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }

  let contenido: ReactNode;
  if (carga.tipo === "cargando") {
    contenido = <VehicleLoader label="Cargando el detalle…" />;
  } else if (carga.tipo === "error") {
    const { error } = carga;
    contenido = (
      <InlineAlert
        tone={error.tipo === "NO_DISPONIBLE" ? "warning" : "error"}
        title={error.titulo}
        action={
          error.tipo === "FALLO" ? (
            <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          ) : undefined
        }
      >
        {error.mensaje}
      </InlineAlert>
    );
  } else {
    contenido = <VistaDetalleCelda detalle={carga.detalle} />;
  }

  return (
    <Modal open onClose={onClose} title={`${nombreFuente} · ${identificador}`} size="lg" footer={acciones ? <div className="flex justify-end gap-2">{acciones}</div> : undefined}>
      <div ref={ancla}>{contenido}</div>
    </Modal>
  );
}
