"use client";

import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { Download } from "lucide-react";
import { DataTable, usePaginacion } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { CANAL, canal, opciones, RESULTADO_CICLO, resultadoCiclo } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import type { LogMigracion, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

function Log() {
  const toast = useToast();
  const params = useSearchParams();
  const [porEmpresa, setPorEmpresa] = useState(false);
  const [empresa, setEmpresa] = useState<string | null>(null);
  const [clave, setClave] = useState("");
  const [canalFiltro, setCanal] = useState("");
  const [resultado, setResultado] = useState("");
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const ciclo = params.get("ciclo");
  const pg = usePaginacion(50);
  const query = { empresa: porEmpresa ? empresa : undefined, clave, canal: canalFiltro, resultado, desde, hasta, ciclo };
  const lista = useCarga(JSON.stringify({ query, p: pg.page, t: pg.pageSize }), (signal) =>
    apiFetch<Pagina<LogMigracion>>("/admin/log-migracion", { signal, query: { ...query, pagina: pg.page, tamano: pg.pageSize } }),
  );
  const cambiar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    pg.setPage(1);
  };

  return (
    <section className="flex flex-col gap-4">
      <Encabezado
        titulo="Log de migración"
        descripcion={ciclo ? "Mostrando un solo ciclo (desde el enlace de la alerta)." : "Qué trajo cada corrida, por empresa y por NIT o placa."}
        acciones={
          <button
            type="button"
            className={controlCls(false)}
            onClick={() => apiDownload("/admin/log-migracion/excel", "log-migracion.xlsx", query).catch((e: unknown) => toast.show(e instanceof Error ? e.message : "No se pudo exportar.", "error"))}
          >
            <Download className="h-3.5 w-3.5" aria-hidden />
            Exportar a Excel
          </button>
        }
      />
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <label className={ETIQUETA_CLS}>
          NIT o placa
          <input className={INPUT_CLS} value={clave} onChange={(e) => cambiar(setClave)(e.target.value)} />
        </label>
        <label className={ETIQUETA_CLS}>
          Canal
          <select className={INPUT_CLS} value={canalFiltro} onChange={(e) => cambiar(setCanal)(e.target.value)}>
            <option value="">Todos</option>
            {opciones(CANAL).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Resultado
          <select className={INPUT_CLS} value={resultado} onChange={(e) => cambiar(setResultado)(e.target.value)}>
            <option value="">Todos</option>
            {opciones(RESULTADO_CICLO).filter((o) => o.valor !== "EnCurso").map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
        <fieldset className={ETIQUETA_CLS}>
          <legend className="mb-1">Fechas</legend>
          <div className="flex gap-2">
            <input type="date" className={INPUT_CLS} value={desde} onChange={(e) => cambiar(setDesde)(e.target.value)} aria-label="Desde" />
            <input type="date" className={INPUT_CLS} value={hasta} onChange={(e) => cambiar(setHasta)(e.target.value)} aria-label="Hasta" />
          </div>
        </fieldset>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-xs font-semibold">
          <input type="checkbox" checked={porEmpresa} onChange={(e) => cambiar(setPorEmpresa)(e.target.checked)} />
          Filtrar por empresa
        </label>
        {porEmpresa && <SelectorEmpresa valor={empresa} onCambio={cambiar(setEmpresa)} />}
      </div>
      <DataTable
        ariaLabel="Log de migración"
        rows={lista.datos?.items ?? []}
        getRowKey={(l) => l.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="No hay registros con estos filtros."
        minWidth={1400}
        columns={[
          { key: "fecha", header: "Fecha", render: (l) => <span className="whitespace-nowrap">{formatoFechaHora(l.inicioEn)}</span> },
          { key: "empresa", header: "Empresa", render: (l) => l.empresa ?? l.empresaId },
          { key: "canal", header: "Canal", render: (l) => <Chip e={canal(l.canal)} /> },
          { key: "clave", header: "Clave", render: (l) => `${l.clave ?? "—"}${l.municipio ? ` · ${l.municipio}` : ""}` },
          { key: "obt", header: "Obtenidos", align: "right", render: (l) => l.obtenidas },
          { key: "nue", header: "Nuevos", align: "right", render: (l) => l.nuevas },
          { key: "act", header: "Actualizados", align: "right", render: (l) => l.actualizadas },
          { key: "sin", header: "Sin cambios", align: "right", render: (l) => l.sinCambios },
          { key: "ina", header: "Inactivados", align: "right", render: (l) => l.inactivadas },
          { key: "rea", header: "Reactivados", align: "right", render: (l) => l.reactivadas },
          { key: "dp", header: "DP favorables", align: "right", render: (l) => l.dpFavorableAutomatico },
          { key: "res", header: "Resultado", render: (l) => <Chip e={resultadoCiclo(l.resultado)} title={l.mensajeError ?? undefined} /> },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: lista.datos?.total ?? 0, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
    </section>
  );
}

/** Log de migración (HU #13463, §7.18). Solo SuperAdmin (RN-042). */
export default function LogMigracionPage() {
  return (
    <Requiere superAdmin>
      <Suspense fallback={<p className="text-sm opacity-60">Cargando…</p>}>
        <Log />
      </Suspense>
    </Requiere>
  );
}
