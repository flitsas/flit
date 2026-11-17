"use client";

import { useEffect, useId, type ReactNode } from "react";
import { Info } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { DocumentoDeclarado } from "@/components/comun/DocumentoDeclarado";
import type { EstadoCelda } from "@/lib/tablero/api";
import {
  FUENTES_PERSONA,
  FRASE_DATO_PARCIAL,
  fraseImpuesto,
  textoMotivoNa,
  textoTipoError,
  type DetalleCelda as Detalle,
  type ResultadoImpuestoDetalle,
  type EstadoPersona,
  type PersonaDetalle,
} from "@/lib/tablero/detalle";
import { ChipEstadoCelda, fechaDelDato } from "./CeldaSemaforo";
import { complementoProveedor, licenciasDesdeProveedor } from "@/lib/tablero/etiquetasRespuesta";
import { RespuestaFuente } from "./RespuestaFuente";
import { nombreRol } from "@/lib/rotulos";
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

const TITULO_SECCION_CLS = "text-sm font-semibold text-flit-primary";
const ROTULO_CLS = "text-xs text-muted-foreground";

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
  const completo = Boolean(persona.documento?.trim());
  const numero = completo ? persona.documento!.trim() : persona.documento_enmascarado;
  if (persona.declarado_por_cliente) return <DocumentoDeclarado tipo={persona.tipo_documento} documento={numero} completo={completo} />;
  return (
    <span className="font-mono text-xs font-medium text-flit-primary">{`${persona.tipo_documento} ${numero}`}</span>
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
    <div className="overflow-x-auto rounded-xl border border-border">
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
              <td className={CELDA_TABLA_CLS}>{nombreRol(p.rol)}</td>
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
            <span className="text-sm font-medium tabular-nums text-flit-primary">{fechaOGuion(detalle.dato_obtenido_en)}</span>
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

/** Lo que se pinta de una persona: su respuesta completa o, si falta, su resultado resumido, más las licencias del conductor. */
const sinLicenciasAnidadas = (valor: unknown): unknown => {
  if (Array.isArray(valor)) return valor.map(sinLicenciasAnidadas);
  if (valor !== null && typeof valor === "object") {
    const copia: Record<string, unknown> = {};
    for (const [clave, hijo] of Object.entries(valor as Record<string, unknown>)) {
      if (clave === "licencias" || clave === "licenses") continue;
      copia[clave] = sinLicenciasAnidadas(hijo);
    }
    return copia;
  }
  return valor;
};

const datosDePersona = (p: PersonaDetalle): object | null => {
  const base = p.respuesta_fuente ?? p.resultado;
  const licencias = licenciasDesdeProveedor(p.respuesta_proveedor);
  const extra = complementoProveedor(p.respuesta_proveedor);
  const hayExtra = Object.keys(extra).length > 0;
  if (!base && licencias.length === 0 && !hayExtra) return null;
  if (base === null || typeof base !== "object" || Array.isArray(base)) {
    return licencias.length > 0 || hayExtra ? { ...extra, ...(licencias.length > 0 ? { licencias } : {}) } : null;
  }
  const objeto = (licencias.length > 0 ? sinLicenciasAnidadas(base) : { ...(base as Record<string, unknown>) }) as Record<string, unknown>;
  if (licencias.length > 0) objeto.licencias = licencias;
  return { ...objeto, ...extra };
};
const personaResumida = (p: PersonaDetalle) => !p.respuesta_fuente && p.resultado !== null;

/**
 * Respuesta de una persona: datos de la consulta, en español, sin el cuerpo ni el nombre del integrador.
 * Las licencias de conducción se toman del detalle guardado del conductor.
 */
function RespuestaPersona({ persona }: { persona: PersonaDetalle }) {
  const id = useId();
  const titulo = `${nombreRol(persona.rol)} ${persona.orden}`;
  const datos = datosDePersona(persona);
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h3 id={id} className={TITULO_SECCION_CLS}>
        {titulo}
      </h3>
      {datos ? (
        <RespuestaFuente datos={datos} etiqueta={`Respuesta de la fuente · ${titulo}`} nivel={4} />
      ) : (
        <Vacio>Sin respuesta de la fuente para esta persona.</Vacio>
      )}
    </section>
  );
}

/** Personas con su respuesta y, una sola vez arriba, el aviso si alguna se muestra con el resultado resumido. */
function RespuestasPersonas({ personas }: { personas: PersonaDetalle[] }) {
  return (
    <>
      {personas.some((p) => personaResumida(p)) && <AvisoResumido />}
      {personas.map((p) => (
        <RespuestaPersona key={p.orden} persona={p} />
      ))}
    </>
  );
}

function esImpuesto(resultado: Detalle["resultado"]): resultado is ResultadoImpuestoDetalle {
  return resultado !== null && "vigencias" in resultado;
}

/** Deuda sin monto, o sin deuda: la frase sustituye al cero y no pinta fechas que no vinieron. */
function Impuesto({ resultado }: { resultado: ResultadoImpuestoDetalle }) {
  const frase = fraseImpuesto(resultado);
  const { monto_total: _monto, tiene_deuda: _deuda, ...resto } = resultado;
  return (
    <>
      {frase && <p>{frase}</p>}
      <AvisoResumido />
      <RespuestaFuente datos={resto} etiqueta="Respuesta de la fuente" />
    </>
  );
}

/**
 * «Respuesta de la fuente»: los datos de la consulta, en español y sin el nombre del integrador.
 * Si no está guardada la respuesta completa, el resultado resumido con un aviso.
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
        {detalle.dato_parcial && <p>{FRASE_DATO_PARCIAL}</p>}
        {personas.length > 0 && <RespuestasPersonas personas={personas} />}
      </>
    );
  } else if (detalle.fuente === "IMPUESTO_DEPARTAMENTAL" && esImpuesto(detalle.resultado) && fraseImpuesto(detalle.resultado)) {
    cuerpo = <Impuesto resultado={detalle.resultado} />;
  } else if (porPersona && personas.length === 0) {
    cuerpo = <Vacio>{`El vehículo no tiene ${detalle.fuente.endsWith("LOCATARIO") ? "locatarios" : "propietarios"} para esta consulta.`}</Vacio>;
  } else if (porPersona) {
    cuerpo = <RespuestasPersonas personas={personas} />;
  } else if (detalle.respuesta_fuente || detalle.resultado) {
    const datos = {
      ...(detalle.respuesta_fuente ?? detalle.resultado),
      ...complementoProveedor(detalle.respuesta_proveedor),
    };
    cuerpo = detalle.respuesta_fuente ? (
      <RespuestaFuente datos={datos} etiqueta="Respuesta de la fuente" />
    ) : (
      <>
        <AvisoResumido />
        <RespuestaFuente datos={datos} etiqueta="Respuesta de la fuente" />
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
