"use client";

import Link from "next/link";
import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { Download, Mail, Send, SlidersHorizontal } from "lucide-react";
import { ActionsMenu, DataTable, InlineAlert, usePaginacion, type DataTableColumn } from "@flit/ui";
import { BarraFiltrosRapidos } from "@/components/bandeja/BarraFiltrosRapidos";
import { COLUMNAS_BANDEJA } from "@/components/bandeja/columnas";
import { EnvioMasivoAviso } from "@/components/bandeja/EnvioMasivoAviso";
import { EnvioMasivoDp } from "@/components/bandeja/EnvioMasivoDp";
import { PanelFiltros } from "@/components/bandeja/PanelFiltros";
import { MultiSeleccion } from "@/components/ui/MultiSeleccion";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import type { SeccionDetalle } from "@/components/detalle/Timeline";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { controlCls, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { COLUMNA_FIJA, guardadas, guardar, ocultasDesde } from "@/lib/columnas";
import { MAXIMO_LOTE } from "@/lib/dp";
import { aQuery, contarFiltros, FILTROS_VACIOS, type FiltrosBandeja } from "@/lib/filtros";
import { P } from "@/lib/nav";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { Contadores, FilaBandeja, Pagina } from "@/lib/tipos";
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
    <Suspense fallback={<p className="text-sm opacity-60">Cargando…</p>}>
      <ContenidoBandeja />
    </Suspense>
  );
}

