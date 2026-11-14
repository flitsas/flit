"use client";

import { useEffect, useId, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { nombreFuente } from "@/lib/configuracion";
import { DocumentoDeclarado } from "@/components/comun/DocumentoDeclarado";
import type { EstadoCelda } from "@/lib/tablero/api";
import {
  FUENTES_PERSONA,
  FRASE_DATO_PARCIAL,
  fraseImpuesto,
  textoMotivoNa,
  textoTipoError,
  textoDuracion,
  textoInfraccion,
  textoPazYSalvo,
  textoTotalSimit,
  formatearPesos,
  FRASE_SIN_RESOLUCIONES_INFORMADAS,
  type AuditoriaConsulta,
  type ResultadoSimitDetalle,
  type DetalleCelda as Detalle,
  type ResultadoImpuestoDetalle,
  type EstadoPersona,
  type PersonaDetalle,
} from "@/lib/tablero/detalle";
import { ChipEstadoCelda, fechaDelDato } from "./CeldaSemaforo";
import { complementoProveedor, licenciasDesdeProveedor } from "@/lib/tablero/etiquetasRespuesta";
import { RespuestaFuente, RespuestaProveedor } from "./RespuestaFuente";
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
// HU #14562 · D2 (axe scrollable-region-focusable, WCAG 2.1.1): una tabla más ancha que la pantalla se desplaza en su
// contenedor; con tabIndex, rol y nombre el teclado puede enfocarlo y moverlo con las flechas. Foco con --di-action.
const FOCO_DESPLAZABLE = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";
const desplazable = (nombre: string) => ({ tabIndex: 0, role: "region", "aria-label": `${nombre}, desplazable` }) as const;

function TablaPersonas({ personas }: { personas: PersonaDetalle[] }) {
  return (
    <div {...desplazable("Personas consultadas")} className={`overflow-x-auto rounded-xl border border-border ${FOCO_DESPLAZABLE}`}>
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
                <Icono nombre="exclamacion" className="h-3.5 w-3.5" />
                Fecha del dato original: reutilizado de una consulta del mismo día
              </span>
            )}
          </dd>
        </div>
      </dl>
      {detalle.auditoria && <AuditoriaIntentos fuente={nombreFuente(detalle.fuente)} auditoria={detalle.auditoria} />}
      {detalle.personas && detalle.personas.length > 0 && <TablaPersonas personas={detalle.personas} />}
    </Seccion>
  );
}

const RESULTADO_INTENTO = { EXITOSO: "Exitoso", ERROR: "Error" } as const;
const DISPARADOR_INTENTO = { AUTOMATICO: "Automático", REPROCESO: "Reproceso" } as const;

