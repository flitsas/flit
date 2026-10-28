"use client";

// HU #14074 (Feature #14070, Épica #14021) — Pestaña «Tarifas» de Plataforma > Persona Natural. Tabla de tarifas por
// organismo y tipo de trámite con sus conceptos (derechos de tránsito, servicio digital, envío y adicionales), el
// estado de cada versión (vigente, futura o cerrada), filtros por columna estándar, historial por fila y creación o
// versionado en un modal. Las cotizaciones ya emitidas no cambian al registrar una tarifa nueva. Sin datos bancarios.
import { useCallback, useEffect, useMemo, useState } from "react";
import { History, PencilLine, ReceiptText } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaCalendario } from "@/lib/format/date";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import {
  CONCEPTOS_TARIFA,
  CONCEPTO_LABEL,
  agruparTarifas,
  fetchTarifasPn,
  formatPesos,
  type ConceptoTarifa,
  type GrupoTarifa,
  type TarifaPn,
} from "@/lib/api/admin-pn-tarifas";
import { fetchOrganismosPn, fetchTiposPn, type OrganismoPn, type TipoPn } from "@/lib/api/admin-pn-config";
import { PN_FIELD_ERROR_CLS, PN_INFO_NOTICE_CLS, PN_PRIMARY_BTN_CLS, PN_PRIMARY_BTN_STYLE } from "./pn-ui";
import { TarifaFormModal, type TarifaFormMode } from "./TarifaFormModal";
import { ESTADO_TARIFA_LABEL, ESTADO_TARIFA_TONE, TarifaHistorialModal } from "./TarifaHistorialModal";

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "organismo", label: "Organismo", tipo: "texto" },
  { id: "tipo", label: "Tipo", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: Object.entries(ESTADO_TARIFA_LABEL).map(([value, label]) => ({ value, label })),
  },
  { id: "desde", label: "Vigente desde", tipo: "fecha" },
];

const CONCEPTOS_ADICIONALES: ConceptoTarifa[] = CONCEPTOS_TARIFA.filter(
  (c) => c !== "DERECHOS_TRANSITO" && c !== "SERVICIO_FLIT" && c !== "ENVIO_DOCUMENTO",
);

function valorDe(g: GrupoTarifa, concepto: ConceptoTarifa): string {
  const t = g.porConcepto[concepto];
  return t ? formatPesos(t.valorCop) : "—";
}

