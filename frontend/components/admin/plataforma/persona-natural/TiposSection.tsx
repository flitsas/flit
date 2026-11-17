"use client";

// HU #14075 AC1 (ajustada, Feature #14248) —Tipos de trámite que el portal de Persona Natural puede ofrecer. La API solo lista los
// trámites con asistente habilitado en FLIT 2, así que todos se pueden encender con el interruptor «Ofrecer en el
// portal» (si aun así la API responde 422 `tipo_no_elegible`, se muestra el motivo en la fila y se recarga). Dejar de
// ofrecerlo pide confirmación porque el ciudadano deja de verlo.
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
import { PnConfirmDialog, PnLinkButton, PnSeccionHeader } from "./pn-componentes";
import { pnError } from "./pnErrors";

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "tipo", label: "Trámite", tipo: "texto" },
  {
    id: "habilitado",
    label: "Ofrecer en el portal",
    tipo: "seleccion",
    opciones: [
      { value: "si", label: "Se ofrece" },
      { value: "no", label: "No se ofrece" },
    ],
  },
];

/** «TRASPASO» / «traspaso» → «Traspaso»: la categoría llega en mayúsculas o minúsculas según el catálogo. */
function legible(texto: string): string {
  const t = texto.trim().toLowerCase().replace(/_/g, " ");
  return t.charAt(0).toUpperCase() + t.slice(1);
}

export interface TiposSectionProps {
  /** Lleva a la pestaña Tarifas (botón «Crear tarifa» del aviso «Falta tarifa»). */
  onIrATarifas: () => void;
  /** Avisa a la consola para refrescar el estado de la configuración. */
  onCambio?: () => void;
}

export function TiposSection({ onIrATarifas, onCambio }: TiposSectionProps) {
  const { show } = useToast();
  const [tipos, setTipos] = useState<TipoPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [guardando, setGuardando] = useState<string | null>(null);
  const [errorFila, setErrorFila] = useState<{ id: string; mensaje: string } | null>(null);
  const [apagando, setApagando] = useState<TipoPn | null>(null);
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

  /** Lanza el cambio; si falla lanza el error para que quien llama (fila o diálogo) lo muestre donde corresponde. */
  async function aplicar(tipo: TipoPn, habilitado: boolean) {
    await establecerTipoPn(tipo.id, habilitado);
    setTipos((prev) =>
      prev.map((t) => (t.id === tipo.id ? { ...t, habilitado, ofrecible: habilitado } : t)),
    );
    show(`«${tipo.nombre}» ${habilitado ? "ahora se ofrece" : "ya no se ofrece"} en el portal.`, "success");
    onCambio?.();
  }

  async function encender(tipo: TipoPn) {
    setGuardando(tipo.id);
    setErrorFila(null);
    try {
      await aplicar(tipo, true);
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
          Trámite
        </EncabezadoFiltrable>
      ),
      render: (t) => (
        <>
          <span className="block font-semibold">{t.nombre}</span>
          <span className="block text-xs text-muted-foreground">{legible(t.familia)}</span>
        </>
      ),
    },
    {
      key: "habilitado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="habilitado">
          Ofrecer en el portal
        </EncabezadoFiltrable>
      ),
      render: (t) => (
        <div className="flex flex-col items-start gap-1">
          <SwitchToggle
            checked={t.habilitado}
            disabled={guardando === t.id}
            label={`${t.habilitado ? "Dejar de ofrecer" : "Ofrecer"} ${t.nombre} en el portal`}
            onChange={(next) => (next ? void encender(t) : setApagando(t))}
          />
          {errorFila?.id === t.id && (
            <span role="alert" className={PN_FIELD_ERROR_CLS}>
              {errorFila.mensaje}
            </span>
          )}
        </div>
      ),
    },
    {
      key: "ofrecible",
      header: "Lo ve el ciudadano",
      render: (t) => {
        if (!t.ofrecible) return <StatusBadge label="No" tone="neutral" />;
        return t.organismosConTarifaVigente === 0 ? (
          <StatusBadge label="Falta tarifa" tone="warning" />
        ) : (
          <StatusBadge label="Sí" tone="success" />
        );
      },
    },
    {
      key: "tarifa",
      header: "Tarifa vigente",
      render: (t) =>
        t.habilitado && t.organismosConTarifaVigente === 0 ? (
          <div className="flex flex-col items-start gap-1">
            <span className="text-xs">Ningún organismo tiene tarifa.</span>
            <PnLinkButton onClick={onIrATarifas} ariaLabel={`Crear tarifa para ${t.nombre}`}>
              Crear tarifa
            </PnLinkButton>
          </div>
        ) : (
          <span className="text-xs">
            {t.organismosConTarifaVigente === 0
              ? "—"
              : `En ${t.organismosConTarifaVigente} ${t.organismosConTarifaVigente === 1 ? "organismo" : "organismos"}`}
          </span>
        ),
    },
  ];

  return (
    <section aria-labelledby="pn-tipos-titulo" className="flex flex-col gap-3">
      <PnSeccionHeader
        id="pn-tipos-titulo"
        paso={1}
        titulo="Trámites que ofrece el portal"
        descripcion="Activa los trámites que una persona natural puede iniciar. Aquí aparecen los trámites que ya tienen su asistente habilitado en FLIT 2; actívalos para que se ofrezcan."
      />
      {status === "loading" ? (
        <CarLoaderModal label="Cargando trámites…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Todavía no hay trámites habilitados en FLIT 2 para Persona Natural. Cuando se habilite uno, aparecerá aquí."
          errorMessage="No se pudieron cargar los trámites."
          onRetry={() => void load()}
        >
          <DataTable<TipoPn>
            ariaLabel="Trámites del portal de Persona Natural"
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

      {apagando && (
        <PnConfirmDialog
          title="Dejar de ofrecer este trámite"
          icono="ojo-tachado"
          tone="danger"
          confirmLabel="Dejar de ofrecer"
          onClose={() => setApagando(null)}
          onConfirm={async () => {
            await aplicar(apagando, false);
            setApagando(null);
          }}
        >
          <p>
            «{apagando.nombre}» dejará de aparecer en el portal: los ciudadanos no podrán iniciar este trámite hasta que
            lo vuelvas a activar. Las tarifas que ya configuraste se conservan.
          </p>
        </PnConfirmDialog>
      )}
    </section>
  );
}

