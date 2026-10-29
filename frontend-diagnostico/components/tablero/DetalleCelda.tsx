"use client";

import { useEffect, useId, type ReactNode } from "react";
import { Info } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { DocumentoDeclarado, enmascararDocumento } from "@/components/comun/DocumentoDeclarado";
import type { EstadoCelda } from "@/lib/tablero/api";
import type { Fuente } from "@/lib/configuracion";
import {
  FUENTES_PERSONA,
  FUENTES_RESPUESTA_PROVEEDOR,
  textoMotivoNa,
  textoTipoError,
  type DetalleCelda as Detalle,
  type EstadoPersona,
  type PersonaDetalle,
} from "@/lib/tablero/detalle";
import { ChipEstadoCelda, fechaDelDato } from "./CeldaSemaforo";
import { RespuestaFuente, RespuestaProveedor } from "./RespuestaFuente";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  FILA_TABLA_CLS,
  TARJETA_DETALLE_CLS,
  Vacio,
} from "./vistasDetalle/comun";

// Cuerpo del detalle de una celda (HU #13801; HU #14087): desde HU #14087 se pinta en su página, en pestaña nueva
// (PaginaDetalleConsulta), ya sin modal. Aquí quedan la vista (VistaDetalleCelda) y el foco atrapado que usa el formulario
// modal del llenado manual del backoffice (useFocoAtrapado).

const ENFOCABLES = 'a[href],summary,button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

/**
 * Atrapa el foco dentro del diálogo que contiene `ancla`, lo pone en su primer control y lo devuelve al cerrar. Lo usa el
 * formulario modal del llenado manual del backoffice (HU #13802).
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

/** Sección del detalle con su título h2 bajo el h1 de la página (región con nombre accesible). */
function Seccion({ titulo, children }: { titulo: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`${TARJETA_DETALLE_CLS} flex flex-col gap-3`}>
      <h2 id={id} className={TITULO_SECCION_CLS}>
        {titulo}
      </h2>
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

/**
 * Aviso cuando no hay respuesta completa guardada (HU #14087 AC8 ajustado). Mismo texto para consulta anterior al cambio,
 * reutilizada o llenada a mano: no revela el origen manual (R-20).
 */
export const AVISO_RESULTADO_RESUMIDO =
  "Esta consulta no tiene guardada la respuesta completa de la fuente (es anterior al cambio, se reutilizó o se registró sin consulta). Se muestra el resultado resumido.";

function AvisoResumido() {
  return <InlineAlert tone="info">{AVISO_RESULTADO_RESUMIDO}</InlineAlert>;
}

/**
 * Aviso cuando falta la respuesta íntegra del proveedor (HU #14154 en RUNT persona y RUES; HU #14184 en SIMIT): consulta
 * anterior al cambio, servida desde caché o llenada a mano. Texto literal de la decisión del usuario.
 */
export const AVISO_SIN_RESPUESTA_PROVEEDOR = "Esta consulta no tiene la respuesta completa del proveedor. Para verla, vuelve a consultar.";

/** Respuesta íntegra del proveedor de la persona (HU #14154), solo en las fuentes que la pintan; si no, undefined. */
const proveedorDePersona = (p: PersonaDetalle, fuente: Fuente): unknown =>
  FUENTES_RESPUESTA_PROVEEDOR.has(fuente) && p.respuesta_proveedor !== null && p.respuesta_proveedor !== undefined ? p.respuesta_proveedor : undefined;

/** Lo que se pinta de una persona: su respuesta completa o, si falta, su resultado resumido. */
const datosDePersona = (p: PersonaDetalle): object | null => p.respuesta_fuente ?? p.resultado;
const personaResumida = (p: PersonaDetalle, fuente: Fuente) =>
  proveedorDePersona(p, fuente) === undefined && !p.respuesta_fuente && p.resultado !== null;

/** JSON del proveedor sin datos: objeto o lista vacíos, o texto vacío. */
const sinDatos = (v: unknown) =>
  v === "" || (Array.isArray(v) && v.length === 0) || (typeof v === "object" && v !== null && !Array.isArray(v) && Object.keys(v).length === 0);

/**
 * «Respuesta del proveedor» de una persona (HU #14154): rol y documento enmascarado en el título (h4, bajo el h3 de la
 * persona) y el JSON íntegro del proveedor, o que el proveedor no devolvió datos.
 */
function RespuestaProveedorPersona({ persona, datos, titulo }: { persona: PersonaDetalle; datos: unknown; titulo: string }) {
  const id = useId();
  const documento = [persona.tipo_documento, enmascararDocumento(persona.documento_enmascarado)].filter(Boolean).join(" ");
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h4 id={id} className={TITULO_SECCION_CLS}>
        {`Respuesta del proveedor · ${ROLES[persona.rol] ?? persona.rol} · ${documento}`}
      </h4>
      {sinDatos(datos) ? (
        <Vacio>El proveedor no devolvió datos para esta persona.</Vacio>
      ) : (
        <RespuestaProveedor datos={datos} etiqueta={`Respuesta del proveedor · ${titulo}`} nivel={5} />
      )}
    </section>
  );
}

/**
 * Respuesta de una persona: en RUNT persona, RUES y SIMIT por persona, la respuesta íntegra del proveedor si la trae
 * (HU #14154 y HU #14184); si no, todos los campos de su respuesta completa (o de su resultado) y, en esas fuentes, el
 * aviso de que falta la del proveedor.
 */
