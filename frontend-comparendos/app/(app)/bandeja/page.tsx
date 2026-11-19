"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowDownWideNarrow, ArrowUpNarrowWide } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { ActionsMenu, Cargador, DataTable, EstadoVacio, InlineAlert, Modal, type DataTableColumn } from "@flit/ui";
import { BusquedaRapida } from "@/components/bandeja/BusquedaRapida";
import { COLUMNAS_BANDEJA } from "@/components/bandeja/columnas";
import { EnvioMasivoAviso } from "@/components/bandeja/EnvioMasivoAviso";
import { EnvioMasivoDp } from "@/components/bandeja/EnvioMasivoDp";
import { EtiquetasFiltros } from "@/components/bandeja/EtiquetasFiltros";
import { MenuFiltros } from "@/components/bandeja/MenuFiltros";
import { TarjetasEstadoLegal } from "@/components/bandeja/TarjetasEstadoLegal";
import { SelectorColumnas } from "@/components/ui/SelectorColumnas";
import { useAbrirDetalle } from "@/components/detalle/useAbrirDetalle";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { textoCambioOrden, textoOrden } from "@/lib/orden";
import { COLUMNA_FIJA, guardadas, guardar, ocultasDesde } from "@/lib/columnas";
import { MAXIMO_LOTE } from "@/lib/dp";
import { FILAS_POR_PARTE, mensajeExportacion, nombreParte, PARTES_SIN_CONFIRMAR, partesDe } from "@/lib/exportar";
import { aQuery, etiquetasAplicadas, FILTROS_VACIOS, filtrosAParams, filtrosDeParams, quitarEtiqueta, type FiltrosBandeja } from "@/lib/filtros";
import { P } from "@/lib/nav";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { Contadores, FilaBandeja, Nit, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const ORDENES = [
  { valor: "", texto: "Por urgencia" },
  { valor: "valor", texto: "Valor" },
  { valor: "imposicion", texto: "Fecha de imposición" },
  { valor: "notificacion", texto: "Fecha de notificación" },
  { valor: "actividad", texto: "Última actividad" },
  { valor: "numero", texto: "Número" },
];

/**
 * Bandeja unificada (HU #13499, §7.1): la misma para todos los roles; el SuperAdmin elige primero una gestora y solo lee.
 */
export default function Bandeja() {
  return (
    <Suspense fallback={<Cargando texto="Cargando…" />}>
      <BandejaConUrl />
    </Suspense>
  );
}

function Cargando({ texto }: { texto: string }) {
  return (
    <div className="flex flex-col items-center gap-2 py-16" role="status">
      <Cargador />
      <p className="text-sm text-muted-foreground">{texto}</p>
    </div>
  );
}

// La última query que la bandeja escribió en su propia URL. Si la URL cambia por otra vía (el dock, un enlace del
// Inicio), la bandeja se rearma con la URL nueva; si es su propio eco, no.
let ultimaEscrita = "";

/** Rediseño S1: filtros, orden, página y empresa viven en la URL; al volver del detalle la bandeja queda igual. */
function BandejaConUrl() {
  const qs = useSearchParams().toString();
  const [base, setBase] = useState({ qs, version: 0 });
  if (qs !== base.qs && qs !== ultimaEscrita) setBase({ qs, version: base.version + 1 });
  return <ContenidoBandeja key={base.version} />;
}

function ContenidoBandeja() {
  const { sesion, cargando: cargandoSesion, error: errorSesion } = useSesion();
  const toast = useToast();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  // La URL trae el estado al llegar: lo que dejó la bandeja al ir al detalle, o un enlace del dashboard o del Inicio
  // (?rapido=Radar, ?nit=…, ?empresa=…).
  const params = useSearchParams();
  const router = useRouter();
  const [inicial] = useState(() => filtrosDeParams(params));
  const [empresa, setEmpresa] = useState<string | null>(inicial.vista.empresa);
  const [filtros, setFiltrosEstado] = useState<FiltrosBandeja>(inicial.filtros);
  // HU #13870: la caja busca en número y placa; consulta cuando se deja de escribir, sin pulsar nada.
  const [textoBuscar, setTextoBuscar] = useState(inicial.filtros.buscar);
  const espera = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Nombres de los NIT y organismos elegidos, para las etiquetas de lo aplicado.
  const [nombresElegidos, setNombresElegidos] = useState<Record<string, string>>({});
  const [orden, setOrden] = useState(inicial.vista.orden);
  const [descendente, setDescendente] = useState(inicial.vista.descendente);
  // Lo que dice el botón mientras se descargan las partes («Parte 2 de 3…»); null = no se está exportando.
  const [exportando, setExportando] = useState<string | null>(null);
  const [confirmarPartes, setConfirmarPartes] = useState<number | null>(null);
  const abrir = useAbrirDetalle();
  // Selección para el envío masivo de DP: se conserva al cambiar de página, se limpia al cambiar los filtros.
  const [marcados, setMarcados] = useState<Map<string, FilaBandeja>>(new Map());
  const [envioMasivo, setEnvioMasivo] = useState(false);
  const [page, setPage] = useState(inicial.vista.pagina);
  const [pageSize, setPageSizeEstado] = useState(inicial.vista.tamano);
  const pg = {
    page,
    pageSize,
    setPage,
    setPageSize: (n: number) => {
      setPageSizeEstado(n);
      setPage(1);
    },
  };
  // Cada cambio de filtros, orden, página o empresa queda en la URL sin sumar pasos al historial.
  const nuevaQuery = filtrosAParams(filtros, { orden, descendente, pagina: page, tamano: pageSize, empresa: esSuperAdmin ? empresa : null }).toString();
  useEffect(() => {
    // Mientras carga la sesión no se sabe si es SuperAdmin: no se escribe para no borrar la empresa de la URL.
    if (cargandoSesion || nuevaQuery === new URLSearchParams(window.location.search).toString()) return;
    ultimaEscrita = nuevaQuery;
    router.replace(nuevaQuery ? `/bandeja?${nuevaQuery}` : "/bandeja", { scroll: false });
  }, [cargandoSesion, nuevaQuery, router]);
  const puedeLote = !esSuperAdmin && tienePermiso(sesion, P.dpEnviarMasivo) && capacidades(sesion).derechosPeticion;
  const puedeAvisos = !esSuperAdmin && tienePermiso(sesion, P.avisosEnviar);
  const seleccionable = puedeLote || puedeAvisos;
  const [envioAviso, setEnvioAviso] = useState(false);
  // Columnas configurables (§7.1): lo elegido en esta pantalla gana; si no, lo guardado para este usuario.
  const usuarioId = sesion?.usuario.id ?? "";
  const [preferencia, setPreferencia] = useState<{ usuario: string; ocultas: string[] } | null>(null);
  const claves = COLUMNAS_BANDEJA.map((c) => c.key);
  const ocultas = preferencia?.usuario === usuarioId ? preferencia.ocultas : guardadas(usuarioId, claves);
  const columnas = COLUMNAS_BANDEJA.filter((c) => !ocultas.includes(c.key));
  const cambiarColumnas = (visibles: string[]) => {
    const nuevas = ocultasDesde(visibles, claves);
    setPreferencia({ usuario: usuarioId, ocultas: nuevas });
    guardar(usuarioId, nuevas, claves);
  };

  const setFiltros = (f: FiltrosBandeja) => {
    setFiltrosEstado(f);
    setMarcados(new Map());
    setPage(1);
  };
  const buscar = (texto: string) => {
    setTextoBuscar(texto);
    if (espera.current) clearTimeout(espera.current);
    espera.current = setTimeout(() => {
      setFiltrosEstado((f) => ({ ...f, buscar: texto }));
      setMarcados(new Map());
      setPage(1);
    }, 400);
  };
  const limpiarTodo = () => {
    if (espera.current) clearTimeout(espera.current);
    setTextoBuscar("");
    setFiltros(FILTROS_VACIOS);
  };

  const listo = !!sesion && (!esSuperAdmin || !!empresa);
  const consulta = aQuery(filtros, { empresa: esSuperAdmin ? empresa : undefined });
  const clave = listo ? JSON.stringify({ consulta, orden, descendente, page: pg.page, size: pg.pageSize }) : null;
  const lista = useCarga(clave, (signal) =>
    apiFetch<Pagina<FilaBandeja>>("/comparendos", {
      signal,
      query: { ...consulta, orden: orden || undefined, descendente: descendente || undefined, pagina: pg.page, tamano: pg.pageSize },
    }),
  );
  // El servidor cuenta cada atajo sin el atajo elegido y cada estado legal sin el estado elegido (HU #13869).
  const contadores = useCarga(listo ? JSON.stringify(consulta) : null, (signal) => apiFetch<Contadores>("/comparendos/contadores", { signal, query: consulta }));
  const nits = useCarga(listo ? `nits:${empresa ?? ""}` : null, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa: esSuperAdmin ? empresa : undefined } }));
  const nombres = { ...Object.fromEntries((nits.datos ?? []).map((n) => [n.id, n.numero])), ...nombresElegidos };

  // HU #13871: lo filtrado sale en partes de hasta 5.000 filas, todas en el mismo clic y con solo las columnas visibles.
  const totalFiltrado = lista.datos?.total ?? 0;
  const exportar = async (partes: number) => {
    setConfirmarPartes(null);
    const visibles = columnas.map((c) => c.key);
    const hoy = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bogota" }).format(new Date());
    try {
      for (let parte = 1; parte <= partes; parte++) {
        setExportando(partes > 1 ? `Parte ${parte} de ${partes}…` : "Exportando…");
        await apiDownload(
          "/comparendos/excel",
          nombreParte(hoy, parte, partes),
          { ...consulta, orden: orden || undefined, descendente: descendente || undefined, parte, columnas: visibles.join(",") },
          true,
        );
      }
      toast.show(mensajeExportacion(totalFiltrado, partes, visibles.length), "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo exportar.", "error");
    } finally {
      setExportando(null);
    }
  };
  const pedirExportacion = () => {
    const partes = partesDe(totalFiltrado);
    if (partes > PARTES_SIN_CONFIRMAR) setConfirmarPartes(partes);
    else void exportar(partes);
  };

  if (cargandoSesion) return <Cargando texto="Abriendo Comparendos…" />;
  if (errorSesion) return <InlineAlert tone="error">{errorSesion}</InlineAlert>;

  const etiquetas = etiquetasAplicadas(filtros, nombres);
  // «Limpiar todo» aparece cuando la bandeja no está como viene de fábrica (solo «Activos»).
  const fueraDeFabrica = !!filtros.buscar || filtros.activos !== "activos" || etiquetas.some((e) => !e.porDefecto);
  const pagina = lista.datos?.items ?? [];
  const todosMarcados = pagina.length > 0 && pagina.every((f) => marcados.has(f.id));
  const alternar = (filas: FilaBandeja[], marcar: boolean) =>
    setMarcados((actual) => {
      const nuevo = new Map(actual);
      for (const f of filas) {
        if (marcar) nuevo.set(f.id, f);
        else nuevo.delete(f.id);
      }
      return nuevo;
    });
  const columnaSeleccion: DataTableColumn<FilaBandeja> = {
    key: "seleccion",
    header: (
      <input type="checkbox" aria-label="Seleccionar los comparendos de esta página" checked={todosMarcados} onChange={(e) => alternar(pagina, e.target.checked)} />
    ),
    render: (f) => (
      <input
        type="checkbox"
        aria-label={`Seleccionar ${f.numero}`}
        checked={marcados.has(f.id)}
        onClick={(e) => e.stopPropagation()}
        onChange={(e) => alternar([f], e.target.checked)}
      />
    ),
  };
  // Acciones por fila (Epic GDC-8, §7.1): cada una abre el detalle en su sección, donde está la acción con su motivo de
  // bloqueo. El SuperAdmin solo lee: ve el detalle.
  const puedeDp = !esSuperAdmin && capacidades(sesion).derechosPeticion && tienePermiso(sesion, P.dpGenerar);
  const puedePago = !esSuperAdmin && tienePermiso(sesion, P.pagosRegistrar);
  const puedeContraventor = !esSuperAdmin && tienePermiso(sesion, P.contraventorAsignar);
  const columnaAcciones: DataTableColumn<FilaBandeja> = {
    key: "acciones",
    header: <span className="sr-only">Acciones</span>,
    render: (f) => (
      <div onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()}>
        <ActionsMenu
          ariaLabel={`Acciones del comparendo ${f.numero}`}
          items={[
            { key: "ver", label: "Ver detalle", onSelect: () => abrir(f.id) },
            // B11-3: con DP se abre para verlo (y registrar la respuesta del OT); pagado y sin DP ya no admite uno.
            ...(puedeDp
              ? [
                  f.dpRadicado
                    ? { key: "dp", label: "Ver derecho de petición", onSelect: () => abrir(f.id, "dp") }
                    : { key: "dp", label: "Derecho de petición", onSelect: () => abrir(f.id, "dp"), disabled: f.pagado, disabledReason: "Ya tiene un pago registrado." },
                ]
              : []),
            ...(puedePago
              ? [{ key: "pago", label: "Registrar pago", onSelect: () => abrir(f.id, "pago"), disabled: f.pagado, disabledReason: "Ya tiene un pago registrado." }]
              : []),
            ...(puedeContraventor ? [{ key: "contraventor", label: "Contraventor", onSelect: () => abrir(f.id, "contraventor") }] : []),
          ]}
        />
      </div>
    ),
  };
  return (
    <section className="flex flex-col gap-4">
      <Encabezado
        titulo="Bandeja de comparendos"
        descripcion={esSuperAdmin ? "Vista de administración FLIT: elige una empresa para ver su bandeja. Solo lectura." : sesion?.empresa?.nombre}
      />

      {esSuperAdmin && (
        <SelectorEmpresa
          valor={empresa}
          onCambio={(id) => {
            setEmpresa(id);
            setPage(1);
          }}
        />
      )}

      {listo && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative min-w-0 flex-[1_1_16rem] sm:max-w-md">
              <Icono nombre="buscar" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-flit-brand" />
              <input
                type="search"
                value={textoBuscar}
                onChange={(e) => buscar(e.target.value)}
                placeholder="Buscar número o placa"
                aria-label="Buscar comparendos por número o placa"
                className={`${INPUT_CLS} pl-9`}
              />
            </div>
            <MenuFiltros
              aplicados={filtros}
              onAplicar={setFiltros}
              empresa={esSuperAdmin ? empresa : null}
              nombres={nombres}
              onNombres={(n) => setNombresElegidos((prev) => ({ ...prev, ...n }))}
            />
            <SelectorColumnas
              columnas={COLUMNAS_BANDEJA.filter((c) => c.key !== COLUMNA_FIJA).map((c) => ({ clave: c.key, texto: typeof c.header === "string" ? c.header : c.key }))}
              visibles={columnas.map((c) => c.key).filter((k) => k !== COLUMNA_FIJA)}
              onCambio={cambiarColumnas}
            />
            <select
              className={`${controlCls(orden !== "")} pr-2`}
              value={orden}
              onChange={(e) => {
                setOrden(e.target.value);
                setPage(1);
              }}
              aria-label="Ordenar la bandeja"
            >
              {ORDENES.map((o) => (
                <option key={o.valor} value={o.valor}>
                  {o.texto}
                </option>
              ))}
            </select>
            {orden && (
              <button
                type="button"
                className={controlCls(false)}
                onClick={() => setDescendente((d) => !d)}
                title={textoCambioOrden(orden, descendente)}
                aria-label={`${textoOrden(orden, descendente)}. ${textoCambioOrden(orden, descendente)}`}
              >
                {descendente ? <ArrowDownWideNarrow className="h-3.5 w-3.5" aria-hidden /> : <ArrowUpNarrowWide className="h-3.5 w-3.5" aria-hidden />}
                {textoOrden(orden, descendente)}
              </button>
            )}
            <div className="ml-auto flex items-center gap-2">
              {tienePermiso(sesion, "comparendos.bandeja.exportar") && (
                <button type="button" className={controlCls(false)} onClick={pedirExportacion} disabled={exportando !== null || lista.estado !== "ready" || totalFiltrado === 0}>
                  <Icono nombre="nube-bajar" className="h-3.5 w-3.5" />
                  {exportando ?? "Exportar"}
                </button>
              )}
              <button
                type="button"
                className={controlCls(false)}
                onClick={() => {
                  lista.recargar();
                  contadores.recargar();
                }}
              >
                <Icono nombre="reintento" className="h-3.5 w-3.5" />
                Actualizar
              </button>
            </div>
          </div>

          <TarjetasEstadoLegal
            conteos={contadores.datos?.estadosLegales ?? null}
            elegido={filtros.estadoLegal.length === 1 ? filtros.estadoLegal[0] : ""}
            onElegir={(e) => setFiltros({ ...filtros, estadoLegal: e ? [e] : [] })}
          />

          <BusquedaRapida
            contadores={contadores.datos}
            rapido={filtros.rapido}
            onRapido={(r) => setFiltros({ ...filtros, rapido: r })}
            sinGestion={filtros.sinGestion}
            onSinGestion={(v) => setFiltros({ ...filtros, sinGestion: v })}
            juridico={filtros.juridico}
            onJuridico={(j) => setFiltros({ ...filtros, juridico: j })}
          />

          <EtiquetasFiltros
            etiquetas={etiquetas}
            onQuitar={(clave) => setFiltros(quitarEtiqueta(filtros, clave))}
            onLimpiarTodo={fueraDeFabrica ? limpiarTodo : undefined}
          />

          {seleccionable && marcados.size > 0 && (
            <div className="flex flex-wrap items-center gap-3 text-xs">
              <span className="font-semibold">
                {marcados.size} seleccionado{marcados.size === 1 ? "" : "s"}
                {marcados.size > MAXIMO_LOTE && ` (máximo ${MAXIMO_LOTE} por envío)`}
              </span>
              {puedeLote && (
                <button type="button" className={controlCls(true)} onClick={() => setEnvioMasivo(true)}>
                  <Icono nombre="radicacion" className="h-3.5 w-3.5" />
                  Enviar derecho de petición
                </button>
              )}
              {puedeAvisos && (
                <button type="button" className={controlCls(true)} onClick={() => setEnvioAviso(true)}>
                  <Icono nombre="correo" className="h-3.5 w-3.5" />
                  Enviar aviso
                </button>
              )}
              <button type="button" className="font-semibold text-flit-brand-ink hover:underline" onClick={() => setMarcados(new Map())}>
                Quitar selección
              </button>
            </div>
          )}

          <DataTable
            ariaLabel="Comparendos"
            columns={[...(seleccionable ? [columnaSeleccion] : []), ...columnas, columnaAcciones]}
            rows={pagina}
            getRowKey={(f) => f.id}
            onRowClick={(f) => abrir(f.id)}
            status={lista.estado}
            errorMessage={lista.error ?? undefined}
            onRetry={lista.recargar}
            emptyMessage="No hay comparendos con estos filtros."
            emptyContent={
              <EstadoVacio
                nivelTitulo={3}
                titulo="No hay comparendos con estos filtros"
                mensaje={fueraDeFabrica ? "Quita algún filtro o límpialos todos para ver más." : undefined}
                acciones={fueraDeFabrica ? [<button key="limpiar" type="button" className={controlCls(false)} onClick={limpiarTodo}>Limpiar todo</button>] : undefined}
              />
            }
            minWidth={Math.max(900, columnas.length * 110)}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: lista.datos?.total ?? 0,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </>
      )}

      {confirmarPartes !== null && (
        <Modal icono="nube-bajar"
          open
          onClose={() => setConfirmarPartes(null)}
          title="Exportar en varios archivos"
          footer={
            <div className="flex justify-end gap-2">
              <button type="button" className={controlCls(false)} onClick={() => setConfirmarPartes(null)}>
                Cancelar
              </button>
              <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => void exportar(confirmarPartes)}>
                Descargar {confirmarPartes} archivos
              </button>
            </div>
          }
        >
          <p className="text-sm">
            Son {totalFiltrado.toLocaleString("es-CO")} comparendos: se descargarán {confirmarPartes} archivos de hasta {FILAS_POR_PARTE.toLocaleString("es-CO")} filas,
            uno detrás de otro. Si el navegador pregunta si permites descargar varios archivos, acepta.
          </p>
        </Modal>
      )}
      {envioMasivo && <EnvioMasivoDp filas={[...marcados.values()]} onCerrar={() => setEnvioMasivo(false)} />}
      {envioAviso && (
        <EnvioMasivoAviso
          filas={[...marcados.values()]}
          onCerrar={() => setEnvioAviso(false)}
          onHecho={() => {
            lista.recargar();
            contadores.recargar();
          }}
        />
      )}
    </section>
  );
}
