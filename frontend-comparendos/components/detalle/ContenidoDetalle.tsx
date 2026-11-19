"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { Cargador, DataTable, EstadoVacio, InlineAlert, ListaDefinicion, Modal, Pestanas, Placa, StatusBadge, TarjetaPlegable, type ItemDefinicion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Timeline, type SeccionDetalle } from "@/components/detalle/Timeline";
import { EXTENSIONES_DETALLE, type ContextoDetalle, type ExtensionesDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
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
        ["Fuente", <span key="f" className="inline-flex flex-wrap items-center gap-1"><Chip e={fuente(d.fuente)} />{d.fuentePrincipal && <span className={TEXTO_SUAVE_CLS}>{canal(d.fuentePrincipal).texto}</span>}</span>],
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
          render: (f) => (f.presente ? <StatusBadge label="Presente" tone="success" /> : <StatusBadge label={`Ausente desde ${formatoFecha(f.ausenteDesde)}`} tone="neutral" />),
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
          <StatusBadge label={consulta.texto} tone={consulta.tono} />
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
            ["Documento", k.documentoPendiente ? <StatusBadge key="d" label="Pendiente: la consulta automática no lo trae" tone="warning" /> : `${k.tipoDocumento} ${k.numeroDocumento}`],
            ["Correo", k.correo],
            ["Teléfono", k.telefono],
            ["Dirección", [k.direccion, k.ciudad].filter(Boolean).join(", ")],
            ["Sucursal", k.sucursal],
            ["Tipo de contrato", k.tipoContrato],
            ["Origen", <StatusBadge key="o" label={ORIGEN_CONTRAVENTOR[k.origen] ?? k.origen} tone={k.origen === "Api" ? "info" : "neutral"} />],
          ]}
        />
      ) : (
        <p className={TEXTO_SUAVE_CLS}>Este comparendo todavía no tiene conductor asignado.</p>
      )}
      {extra}

      {d.documentos.length > 0 && (
        <div className="flex flex-col gap-1">
          <h4 className={SUBTITULO_CLS}>Documentos</h4>
          {d.documentos.map((doc) => (
            <div key={doc.id} className="flex flex-wrap items-center gap-x-2 text-sm">
              <Archivo id={doc.archivoId} nombre={doc.nombre} />
              <span className="text-muted-foreground">
                {doc.tipo} · {formatoFecha(doc.creadoEn)}
                {!doc.vigente && " · reemplazado"}
              </span>
            </div>
          ))}
        </div>
      )}

      {d.historialContraventor.length > 0 && (
        <div className="flex flex-col gap-2">
          <h4 className={SUBTITULO_CLS}>Historial de conductores</h4>
          {d.historialContraventor.map((h) => {
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
          })}
        </div>
      )}
    </div>
  );
}

