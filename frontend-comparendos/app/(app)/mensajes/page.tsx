"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { DataTable, InlineAlert, Modal, StatusBadge, VehicleLoader } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { useAbrirDetalle } from "@/components/detalle/useAbrirDetalle";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { Interruptor } from "@/components/ui/Interruptor";
import { controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { CANAL_CORREO, estadoComunicacion, opciones, TIPO_COMUNICACION } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import { mensajesAParams, mensajesDeParams, opcionesEstadoMensajes } from "@/lib/estadoMensajes";
import { leerDestinatarios, resumenDestinatarios, sePuedeReintentar } from "@/lib/mensajes";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";
import type { MensajeDetalle, MensajeFila, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { crearUrlPropia } from "@/lib/urlPropia";

// Rediseño S1: filtros, página y mensaje abierto viven en la URL; al volver del detalle del comparendo queda igual.
const UrlMensajes = crearUrlPropia();

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
    <Modal icono="correo" open onClose={onCerrar} size="2xl" title={m?.asunto ?? "Mensaje"} description={m ? `${TIPO_COMUNICACION[m.tipo] ?? m.tipo} · ${formatoFechaHora(m.creadoEn)}` : undefined}>
      {detalle.estado === "loading" && <VehicleLoader label="Cargando…" />}
      {detalle.estado === "error" && <InlineAlert tone="error">{detalle.error}</InlineAlert>}
      {m && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_320px]">
          <iframe title="Cuerpo del correo" sandbox="" srcDoc={m.html ?? "<p>El cuerpo de este correo no está disponible.</p>"} className="h-[520px] w-full rounded-xl border border-border bg-card" />
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
              <p className="mt-1 text-muted-foreground">{m.avisoApertura}</p>
            </div>
            {m.plantilla && <p className="text-xs"><span className="font-semibold">Plantilla:</span> v{m.plantilla.numeroVersion}</p>}
            {m.adjuntos.length > 0 && (
              <div className="flex flex-col gap-1">
                <h3 className="text-xs font-semibold">Adjuntos</h3>
                {m.adjuntos.map((a) => (
                  <button key={a.archivoId} type="button" className={`${controlCls(false)} justify-start`} onClick={() => void apiDownload(`/archivos/${a.archivoId}`, a.nombre).catch((e: unknown) => setError(e instanceof Error ? e.message : "No se pudo descargar."))}>
                    <Icono nombre="nube-bajar" className="h-3.5 w-3.5" />
                    {a.nombre}
                  </button>
                ))}
              </div>
            )}
            <div className="flex flex-col gap-1">
              <h3 className="text-xs font-semibold">Intentos de envío</h3>
              {m.intentos.length === 0 && <p className="text-xs text-muted-foreground">Todavía no se ha intentado enviar.</p>}
              {m.intentos.map((i) => (
                <p key={i.numero} className="text-xs">
                  #{i.numero} · {formatoFechaHora(i.intentadoEn)} · {i.resultadoPlataforma ?? "—"}
                  {i.mensaje ? ` · ${i.mensaje}` : ""}
                </p>
              ))}
              {m.proximoIntentoEn && <p className="text-xs text-muted-foreground">Próximo intento: {formatoFechaHora(m.proximoIntentoEn)}</p>}
            </div>
            {puedeReintentar && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={reintentar}>
                <Icono nombre="reintento" className="h-3.5 w-3.5" />
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

function CentroConUrl() {
  const version = UrlMensajes.useVersion(useSearchParams().toString());
  return <Centro key={version} />;
}

function Centro() {
  const { sesion, cargando: cargandoSesion } = useSesion();
  // ?mensaje=<id> abre su detalle (enlace desde el timeline del comparendo, §7.2). El resto de la URL trae los filtros.
  const params = useSearchParams();
  const router = useRouter();
  const [inicial] = useState(() => mensajesDeParams(params));
  const superAdmin = !!sesion?.usuario.esSuperAdmin;
  const [page, setPage] = useState(inicial.pagina);
  const [pageSize, setPageSizeEstado] = useState(inicial.tamano);
  const pg = {
    page,
    pageSize,
    setPage,
    setPageSize: (n: number) => {
      setPageSizeEstado(n);
      setPage(1);
    },
  };
  const [empresa, setEmpresa] = useState<string | null>(inicial.empresa);
  const [internas, setInternas] = useState(inicial.internas);
  const [tipo, setTipo] = useState(inicial.tipo);
  const [estado, setEstado] = useState(inicial.estado);
  const [canal, setCanal] = useState(inicial.canal);
  const [desde, setDesde] = useState(inicial.desde);
  const [hasta, setHasta] = useState(inicial.hasta);
  const [comparendo, setComparendo] = useState(inicial.comparendo);
  const [placa, setPlaca] = useState(inicial.placa);
  // Lo que se ve en las cajas; el filtro (y la URL) se aplica cuando se deja de escribir, como la búsqueda de la bandeja.
  const [textoComparendo, setTextoComparendo] = useState(inicial.comparendo);
  const [textoPlaca, setTextoPlaca] = useState(inicial.placa);
  // Una espera por caja: pasar de Placa a Comparendo antes de 400 ms no descarta lo escrito en la primera.
  const espera = useRef<Record<"comparendo" | "placa", ReturnType<typeof setTimeout> | undefined>>({ comparendo: undefined, placa: undefined });
  const escribir = (caja: "comparendo" | "placa", mostrar: (v: string) => void, aplicar: (v: string) => void) => (v: string) => {
    mostrar(v);
    clearTimeout(espera.current[caja]);
    espera.current[caja] = setTimeout(() => {
      aplicar(v);
      setPage(1);
    }, 400);
  };
  useEffect(() => {
    const pendientes = espera.current;
    return () => {
      clearTimeout(pendientes.comparendo);
      clearTimeout(pendientes.placa);
    };
  }, []);
  const [abierto, setAbierto] = useState<string | null>(inicial.mensaje);
  const setVerComparendo = useAbrirDetalle();

  // Cada cambio queda en la URL sin sumar pasos al historial. Mientras carga la sesión no se sabe si es SuperAdmin:
  // no se escribe, para no borrar la empresa elegida.
  // La empresa solo la elige el SuperAdmin: una ?empresa= puesta a mano no viaja para nadie más.
  const empresaVista = superAdmin ? empresa : null;
  const nuevaQuery = mensajesAParams({
    empresa: empresaVista,
    internas: superAdmin && internas,
    tipo,
    estado,
    canal,
    desde,
    hasta,
    comparendo,
    placa,
    pagina: page,
    tamano: pageSize,
    mensaje: abierto,
  }).toString();
  useEffect(() => {
    if (cargandoSesion) return;
    UrlMensajes.registrar(nuevaQuery);
    if (nuevaQuery === new URLSearchParams(window.location.search).toString()) return;
    router.replace(nuevaQuery ? `/mensajes?${nuevaQuery}` : "/mensajes", { scroll: false });
  }, [cargandoSesion, nuevaQuery, router]);

  const filtros = { empresa: internas ? undefined : (empresaVista ?? undefined), internas: superAdmin && internas ? true : undefined, tipo, estado, canal, desde, hasta, comparendo: comparendo.trim(), placa: placa.trim() };
  const lista = useCarga(`mensajes:${JSON.stringify(filtros)}:${pg.page}:${pg.pageSize}`, (signal) =>
    apiFetch<Pagina<MensajeFila>>("/mensajes", {
      signal,
      query: {
        empresa: filtros.empresa,
        internas: filtros.internas,
        tipo: tipo || undefined,
        estado: estado || undefined,
        canal: canal || undefined,
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
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3 xl:grid-cols-7">
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
            {opcionesEstadoMensajes(estado).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Cuenta de salida
          <select className={INPUT_CLS} value={canal} onChange={(e) => filtrar(setCanal)(e.target.value)}>
            <option value="">Todas</option>
            {opciones(CANAL_CORREO).map((o) => (
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
          <input className={INPUT_CLS} value={textoComparendo} onChange={(e) => escribir("comparendo", setTextoComparendo, setComparendo)(e.target.value)} />
        </label>
        <label className={ETIQUETA_CLS}>
          Placa
          <input className={INPUT_CLS} value={textoPlaca} onChange={(e) => escribir("placa", setTextoPlaca, setPlaca)(e.target.value.toUpperCase())} />
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
          {
            key: "remitente",
            header: "Remitente",
            render: (m) => (
              <div className="flex flex-col">
                <span>{m.remitente ?? "—"}</span>
                {m.canalCorreo && <span className="text-xs text-muted-foreground">{CANAL_CORREO[m.canalCorreo] ?? m.canalCorreo}</span>}
              </div>
            ),
          },
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
          { key: "reintentos", header: "Intentos", align: "right", render: (m) => m.reintentos }, // B8-3: la API cuenta intentos de envío (1 = el primero).
          { key: "apertura", header: "Posiblemente abierto", render: (m) => (m.abiertoEn ? formatoFechaHora(m.abiertoEn) : "—") },
          { key: "comparendo", header: "Comparendo", render: (m) => m.comparendo ?? "—" },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: lista.datos?.total ?? 0, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
      <p className="text-xs text-muted-foreground">La apertura es un dato informativo: no constituye prueba de notificación.</p>
      {abierto && <DetalleMensaje key={abierto} id={abierto} onCerrar={() => setAbierto(null)} onCambio={lista.recargar} onComparendo={(id) => setVerComparendo(id, null, nuevaQuery ? `/mensajes?${nuevaQuery}` : "/mensajes")} />}
    </section>
  );
}

/** Centro de Mensajes (HU #13486, §7.7): todo lo que Comparendos envió, con su estado, aperturas y reintento de fallidos. */
export default function CentroMensajes() {
  return (
    <Requiere permiso={P.mensajesVer}>
      <Suspense fallback={<VehicleLoader label="Cargando…" />}>
        <CentroConUrl />
      </Suspense>
    </Requiere>
  );
}
