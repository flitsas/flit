"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { Cargador, DataTable, EstadoVacio, InlineAlert, ListaDefinicion, Modal, Pestanas, Placa, type ItemDefinicion } from "@flit/ui";
import { BarraSecciones, type SeccionBarra } from "@/components/detalle/BarraSecciones";
import { Estado } from "@/components/detalle/EstadoTexto";
import { contenedorScroll, useCercania } from "@/components/detalle/useCercania";
import { useSesion } from "@/components/ComparendosShell";
import { Timeline, type SeccionDetalle } from "@/components/detalle/Timeline";
import { EXTENSIONES_DETALLE, type ContextoDetalle, type ExtensionesDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { BOTON_PELIGRO_CLS, controlCls, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import {
  consultaContraventor,
  canal,
  cambioContraventor,
  estadoDp,
  estadoProceso,
  ultimoHito,
  estadoSolicitud,
  fuente,
  nombreActor,
  textoParaGestora,
  resultadoDp,
  textoSemaforo,
  tonoSemaforo,
  ventanaDescuento,
} from "@/lib/etiquetas";
import { formatoFecha, formatoFechaHora, formatoPesos } from "@/lib/format/date";
import { tienePermiso } from "@/lib/sesion";
import type { CambioCampo, DetalleComparendo as Detalle } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const ORIGEN_CONTRAVENTOR: Record<string, string> = { Api: "Automático", Manual: "Manual", MigracionV1: "Migrado de V1" };
const TIPO_PAGO: Record<string, string> = { CC: "Cambio de contraventor", PC: "Pago del contraventor", BC: "Banco / empresa" };

/** Superficie de tarjeta del kit (borde, radio 9 px y fondo elevado), la misma de `TarjetaPlegable`. */
const TARJETA_CLS = "rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)]";
const SUBTITULO_CLS = "text-xs font-semibold uppercase tracking-wide text-muted-foreground";
const TEXTO_SUAVE_CLS = "text-sm text-muted-foreground";

type Fila = [string, ReactNode];
const items = (filas: Fila[]): ItemDefinicion[] => filas.map(([termino, valor]) => ({ termino, valor }));

function Datos({ filas }: { filas: Fila[] }) {
  return <ListaDefinicion columnas={2} vacio="—" items={items(filas)} />;
}

function Archivo({ id, nombre }: { id: string; nombre: string }) {
  const toast = useToast();
  return (
    <button
      type="button"
      className="text-sm font-semibold text-flit-brand-ink hover:underline"
      onClick={() => apiDownload(`/archivos/${id}`, nombre).catch((e: unknown) => toast.show(e instanceof Error ? e.message : "No se pudo descargar.", "error"))}
    >
      {nombre}
    </button>
  );
}

type PestanaDetalle = "datos" | "fuentes" | "cambios";

/** En celular las tres pestañas caben con su rótulo corto; desde `sm` va el completo. */
function Rotulo({ corto, largo }: { corto: string; largo: string }) {
  return (
    <>
      <span className="sm:hidden">{corto}</span>
      <span className="hidden sm:inline">{largo}</span>
    </>
  );
}

/** Pestañas del final: todos los datos, las fuentes y el historial de cambios que trajo cada fuente. */
function PestanasDetalle({ d, activa, onCambiar }: { d: Detalle; activa: PestanaDetalle; onCambiar: (p: PestanaDetalle) => void }) {
  // El historial se pide solo al abrir su pestaña, como antes.
  const cambios = useCarga(activa === "cambios" ? `cambios:${d.id}` : null, (signal) => apiFetch<CambioCampo[]>(`/comparendos/${d.id}/cambios`, { signal }));

  const datos = (
    <Datos
      filas={[
        ["Placa", d.vehiculo.placa],
        ["Tipo y servicio del vehículo", [d.vehiculo.tipoVehiculo, d.vehiculo.servicioVehiculo].filter(Boolean).join(" · ")],
        ["NIT consultado", d.compania.nitConsultado],
        ["Titular o razón social", d.compania.titular],
        ["Infractor", [d.compania.tipoDocumentoInfractor, d.compania.numeroDocumentoInfractor].filter(Boolean).join(" ")],
        ["Placa monitoreada", d.compania.placaMonitoreada],
        ["Fecha de imposición", `${formatoFecha(d.infraccion.fechaImposicion)}${d.infraccion.horaImposicion ? ` · ${d.infraccion.horaImposicion.slice(0, 5)}` : ""}`],
        ["Fecha de notificación", formatoFecha(d.infraccion.fechaNotificacion)],
        ["Tipo", d.infraccion.esFotomulta === false ? "Comparendo físico" : "Fotomulta"],
        ["Infracción", [d.infraccion.codigo, d.infraccion.descripcion].filter(Boolean).join(" — ")],
        ["Dirección", d.infraccion.direccion],
        ["Estado en la fuente", d.estadoFuente],
        ["Fuente", <span key="f" className="inline-flex flex-wrap items-center gap-1"><Estado e={fuente(d.fuente)} />{d.fuentePrincipal && <span className={TEXTO_SUAVE_CLS}>{canal(d.fuentePrincipal).texto}</span>}</span>],
        ["Límite de gestión", formatoFecha(d.plazos.limiteGestion)],
        ["Fin del descuento del 50%", formatoFecha(d.plazos.limite50)],
        ["Fin del descuento del 25%", formatoFecha(d.plazos.limite25)],
        ["Caducidad", formatoFecha(d.plazos.caducidad)],
        ["Prescripción", formatoFecha(d.plazos.prescripcion)],
        ["Valor capital", formatoPesos(d.valores.valorCapital)],
        ["Intereses y otros cargos", formatoPesos(d.valores.valorAdicional)],
        [
          "Evidencia",
          d.evidencia.archivoEvidenciaId ? (
            <Archivo id={d.evidencia.archivoEvidenciaId} nombre="Documento original" />
          ) : d.evidencia.fotoEvidenciaUrl ? (
            <a href={d.evidencia.fotoEvidenciaUrl} target="_blank" rel="noreferrer" className="text-sm font-semibold text-flit-brand-ink hover:underline">
              Foto de la fuente
            </a>
          ) : null,
        ],
      ]}
    />
  );

  type FilaFuente = Detalle["fuentes"][number];
  const fuentes = (
    <DataTable<FilaFuente & { i: number }>
      ariaLabel="Fuentes del comparendo"
      tarjeta={false}
      rows={d.fuentes.map((f, i) => ({ ...f, i }))}
      getRowKey={(f) => String(f.i)}
      emptyContent={<EstadoVacio nivelTitulo={3} titulo="Sin fuentes" mensaje="Solo existe por carga OCR." />}
      minWidth={640}
      columns={[
        { key: "canal", header: "Canal", render: (f) => canal(f.canal).texto },
        { key: "municipio", header: "Municipio", render: (f) => f.municipioCodigo ?? "—" },
        {
          key: "estado",
          header: "Estado",
          render: (f) => (f.presente ? <Estado e={{ texto: "Presente", tono: "success" }} /> : <Estado e={{ texto: `Ausente desde ${formatoFecha(f.ausenteDesde)}`, tono: "neutral" }} />),
        },
        { key: "primera", header: "Primera vez", render: (f) => formatoFechaHora(f.primeraVezEn) },
        { key: "ultima", header: "Última vez visto", render: (f) => formatoFechaHora(f.ultimaVezVistaEn) },
      ]}
    />
  );

  const historial =
    cambios.estado === "error" ? (
      <InlineAlert tone="error">{cambios.error}</InlineAlert>
    ) : (
      <DataTable<CambioCampo & { i: number }>
        ariaLabel="Historial de cambios"
        tarjeta={false}
        status={cambios.estado === "ready" ? "ready" : "loading"}
        rows={(cambios.datos ?? []).map((c, i) => ({ ...c, i }))}
        getRowKey={(c) => String(c.i)}
        emptyContent={<EstadoVacio nivelTitulo={3} titulo="Sin cambios" mensaje="La fuente no ha cambiado ningún dato." />}
        minWidth={640}
        columns={[
          { key: "fecha", header: "Fecha", render: (c) => <span className="whitespace-nowrap">{formatoFechaHora(c.registradoEn)}</span> },
          { key: "fuente", header: "Fuente", render: (c) => canal(c.canal).texto },
          { key: "campo", header: "Campo", render: (c) => c.campo.replaceAll("_", " ") },
          { key: "antes", header: "Antes", render: (c) => <span className="text-muted-foreground">{c.valorAnterior ?? "—"}</span> },
          {
            key: "ahora",
            header: "Ahora",
            render: (c) => (
              <>
                {c.valorNuevo ?? "—"}
                {!c.aplicadoAMaestro && <span className="ml-1 text-xs text-muted-foreground">(no es la fuente principal)</span>}
              </>
            ),
          },
        ]}
      />
    );

  return (
    <Pestanas<PestanaDetalle>
      ariaLabel="Información del comparendo"
      active={activa}
      onChange={onCambiar}
      tabs={[
        { id: "datos", label: <Rotulo corto="Datos" largo="Datos completos" />, content: datos },
        { id: "fuentes", label: "Fuentes", content: fuentes },
        { id: "cambios", label: <Rotulo corto="Historial" largo="Historial de cambios" />, content: historial },
      ]}
    />
  );
}

/** Lo secundario y largo de una sección (historiales, intentos, documentos): los primeros y «Ver los N» en su sitio (R2). */
function ConMas<T>({ items, visibles = 3, texto, children }: { items: T[]; visibles?: number; texto: (n: number) => string; children: (item: T) => ReactNode }) {
  const [todos, setTodos] = useState(false);
  return (
    <>
      {(todos ? items : items.slice(0, visibles)).map(children)}
      {!todos && items.length > visibles && (
        <button type="button" className="self-start text-sm font-semibold text-flit-brand-ink hover:underline" onClick={() => setTodos(true)}>
          {texto(items.length)}
        </button>
      )}
    </>
  );
}

/** «Nombre (CC 123)» desde la foto JSON que guarda el historial de conductores. */
function conductorDeFoto(json: string | null): string | null {
  if (!json) return null;
  try {
    const f = JSON.parse(json) as { nombres?: string; tipoDocumento?: string; numeroDocumento?: string };
    return [f.nombres, f.numeroDocumento ? `(${f.tipoDocumento ?? ""} ${f.numeroDocumento})`.replace("( ", "(") : null].filter(Boolean).join(" ") || null;
  } catch {
    return null;
  }
}

function Contraventor({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  const k = d.contraventor;
  const consulta = consultaContraventor(d.estadoConsultaContraventor);
  // La respuesta cruda de la consulta es para soporte de FLIT: la gestora no ve cómo funciona por dentro.
  const { sesion } = useSesion();
  return (
    <div className="flex flex-col gap-4">
      {/* §7.2: el estado de la consulta automática, si la empresa la tiene (NoAplica = asignación manual). */}
      {d.estadoConsultaContraventor !== "NoAplica" && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">Consulta automática del conductor:</span>
          <Estado e={{ texto: consulta.texto, tono: consulta.tono }} />
        </div>
      )}
      {!k && d.estadoConsultaContraventor === "SinHora" && (
        <p className={TEXTO_SUAVE_CLS}>
          El comparendo no trae hora y ese día el vehículo no tuvo el mismo conductor todo el día: revisa quién conducía y asígnalo a mano.
        </p>
      )}
      {!k && d.estadoConsultaContraventor === "Agotada" && <p className={TEXTO_SUAVE_CLS}>La consulta automática se intentó 3 veces sin respuesta: asigna el conductor a mano.</p>}
      {sesion?.usuario.esSuperAdmin && (d.estadoConsultaContraventor === "Error" || d.estadoConsultaContraventor === "Agotada") && d.errorConsultaContraventor && (
        <details className="text-sm">
          <summary className="cursor-pointer text-muted-foreground">Qué respondió la consulta (solo FLIT)</summary>
          <pre className="mt-1 max-h-40 overflow-auto whitespace-pre-wrap rounded-lg bg-secondary p-2 text-xs">{d.errorConsultaContraventor}</pre>
        </details>
      )}
      {k ? (
        <Datos
          filas={[
            ["Nombre", k.nombres],
            ["Documento", k.documentoPendiente ? <Estado key="d" e={{ texto: "Pendiente: la consulta automática no lo trae", tono: "warning" }} /> : `${k.tipoDocumento} ${k.numeroDocumento}`],
            ["Correo", k.correo],
            ["Teléfono", k.telefono],
            ["Dirección", [k.direccion, k.ciudad].filter(Boolean).join(", ")],
            ["Sucursal", k.sucursal],
            ["Tipo de contrato", k.tipoContrato],
            ["Origen", <Estado key="o" e={{ texto: ORIGEN_CONTRAVENTOR[k.origen] ?? k.origen, tono: k.origen === "Api" ? "info" : "neutral" }} />],
          ]}
        />
      ) : (
        <p className={TEXTO_SUAVE_CLS}>Este comparendo todavía no tiene conductor asignado.</p>
      )}
      {extra}

      {d.documentos.length > 0 && (
        <div className="flex flex-col gap-1">
          <h3 className={SUBTITULO_CLS}>Documentos</h3>
          <ConMas items={d.documentos} texto={(n) => `Ver los ${n} documentos`}>
            {(doc) => (
            <div key={doc.id} className="flex flex-wrap items-center gap-x-2 text-sm">
              <Archivo id={doc.archivoId} nombre={doc.nombre} />
              <span className="text-muted-foreground">
                {doc.tipo} · {formatoFecha(doc.creadoEn)}
                {!doc.vigente && " · reemplazado"}
              </span>
            </div>
            )}
          </ConMas>
        </div>
      )}

      {d.historialContraventor.length > 0 && (
        <div className="flex flex-col gap-2">
          <h3 className={SUBTITULO_CLS}>Historial de conductores</h3>
          <ConMas items={d.historialContraventor} visibles={2} texto={(n) => `Ver los ${n} cambios de conductor`}>
            {(h) => {
            const anterior = conductorDeFoto(h.datosAnteriores);
            const nuevo = conductorDeFoto(h.datosNuevos);
            return (
              <div key={h.id} className="text-sm">
                <p>
                  {formatoFechaHora(h.registradoEn)} · {ORIGEN_CONTRAVENTOR[h.origen] ?? h.origen}
                  {h.actorNombre ? ` · ${nombreActor(h.actorNombre)}` : ""}
                </p>
                <p className="text-muted-foreground">
                  {anterior ? `${anterior} → ${nuevo ?? "—"}` : `Asignado: ${nuevo ?? "—"}`}
                  {h.motivo ? ` · Motivo: ${textoParaGestora(h.motivo)}` : ""}
                </p>
              </div>
            );
            }}
          </ConMas>
        </div>
      )}
    </div>
  );
}

function CambioContraventor({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 text-sm">
        Estado: <Estado e={cambioContraventor(d.estadoCambioContraventor)} />
      </div>
      <ConMas items={d.solicitudesCambio} visibles={2} texto={(n) => `Ver los ${n} intentos`}>
        {(s) => (
        <div key={s.id} className="flex flex-wrap items-center gap-2 border-t border-border pt-2 text-sm">
          <span className="font-semibold">Intento {s.numeroIntento}</span>
          <Estado e={estadoSolicitud(s.estado)} />
          <span className="text-muted-foreground">Solicitado {formatoFechaHora(s.solicitadoEn)}</span>
          {s.fechaRespuestaOt && <span className="text-muted-foreground">Respuesta del OT {formatoFecha(s.fechaRespuestaOt)}</span>}
          {s.causalRechazo && <span className="w-full text-destructive">Causal: {s.causalRechazo}</span>}
          <Archivo id={s.formatoArchivoId} nombre="Formato enviado" />
          {s.soporteRespuestaArchivoId && <Archivo id={s.soporteRespuestaArchivoId} nombre="Soporte de la respuesta" />}
        </div>
        )}
      </ConMas>
      {extra}
    </div>
  );
}

function ResolucionDp({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  const dp = d.dp;
  if (!dp)
    return (
      <div className="flex flex-col gap-2">
        <p className={TEXTO_SUAVE_CLS}>Este comparendo no tiene derecho de petición.</p>
        {extra}
      </div>
    );
  return (
    <div className="flex flex-col gap-3">
      <Datos
        filas={[
          ["Radicado", <span key="r" className="font-mono">{dp.radicado}</span>],
          ["Tipología", dp.tipologia ? `${dp.tipologia.codigo} — ${dp.tipologia.nombre}` : null],
          ["Estado", <Estado key="e" e={estadoDp(dp.estado)} />],
          ["Fecha de radicación", formatoFecha(dp.fechaRadicacion)],
          ["Enviado a", dp.correosDestino.join(", ")],
          ["Plazo de respuesta del OT (informativo)", formatoFecha(dp.fechaLimiteRespuestaOt)],
          ["Documento enviado", <Archivo key="d" id={dp.documentoArchivoId} nombre={`${dp.radicado}.pdf`} />],
        ]}
      />
      {dp.respuesta && (
        <div className={`${TARJETA_CLS} p-3`}>
          <h3 className="mb-2 text-sm font-semibold">Respuesta del organismo</h3>
          <Datos
            filas={[
              ["Sentido", <Estado key="s" e={resultadoDp(dp.respuesta.sentido)} />],
              ["Origen", dp.respuesta.origen === "AutomaticaDesaparicion" ? "Automática: el comparendo desapareció de la fuente" : "Registrada por un usuario"],
              ["Radicado del OT", dp.respuesta.respuestaRadicadoOt],
              ["Fecha", formatoFecha(dp.respuesta.respuestaFecha)],
              ["Documento", dp.respuesta.respuestaArchivoId ? <Archivo key="a" id={dp.respuesta.respuestaArchivoId} nombre="Respuesta del OT" /> : null],
            ]}
          />
        </div>
      )}
      {extra}
    </div>
  );
}

function Pago({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  const p = d.pago;
  return (
    <div className="flex flex-col gap-2">
      {p ? (
        <Datos
          filas={[
            ["Tipo", TIPO_PAGO[p.tipo] ?? p.tipo],
            ["Valor pagado", formatoPesos(p.valorPagado)],
            ["Fecha de pago", formatoFecha(p.fechaPago)],
            ["Registrado", `${formatoFechaHora(p.registradoEn)}${p.registradoPorNombre ? ` · ${p.registradoPorNombre}` : ""}`],
            ["Comprobante", p.comprobanteArchivoId ? <Archivo key="c" id={p.comprobanteArchivoId} nombre="Comprobante" /> : null],
          ]}
        />
      ) : (
        <p className={TEXTO_SUAVE_CLS}>Sin pago registrado.</p>
      )}
      {extra}
    </div>
  );
}

/** Inactivación irreversible con doble confirmación y motivo (MOD06 RN-05). */
function Inactivar({ d, onHecho }: { d: Detalle; onHecho: () => void }) {
  const toast = useToast();
  const [paso, setPaso] = useState<0 | 1 | 2>(0);
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);

  const confirmar = async () => {
    setEnviando(true);
    try {
      await apiFetch(`/comparendos/${d.id}/inactivar`, { method: "POST", body: { motivo } });
      toast.show("Comparendo inactivado.", "success");
      setPaso(0);
      onHecho();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo inactivar.", "error");
    } finally {
      setEnviando(false);
    }
  };

  return (
    <>
      <button type="button" className={controlCls(false)} onClick={() => setPaso(1)}>
        <Icono nombre="documento" className="h-3.5 w-3.5" />
        Inactivar
      </button>
      <Modal
        icono="alerta"
        open={paso > 0}
        onClose={() => setPaso(0)}
        busy={enviando}
        title={paso === 1 ? "Inactivar comparendo" : "¿Confirmas la inactivación?"}
        size="md"
        zClassName="z-[120]"
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setPaso(0)} disabled={enviando}>
              Cancelar
            </button>
            {paso === 1 ? (
              <button type="button" className={BOTON_PELIGRO_CLS} disabled={motivo.trim().length < 5} onClick={() => setPaso(2)}>
                Continuar
              </button>
            ) : (
              <button type="button" className={BOTON_PELIGRO_CLS} disabled={enviando} onClick={confirmar}>
                {enviando ? "Inactivando…" : "Inactivar definitivamente"}
              </button>
            )}
          </div>
        }
      >
        {paso === 1 ? (
          <label className="flex flex-col gap-1 text-xs font-semibold">
            Motivo
            <textarea className={TEXTAREA_CLS} rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Por qué se inactiva este comparendo" />
          </label>
        ) : (
          <InlineAlert tone="warning" title="Esta acción no se puede deshacer">
            El comparendo {d.numero} sale de la bandeja de activos y de los avisos, y no se reactivará aunque vuelva a aparecer en la fuente. Su gestión se conserva.
          </InlineAlert>
        )}
      </Modal>
    </>
  );
}

/** Cabecera: lo que se decide de un vistazo (número, infracción, estado legal, valor y descuento) y las acciones. */
function Cabecera({ d, acciones }: { d: Detalle; acciones: ReactNode }) {
  const s = d.situacion;
  const conDescuento = s.ventanaDescuento === "Descuento50" || s.ventanaDescuento === "Descuento25";
  const infraccion = [d.infraccion.codigo, d.infraccion.descripcion].filter(Boolean).join(" — ");
  return (
    <header className={`${TARJETA_CLS} flex flex-col gap-4 p-4 sm:p-5`} data-testid="detalle-cabecera">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="flex min-w-0 flex-col gap-2">
          <p className={SUBTITULO_CLS}>Comparendo</p>
          <h1 className="m-0 break-all text-xl font-bold leading-tight sm:text-2xl">{d.numero}</h1>
          {infraccion && <p className={TEXTO_SUAVE_CLS}>{infraccion}</p>}
          <div className="flex flex-wrap items-center gap-2">
            <Estado e={{ texto: textoSemaforo(s.estadoLegal, s.diasHabilesRestantes), tono: tonoSemaforo(s.estadoLegal, s.proximoAVencer) }} />
            {d.pagado && <Estado e={{ texto: "Pagado", tono: "success" }} />}
            {!d.activo && <Estado e={{ texto: d.motivoInactivacion === "InactivadoOperador" ? "Inactivo (por un usuario)" : "Inactivo (no está en la fuente)", tono: "neutral" }} />}
            {d.enCobroCoactivo && <Estado e={{ texto: "Cobro coactivo", tono: "danger" }} />}
          </div>
        </div>
        <div className="flex shrink-0 flex-col gap-1 md:items-end md:text-right">
          <p className={SUBTITULO_CLS}>Valor total</p>
          <p className="m-0 text-2xl font-bold tabular-nums sm:text-3xl">{formatoPesos(d.valores.valorTotal)}</p>
          <div className="flex flex-wrap items-center gap-2 md:justify-end">
            <Estado e={ventanaDescuento(s.ventanaDescuento)} />
            {conDescuento && s.finDescuentoVigente && (
              <span className={TEXTO_SUAVE_CLS}>
                hasta {formatoFecha(s.finDescuentoVigente)}
                {s.diasHabilesDescuento !== null && ` · ${s.diasHabilesDescuento === 1 ? "1 día hábil" : `${s.diasHabilesDescuento} días hábiles`}`}
              </span>
            )}
          </div>
        </div>
      </div>

      {/* B6-1: los dos estados pueden decir «Sin procesar»; cada uno con su rótulo. Los largos van como texto (R2). */}
      <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <dt className="text-muted-foreground">Proceso</dt>
          <dd className="m-0 min-w-0">
            <Estado e={estadoProceso(d.estadoProceso)} />
          </dd>
        </div>
        <div className="flex min-w-0 flex-col gap-0.5">
          <dt className="text-muted-foreground">Último aviso</dt>
          <dd className="m-0 min-w-0">
            <Estado e={ultimoHito(d.ultimoHito)} />
          </dd>
        </div>
      </dl>
      {!d.activo && d.detalleInactivacion && <p className={TEXTO_SUAVE_CLS}>Motivo de la inactivación: {d.detalleInactivacion}</p>}

      {acciones && (
        <div role="group" aria-label="Acciones del comparendo" className="flex flex-wrap items-center gap-2 border-t border-[color:var(--di-divider)] pt-4" data-testid="detalle-acciones">
          {acciones}
        </div>
      )}
    </header>
  );
}

/** Tarjeta de contexto: un dato corto y lo que lleva a su sección. */
function TarjetaContexto({ titulo, children, accion, onIr }: { titulo: string; children: ReactNode; accion: string; onIr: () => void }) {
  return (
    <div className={`${TARJETA_CLS} flex min-w-0 flex-col gap-2 p-4`}>
      <h2 className={`m-0 ${SUBTITULO_CLS}`}>{titulo}</h2>
      <div className="flex min-w-0 flex-1 flex-col gap-1 text-sm">{children}</div>
      <button type="button" onClick={onIr} className="self-start text-sm font-semibold text-flit-brand-ink hover:underline focus-visible:outline-2 focus-visible:outline-[var(--di-action)]">
        {accion}
      </button>
    </div>
  );
}

function TarjetasContexto({ d, soloLectura, onIr, onPestana }: { d: Detalle; soloLectura: boolean; onIr: (s: SeccionDetalle) => void; onPestana: (p: PestanaDetalle) => void }) {
  const k = d.contraventor;
  const tipoVehiculo = [d.vehiculo.tipoVehiculo, d.vehiculo.servicioVehiculo].filter(Boolean).join(" · ");
  return (
    <section
      aria-label="Resumen del comparendo"
      data-testid="detalle-tarjetas"
      className="grid grid-cols-1 gap-3 min-[480px]:grid-cols-2 min-[480px]:max-lg:[&>*:last-child]:col-span-2 lg:grid-cols-3 xl:grid-cols-5"
    >
      <TarjetaContexto titulo="Vehículo" accion="Ver los datos completos" onIr={() => onPestana("datos")}>
        {d.placa ? <Placa placa={d.placa} className="self-start" /> : <span className="text-muted-foreground">Sin placa</span>}
        {tipoVehiculo && <span className="text-muted-foreground">{tipoVehiculo}</span>}
      </TarjetaContexto>
      <TarjetaContexto titulo="Organismo de tránsito" accion="Ver las fuentes" onIr={() => onPestana("fuentes")}>
        <span className="font-semibold">{d.organismo?.nombre ?? (d.secretariaTexto ? `Sin mapear: ${d.secretariaTexto}` : "—")}</span>
        {d.organismo && (d.organismo.municipio || d.organismo.departamento) && (
          <span className="text-muted-foreground">{[d.organismo.municipio, d.organismo.departamento].filter(Boolean).join(", ")}</span>
        )}
      </TarjetaContexto>
      <TarjetaContexto titulo="Contraventor" accion={k || soloLectura ? "Ver el contraventor" : "Asignar o consultar"} onIr={() => onIr("contraventor")}>
        {k ? (
          <>
            <span className="break-words font-semibold">{k.nombres}</span>
            <span className="text-muted-foreground">{k.documentoPendiente ? "Documento pendiente" : `${k.tipoDocumento} ${k.numeroDocumento}`}</span>
          </>
        ) : (
          <span className="text-muted-foreground">Sin conductor asignado</span>
        )}
        {d.estadoCambioContraventor && d.solicitudesCambio.length > 0 && <Estado e={cambioContraventor(d.estadoCambioContraventor)} />}
      </TarjetaContexto>
      <TarjetaContexto titulo="Derecho de petición" accion={d.dp ? "Ver el derecho de petición" : "Ir al derecho de petición"} onIr={() => onIr("dp")}>
        {d.dp ? (
          <>
            <span className="break-all font-mono font-semibold">{d.dp.radicado}</span>
            <span className="flex flex-wrap gap-1">
              <Estado e={estadoDp(d.dp.estado)} />
              {d.dp.respuesta && <Estado e={resultadoDp(d.dp.respuesta.sentido)} />}
            </span>
          </>
        ) : (
          <span className="text-muted-foreground">Sin derecho de petición</span>
        )}
      </TarjetaContexto>
      <TarjetaContexto titulo="Pago" accion={d.pago ? "Ver el pago" : "Ir al pago"} onIr={() => onIr("pago")}>
        {d.pago ? (
          <>
            <span className="font-semibold tabular-nums">{formatoPesos(d.pago.valorPagado)}</span>
            <span className="text-muted-foreground">
              {formatoFecha(d.pago.fechaPago)} · {TIPO_PAGO[d.pago.tipo] ?? d.pago.tipo}
            </span>
          </>
        ) : (
          <span className="text-muted-foreground">Sin pago registrado</span>
        )}
      </TarjetaContexto>
    </section>
  );
}

/** Las secciones de la barra, en el orden de la página (constante: la barra no rehace su observador en cada render). */
const SECCIONES_BARRA: SeccionBarra[] = [
  { id: "detalle-resumen", texto: "Resumen" },
  { id: "detalle-timeline", texto: "Línea de tiempo", corto: "Tiempo" },
  { id: "detalle-contraventor", texto: "Contraventor" },
  { id: "detalle-dp", texto: "Derecho de petición", corto: "DP" },
  { id: "detalle-pago", texto: "Pago" },
  { id: "detalle-pestanas", texto: "Datos" },
];

/** Altura de la barra de secciones: cada sección se ubica debajo de ella. */
const BAJO_LA_BARRA = "scroll-mt-16";

/** Sección del detalle, siempre abierta, con su título enfocable (?seccion= y la barra llevan el foco ahí). */
function Seccion({ id, titulo, children, testId, className = "" }: { id: string; titulo: string; children: ReactNode; testId?: string; className?: string }) {
  return (
    <section id={id} aria-labelledby={`${id}-titulo`} className={`${TARJETA_CLS} ${BAJO_LA_BARRA} flex min-w-0 flex-col gap-3 p-4 ${className}`} data-testid={testId}>
      <h2 id={`${id}-titulo`} tabIndex={-1} className="m-0 text-base font-semibold focus:outline-none focus-visible:outline-2 focus-visible:outline-[var(--di-action)]">
        {titulo}
      </h2>
      {children}
    </section>
  );
}

/**
 * Las acciones de una sección (extensiones de otras Features) se montan cuando la sección se acerca a la vista o cuando
 * la gestora salta a ella: son las mismas llamadas a la API que antes hacía al abrir el acordeón, en otro momento (R2).
 * Mientras tanto se reserva su sitio para que la página no salte al montarlas.
 */
function AccionesDiferidas({ forzada, children }: { forzada: boolean; children: () => ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const cerca = useCercania(ref);
  const montada = cerca || forzada;
  return (
    <div ref={ref} className={montada ? "" : "min-h-24"} data-acciones-montadas={montada ? "si" : "no"}>
      {montada ? children() : null}
    </div>
  );
}

/**
 * Contenido del detalle del comparendo (HU #13499, §7.2), como página propia (rediseño S1, R2): cabecera con acciones →
 * barra de secciones fija → resumen → línea de tiempo → contraventor → derecho de petición → pago → datos, en una sola
 * columna y siempre abiertas. Las acciones de otras Features entran por
 * `extensiones` sin tocar este componente.
 */
export function ContenidoDetalle({
  id,
  seccionInicial = null,
  onCambio,
  extensiones = EXTENSIONES_DETALLE,
  onCargado,
}: {
  id: string;
  /** Sección que se abre al llegar (acciones por fila de la bandeja). */
  seccionInicial?: SeccionDetalle | null;
  /** Algo cambió (observación, inactivación…). */
  onCambio?: () => void;
  extensiones?: ExtensionesDetalle;
  /** El comparendo cargó (la página arma su miga con el número). */
  onCargado?: (d: Detalle) => void;
}) {
  const { sesion } = useSesion();
  const carga = useCarga(`detalle:${id}`, (signal) => apiFetch<Detalle>(`/comparendos/${id}`, { signal }));
  const d = carga.datos;
  const soloLectura = !!sesion?.usuario.esSuperAdmin;
  // B7-1: cada acción del detalle (conductor, documentos, cambio, DP, pago, avisos) deja eventos nuevos en el timeline.
  const [versionTimeline, setVersionTimeline] = useState(0);
  const recargar = () => {
    carga.recargar();
    setVersionTimeline((v) => v + 1);
    onCambio?.();
  };
  const ctx: ContextoDetalle | null = d ? { detalle: d, recargar, sesion, soloLectura } : null;
  // Secciones cuyas acciones se montan ya, sin esperar a que se acerquen: a las que se salta y la de ?seccion=.
  const [forzadas, setForzadas] = useState<SeccionDetalle[]>(() => (seccionInicial ? [seccionInicial] : []));
  const [pestana, setPestana] = useState<PestanaDetalle>("datos");
  const cargado = !!d;
  useEffect(() => {
    if (d) onCargado?.(d);
  }, [d, onCargado]);
  // Lleva a una sección y deja el foco en su título. Las acciones y el timeline cargan después y empujan el destino: se
  // re-ubica mientras el contenido se acomoda (~1 s, B11-2), solo si de verdad se corrió y sin animación. Un salto nuevo o
  // cualquier gesto de la gestora (rueda, toque, tecla, clic) cancela los re-ubicados pendientes: nunca la devuelven.
  const pendientes = useRef<number[]>([]);
  const cancelar = () => {
    pendientes.current.forEach((t) => window.clearTimeout(t));
    pendientes.current = [];
  };
  const ubicar = (idSeccion: string, suave: boolean) => {
    cancelar();
    const corrida = () => {
      const el = document.getElementById(idSeccion);
      if (!el) return null;
      const margen = parseFloat(getComputedStyle(el).scrollMarginTop) || 0;
      const raiz = contenedorScroll(el)?.getBoundingClientRect().top ?? 0;
      return { el, desvio: el.getBoundingClientRect().top - raiz - margen };
    };
    const reubicar = () => {
      const c = corrida();
      if (c && Math.abs(c.desvio) > 8) c.el.scrollIntoView({ behavior: "auto", block: "start" });
    };
    pendientes.current = [
      window.setTimeout(() => {
        document.getElementById(idSeccion)?.scrollIntoView({ behavior: suave ? "smooth" : "auto", block: "start" });
        document.getElementById(`${idSeccion}-titulo`)?.focus({ preventScroll: true });
      }, 0),
      // Con scroll suave se espera a que termine antes de corregir.
      ...(suave ? [700, 1200] : [150, 400, 800, 1200]).map((ms) => window.setTimeout(reubicar, ms)),
    ];
  };
  useEffect(() => {
    const gesto = () => cancelar();
    const eventos = ["wheel", "touchstart", "pointerdown", "keydown"] as const;
    eventos.forEach((e) => window.addEventListener(e, gesto, { capture: true, passive: true }));
    return () => {
      eventos.forEach((e) => window.removeEventListener(e, gesto, { capture: true }));
      cancelar();
    };
  }, []);
  useEffect(() => {
    if (!cargado || !seccionInicial) return;
    ubicar(`detalle-${seccionInicial}`, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo al llegar con ?seccion=
  }, [cargado, id, seccionInicial]);
  const ir = (s: SeccionDetalle) => {
    setForzadas((actual) => (actual.includes(s) ? actual : [...actual, s]));
    ubicar(`detalle-${s}`, true);
  };
  const irAPestana = (p: PestanaDetalle) => {
    setPestana(p);
    ubicar("detalle-pestanas", true);
  };
  const irA = (idSeccion: string) => {
    const gestion = idSeccion.replace("detalle-", "") as SeccionDetalle;
    if (["contraventor", "dp", "pago"].includes(gestion)) ir(gestion);
    else ubicar(idSeccion, true);
  };

  if (carga.estado === "error") return <InlineAlert tone="error" action={<button type="button" onClick={carga.recargar}>Reintentar</button>}>{carga.error}</InlineAlert>;
  if (!d || !ctx)
    return (
      <div className="flex flex-col items-center gap-2 py-16" role="status">
        <Cargador />
        <p className={TEXTO_SUAVE_CLS}>Cargando comparendo…</p>
      </div>
    );

  const puedeInactivar = !soloLectura && d.motivoInactivacion !== "InactivadoOperador" && tienePermiso(sesion, "comparendos.comparendo.inactivar");
  const acciones = !soloLectura ? (
    <>
      {extensiones.acciones?.(ctx)}
      {/* Uno inactivo por desaparición también se puede inactivar, para que no se reactive solo (S-28). */}
      {puedeInactivar && <Inactivar d={d} onHecho={recargar} />}
    </>
  ) : null;

  // R2: una sola columna de secciones siempre abiertas, con una barra fija que lleva a cada una (nada de dos columnas de
  // alto libre: abrir una dejaba un hueco al lado).
  return (
    <div className="flex flex-col gap-4">
      <Cabecera d={d} acciones={acciones} />
      <BarraSecciones secciones={SECCIONES_BARRA} onIr={irA} />

      <section id="detalle-resumen" aria-labelledby="detalle-resumen-titulo" className={BAJO_LA_BARRA}>
        <h2 id="detalle-resumen-titulo" tabIndex={-1} className="sr-only">
          Resumen
        </h2>
        <TarjetasContexto d={d} soloLectura={soloLectura} onIr={ir} onPestana={irAPestana} />
      </section>

      <Seccion id="detalle-timeline" titulo="Línea de tiempo" testId="detalle-timeline">
        <Timeline
          comparendoId={d.id}
          version={versionTimeline}
          fechaInfraccion={d.infraccion.fechaImposicion}
          puedeObservar={!soloLectura && tienePermiso(sesion, "comparendos.comparendo.observar")}
          onCambio={onCambio}
          onIr={ir}
        />
      </Seccion>

      <Seccion id="detalle-contraventor" titulo="Contraventor" testId="detalle-contraventor">
        <div className="grid grid-cols-1 gap-x-8 gap-y-6 lg:grid-cols-2">
          <Contraventor
            d={d}
            extra={!soloLectura ? <AccionesDiferidas forzada={forzadas.includes("contraventor")}>{() => extensiones.contraventor?.(ctx)}</AccionesDiferidas> : null}
          />
          <div className="flex min-w-0 flex-col gap-2 border-t border-[color:var(--di-divider)] pt-4 lg:border-l lg:border-t-0 lg:pl-8 lg:pt-0">
            <h3 className="m-0 text-sm font-semibold">Cambio de contraventor</h3>
            <CambioContraventor
              d={d}
              extra={!soloLectura ? <AccionesDiferidas forzada={forzadas.includes("contraventor")}>{() => extensiones.cambioContraventor?.(ctx)}</AccionesDiferidas> : null}
            />
          </div>
        </div>
      </Seccion>

      <Seccion id="detalle-dp" titulo="Resolución OT / derecho de petición" testId="detalle-dp">
        <ResolucionDp d={d} extra={!soloLectura ? <AccionesDiferidas forzada={forzadas.includes("dp")}>{() => extensiones.dp?.(ctx)}</AccionesDiferidas> : null} />
      </Seccion>

      <Seccion id="detalle-pago" titulo="Pago" testId="detalle-pago">
        <Pago d={d} extra={!soloLectura ? <AccionesDiferidas forzada={forzadas.includes("pago")}>{() => extensiones.pago?.(ctx)}</AccionesDiferidas> : null} />
      </Seccion>

      <section id="detalle-pestanas" aria-labelledby="detalle-pestanas-titulo" className={`${TARJETA_CLS} ${BAJO_LA_BARRA} p-4`} data-testid="detalle-pestanas">
        <h2 id="detalle-pestanas-titulo" tabIndex={-1} className="sr-only">
          Datos, fuentes e historial
        </h2>
        <PestanasDetalle d={d} activa={pestana} onCambiar={setPestana} />
      </section>
    </div>
  );
}
