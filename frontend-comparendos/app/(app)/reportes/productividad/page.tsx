"use client";

import { Suspense, useEffect, useState } from "react";
import { useSesion } from "@/components/ComparendosShell";
import { useRouter, useSearchParams } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { DataTable, Modal, usePaginacion } from "@flit/ui";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { useAbrirDetalle } from "@/components/detalle/useAbrirDetalle";
import { Encabezado } from "@/components/Pantalla";
import { FiltrosPeriodo, periodoInicial, type Periodo } from "@/components/reportes/FiltrosPeriodo";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { useToast } from "@/components/Toast";
import { controlCls } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { accionesProductividad, METRICAS, totalesProductividad, type ComparendoMetrica, type FilaProductividad, type Metrica } from "@/lib/reportes";
import { productividadAParams, productividadDeParams, type CifraAbierta } from "@/lib/estadoProductividad";
import { useCarga } from "@/lib/useCarga";
import { crearUrlPropia } from "@/lib/urlPropia";

// Rediseño S1: periodo y cifra abierta viven en la URL; al volver del detalle del comparendo se reabre la cifra.
const UrlProductividad = crearUrlPropia();

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
    <Modal icono="reportes" open onClose={onCerrar} title={`${titulo} · ${sel.usuario}`} description={`Del ${formatoFecha(periodo.desde)} al ${formatoFecha(periodo.hasta)}.`} size="xl">
      <DataTable
        tarjeta={false}
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

function ContenidoConUrl() {
  const version = UrlProductividad.useVersion(useSearchParams().toString());
  return <Contenido key={version} />;
}

function Contenido() {
  const toast = useToast();
  const params = useSearchParams();
  const router = useRouter();
  const [porDefecto] = useState(periodoInicial);
  const [inicial] = useState(() => productividadDeParams(params, porDefecto));
  const { sesion } = useSesion();
  const [periodoElegido, setPeriodo] = useState<Periodo>(inicial.periodo);
  // La empresa solo la elige el SuperAdmin: una ?empresa= puesta a mano no viaja para nadie más.
  const periodo: Periodo = sesion?.usuario.esSuperAdmin ? periodoElegido : { ...periodoElegido, empresa: null };
  const [cifra, setCifra] = useState<CifraAbierta | null>(inicial.cifra);
  const setVer = useAbrirDetalle();
  const pg = usePaginacion(25);
  const datos = useCarga(JSON.stringify(periodo), (signal) =>
    apiFetch<{ filas: FilaProductividad[] }>("/reportes/productividad", { signal, query: { empresa: periodo.empresa, desde: periodo.desde, hasta: periodo.hasta } }),
  );
  const filas = datos.datos?.filas ?? [];
  const totales = totalesProductividad(filas);
  // El nombre del usuario sale de la fila: la URL solo lleva su id y la métrica.
  const filaCifra = cifra ? filas.find((f) => f.usuarioId === cifra.usuarioId) : undefined;
  const sel: Seleccion | null = cifra && filaCifra ? { usuarioId: cifra.usuarioId, usuario: filaCifra.usuario, metrica: cifra.metrica } : null;

  // Una cifra cuyo usuario no está en el periodo ya cargado no se guarda: no debe reabrirse sola más adelante.
  const cifraVigente = datos.estado === "ready" && cifra && !filaCifra ? null : cifra;
  const nuevaQuery = productividadAParams(periodo, cifraVigente, porDefecto).toString();
  useEffect(() => {
    UrlProductividad.registrar(nuevaQuery);
    if (nuevaQuery === new URLSearchParams(window.location.search).toString()) return;
    router.replace(nuevaQuery ? `/reportes/productividad?${nuevaQuery}` : "/reportes/productividad", { scroll: false });
  }, [nuevaQuery, router]);

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
            <Icono nombre="nube-bajar" className="h-3.5 w-3.5" />
            Exportar a Excel
          </button>
        }
      />
      <FiltrosPeriodo
        valor={periodo}
        onAplicar={(p) => {
          // Otro periodo es otra consulta: la cifra abierta se cierra.
          setPeriodo(p);
          setCifra(null);
        }}
      />
      {filas.length > 0 && (
        <div className={METRICAS_GRID_CLS} aria-label="Totales del periodo" role="group">
          <TarjetaMetrica etiqueta="Comparendos gestionados" cifra={totales.gestionados.toLocaleString("es-CO")} pie={`${filas.length.toLocaleString("es-CO")} usuarios en el periodo`} />
          <TarjetaMetrica etiqueta="DPs enviados" cifra={totales.dpEnviados.toLocaleString("es-CO")} pie={`${totales.respuestasOt.toLocaleString("es-CO")} respuestas del organismo registradas`} />
          <TarjetaMetrica etiqueta="Pagos registrados" cifra={totales.pagos.toLocaleString("es-CO")} pie={`${totales.contraventoresAsignados.toLocaleString("es-CO")} contraventores asignados`} />
          <TarjetaMetrica etiqueta="Avisos y OCR" cifra={(totales.avisosManuales + totales.ocrRegistrados).toLocaleString("es-CO")} pie={`${totales.avisosManuales.toLocaleString("es-CO")} avisos manuales · ${totales.ocrRegistrados.toLocaleString("es-CO")} por OCR`} />
        </div>
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
                  className="font-semibold text-flit-brand-ink hover:underline"
                  onClick={() => setCifra({ usuarioId: f.usuarioId, metrica: m.clave })}
                >
                  {f[m.clave].toLocaleString("es-CO")}
                </button>
              ) : (
                <span className="text-[color:var(--di-text-tertiary)]">0</span>
              ),
          })),
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
      {/* Al ver un comparendo la cifra sigue abierta en la URL: así «volver» la reabre. */}
      {sel && <DetalleCifra sel={sel} periodo={periodo} onCerrar={() => setCifra(null)} onVer={(id) => setVer(id, null, `/reportes/productividad?${nuevaQuery}`)} />}
    </section>
  );
}

/** Reporte de productividad por usuario (HU #13500 sobre la API de la HU #13498; §6.12, §7.9). */
export default function Productividad() {
  return (
    <RequiereReportes permiso={P.productividadVer}>
      <Suspense fallback={null}>
        <ContenidoConUrl />
      </Suspense>
    </RequiereReportes>
  );
}