/** HU #14563 B5: fuente, inicio, fin, duración e intentos, y la lista de intentos no manuales. */
function AuditoriaIntentos({ fuente, auditoria }: { fuente: string; auditoria: AuditoriaConsulta }) {
  return (
    <>
      <ListaDefinicion
        columnas={2}
        items={[
          { termino: "Fuente", valor: fuente },
          { termino: "Inicio", valor: fechaDelDato(auditoria.inicio) },
          { termino: "Fin", valor: fechaDelDato(auditoria.fin) },
          { termino: "Duración", valor: textoDuracion(auditoria.duracion_ms) },
          { termino: "Intentos", valor: String(auditoria.intentos) },
        ]}
      />
      <div {...desplazable("Intentos de la consulta")} className={`overflow-x-auto ${FOCO_DESPLAZABLE}`}>
        <table aria-label="Intentos de la consulta" className="w-full text-sm">
          <thead className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
            <tr>
              <th scope="col" className={CELDA_TABLA_CLS}>N.º</th>
              <th scope="col" className={CELDA_TABLA_CLS}>Inicio</th>
              <th scope="col" className={CELDA_TABLA_CLS}>Duración</th>
              <th scope="col" className={CELDA_TABLA_CLS}>Resultado</th>
              <th scope="col" className={CELDA_TABLA_CLS}>Disparador</th>
            </tr>
          </thead>
          <tbody>
            {auditoria.intentos_detalle.map((i) => (
              <tr key={i.n} className={FILA_TABLA_CLS}>
                <td className={`${CELDA_TABLA_CLS} tabular-nums`}>{i.n}</td>
                <td className={`${CELDA_TABLA_CLS} whitespace-nowrap tabular-nums`}>{fechaDelDato(i.inicio)}</td>
                <td className={`${CELDA_TABLA_CLS} tabular-nums`}>{textoDuracion(i.duracion_ms)}</td>
                <td className={CELDA_TABLA_CLS}>
                  <span>{RESULTADO_INTENTO[i.resultado]}</span>
                  {i.resultado === "ERROR" && i.tipo_error && <span className={`block ${ROTULO_CLS}`}>{textoTipoError(i.tipo_error)}</span>}
                </td>
                <td className={CELDA_TABLA_CLS}>{DISPARADOR_INTENTO[i.disparador]}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

function TablaSimple({ etiqueta, columnas, filas }: { etiqueta: string; columnas: string[]; filas: (string | null | undefined)[][] }) {
  return (
    <div {...desplazable(etiqueta)} className={`overflow-x-auto ${FOCO_DESPLAZABLE}`}>
      <table aria-label={etiqueta} className="w-full text-sm">
        <thead className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
          <tr>
            {columnas.map((c) => (
              <th key={c} scope="col" className={CELDA_TABLA_CLS}>{c}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {filas.map((f, i) => (
            <tr key={i} className={FILA_TABLA_CLS}>
              {f.map((v, j) => (
                <td key={j} className={CELDA_TABLA_CLS}>{v ?? "—"}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const pesosOGuion = (v: string | null | undefined) => (v ? formatearPesos(v) : null);

/**
 * HU #14563 B4 · AC40: resumen SIMIT desde la proyección tipada del backend (no lee la respuesta cruda). Lo que la fuente no
 * informó se dice con su frase; nunca se pinta «$ 0» ni se deduce paz y salvo de cero comparendos.
 */
function ResumenSimit({ resultado }: { resultado: ResultadoSimitDetalle }) {
  const resoluciones = resultado.resoluciones;
  const cantidad = resultado.cantidad_resoluciones ?? (resoluciones ? resoluciones.length : null);
  const infracciones = (resultado.multas ?? []).flatMap((m) => (m.infracciones ?? []).map((i) => ({ multa: m.numero ?? "—", texto: textoInfraccion(i) })));
  return (
    <Seccion titulo="Resumen SIMIT">
      <ListaDefinicion
        columnas={2}
        items={[
          { termino: "Total adeudado", valor: textoTotalSimit(resultado) },
          { termino: "Paz y salvo", valor: textoPazYSalvo(resultado.paz_y_salvo) },
          {
            termino: "Resoluciones",
            valor: cantidad === null ? FRASE_SIN_RESOLUCIONES_INFORMADAS : `${cantidad} ${cantidad === 1 ? "resolución" : "resoluciones"}`,
          },
        ]}
      />
      {resultado.acuerdos_pago && resultado.acuerdos_pago.length > 0 && (
        <TarjetaPlegable titulo="Acuerdos de pago" abiertaInicial>
          <TablaSimple
            etiqueta="Acuerdos de pago"
            columnas={["Estado", "Cuotas abonadas", "Cuotas pendientes", "Pendiente", "Fecha", "Organismo"]}
            filas={resultado.acuerdos_pago.map((a) => [a.estado, a.cuotas_abonadas, a.cuotas_pendientes, pesosOGuion(a.pendiente), a.fecha, a.organismo])}
          />
        </TarjetaPlegable>
      )}
      {resoluciones && resoluciones.length > 0 && (
        <TarjetaPlegable titulo="Resoluciones" abiertaInicial>
          <TablaSimple
            etiqueta="Resoluciones"
            columnas={["Número", "Fecha", "Estado", "Valor", "Organismo"]}
            filas={resoluciones.map((r) => [r.numero, r.fecha, r.estado, pesosOGuion(r.valor), r.organismo])}
          />
        </TarjetaPlegable>
      )}
      {infracciones.length > 0 && (
        <TarjetaPlegable titulo="Infracciones por comparendo" abiertaInicial>
          <TablaSimple etiqueta="Infracciones" columnas={["Comparendo", "Infracción"]} filas={infracciones.map((i) => [i.multa, i.texto])} />
        </TarjetaPlegable>
      )}
    </Seccion>
  );
}

const esSimitTipado = (d: Detalle): d is Detalle & { resultado: ResultadoSimitDetalle } =>
  d.fuente === "SIMIT_VEHICULO" && d.estado === "COMPLETADO" && d.resultado !== null && "cantidad_comparendos" in d.resultado;

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
 * Respuesta de una persona: datos de la consulta, en español, sin el cuerpo ni el nombre del integrador; debajo, la
 * respuesta cruda del proveedor en su tarjeta plegable (D19).
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
      {hayCrudo(persona.respuesta_proveedor) && (
        <CrudoProveedor datos={persona.respuesta_proveedor} titulo={`Respuesta del proveedor · ${titulo}`} nivel={4} />
      )}
    </section>
  );
}

/** Hay respuesta cruda del proveedor que mostrar (null o ausente: consulta anterior, caché o llenado a mano). */
const hayCrudo = (valor: unknown) => valor !== null && valor !== undefined;

/**
 * D19 = b (HU #14562 · AC39, D1 = a): la respuesta cruda del proveedor, íntegra y sin filtrar, dentro de una TarjetaPlegable
 * cerrada por defecto. La vista curada («Respuesta de la fuente») sigue en español y sin el cuerpo del integrador.
 */
function CrudoProveedor({ datos, titulo, nivel }: { datos: unknown; titulo: string; nivel: 3 | 4 }) {
  return (
    <TarjetaPlegable titulo={titulo} nivelTitulo={nivel}>
      <RespuestaProveedor datos={datos} etiqueta={titulo} nivel={nivel + 1} />
    </TarjetaPlegable>
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
 * «Respuesta de la fuente»: los datos de la consulta, en español y sin el nombre del integrador, y la respuesta cruda del
 * proveedor de la celda en su tarjeta plegable (D19).
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
  return (
    <Seccion titulo="Respuesta de la fuente">
      {cuerpo}
      {hayCrudo(detalle.respuesta_proveedor) && <CrudoProveedor datos={detalle.respuesta_proveedor} titulo="Respuesta del proveedor" nivel={3} />}
    </Seccion>
  );
}

/**
 * Cuerpo del detalle ya cargado (HU #14087): «Auditoría de consulta» de FLIT y «Respuesta de la fuente». Solo lee las claves
 * del contrato (DTO cerrado, R-20): las claves de más que llegaran fuera de `resultado` y `respuesta_fuente` no se pintan.
 */
export function VistaDetalleCelda({ detalle }: { detalle: Detalle }) {
  return (
    <div className="flex flex-col gap-4">
      <Auditoria detalle={detalle} />
      {esSimitTipado(detalle) && <ResumenSimit resultado={detalle.resultado} />}
      <Respuesta detalle={detalle} />
    </div>
  );
}