function RespuestaPersona({ persona, fuente }: { persona: PersonaDetalle; fuente: Fuente }) {
  const id = useId();
  const titulo = `${ROLES[persona.rol] ?? persona.rol} ${persona.orden}`;
  const proveedor = proveedorDePersona(persona, fuente);
  const datos = datosDePersona(persona);
  let cuerpo: ReactNode;
  if (proveedor !== undefined) {
    cuerpo = <RespuestaProveedorPersona persona={persona} datos={proveedor} titulo={titulo} />;
  } else {
    cuerpo = (
      <>
        {FUENTES_RESPUESTA_PROVEEDOR.has(fuente) && <InlineAlert tone="info">{AVISO_SIN_RESPUESTA_PROVEEDOR}</InlineAlert>}
        {datos ? (
          <RespuestaFuente datos={datos} etiqueta={`Respuesta de la fuente · ${titulo}`} nivel={4} />
        ) : (
          <Vacio>Sin respuesta de la fuente para esta persona.</Vacio>
        )}
      </>
    );
  }
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h3 id={id} className={TITULO_SECCION_CLS}>
        {titulo}
      </h3>
      {cuerpo}
    </section>
  );
}

/** Personas con su respuesta y, una sola vez arriba, el aviso si alguna se muestra con el resultado resumido. */
function RespuestasPersonas({ personas, fuente }: { personas: PersonaDetalle[]; fuente: Fuente }) {
  return (
    <>
      {personas.some((p) => personaResumida(p, fuente)) && <AvisoResumido />}
      {personas.map((p) => (
        <RespuestaPersona key={p.orden} persona={p} fuente={fuente} />
      ))}
    </>
  );
}

/** Objeto JSON del proveedor en la celda del vehículo. Solo SIMIT_VEHICULO; ausente o null no cuenta. */
const proveedorDeCelda = (detalle: Detalle): unknown => {
  if (detalle.fuente !== "SIMIT_VEHICULO") return undefined;
  const valor = detalle.respuesta_proveedor;
  if (valor === null || valor === undefined || typeof valor !== "object" || Array.isArray(valor)) return undefined;
  return valor;
};

/**
 * «Respuesta del proveedor» de SIMIT vehículo (HU #14184): un bloque de celda con el mismo renderizador que el conductor.
 */
function RespuestaProveedorCelda({ datos }: { datos: unknown }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h3 id={id} className={TITULO_SECCION_CLS}>
        Respuesta del proveedor
      </h3>
      <RespuestaProveedor datos={datos} etiqueta="Respuesta del proveedor" nivel={4} />
    </section>
  );
}

/**
 * «Respuesta de la fuente» (HU #14087): los datos de la consulta de la respuesta completa (`respuesta_fuente`), en español y
 * sin la auditoría del proveedor (RespuestaFuente); si no está guardada, el `resultado` resumido con un aviso; sin resultado
 * (Sin ejecutar, Novedad, Error), solo el mensaje. SIMIT vehículo (HU #14184) pinta `respuesta_proveedor` y, si falta, el
 * aviso del proveedor más el resultado resumido.
 */
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
        {personas.length > 0 && <RespuestasPersonas personas={personas} fuente={detalle.fuente} />}
      </>
    );
  } else if (porPersona && personas.length === 0) {
    cuerpo = <Vacio>{`El vehículo no tiene ${detalle.fuente.endsWith("LOCATARIO") ? "locatarios" : "propietarios"} para esta consulta.`}</Vacio>;
  } else if (porPersona) {
    cuerpo = <RespuestasPersonas personas={personas} fuente={detalle.fuente} />;
  } else if (detalle.fuente === "SIMIT_VEHICULO" && proveedorDeCelda(detalle) !== undefined) {
    cuerpo = <RespuestaProveedorCelda datos={proveedorDeCelda(detalle)} />;
  } else if (detalle.fuente === "SIMIT_VEHICULO" && detalle.resultado) {
    cuerpo = (
      <>
        <InlineAlert tone="info">{AVISO_SIN_RESPUESTA_PROVEEDOR}</InlineAlert>
        <RespuestaFuente datos={detalle.resultado} etiqueta="Respuesta de la fuente" />
      </>
    );
  } else if (detalle.respuesta_fuente) {
    cuerpo = <RespuestaFuente datos={detalle.respuesta_fuente} etiqueta="Respuesta de la fuente" />;
  } else if (detalle.resultado) {
    cuerpo = (
      <>
        <AvisoResumido />
        <RespuestaFuente datos={detalle.resultado} etiqueta="Respuesta de la fuente" />
      </>
    );
  } else {
    cuerpo = <Vacio>Esta consulta no trae campos de respuesta para mostrar.</Vacio>;
  }
  return <Seccion titulo="Respuesta de la fuente">{cuerpo}</Seccion>;
}

/**
 * Cuerpo del detalle ya cargado (HU #14087): «Auditoría de consulta» de FLIT y «Respuesta de la fuente». Solo lee las claves
 * del contrato (DTO cerrado, R-20): las claves de más que llegaran fuera de `resultado` y `respuesta_fuente` no se pintan.
 */
export function VistaDetalleCelda({ detalle }: { detalle: Detalle }) {
  return (
    <div className="flex flex-col gap-4">
      <Auditoria detalle={detalle} />
      <Respuesta detalle={detalle} />
    </div>
  );
}
