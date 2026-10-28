"use client";

// HU #14075 AC1 — Tipos de trámite candidatos para el portal de Persona Natural. Cada tipo trae su elegibilidad (regla
// K2: perfil del tipo, asistente encendido y publicado) y el interruptor de habilitación. Solo se puede habilitar un
// tipo elegible (la API responde 422 `tipo_no_elegible`); apagarlo siempre se puede. «Sin tarifa vigente» avisa de un
// tipo habilitado que ningún organismo puede cotizar todavía.
import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import { establecerTipoPn, fetchTiposPn, type TipoPn } from "@/lib/api/admin-pn-config";
import { PN_FIELD_ERROR_CLS } from "./pn-ui";
import { pnError } from "./pnErrors";

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "tipo", label: "Tipo de trámite", tipo: "texto" },
  {
    id: "habilitado",
    label: "Habilitado",
    tipo: "seleccion",
    opciones: [
      { value: "si", label: "Habilitado" },
      { value: "no", label: "No habilitado" },
    ],
  },
];

export interface TiposSectionProps {
  /** Lleva a la pestaña Tarifas (enlace del aviso «Sin tarifa vigente»). */
  onIrATarifas: () => void;
}

export function TiposSection({ onIrATarifas }: TiposSectionProps) {
  const { show } = useToast();
  const [tipos, setTipos] = useState<TipoPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [guardando, setGuardando] = useState<string | null>(null);
  const [errorFila, setErrorFila] = useState<{ id: string; mensaje: string } | null>(null);
  const pg = usePaginacion("pn.tipos");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: volverAPrimeraPagina });

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchTiposPn(signal);
      if (signal?.aborted) return;
      setTipos(list);
      setStatus(list.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const visibles = useMemo(
    () =>
      filtrarFilas(tipos, filtros.condicionesTotales, "", {
        columnas: { tipo: (t) => [t.nombre, t.codigo], habilitado: (t) => (t.habilitado ? "si" : "no") },
      }),
    [tipos, filtros.condicionesTotales],
  );

  async function cambiar(tipo: TipoPn, habilitado: boolean) {
    setGuardando(tipo.id);
    setErrorFila(null);
    try {
      await establecerTipoPn(tipo.id, habilitado);
      setTipos((prev) =>
        prev.map((t) => (t.id === tipo.id ? { ...t, habilitado, ofrecible: habilitado && t.elegible } : t)),
      );
      show(`«${tipo.nombre}» ${habilitado ? "habilitado" : "deshabilitado"} para el portal.`, "success");
    } catch (err) {
      const info = pnError(err);
      setErrorFila({ id: tipo.id, mensaje: info.message });
      // El 422 deja al tipo como estaba: se recarga por si la elegibilidad cambió desde que se abrió la pestaña.
      if (info.status === 422) void load();
    } finally {
      setGuardando(null);
    }
  }

  const columns: DataTableColumn<TipoPn>[] = [
    {
      key: "tipo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="tipo">
          Tipo de trámite
        </EncabezadoFiltrable>
      ),
      render: (t) => (
        <>
          <span className="block font-semibold">{t.nombre}</span>
          <span className="block font-mono text-[11px] text-[#59677D] dark:text-white/70">{t.codigo}</span>
        </>
      ),
    },
    { key: "familia", header: "Familia", render: (t) => t.familia },
    {
      key: "elegible",
      header: "Elegible",
      render: (t) => (
        <div className="flex flex-col gap-1">
          <StatusBadge label={t.elegible ? "Elegible" : "No elegible"} tone={t.elegible ? "success" : "neutral"} />
          {!t.elegible && t.motivo && <span className="max-w-[260px] text-[11px] text-[#59677D] dark:text-white/70">{t.motivo}</span>}
        </div>
      ),
    },
    {
      key: "habilitado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="habilitado">
          Habilitado
        </EncabezadoFiltrable>
      ),
      render: (t) => {
        const bloqueado = !t.elegible && !t.habilitado;
        const motivoId = `pn-tipo-motivo-${t.id}`;
        return (
          <div className="flex flex-col gap-1">
            <SwitchToggle
              checked={t.habilitado}
              disabled={guardando === t.id || bloqueado}
              label={`${t.habilitado ? "Deshabilitar" : "Habilitar"} ${t.nombre} en el portal`}
              describedById={bloqueado ? motivoId : undefined}
              onChange={(next) => void cambiar(t, next)}
            />
            {bloqueado && (
              <span id={motivoId} className="text-[11px] text-[#59677D] dark:text-white/70">
                Solo se habilitan los tipos elegibles.
              </span>
            )}
            {errorFila?.id === t.id && (
              <span role="alert" className={PN_FIELD_ERROR_CLS}>
                {errorFila.mensaje}
              </span>
            )}
          </div>
        );
      },
    },
    {
      key: "tarifa",
      header: "Tarifa vigente",
      render: (t) =>
        t.habilitado && t.elegible && t.organismosConTarifaVigente === 0 ? (
          <div className="flex flex-col items-start gap-1">
            <StatusBadge label="Sin tarifa vigente" tone="warning" />
            <button
              type="button"
              onClick={onIrATarifas}
              className="rounded-md text-[11px] font-semibold text-[color:var(--flit-brand-ink)] underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
            >
              Ir a Tarifas
            </button>
          </div>
        ) : (
          <span className="text-xs">
            {t.organismosConTarifaVigente} {t.organismosConTarifaVigente === 1 ? "organismo" : "organismos"}
          </span>
        ),
    },
    {
      key: "ofrecible",
      header: "En el portal",
      render: (t) => <StatusBadge label={t.ofrecible ? "Se ofrece" : "No se ofrece"} tone={t.ofrecible ? "success" : "neutral"} />,
    },
  ];

  return (
    <section aria-labelledby="pn-tipos-titulo" className="flex flex-col gap-3">
      <div>
        <h2 id="pn-tipos-titulo" className="text-base font-bold text-flit-primary dark:text-white">
          Tipos de trámite
        </h2>
        <p className="text-xs text-[#59677D] dark:text-white/70">
          Un trámite se ofrece en el portal cuando el tipo es elegible y está habilitado, el organismo está habilitado y
          existe una tarifa vigente.
        </p>
      </div>
      {status === "loading" ? (
        <CarLoaderModal label="Cargando tipos de trámite…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="No hay tipos de trámite elegibles para Persona Natural todavía."
          errorMessage="No se pudieron cargar los tipos de trámite."
          onRetry={() => void load()}
        >
          <DataTable<TipoPn>
            ariaLabel="Tipos de trámite del portal de Persona Natural"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(t) => t.id}
            emptyContent={<TablaFiltrosVacio onLimpiar={filtros.limpiar} />}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: visibles.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}
    </section>
  );
}