export function TarifasTab() {
  const { show } = useToast();
  const [tarifas, setTarifas] = useState<TarifaPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [incluirCerradas, setIncluirCerradas] = useState(false);
  const [tipos, setTipos] = useState<TipoPn[]>([]);
  const [organismos, setOrganismos] = useState<OrganismoPn[]>([]);
  const [catalogoError, setCatalogoError] = useState<string | null>(null);
  const [form, setForm] = useState<TarifaFormMode | null>(null);
  const [historial, setHistorial] = useState<GrupoTarifa | null>(null);
  const pg = usePaginacion("pn.tarifas");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: volverAPrimeraPagina });

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchTarifasPn({}, signal);
      if (signal?.aborted) return;
      setTarifas(list);
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

  // Los tipos y organismos solo alimentan el formulario: si fallan, la tabla sigue y el formulario avisa.
  useEffect(() => {
    const controller = new AbortController();
    Promise.all([fetchTiposPn(controller.signal), fetchOrganismosPn(controller.signal)])
      .then(([t, o]) => {
        if (controller.signal.aborted) return;
        setTipos(t);
        setOrganismos(o);
      })
      .catch(() => {
        if (!controller.signal.aborted) setCatalogoError("No se pudieron cargar los tipos y organismos para el formulario.");
      });
    return () => controller.abort();
  }, []);

  const grupos = useMemo(
    () => agruparTarifas(tarifas).filter((g) => incluirCerradas || g.estado !== "cerrada"),
    [tarifas, incluirCerradas],
  );

  const visibles = useMemo(
    () =>
      filtrarFilas(grupos, filtros.condicionesTotales, "", {
        columnas: {
          organismo: (g) => g.organismoNombre ?? "Valor general",
          tipo: (g) => g.tipoTramiteNombre ?? "Todos los tipos",
          estado: (g) => g.estado,
          desde: (g) => g.vigenteDesde,
        },
      }),
    [grupos, filtros.condicionesTotales],
  );

  function abrirFormulario(mode: TarifaFormMode) {
    if (catalogoError) {
      show(catalogoError, "error");
      return;
    }
    setForm(mode);
  }

  function actionsFor(g: GrupoTarifa): RowAction[] {
    const nombre = `${g.tipoTramiteNombre ?? "todos los tipos"} en ${g.organismoNombre ?? "valor general"}`;
    const actions: RowAction[] = [
      { icon: History, label: `Historial de ${nombre}`, onClick: () => setHistorial(g) },
    ];
    if (g.estado !== "cerrada") {
      actions.push({
        icon: PencilLine,
        label: `Nueva versión de ${nombre}`,
        tone: "primary",
        onClick: () => abrirFormulario({ kind: "version", grupo: g }),
      });
    }
    return actions;
  }

  const columns: DataTableColumn<GrupoTarifa>[] = [
    {
      key: "organismo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="organismo">
          Organismo
        </EncabezadoFiltrable>
      ),
      render: (g) => g.organismoNombre ?? <span className="text-[#59677D] dark:text-white/70">Valor general</span>,
    },
    {
      key: "tipo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="tipo">
          Tipo
        </EncabezadoFiltrable>
      ),
      render: (g) => g.tipoTramiteNombre ?? <span className="text-[#59677D] dark:text-white/70">Todos los tipos</span>,
    },
    { key: "derechos", header: "Derechos de tránsito", cellClassName: "whitespace-nowrap", render: (g) => valorDe(g, "DERECHOS_TRANSITO") },
    { key: "servicio", header: "Servicio digital", cellClassName: "whitespace-nowrap", render: (g) => valorDe(g, "SERVICIO_FLIT") },
    { key: "envio", header: "Envío", cellClassName: "whitespace-nowrap", render: (g) => valorDe(g, "ENVIO_DOCUMENTO") },
    {
      key: "otros",
      header: "Otros conceptos",
      cellClassName: "text-xs",
      render: (g) => {
        const otros = CONCEPTOS_ADICIONALES.filter((c) => g.porConcepto[c]);
        return otros.length === 0 ? (
          "—"
        ) : (
          <ul className="flex flex-col gap-0.5">
            {otros.map((c) => (
              <li key={c}>
                {CONCEPTO_LABEL[c]}: {valorDe(g, c)}
              </li>
            ))}
          </ul>
        );
      },
    },
    {
      key: "desde",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="desde">
          Vigente desde
        </EncabezadoFiltrable>
      ),
      cellClassName: "whitespace-nowrap",
      render: (g) => formatFechaCalendario(g.vigenteDesde),
    },
    {
      key: "estado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="estado">
          Estado
        </EncabezadoFiltrable>
      ),
      render: (g) => <StatusBadge label={ESTADO_TARIFA_LABEL[g.estado]} tone={ESTADO_TARIFA_TONE[g.estado]} />,
    },
    { key: "acciones", header: "Acciones", align: "right", render: (g) => <RowActions actions={actionsFor(g)} /> },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex max-w-3xl flex-col gap-2">
          <p className="text-sm text-[#59677D] dark:text-white/78">
            Tarifas que el portal usa para cotizar. Cada cambio registra una versión nueva: la anterior queda en el
            historial y no se edita.
          </p>
          <p className={PN_INFO_NOTICE_CLS}>
            Las cotizaciones ya emitidas no cambian cuando se registra una tarifa nueva.
          </p>
        </div>
        <ActionsMenu
          ariaLabel="Acciones de tarifas"
          items={[
            { key: "nueva", label: "Nueva tarifa", icon: ReceiptText, onSelect: () => abrirFormulario({ kind: "create" }) },
          ]}
        />
      </div>

      <label className="flex w-fit cursor-pointer items-center gap-2 text-xs font-semibold text-flit-primary dark:text-white">
        <input
          type="checkbox"
          checked={incluirCerradas}
          onChange={(e) => {
            setIncluirCerradas(e.target.checked);
            setPage(1);
          }}
          className="h-4 w-4 accent-[color:var(--color-flit-brand)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
        />
        Incluir tarifas cerradas
      </label>
      {catalogoError && (
        <p role="status" className={PN_FIELD_ERROR_CLS}>
          {catalogoError} Recargue la página para crear o versionar tarifas.
        </p>
      )}

      {status === "loading" ? (
        <CarLoaderModal label="Cargando tarifas…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no hay tarifas del portal. Cree la primera para que el portal pueda cotizar."
          emptyCta={
            <button
              type="button"
              onClick={() => abrirFormulario({ kind: "create" })}
              className={PN_PRIMARY_BTN_CLS}
              style={PN_PRIMARY_BTN_STYLE}
            >
              <ReceiptText className="h-4 w-4" aria-hidden="true" />
              Nueva tarifa
            </button>
          }
          errorMessage="No se pudieron cargar las tarifas."
          onRetry={() => void load()}
        >
          <DataTable<GrupoTarifa>
            ariaLabel="Tarifas del portal de Persona Natural"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(g) => g.clave}
            emptyContent={
              grupos.length === 0 && !filtros.hayFiltros ? (
                <p className="text-sm text-[#59677D] dark:text-white/70">
                  No hay tarifas vigentes ni futuras. Active «Incluir tarifas cerradas» para ver el historial.
                </p>
              ) : (
                <TablaFiltrosVacio onLimpiar={filtros.limpiar} />
              )
            }
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

      {form && (
        <TarifaFormModal
          mode={form}
          tipos={tipos}
          organismos={organismos}
          onClose={() => setForm(null)}
          onSaved={(nuevas) => {
            setForm(null);
            show(
              nuevas.length === 1 ? "Tarifa registrada." : `Tarifa registrada: ${nuevas.length} conceptos nuevos.`,
              "success",
            );
            void load();
          }}
        />
      )}

      {historial && <TarifaHistorialModal grupo={historial} onClose={() => setHistorial(null)} />}
    </div>
  );
}
