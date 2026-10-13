"use client";

import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { Download, RotateCcw } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge, usePaginacion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { Interruptor } from "@/components/ui/Interruptor";
import { controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { ESTADO_COMUNICACION, estadoComunicacion, opciones, TIPO_COMUNICACION } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import { leerDestinatarios, resumenDestinatarios, sePuedeReintentar } from "@/lib/mensajes";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";
import type { MensajeDetalle, MensajeFila, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

function Lista({ titulo, valores }: { titulo: string; valores: string[] }) {
  if (valores.length === 0) return null;
  return (
    <p className="text-xs">
      <span className="font-semibold">{titulo}:</span> {valores.join(", ")}
    </p>
  );
}

function DetalleMensaje({ id, onCerrar, onCambio, onComparendo }: { id: string; onCerrar: () => void; onCambio: () => void; onComparendo: (id: string) => void }) {
  const { sesion } = useSesion();
  const toast = useToast();
  const detalle = useCarga(`mensaje:${id}`, (signal) => apiFetch<MensajeDetalle>(`/mensajes/${id}`, { signal }));
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const m = detalle.datos;
  const dest = leerDestinatarios(m?.destinatarios);
  const puedeReintentar = !!m && sePuedeReintentar(m.estado, !!sesion?.usuario.esSuperAdmin) && tienePermiso(sesion, P.avisosEnviar);

  const reintentar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/mensajes/${id}/reintentar`, { method: "POST" });
      toast.show("El correo volvió a la cola de envío.", "success");
      detalle.recargar();
      onCambio();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo reintentar.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal open onClose={onCerrar} size="2xl" title={m?.asunto ?? "Mensaje"} description={m ? `${TIPO_COMUNICACION[m.tipo] ?? m.tipo} · ${formatoFechaHora(m.creadoEn)}` : undefined}>
      {detalle.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
      {detalle.estado === "error" && <InlineAlert tone="error">{detalle.error}</InlineAlert>}
      {m && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_320px]">
          <iframe title="Cuerpo del correo" sandbox="" srcDoc={m.html ?? "<p>El cuerpo de este correo no está disponible.</p>"} className="h-[520px] w-full rounded-xl border border-[#DFE5ED] bg-white" />
          <aside className="flex flex-col gap-3">
            <div className="flex flex-wrap gap-2">
              <Chip e={estadoComunicacion(m.estado)} title={m.motivoFallo ?? undefined} />
              {m.desviado && <StatusBadge label="Desviado" tone="warning" title="En este ambiente los correos se desvían a una dirección de pruebas" />}
            </div>
            {m.remitenteNombre && <p className="text-xs"><span className="font-semibold">Remitente:</span> {m.remitenteNombre}</p>}
            <Lista titulo="Para" valores={dest.para} />
            <Lista titulo="CC" valores={dest.cc} />
            <Lista titulo="CCO" valores={dest.bcc} />
            {m.comparendoId && (
              <button type="button" className={controlCls(false)} onClick={() => onComparendo(m.comparendoId!)}>
                Ver el comparendo
              </button>
            )}
            {m.motivoFallo && <InlineAlert tone="error" compact title="Motivo del fallo">{m.motivoFallo}</InlineAlert>}
            <div className="text-xs">
              <p>
                <span className="font-semibold">Apertura:</span> {m.abiertoEn ? `posiblemente abierto el ${formatoFechaHora(m.abiertoEn)} (${m.aperturas} ${m.aperturas === 1 ? "vez" : "veces"})` : "sin registro"}
              </p>
              <p className="mt-1 opacity-70">{m.avisoApertura}</p>
            </div>
            {m.plantilla && <p className="text-xs"><span className="font-semibold">Plantilla:</span> v{m.plantilla.numeroVersion}</p>}
            {m.adjuntos.length > 0 && (
              <div className="flex flex-col gap-1">
                <h3 className="text-xs font-semibold">Adjuntos</h3>
                {m.adjuntos.map((a) => (
                  <button key={a.archivoId} type="button" className={`${controlCls(false)} justify-start`} onClick={() => void apiDownload(`/archivos/${a.archivoId}`, a.nombre).catch((e: unknown) => setError(e instanceof Error ? e.message : "No se pudo descargar."))}>
                    <Download className="h-3.5 w-3.5" aria-hidden />
                    {a.nombre}
                  </button>
                ))}
              </div>
            )}
            <div className="flex flex-col gap-1">
              <h3 className="text-xs font-semibold">Intentos de envío</h3>
              {m.intentos.length === 0 && <p className="text-xs opacity-60">Todavía no se ha intentado enviar.</p>}
              {m.intentos.map((i) => (
                <p key={i.numero} className="text-xs">
                  #{i.numero} · {formatoFechaHora(i.intentadoEn)} · {i.resultadoPlataforma ?? "—"}
                  {i.mensaje ? ` · ${i.mensaje}` : ""}
                </p>
              ))}
              {m.proximoIntentoEn && <p className="text-xs opacity-70">Próximo intento: {formatoFechaHora(m.proximoIntentoEn)}</p>}
            </div>
            {puedeReintentar && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={reintentar}>
                <RotateCcw className="h-3.5 w-3.5" aria-hidden />
                Reintentar envío
              </button>
            )}
            {m.estado === "Rebotado" && <InlineAlert tone="warning" compact>Rebotó: la dirección no existe. Corrige el correo del destinatario en vez de reintentar.</InlineAlert>}
            {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
          </aside>
        </div>
      )}
    </Modal>
  );
}

function Centro() {
  const { sesion } = useSesion();
  // ?mensaje=<id> abre su detalle (enlace desde el timeline del comparendo, §7.2).
  const params = useSearchParams();
  const superAdmin = !!sesion?.usuario.esSuperAdmin;
  const pg = usePaginacion(25);
  const [empresa, setEmpresa] = useState<string | null>(null);
  const [internas, setInternas] = useState(false);
  const [tipo, setTipo] = useState("");
  const [estado, setEstado] = useState("");
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const [comparendo, setComparendo] = useState("");
  const [placa, setPlaca] = useState("");
  const [abierto, setAbierto] = useState<string | null>(() => params.get("mensaje"));
  const [verComparendo, setVerComparendo] = useState<string | null>(null);

  const filtros = { empresa: internas ? undefined : (empresa ?? undefined), internas: superAdmin && internas ? true : undefined, tipo, estado, desde, hasta, comparendo: comparendo.trim(), placa: placa.trim() };
  const lista = useCarga(`mensajes:${JSON.stringify(filtros)}:${pg.page}:${pg.pageSize}`, (signal) =>
    apiFetch<Pagina<MensajeFila>>("/mensajes", {
      signal,
      query: {
        empresa: filtros.empresa,
        internas: filtros.internas,
        tipo: tipo || undefined,
        estado: estado || undefined,
        desde: desde || undefined,
        hasta: hasta || undefined,
        comparendo: filtros.comparendo || undefined,
        placa: filtros.placa || undefined,
        pagina: pg.page,
        tamano: pg.pageSize,
      },
    }),
  );
  const filtrar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    pg.setPage(1);
  };

  return (
    <section className="flex flex-col gap-4">
      <Encabezado titulo="Centro de mensajes" descripcion="Los correos que envió Comparendos: a quién, cuándo, si llegaron y si posiblemente se abrieron." />
      {superAdmin && (
        <div className="flex flex-wrap items-end gap-4">
          {!internas && <SelectorEmpresa valor={empresa} onCambio={filtrar(setEmpresa)} />}
          <Interruptor etiqueta="Solo alertas internas de FLIT" activo={internas} onCambio={filtrar(setInternas)} />
        </div>
      )}
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <label className={ETIQUETA_CLS}>
          Tipo
          <select className={INPUT_CLS} value={tipo} onChange={(e) => filtrar(setTipo)(e.target.value)}>
            <option value="">Todos</option>
            {opciones(TIPO_COMUNICACION).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Estado
          <select className={INPUT_CLS} value={estado} onChange={(e) => filtrar(setEstado)(e.target.value)}>
            <option value="">Todos</option>
            {opciones(ESTADO_COMUNICACION).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Desde
          <input type="date" className={INPUT_CLS} value={desde} onChange={(e) => filtrar(setDesde)(e.target.value)} />
        </label>
        <label className={ETIQUETA_CLS}>
          Hasta
          <input type="date" className={INPUT_CLS} value={hasta} onChange={(e) => filtrar(setHasta)(e.target.value)} />
        </label>
        <label className={ETIQUETA_CLS}>
          Comparendo
          <input className={INPUT_CLS} value={comparendo} onChange={(e) => filtrar(setComparendo)(e.target.value)} />
        </label>
        <label className={ETIQUETA_CLS}>
          Placa
          <input className={INPUT_CLS} value={placa} onChange={(e) => filtrar(setPlaca)(e.target.value.toUpperCase())} />
        </label>
      </div>
      <DataTable
        ariaLabel="Mensajes enviados"
        rows={lista.datos?.items ?? []}
        getRowKey={(m) => m.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        onRowClick={(m) => setAbierto(m.id)}
        emptyMessage="No hay mensajes con esos filtros."
        columns={[
          { key: "fecha", header: "Fecha", render: (m) => formatoFechaHora(m.enviadoEn ?? m.creadoEn) },
          { key: "tipo", header: "Tipo", render: (m) => TIPO_COMUNICACION[m.tipo] ?? m.tipo },
          { key: "dest", header: "Destinatarios", render: (m) => resumenDestinatarios(leerDestinatarios(m.destinatarios)) },
          { key: "asunto", header: "Asunto", render: (m) => <span className="line-clamp-2">{m.asunto}</span> },
          {
            key: "estado",
            header: "Estado",
            render: (m) => (
              <span className="flex flex-wrap gap-1">
                <Chip e={estadoComunicacion(m.estado)} title={m.motivoFallo ?? undefined} />
                {m.desviado && <StatusBadge label="Desviado" tone="warning" />}
              </span>
            ),
          },
          { key: "reintentos", header: "Reintentos", align: "right", render: (m) => m.reintentos },
          { key: "apertura", header: "Posiblemente abierto", render: (m) => (m.abiertoEn ? formatoFechaHora(m.abiertoEn) : "—") },
          { key: "comparendo", header: "Comparendo", render: (m) => m.comparendo ?? "—" },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: lista.datos?.total ?? 0, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
      <p className="text-[11px] opacity-60">La apertura es un dato informativo: no constituye prueba de notificación.</p>
      {abierto && <DetalleMensaje key={abierto} id={abierto} onCerrar={() => setAbierto(null)} onCambio={lista.recargar} onComparendo={setVerComparendo} />}
      <DetalleComparendo id={verComparendo} onCerrar={() => setVerComparendo(null)} onCambio={lista.recargar} />
    </section>
  );
}

/** Centro de Mensajes (HU #13486, §7.7): todo lo que Comparendos envió, con su estado, aperturas y reintento de fallidos. */
export default function CentroMensajes() {
  return (
    <Requiere permiso={P.mensajesVer}>
      <Suspense fallback={<p className="text-sm opacity-60">Cargando…</p>}>
        <Centro />
      </Suspense>
    </Requiere>
  );
}