function CambioContraventor({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 text-sm">
        Estado: <Chip e={cambioContraventor(d.estadoCambioContraventor)} />
      </div>
      {d.solicitudesCambio.map((s) => (
        <div key={s.id} className="flex flex-wrap items-center gap-2 border-t border-border pt-2 text-sm">
          <span className="font-semibold">Intento {s.numeroIntento}</span>
          <Chip e={estadoSolicitud(s.estado)} />
          <span className="text-muted-foreground">Solicitado {formatoFechaHora(s.solicitadoEn)}</span>
          {s.fechaRespuestaOt && <span className="text-muted-foreground">Respuesta del OT {formatoFecha(s.fechaRespuestaOt)}</span>}
          {s.causalRechazo && <span className="w-full text-destructive">Causal: {s.causalRechazo}</span>}
          <Archivo id={s.formatoArchivoId} nombre="Formato enviado" />
          {s.soporteRespuestaArchivoId && <Archivo id={s.soporteRespuestaArchivoId} nombre="Soporte de la respuesta" />}
        </div>
      ))}
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
          ["Estado", <Chip key="e" e={estadoDp(dp.estado)} />],
          ["Fecha de radicación", formatoFecha(dp.fechaRadicacion)],
          ["Enviado a", dp.correosDestino.join(", ")],
          ["Plazo de respuesta del OT (informativo)", formatoFecha(dp.fechaLimiteRespuestaOt)],
          ["Documento enviado", <Archivo key="d" id={dp.documentoArchivoId} nombre={`${dp.radicado}.pdf`} />],
        ]}
      />
      {dp.respuesta && (
        <div className={`${TARJETA_CLS} p-3`}>
          <h4 className="mb-2 text-sm font-semibold">Respuesta del organismo</h4>
          <Datos
            filas={[
              ["Sentido", <Chip key="s" e={resultadoDp(dp.respuesta.sentido)} />],
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
            <StatusBadge label={textoSemaforo(s.estadoLegal, s.diasHabilesRestantes)} tone={tonoSemaforo(s.estadoLegal, s.proximoAVencer)} />
            {d.pagado && <StatusBadge label="Pagado" tone="success" />}
            {!d.activo && <StatusBadge label={d.motivoInactivacion === "InactivadoOperador" ? "Inactivo (por un usuario)" : "Inactivo (no está en la fuente)"} tone="neutral" />}
            {d.enCobroCoactivo && <StatusBadge label="Cobro coactivo" tone="danger" />}
          </div>
        </div>
        <div className="flex shrink-0 flex-col gap-1 md:items-end md:text-right">
          <p className={SUBTITULO_CLS}>Valor total</p>
          <p className="m-0 text-2xl font-bold tabular-nums sm:text-3xl">{formatoPesos(d.valores.valorTotal)}</p>
          <div className="flex flex-wrap items-center gap-2 md:justify-end">
            <Chip e={ventanaDescuento(s.ventanaDescuento)} />
            {conDescuento && s.finDescuentoVigente && (
              <span className={TEXTO_SUAVE_CLS}>
                hasta {formatoFecha(s.finDescuentoVigente)}
                {s.diasHabilesDescuento !== null && ` · ${s.diasHabilesDescuento === 1 ? "1 día hábil" : `${s.diasHabilesDescuento} días hábiles`}`}
              </span>
            )}
          </div>
        </div>
      </div>

      {/* B6-1: los dos chips pueden decir «Sin procesar»; cada uno con su rótulo. En celular el rótulo va encima y la
          etiqueta larga («Notificado a secretaría — derecho de petición») parte en dos líneas en vez de salirse. */}
      <div className="flex flex-wrap items-start gap-x-6 gap-y-3 text-sm [&_.kit-etiqueta]:max-w-full [&_.kit-etiqueta]:whitespace-normal">
        <span className="flex min-w-0 max-w-full flex-col items-start gap-1 sm:flex-row sm:items-center sm:gap-1.5">
          <span className="text-muted-foreground">Proceso</span>
          <Chip e={estadoProceso(d.estadoProceso)} />
        </span>
        <span className="flex min-w-0 max-w-full flex-col items-start gap-1 sm:flex-row sm:items-center sm:gap-1.5">
          <span className="text-muted-foreground">Último aviso</span>
          <Chip e={ultimoHito(d.ultimoHito)} />
        </span>
      </div>
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

function TarjetasContexto({ d, onIr, onPestana }: { d: Detalle; onIr: (s: SeccionDetalle) => void; onPestana: (p: PestanaDetalle) => void }) {
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
      <TarjetaContexto titulo="Contraventor" accion={k ? "Ver el contraventor" : "Asignar o consultar"} onIr={() => onIr("contraventor")}>
        {k ? (
          <>
            <span className="break-words font-semibold">{k.nombres}</span>
            <span className="text-muted-foreground">{k.documentoPendiente ? "Documento pendiente" : `${k.tipoDocumento} ${k.numeroDocumento}`}</span>
          </>
        ) : (
          <span className="text-muted-foreground">Sin conductor asignado</span>
        )}
        {d.estadoCambioContraventor && d.solicitudesCambio.length > 0 && <Chip e={cambioContraventor(d.estadoCambioContraventor)} />}
      </TarjetaContexto>
      <TarjetaContexto titulo="Derecho de petición" accion={d.dp ? "Ver el derecho de petición" : "Ir al derecho de petición"} onIr={() => onIr("dp")}>
        {d.dp ? (
          <>
            <span className="break-all font-mono font-semibold">{d.dp.radicado}</span>
            <span className="flex flex-wrap gap-1">
              <Chip e={estadoDp(d.dp.estado)} />
              {d.dp.respuesta && <Chip e={resultadoDp(d.dp.respuesta.sentido)} />}
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

/** Una sección de gestión plegable; su contenido se monta solo abierta (las acciones cargan al abrirse, como antes). */
function Seccion({ id, titulo, abierta, onAlternar, children }: { id: string; titulo: string; abierta: boolean; onAlternar: (a: boolean) => void; children: ReactNode }) {
  return (
    <div id={id} className="scroll-mt-4">
      <TarjetaPlegable titulo={titulo} abierta={abierta} onAlternar={onAlternar} nivelTitulo={3}>
        {abierta ? children : null}
      </TarjetaPlegable>
    </div>
  );
}

/**
 * Contenido del detalle del comparendo (HU #13499, §7.2), como página propia (rediseño S1): cabecera → acciones →
 * tarjetas de contexto → línea de tiempo con la gestión al lado → pestañas. Las acciones de otras Features entran por
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
  const [secciones, setSecciones] = useState<SeccionDetalle[]>(() => (seccionInicial ? [seccionInicial] : []));
  const [pestana, setPestana] = useState<PestanaDetalle>("datos");
  const cargado = !!d;
  useEffect(() => {
    if (d) onCargado?.(d);
  }, [d, onCargado]);
  useEffect(() => {
    if (!cargado || !seccionInicial) return;
    // B11-2: el timeline y las secciones cargan después y empujan la sección hacia abajo; se vuelve a ubicar mientras
    // el contenido se acomoda (hasta ~1 s), sin animación para que no «persiga» el destino.
    const ubicar = () => document.getElementById(`detalle-${seccionInicial}`)?.scrollIntoView({ behavior: "auto", block: "start" });
    const tiempos = [0, 150, 400, 800, 1200].map((ms) => window.setTimeout(ubicar, ms));
    return () => tiempos.forEach((t) => window.clearTimeout(t));
  }, [cargado, id, seccionInicial]);
  const alternar = (s: SeccionDetalle) => (abrir: boolean) => setSecciones((actual) => (abrir ? [...actual.filter((x) => x !== s), s] : actual.filter((x) => x !== s)));
  const ir = (s: SeccionDetalle) => {
    alternar(s)(true);
    window.setTimeout(() => document.getElementById(`detalle-${s}`)?.scrollIntoView({ behavior: "smooth", block: "start" }), 0);
  };
  const irAPestana = (p: PestanaDetalle) => {
    setPestana(p);
    window.setTimeout(() => document.getElementById("detalle-pestanas")?.scrollIntoView({ behavior: "smooth", block: "start" }), 0);
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

  return (
    <div className="flex flex-col gap-4">
      <Cabecera d={d} acciones={acciones} />
      <TarjetasContexto d={d} onIr={ir} onPestana={irAPestana} />

      <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <section aria-labelledby="detalle-titulo-timeline" className={`${TARJETA_CLS} flex min-w-0 flex-col gap-3 p-4`} data-testid="detalle-timeline">
          <h2 id="detalle-titulo-timeline" className="m-0 text-base font-semibold">
            Línea de tiempo
          </h2>
          <Timeline
            comparendoId={d.id}
            version={versionTimeline}
            fechaInfraccion={d.infraccion.fechaImposicion}
            puedeObservar={!soloLectura && tienePermiso(sesion, "comparendos.comparendo.observar")}
            onCambio={onCambio}
            onIr={ir}
          />
        </section>

        <section aria-label="Gestión del comparendo" className="flex min-w-0 flex-col gap-3" data-testid="detalle-gestion">
          <Seccion id="detalle-contraventor" titulo="Contraventor" abierta={secciones.includes("contraventor")} onAlternar={alternar("contraventor")}>
            <Contraventor d={d} extra={!soloLectura ? extensiones.contraventor?.(ctx) : null} />
            <div className="mt-4 border-t border-border pt-3">
              <h4 className="mb-2 text-sm font-semibold">Cambio de contraventor</h4>
              <CambioContraventor d={d} extra={!soloLectura ? extensiones.cambioContraventor?.(ctx) : null} />
            </div>
          </Seccion>
          <Seccion id="detalle-dp" titulo="Resolución OT / derecho de petición" abierta={secciones.includes("dp")} onAlternar={alternar("dp")}>
            <ResolucionDp d={d} extra={!soloLectura ? extensiones.dp?.(ctx) : null} />
          </Seccion>
          <Seccion id="detalle-pago" titulo="Pago" abierta={secciones.includes("pago")} onAlternar={alternar("pago")}>
            <Pago d={d} extra={!soloLectura ? extensiones.pago?.(ctx) : null} />
          </Seccion>
        </section>
      </div>

      <section id="detalle-pestanas" aria-label="Datos, fuentes e historial" className={`${TARJETA_CLS} scroll-mt-4 p-4`} data-testid="detalle-pestanas">
        <PestanasDetalle d={d} activa={pestana} onCambiar={setPestana} />
      </section>
    </div>
  );
}
