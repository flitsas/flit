"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { DataTable, Modal, usePaginacion } from "@flit/ui";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import { Encabezado } from "@/components/Pantalla";
import { FiltrosPeriodo, periodoInicial, type Periodo } from "@/components/reportes/FiltrosPeriodo";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { useToast } from "@/components/Toast";
import { controlCls } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { accionesProductividad, METRICAS, totalesProductividad, type ComparendoMetrica, type FilaProductividad, type Metrica } from "@/lib/reportes";
import { useCarga } from "@/lib/useCarga";

interface Seleccion {
  usuarioId: string;
  usuario: string;
  metrica: Metrica;
}

/** Los comparendos detrás de una cifra (§7.9): clic en un número de la tabla. */
function DetalleCifra({ sel, periodo, onCerrar, onVer }: { sel: Seleccion; periodo: Periodo; onCerrar: () => void; onVer: (id: string) => void }) {
  const lista = useCarga(JSON.stringify({ sel, periodo }), (signal) =>
    apiFetch<ComparendoMetrica[]>("/reportes/productividad/detalle", {
      signal,
      query: { usuarioId: sel.usuarioId, metrica: sel.metrica, empresa: periodo.empresa, desde: periodo.desde, hasta: periodo.hasta },
    }),
  );
  const pg = usePaginacion(10);
  const filas = lista.datos ?? [];
  const titulo = METRICAS.find((m) => m.clave === sel.metrica)?.titulo ?? sel.metrica;
  return (
    <Modal open onClose={onCerrar} title={`${titulo} · ${sel.usuario}`} description={`Del ${formatoFecha(periodo.desde)} al ${formatoFecha(periodo.hasta)}.`} size="xl">
      <DataTable
        ariaLabel="Comparendos de la cifra"
        rows={pg.paginar(filas)}
        getRowKey={(c) => c.id}
        onRowClick={(c) => onVer(c.id)}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="No hay comparendos para esta cifra."
        columns={[
          { key: "numero", header: "Comparendo", render: (c) => <span className="font-semibold">{c.numero}</span> },
          { key: "placa", header: "Placa", render: (c) => c.placa ?? "—" },
          { key: "imposicion", header: "Imposición", render: (c) => formatoFecha(c.fechaImposicion) },
          { key: "valor", header: "Valor", align: "right", render: (c) => formatoPesos(c.valorTotal) },
          { key: "acciones", header: "Lo que hizo", render: (c) => accionesProductividad(c.acciones) },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
    </Modal>
  );
}

function Contenido() {
  const toast = useToast();
  const [periodo, setPeriodo] = useState<Periodo>(periodoInicial);
  const [sel, setSel] = useState<Seleccion | null>(null);
  const [ver, setVer] = useState<string | null>(null);
  const pg = usePaginacion(25);
  const datos = useCarga(JSON.stringify(periodo), (signal) =>
    apiFetch<{ filas: FilaProductividad[] }>("/reportes/productividad", { signal, query: { empresa: periodo.empresa, desde: periodo.desde, hasta: periodo.hasta } }),
  );
  const filas = datos.datos?.filas ?? [];
  const totales = totalesProductividad(filas);

  const exportar = () =>
    apiDownload("/reportes/productividad/excel", "productividad.xlsx", { empresa: periodo.empresa, desde: periodo.desde, hasta: periodo.hasta }).catch((e: unknown) =>
      toast.show(e instanceof Error ? e.message : "No se pudo exportar.", "error"),
    );

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Productividad"
        descripcion="Acciones hechas por cada usuario en el periodo. Lo automático (consultas, envíos programados y favorables automáticos) no se atribuye a nadie. Haz clic en una cifra para ver sus comparendos."
        acciones={
          <button type="button" className={controlCls(false)} onClick={exportar} disabled={filas.length === 0}>
            <Download className="h-3.5 w-3.5" aria-hidden />
            Exportar a Excel
          </button>
        }
      />
      <FiltrosPeriodo valor={periodo} onAplicar={setPeriodo} />
      {filas.length > 0 && (
        <p className="text-xs text-[#5B6B82] dark:text-slate-300">
          Total del periodo: <b>{totales.gestionados.toLocaleString("es-CO")}</b> comparendos gestionados, <b>{totales.dpEnviados.toLocaleString("es-CO")}</b> DPs enviados y{" "}
          <b>{totales.pagos.toLocaleString("es-CO")}</b> pagos registrados.
        </p>
      )}
      <DataTable
        ariaLabel="Productividad por usuario"
        rows={pg.paginar(filas)}
        getRowKey={(f) => f.usuarioId}
        status={datos.estado}
        errorMessage={datos.error ?? undefined}
        onRetry={datos.recargar}
        emptyMessage="Nadie registró acciones en este periodo."
        minWidth={1000}
        columns={[
          { key: "usuario", header: "Usuario", render: (f) => <span className="font-semibold">{f.usuario}</span> },
          ...METRICAS.map((m) => ({
            key: m.clave,
            header: m.titulo,
            align: "right" as const,
            render: (f: FilaProductividad) =>
              f[m.clave] > 0 ? (
                <button
                  type="button"
                  className="font-semibold text-[#3B4FD6] hover:underline dark:text-[#9FB3FF]"
                  onClick={() => setSel({ usuarioId: f.usuarioId, usuario: f.usuario, metrica: m.clave })}
                >
                  {f[m.clave].toLocaleString("es-CO")}
                </button>
              ) : (
                <span className="opacity-50">0</span>
              ),
          })),
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
      {sel && <DetalleCifra sel={sel} periodo={periodo} onCerrar={() => setSel(null)} onVer={(id) => { setSel(null); setVer(id); }} />}
      <DetalleComparendo id={ver} onCerrar={() => setVer(null)} />
    </section>
  );
}

/** Reporte de productividad por usuario (HU #13500 sobre la API de la HU #13498; §6.12, §7.9). */
export default function Productividad() {
  return (
    <RequiereReportes permiso={P.productividadVer}>
      <Contenido />
    </RequiereReportes>
  );
}
