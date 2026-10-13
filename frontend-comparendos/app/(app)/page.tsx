"use client";

import Link from "next/link";
import { useState } from "react";
import { Download, Mail, Send, SlidersHorizontal } from "lucide-react";
import { DataTable, InlineAlert, usePaginacion, type DataTableColumn } from "@flit/ui";
import { BarraFiltrosRapidos } from "@/components/bandeja/BarraFiltrosRapidos";
import { COLUMNAS_BANDEJA } from "@/components/bandeja/columnas";
import { EnvioMasivoAviso } from "@/components/bandeja/EnvioMasivoAviso";
import { EnvioMasivoDp } from "@/components/bandeja/EnvioMasivoDp";
import { PanelFiltros } from "@/components/bandeja/PanelFiltros";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { controlCls, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
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
  const { sesion, cargando: cargandoSesion, error: errorSesion } = useSesion();
  const toast = useToast();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  const [empresa, setEmpresa] = useState<string | null>(null);
  const [filtros, setFiltrosEstado] = useState<FiltrosBandeja>(FILTROS_VACIOS);
  const [verPanel, setVerPanel] = useState(false);
  const [orden, setOrden] = useState("");
  const [descendente, setDescendente] = useState(false);
  const [exportando, setExportando] = useState(false);
  const [seleccionado, setSeleccionado] = useState<string | null>(null);
  // Selección para el envío masivo de DP: se conserva al cambiar de página, se limpia al cambiar los filtros.
  const [marcados, setMarcados] = useState<Map<string, FilaBandeja>>(new Map());
  const [envioMasivo, setEnvioMasivo] = useState(false);
  const pg = usePaginacion(50);
  const { setPage } = pg;
  const puedeLote = !esSuperAdmin && tienePermiso(sesion, P.dpEnviarMasivo) && capacidades(sesion).derechosPeticion;
  const puedeAvisos = !esSuperAdmin && tienePermiso(sesion, P.avisosEnviar);
  const seleccionable = puedeLote || puedeAvisos;
  const [envioAviso, setEnvioAviso] = useState(false);

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
            columns={seleccionable ? [columnaSeleccion, ...COLUMNAS_BANDEJA] : COLUMNAS_BANDEJA}
            rows={pagina}
            getRowKey={(f) => f.id}
            onRowClick={(f) => setSeleccionado(f.id)}
            status={lista.estado}
            errorMessage={lista.error ?? undefined}
            onRetry={lista.recargar}
            emptyMessage="No hay comparendos con estos filtros."
            minWidth={1800}
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
        id={seleccionado}
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
