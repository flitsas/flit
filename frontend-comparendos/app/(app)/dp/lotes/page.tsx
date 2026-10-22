"use client";

import { Suspense, useEffect } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Download } from "lucide-react";
import { DataTable, InlineAlert, Modal, usePaginacion } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { Chip } from "@/components/ui/Chip";
import { controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { avanceLote, ESTADO_ITEM_LOTE, ESTADO_LOTE, hojaLote } from "@/lib/dp";
import type { Etiqueta } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { useCarga } from "@/lib/useCarga";
import { buildXlsx, XLSX_MIME } from "@/lib/xlsx";

interface Lote {
  id: string;
  estado: string;
  total: number;
  enviados: number;
  omitidos: number;
  fallidos: number;
  creadoEn: string;
  terminadoEn: string | null;
}

interface ItemLote {
  comparendoId: string;
  numero: string | null;
  estado: string;
  motivo: string | null;
  radicado: string | null;
}

const etiqueta = (mapa: Record<string, Etiqueta>, v: string): Etiqueta => mapa[v] ?? { texto: v, tono: "neutral" };

/** Mientras un lote está en curso, se vuelve a consultar cada pocos segundos. */
function useSondeo(activo: boolean, recargar: () => void, ms = 4000) {
  useEffect(() => {
    if (!activo) return;
    const t = setInterval(recargar, ms);
    return () => clearInterval(t);
  }, [activo, recargar, ms]);
}

function Avance({ l }: { l: Lote }) {
  const pct = avanceLote(l);
  return (
    <div className="flex min-w-32 items-center gap-2">
      <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-[#EEF1F6] dark:bg-white/10">
        <div className="h-full rounded-full bg-flit-brand" style={{ width: `${pct}%` }} />
      </div>
      <span className="tabular-nums opacity-70">{pct}%</span>
    </div>
  );
}

/** Descarga el resumen del lote como Excel (§7.3.2); se arma en el navegador con lo que ya está en pantalla. */
function exportar(l: Lote & { items: ItemLote[] }) {
  const url = URL.createObjectURL(new Blob([buildXlsx(hojaLote(l))], { type: XLSX_MIME }));
  const enlace = document.createElement("a");
  enlace.href = url;
  enlace.download = `envio-masivo-dp-${l.creadoEn.slice(0, 10)}.xlsx`;
  enlace.click();
  URL.revokeObjectURL(url);
}

function DetalleLote({ id, onCerrar }: { id: string; onCerrar: () => void }) {
  const carga = useCarga(`lote-dp:${id}`, (signal) => apiFetch<Lote & { items: ItemLote[] }>(`/dp/lotes/${id}`, { signal }));
  const pg = usePaginacion(25);
  const l = carga.datos;
  useSondeo(l?.estado === "EnCurso", carga.recargar);
  const items = l?.items ?? [];

  return (
    <Modal open onClose={onCerrar} size="2xl" title="Envío masivo de derechos de petición" description={l ? `Creado ${formatoFechaHora(l.creadoEn)}` : undefined}>
      {carga.estado === "error" && <InlineAlert tone="error">{carga.error}</InlineAlert>}
      {l && (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-4 text-xs">
            <Chip e={etiqueta(ESTADO_LOTE, l.estado)} />
            <span>
              <b>{l.enviados}</b> enviados · <b>{l.omitidos}</b> omitidos · <b>{l.fallidos}</b> fallidos de <b>{l.total}</b>
            </span>
            <Avance l={l} />
            <button type="button" className={`${controlCls(false)} ml-auto`} disabled={items.length === 0} onClick={() => exportar(l)}>
              <Download className="h-3.5 w-3.5" aria-hidden />
              Exportar resumen
            </button>
          </div>
          <DataTable
            ariaLabel="Comparendos del lote"
            rows={pg.paginar(items)}
            getRowKey={(i) => i.comparendoId}
            status={carga.estado === "loading" && !l ? "loading" : "ready"}
            emptyMessage="El lote no tiene comparendos."
            allowHorizontalScroll={false}
            columns={[
              { key: "numero", header: "Comparendo", render: (i) => <span className="font-semibold">{i.numero ?? "—"}</span> },
              { key: "estado", header: "Estado", render: (i) => <Chip e={etiqueta(ESTADO_ITEM_LOTE, i.estado)} /> },
              { key: "radicado", header: "Radicado", render: (i) => <span className="font-mono">{i.radicado ?? "—"}</span> },
              { key: "motivo", header: "Motivo", render: (i) => i.motivo ?? "—" },
            ]}
            pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: items.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
          />
        </div>
      )}
    </Modal>
  );
}

function Lotes() {
  const router = useRouter();
  const ruta = usePathname();
  const params = useSearchParams();
  const abierto = params.get("lote");
  const pg = usePaginacion(10);
  const lista = useCarga("lotes-dp", (signal) => apiFetch<Lote[]>("/dp/lotes", { signal }));
  const filas = lista.datos ?? [];
  useSondeo(filas.some((l) => l.estado === "EnCurso"), lista.recargar, 6000);
  const abrir = (id: string | null) => router.replace(id ? `${ruta}?lote=${id}` : ruta);

  return (
    <section className="flex flex-col gap-4">
      <Encabezado
        titulo="Envíos masivos de derechos de petición"
        descripcion="Para iniciar uno, selecciona los comparendos en la bandeja (mismo organismo de tránsito, hasta 500) y elige «Enviar derecho de petición»."
      />
      <DataTable
        ariaLabel="Envíos masivos"
        rows={pg.paginar(filas)}
        getRowKey={(l) => l.id}
        onRowClick={(l) => abrir(l.id)}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="Todavía no hay envíos masivos."
        columns={[
          { key: "creado", header: "Creado", render: (l) => formatoFechaHora(l.creadoEn) },
          { key: "estado", header: "Estado", render: (l) => <Chip e={etiqueta(ESTADO_LOTE, l.estado)} /> },
          { key: "total", header: "Comparendos", align: "right", render: (l) => l.total },
          { key: "enviados", header: "Enviados", align: "right", render: (l) => l.enviados },
          { key: "omitidos", header: "Omitidos", align: "right", render: (l) => l.omitidos },
          { key: "fallidos", header: "Fallidos", align: "right", render: (l) => l.fallidos },
          { key: "avance", header: "Avance", render: (l) => <Avance l={l} /> },
          { key: "terminado", header: "Terminado", render: (l) => formatoFechaHora(l.terminadoEn) },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
      {abierto && <DetalleLote id={abierto} onCerrar={() => abrir(null)} />}
    </section>
  );
}

/** Lotes de DP de la empresa y su avance (HU #13491 sobre la API de la HU #13489, §6.17). */
export default function LotesDp() {
  return (
    <Requiere permiso={P.dpEnviarMasivo}>
      <Suspense fallback={<p className="text-sm opacity-60">Cargando…</p>}>
        <Lotes />
      </Suspense>
    </Requiere>
  );
}
