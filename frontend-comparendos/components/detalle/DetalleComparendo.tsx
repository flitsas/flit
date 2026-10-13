"use client";

import { useEffect, useState, type ReactNode } from "react";
import { FileWarning } from "lucide-react";
import { InlineAlert, Modal, StatusBadge } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Timeline, type SeccionDetalle } from "@/components/detalle/Timeline";
import { EXTENSIONES_DETALLE, type ContextoDetalle, type ExtensionesDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { Acordeon, Datos } from "@/components/ui/Acordeon";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PELIGRO_CLS, controlCls, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import {
  canal,
  cambioContraventor,
  estadoDp,
  estadoProceso,
  ultimoHito,
  estadoSolicitud,
  fuente,
  resultadoDp,
  textoSemaforo,
  tonoSemaforo,
  ventanaDescuento,
} from "@/lib/etiquetas";
import { formatoFecha, formatoFechaHora, formatoPesos } from "@/lib/format/date";
import { tienePermiso } from "@/lib/sesion";
import type { CambioCampo, DetalleComparendo as Detalle } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const ORIGEN_CONTRAVENTOR: Record<string, string> = { Api: "Automático (API)", Manual: "Manual", MigracionV1: "Migrado de V1" };

function Archivo({ id, nombre }: { id: string; nombre: string }) {
  const toast = useToast();
  return (
    <button
      type="button"
      className="text-xs font-semibold text-[#3B4FD6] hover:underline"
      onClick={() => apiDownload(`/archivos/${id}`, nombre).catch((e: unknown) => toast.show(e instanceof Error ? e.message : "No se pudo descargar.", "error"))}
    >
      {nombre}
    </button>
  );
}

/** Pestañas del acordeón Vehículo y compañía: datos, fuentes y el historial de cambios que trajo cada fuente. */
function VehiculoYCompania({ d }: { d: Detalle }) {
  const [pestana, setPestana] = useState<"datos" | "fuentes" | "cambios">("datos");
  const cambios = useCarga(pestana === "cambios" ? `cambios:${d.id}` : null, (signal) => apiFetch<CambioCampo[]>(`/comparendos/${d.id}/cambios`, { signal }));

  return (
    <div className="flex flex-col gap-3">
      <div className="flex gap-2" role="tablist">
        {(["datos", "fuentes", "cambios"] as const).map((p) => (
          <button key={p} type="button" role="tab" aria-selected={pestana === p} className={controlCls(pestana === p)} onClick={() => setPestana(p)}>
            {p === "datos" ? "Datos" : p === "fuentes" ? "Fuentes" : "Historial de cambios"}
          </button>
        ))}
      </div>

      {pestana === "datos" && (
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
                <a href={d.evidencia.fotoEvidenciaUrl} target="_blank" rel="noreferrer" className="text-xs font-semibold text-[#3B4FD6] hover:underline">
                  Foto de la fuente
                </a>
              ) : null,
            ],
          ]}
        />
      )}

      {pestana === "fuentes" && (
        <table className="w-full text-xs">
          <thead>
            <tr className="text-left text-[11px] opacity-60">
              <th className="py-1">Canal</th>
              <th>Municipio</th>
              <th>Estado</th>
              <th>Primera vez</th>
              <th>Última vez visto</th>
            </tr>
          </thead>
          <tbody>
            {d.fuentes.map((f, i) => (
              <tr key={i} className="border-t border-[#EEF1F6] dark:border-white/5">
                <td className="py-1.5">{canal(f.canal).texto}</td>
                <td>{f.municipioCodigo ?? "—"}</td>
                <td>{f.presente ? <StatusBadge label="Presente" tone="success" /> : <StatusBadge label={`Ausente desde ${formatoFecha(f.ausenteDesde)}`} tone="neutral" />}</td>
                <td>{formatoFechaHora(f.primeraVezEn)}</td>
                <td>{formatoFechaHora(f.ultimaVezVistaEn)}</td>
              </tr>
            ))}
            {d.fuentes.length === 0 && (
              <tr>
                <td colSpan={5} className="py-2 opacity-60">
                  Solo existe por carga OCR.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      {pestana === "cambios" && (
        <>
          {cambios.estado === "loading" && <p className="text-xs opacity-60">Cargando historial…</p>}
          {cambios.estado === "error" && <InlineAlert tone="error">{cambios.error}</InlineAlert>}
          {cambios.estado === "ready" && (cambios.datos ?? []).length === 0 && <p className="text-xs opacity-60">La fuente no ha cambiado ningún dato.</p>}
          {(cambios.datos ?? []).length > 0 && (
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-[11px] opacity-60">
                  <th className="py-1">Fecha</th>
                  <th>Fuente</th>
                  <th>Campo</th>
                  <th>Antes</th>
                  <th>Ahora</th>
                </tr>
              </thead>
              <tbody>
                {(cambios.datos ?? []).map((c, i) => (
                  <tr key={i} className="border-t border-[#EEF1F6] dark:border-white/5">
                    <td className="py-1.5 whitespace-nowrap">{formatoFechaHora(c.registradoEn)}</td>
                    <td>{canal(c.canal).texto}</td>
                    <td>{c.campo.replaceAll("_", " ")}</td>
                    <td className="opacity-70">{c.valorAnterior ?? "—"}</td>
                    <td>
                      {c.valorNuevo ?? "—"}
                      {!c.aplicadoAMaestro && <span className="ml-1 text-[10px] opacity-60">(no es la fuente principal)</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </div>
  );
}

function Contraventor({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  const k = d.contraventor;
  return (
    <div className="flex flex-col gap-4">
      {k ? (
        <Datos
          filas={[
            ["Nombre", k.nombres],
            ["Documento", `${k.tipoDocumento} ${k.numeroDocumento}`],
            ["Correo", k.correo],
            ["Teléfono", k.telefono],
            ["Dirección", [k.direccion, k.ciudad].filter(Boolean).join(", ")],
            ["Sucursal", k.sucursal],
            ["Tipo de contrato", k.tipoContrato],
            ["Origen", <StatusBadge key="o" label={ORIGEN_CONTRAVENTOR[k.origen] ?? k.origen} tone={k.origen === "Api" ? "info" : "neutral"} />],
          ]}
        />
      ) : (
        <p className="text-xs opacity-70">Este comparendo todavía no tiene conductor asignado.</p>
      )}
      {extra}

      {d.documentos.length > 0 && (
        <div className="flex flex-col gap-1">
          <h4 className="text-[11px] font-semibold uppercase opacity-60">Documentos</h4>
          {d.documentos.map((doc) => (
            <div key={doc.id} className="flex items-center gap-2 text-xs">
              <Archivo id={doc.archivoId} nombre={doc.nombre} />
              <span className="opacity-60">
                {doc.tipo} · {formatoFecha(doc.creadoEn)}
                {!doc.vigente && " · reemplazado"}
              </span>
            </div>
          ))}
        </div>
      )}

      {d.historialContraventor.length > 0 && (
        <div className="flex flex-col gap-1">
          <h4 className="text-[11px] font-semibold uppercase opacity-60">Historial de conductores</h4>
          {d.historialContraventor.map((h) => (
            <p key={h.id} className="text-xs">
              {formatoFechaHora(h.registradoEn)} · {ORIGEN_CONTRAVENTOR[h.origen] ?? h.origen}
              {h.motivo ? ` · ${h.motivo}` : ""}
            </p>
          ))}
        </div>
      )}
    </div>
  );
}

function CambioContraventor({ d, extra }: { d: Detalle; extra?: ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 text-xs">
        Estado: <Chip e={cambioContraventor(d.estadoCambioContraventor)} />
      </div>
      {d.solicitudesCambio.map((s) => (
        <div key={s.id} className="flex flex-wrap items-center gap-2 border-t border-[#EEF1F6] pt-2 text-xs dark:border-white/5">
          <span className="font-semibold">Intento {s.numeroIntento}</span>
          <Chip e={estadoSolicitud(s.estado)} />
          <span className="opacity-70">Solicitado {formatoFechaHora(s.solicitadoEn)}</span>
          {s.fechaRespuestaOt && <span className="opacity-70">Respuesta del OT {formatoFecha(s.fechaRespuestaOt)}</span>}
          {s.causalRechazo && <span className="w-full text-[#B42318]">Causal: {s.causalRechazo}</span>}
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
  if (!dp) return <div className="flex flex-col gap-2"><p className="text-xs opacity-70">Este comparendo no tiene derecho de petición.</p>{extra}</div>;
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
        <div className="rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
          <h4 className="mb-2 text-xs font-semibold">Respuesta del organismo</h4>
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
  const tipos: Record<string, string> = { CC: "Cambio de contraventor", PC: "Pago del contraventor", BC: "Banco / empresa" };
  return (
    <div className="flex flex-col gap-2">
      {p ? (
        <Datos
          filas={[
            ["Tipo", tipos[p.tipo] ?? p.tipo],
            ["Valor pagado", formatoPesos(p.valorPagado)],
            ["Fecha de pago", formatoFecha(p.fechaPago)],
            ["Registrado", `${formatoFechaHora(p.registradoEn)}${p.registradoPorNombre ? ` · ${p.registradoPorNombre}` : ""}`],
            ["Comprobante", p.comprobanteArchivoId ? <Archivo key="c" id={p.comprobanteArchivoId} nombre="Comprobante" /> : null],
          ]}
        />
      ) : (
        <p className="text-xs opacity-70">Sin pago registrado.</p>
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
        <FileWarning className="h-3.5 w-3.5" aria-hidden />
        Inactivar
      </button>
      <Modal
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

/**
 * Detalle del comparendo (HU #13499, §7.2). Las acciones de otras Features (contraventor, avisos, DP, pago, cambio de
 * contraventor) entran por `extensiones` sin tocar este componente.
 */
export function DetalleComparendo({
  id,
  onCerrar,
  onCambio,
  extensiones = EXTENSIONES_DETALLE,
  seccionInicial = null,
}: {
  id: string | null;
  /** Sección que se abre al llegar (acciones por fila de la bandeja). */
  seccionInicial?: SeccionDetalle | null;
  onCerrar: () => void;
  /** Algo cambió (observación, inactivación…): la bandeja recarga. */
  onCambio?: () => void;
  extensiones?: ExtensionesDetalle;
}) {
  const { sesion } = useSesion();
  const carga = useCarga(id ? `detalle:${id}` : null, (signal) => apiFetch<Detalle>(`/comparendos/${id}`, { signal }));
  const d = carga.datos;
  const soloLectura = !!sesion?.usuario.esSuperAdmin;
  const recargar = () => {
    carga.recargar();
    onCambio?.();
  };
  const ctx: ContextoDetalle | null = d ? { detalle: d, recargar, sesion, soloLectura } : null;
  // Secciones abiertas desde el timeline; se olvidan al cambiar de comparendo.
  const [abiertas, setAbiertas] = useState<{ id: string | null; secciones: SeccionDetalle[] }>({ id: null, secciones: [] });
  const secciones = abiertas.id === id ? abiertas.secciones : seccionInicial ? [seccionInicial] : [];
  const cargado = !!d;
  useEffect(() => {
    if (cargado && seccionInicial) document.getElementById(`detalle-${seccionInicial}`)?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [cargado, id, seccionInicial]);
  const alternar = (s: SeccionDetalle) => (abrir: boolean) =>
    setAbiertas({ id, secciones: abrir ? [...secciones.filter((x) => x !== s), s] : secciones.filter((x) => x !== s) });
  const ir = (s: SeccionDetalle) => {
    alternar(s)(true);
    window.setTimeout(() => document.getElementById(`detalle-${s}`)?.scrollIntoView({ behavior: "smooth", block: "start" }), 0);
  };

  return (
    <Modal open={id !== null} onClose={onCerrar} title={d ? `Comparendo ${d.numero}` : "Comparendo"} size="2xl">
      {carga.estado === "loading" && <p className="text-sm opacity-60">Cargando comparendo…</p>}
      {carga.estado === "error" && <InlineAlert tone="error">{carga.error}</InlineAlert>}
      {d && ctx && (
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-3 rounded-2xl bg-[#F4F7FC] p-4 dark:bg-white/5">
            <div className="flex flex-wrap items-center gap-2">
              <Chip e={estadoProceso(d.estadoProceso)} />
              <Chip e={ultimoHito(d.ultimoHito)} />
              <StatusBadge label={textoSemaforo(d.situacion.estadoLegal, d.situacion.diasHabilesRestantes)} tone={tonoSemaforo(d.situacion.estadoLegal, d.situacion.proximoAVencer)} />
              <Chip e={ventanaDescuento(d.situacion.ventanaDescuento)} />
              {d.situacion.finDescuentoVigente && <span className="text-xs opacity-70">hasta {formatoFecha(d.situacion.finDescuentoVigente)}</span>}
              {d.pagado && <StatusBadge label="Pagado" tone="success" />}
              {!d.activo && <StatusBadge label={d.motivoInactivacion === "InactivadoOperador" ? "Inactivo (por un usuario)" : "Inactivo (no está en la fuente)"} tone="neutral" />}
              {d.enCobroCoactivo && <StatusBadge label="Cobro coactivo" tone="danger" />}
            </div>
            <div className="grid grid-cols-2 gap-3 text-xs sm:grid-cols-4">
              <div>
                <div className="text-[11px] opacity-60">Organismo de tránsito</div>
                <div className="font-semibold">{d.organismo?.nombre ?? (d.secretariaTexto ? `Sin mapear: ${d.secretariaTexto}` : "—")}</div>
              </div>
              <div>
                <div className="text-[11px] opacity-60">Fuente</div>
                <div className="flex items-center gap-1">
                  <Chip e={fuente(d.fuente)} />
                  {d.fuentePrincipal && <span className="opacity-70">{canal(d.fuentePrincipal).texto}</span>}
                </div>
              </div>
              <div>
                <div className="text-[11px] opacity-60">Valor total</div>
                <div className="text-base font-semibold">{formatoPesos(d.valores.valorTotal)}</div>
              </div>
              <div>
                <div className="text-[11px] opacity-60">Placa</div>
                <div className="font-semibold">{d.placa ?? "—"}</div>
              </div>
            </div>
            {!d.activo && d.detalleInactivacion && <p className="text-xs opacity-70">Motivo de la inactivación: {d.detalleInactivacion}</p>}
            <div className="flex flex-wrap items-center gap-2">
              {!soloLectura && extensiones.acciones?.(ctx)}
              {/* Uno inactivo por desaparición también se puede inactivar, para que no se reactive solo (S-28). */}
              {!soloLectura && d.motivoInactivacion !== "InactivadoOperador" && tienePermiso(sesion, "comparendos.comparendo.inactivar") && (
                <Inactivar d={d} onHecho={recargar} />
              )}
            </div>
          </div>

          <Acordeon titulo="Vehículo y compañía" abiertoInicial>
            <VehiculoYCompania d={d} />
          </Acordeon>
          <Acordeon titulo="Contraventor" id="detalle-contraventor" abierto={secciones.includes("contraventor")} onAlternar={alternar("contraventor")}>
            <Contraventor d={d} extra={!soloLectura ? extensiones.contraventor?.(ctx) : null} />
            <div className="mt-4 border-t border-[#EEF1F6] pt-3 dark:border-white/5">
              <h4 className="mb-2 text-xs font-semibold">Cambio de contraventor</h4>
              <CambioContraventor d={d} extra={!soloLectura ? extensiones.cambioContraventor?.(ctx) : null} />
            </div>
          </Acordeon>
          <Acordeon titulo="Timeline" abiertoInicial>
            <Timeline comparendoId={d.id} puedeObservar={!soloLectura && tienePermiso(sesion, "comparendos.comparendo.observar")} onCambio={onCambio} onIr={ir} />
          </Acordeon>
          <Acordeon titulo="Resolución OT / derecho de petición" id="detalle-dp" abierto={secciones.includes("dp")} onAlternar={alternar("dp")}>
            <ResolucionDp d={d} extra={!soloLectura ? extensiones.dp?.(ctx) : null} />
          </Acordeon>
          <Acordeon titulo="Pago" id="detalle-pago" abierto={secciones.includes("pago")} onAlternar={alternar("pago")}>
            <Pago d={d} extra={!soloLectura ? extensiones.pago?.(ctx) : null} />
          </Acordeon>
        </div>
      )}
    </Modal>
  );
}