function ContenidoBandeja() {
  const { sesion, cargando: cargandoSesion, error: errorSesion } = useSesion();
  const toast = useToast();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  // Desde el KPI «Radar de vencimientos» del dashboard llega ?rapido=Radar (y el NIT y la empresa elegidos allá).
  const params = useSearchParams();
  const [empresa, setEmpresa] = useState<string | null>(() => params.get("empresa"));
  const [filtros, setFiltrosEstado] = useState<FiltrosBandeja>(() => {
    const nit = params.get("nit");
    return { ...FILTROS_VACIOS, rapido: params.get("rapido") === "Radar" ? "Radar" : null, nits: nit ? [nit] : [] };
  });
  const [verPanel, setVerPanel] = useState(false);
  const [orden, setOrden] = useState("");
  const [descendente, setDescendente] = useState(false);
  const [exportando, setExportando] = useState(false);
  const [seleccionado, setSeleccionado] = useState<string | null>(null);
  const [seccion, setSeccion] = useState<SeccionDetalle | null>(null);
  const abrir = (id: string, s: SeccionDetalle | null = null) => {
    setSeccion(s);
    setSeleccionado(id);
  };
  // Selección para el envío masivo de DP: se conserva al cambiar de página, se limpia al cambiar los filtros.
  const [marcados, setMarcados] = useState<Map<string, FilaBandeja>>(new Map());
  const [envioMasivo, setEnvioMasivo] = useState(false);
  const pg = usePaginacion(50);
  const { setPage } = pg;
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
    guardar(usuarioId, nuevas);
  };

  const setFiltros = (f: FiltrosBandeja) => {
    setFiltrosEstado(f);
    setMarcados(new Map());
    setPage(1);
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
  const contadores = useCarga(listo ? JSON.stringify({ ...consulta, rapido: undefined }) : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...consulta, rapido: undefined } }),
  );

  const exportar = async () => {
    setExportando(true);
    try {
      const r = await apiDownload("/comparendos/excel", "comparendos.xlsx", { ...consulta, orden: orden || undefined, descendente: descendente || undefined });
      if (r.aceptado) toast.show(r.mensaje ?? "La exportación es grande: te avisamos por correo cuando esté lista.", "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo exportar.", "error");
    } finally {
      setExportando(false);
    }
  };

  if (cargandoSesion) return <p className="text-sm opacity-60">Abriendo Comparendos…</p>;
  if (errorSesion) return <InlineAlert tone="error">{errorSesion}</InlineAlert>;

  const nFiltros = contarFiltros(filtros);
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
            ...(puedeDp ? [{ key: "dp", label: "Derecho de petición", onSelect: () => abrir(f.id, "dp") }] : []),
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
        acciones={
          <>
            {tienePermiso(sesion, "comparendos.bandeja.exportar") && listo && (
              <button type="button" className={controlCls(false)} onClick={exportar} disabled={exportando}>
                <Download className="h-3.5 w-3.5" aria-hidden />
                {exportando ? "Exportando…" : "Exportar a Excel"}
              </button>
            )}
            <Link href="/exportaciones" className="text-xs font-semibold text-[#3B4FD6] hover:underline">
              Mis exportaciones
            </Link>
          </>
        }
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
          <BarraFiltrosRapidos
            contadores={contadores.datos}
            rapido={filtros.rapido}
            onRapido={(r) => setFiltros({ ...filtros, rapido: r })}
            sinGestion={filtros.sinGestion}
            onSinGestion={(v) => setFiltros({ ...filtros, sinGestion: v })}
            juridico={filtros.juridico}
            onJuridico={(j) => setFiltros({ ...filtros, juridico: j })}
          />

          <div className="flex flex-wrap items-center gap-2">
            <button type="button" className={controlCls(nFiltros > 0)} onClick={() => setVerPanel((v) => !v)} aria-expanded={verPanel}>
              <SlidersHorizontal className="h-3.5 w-3.5" aria-hidden />
              {nFiltros > 0 ? `Filtros (${nFiltros})` : "Filtros"}
            </button>
            {nFiltros > 0 && (
              <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => setFiltros({ ...FILTROS_VACIOS, rapido: filtros.rapido, juridico: filtros.juridico, sinGestion: filtros.sinGestion })}>
                Limpiar filtros
              </button>
            )}
            <div className="w-44">
              <MultiSeleccion
                etiqueta="Columnas"
                opciones={COLUMNAS_BANDEJA.filter((c) => c.key !== COLUMNA_FIJA).map((c) => ({ valor: c.key, texto: typeof c.header === "string" ? c.header : c.key }))}
                valores={columnas.map((c) => c.key).filter((k) => k !== COLUMNA_FIJA)}
                onCambio={cambiarColumnas}
              />
            </div>
            <label className="ml-auto flex items-center gap-2 text-xs font-semibold">
              Ordenar
              <select className={`${INPUT_CLS} w-48`} value={orden} onChange={(e) => { setOrden(e.target.value); setPage(1); }}>
                {ORDENES.map((o) => (
                  <option key={o.valor} value={o.valor}>
                    {o.texto}
                  </option>
                ))}
              </select>
            </label>
            {orden && (
              <button type="button" className={controlCls(false)} onClick={() => setDescendente((d) => !d)}>
                {descendente ? "Mayor a menor" : "Menor a mayor"}
              </button>
            )}
          </div>

          {seleccionable && marcados.size > 0 && (
            <div className="flex flex-wrap items-center gap-3 text-xs">
              <span className="font-semibold">
                {marcados.size} seleccionado{marcados.size === 1 ? "" : "s"}
                {marcados.size > MAXIMO_LOTE && ` (máximo ${MAXIMO_LOTE} por envío)`}
              </span>
              {puedeLote && (
                <button type="button" className={controlCls(true)} onClick={() => setEnvioMasivo(true)}>
                  <Send className="h-3.5 w-3.5" aria-hidden />
                  Enviar derecho de petición
                </button>
              )}
              {puedeAvisos && (
                <button type="button" className={controlCls(true)} onClick={() => setEnvioAviso(true)}>
                  <Mail className="h-3.5 w-3.5" aria-hidden />
                  Enviar aviso
                </button>
              )}
              <button type="button" className="font-semibold text-[#3B4FD6] hover:underline" onClick={() => setMarcados(new Map())}>
                Quitar selección
              </button>
            </div>
          )}

          {verPanel && <PanelFiltros filtros={filtros} onCambio={setFiltros} empresa={esSuperAdmin ? empresa : null} />}

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

      <DetalleComparendo
        key={`${seleccionado}:${seccion ?? ""}`}
        id={seleccionado}
        seccionInicial={seccion}
        onCerrar={() => setSeleccionado(null)}
        onCambio={() => {
          lista.recargar();
          contadores.recargar();
        }}
      />
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
