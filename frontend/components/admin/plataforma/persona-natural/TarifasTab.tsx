"use client";

// HU #14074 (Feature #14070, Épica #14021) — Pestaña «Tarifas» de Plataforma > Persona Natural. Tabla de tarifas por
// organismo y tipo de trámite con sus conceptos (derechos de tránsito, servicio digital, envío y adicionales), el
// estado de cada versión (vigente, futura o cerrada), filtros por columna estándar, historial por fila y creación o
// versionado en un modal. Las cotizaciones ya emitidas no cambian al registrar una tarifa nueva. Sin datos bancarios.
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { History, PencilLine, ReceiptText } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CreateButton } from "@/components/atom/CreateButton";
import { InlineAlert } from "@/components/atom/InlineAlert";
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
  formatPesos,
  type ConceptoTarifa,
  type GrupoTarifa,
  type TarifaPn,
} from "@/lib/api/admin-pn-tarifas";
import type { OrganismoPn } from "@/lib/api/admin-pn-config";
import { TARIFAS_API_PLATAFORMA, type TarifasApi, type TarifasModo, type TipoTarifa } from "./tarifas-api";
import { PN_FIELD_ERROR_CLS } from "./pn-ui";
import { nombrePropio } from "./formato";
import { PnSeccionHeader } from "./pn-componentes";
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

/** Un grupo es de solo lectura cuando el servidor marca todos sus cargos como no editables (valor general, ajenos). */
function esSoloLectura(g: GrupoTarifa): boolean {
  const cargos = Object.values(g.porConcepto);
  return cargos.length > 0 && cargos.every((t) => t.editable === false);
}

/** Pedido externo de abrir el formulario de precio de un trámite (lo hace el paso «Medios de pago»). */
export interface SolicitudPrecio {
  tipoTramiteId: string;
  /** Distingue dos pedidos seguidos del mismo trámite. */
  nonce: number;
}

export interface TarifasTabProps {
  onCambio?: () => void;
  /** Abre el formulario de nueva tarifa con ese trámite elegido. */
  solicitudPrecio?: SolicitudPrecio | null;
  /** Se invoca al cerrar (guardando o no) un formulario abierto por `solicitudPrecio`. */
  onSolicitudCerrada?: () => void;
  /** Cliente de API; por defecto el de Plataforma. Debe ser estable entre renders. */
  api?: TarifasApi;
  /** Presentación y restricciones; por defecto Plataforma, sin cambios. */
  modo?: TarifasModo;
}

export function TarifasTab({ onCambio, solicitudPrecio, onSolicitudCerrada, api = TARIFAS_API_PLATAFORMA, modo }: TarifasTabProps = {}) {
  const fijo = modo?.organismoFijo === true;
  const { show } = useToast();
  const [tarifas, setTarifas] = useState<TarifaPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [tipos, setTipos] = useState<TipoTarifa[]>([]);
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
      const list = await api.listar({}, signal);
      if (signal?.aborted) return;
      setTarifas(list.map((t) => ({ ...t, organismoNombre: t.organismoNombre ? nombrePropio(t.organismoNombre) : t.organismoNombre })));
      setStatus(list.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, [api]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Los tipos y organismos solo alimentan el formulario: si fallan, la tabla sigue y el formulario avisa.
  useEffect(() => {
    const controller = new AbortController();
    api
      .catalogos(controller.signal)
      .then(({ tipos: t, organismos: o }) => {
        if (controller.signal.aborted) return;
        setTipos(t);
        setOrganismos(
          o.map((x) => ({ ...x, nombre: nombrePropio(x.nombre), municipio: nombrePropio(x.municipio), departamento: nombrePropio(x.departamento) })),
        );
      })
      .catch(() => {
        if (!controller.signal.aborted) setCatalogoError("No se pudieron cargar los trámites y organismos para el formulario.");
      });
    return () => controller.abort();
  }, [api]);

  // Por defecto solo se ven las vigentes y próximas; con el filtro de Estado se pueden ver también las anteriores.
  const filtraPorEstado = filtros.condicionDe("estado") !== undefined;
  const grupos = useMemo(
    () => agruparTarifas(tarifas).filter((g) => filtraPorEstado || g.estado !== "cerrada"),
    [tarifas, filtraPorEstado],
  );

  const visibles = useMemo(
    () =>
      filtrarFilas(grupos, filtros.condicionesTotales, "", {
        columnas: {
          organismo: (g) => g.organismoNombre ?? "Todos los organismos",
          tipo: (g) => g.tipoTramiteNombre ?? "Todos los trámites",
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

  // «Definir precio» desde Medios de pago: el formulario se abre con el trámite ya elegido.
  const nonceAtendido = useRef<number | null>(null);
  const abiertoPorSolicitud = useRef(false);
  useEffect(() => {
    if (!solicitudPrecio || nonceAtendido.current === solicitudPrecio.nonce) return;
    if (tipos.length === 0 && !catalogoError) return; // espera los catálogos
    nonceAtendido.current = solicitudPrecio.nonce;
    if (catalogoError) {
      show(catalogoError, "error");
      onSolicitudCerrada?.();
      return;
    }
    abiertoPorSolicitud.current = true;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reacción a un pedido externo
    setForm({ kind: "create", tipoTramiteId: solicitudPrecio.tipoTramiteId });
  }, [solicitudPrecio, tipos, catalogoError, show, onSolicitudCerrada]);

  function cerrarFormulario() {
    setForm(null);
    if (abiertoPorSolicitud.current) {
      abiertoPorSolicitud.current = false;
      onSolicitudCerrada?.();
    }
  }

  function actionsFor(g: GrupoTarifa): RowAction[] {
    const nombre = `${g.tipoTramiteNombre ?? "todos los trámites"} en ${g.organismoNombre ?? "todos los organismos"}`;
    const actions: RowAction[] = [
      { icon: History, label: `Ver historial de ${nombre}`, onClick: () => setHistorial(g) },
    ];
    if (g.estado !== "cerrada" && !esSoloLectura(g)) {
      actions.push({
        icon: PencilLine,
        label: `Cambiar valores de ${nombre}`,
        tone: "primary",
        onClick: () => abrirFormulario({ kind: "version", grupo: g }),
      });
    }
    return actions;
  }

  const columns: DataTableColumn<GrupoTarifa>[] = [
    fijo
      ? {
          key: "origen",
          header: "Origen",
          render: (g) =>
            g.organismoId === null ? (
              <StatusBadge label="Valor general" tone="neutral" />
            ) : (
              <StatusBadge label={modo?.etiquetaPropia ?? "De tu organismo"} tone="info" />
            ),
        }
      : {
          key: "organismo",
          header: (
            <EncabezadoFiltrable filtros={filtros} columnaId="organismo">
              Organismo
            </EncabezadoFiltrable>
          ),
          render: (g) => g.organismoNombre ?? <span className="text-[#59677D] dark:text-white/70">Todos los organismos</span>,
        },
    {
      key: "tipo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="tipo">
          Trámite
        </EncabezadoFiltrable>
      ),
      render: (g) => g.tipoTramiteNombre ?? <span className="text-[#59677D] dark:text-white/70">Todos los trámites</span>,
    },
    { key: "derechos", header: "Derechos de tránsito", cellClassName: "whitespace-nowrap", render: (g) => valorDe(g, "DERECHOS_TRANSITO") },
    { key: "servicio", header: "Servicio digital FLIT", cellClassName: "whitespace-nowrap", render: (g) => valorDe(g, "SERVICIO_FLIT") },
    { key: "envio", header: "Envío", cellClassName: "whitespace-nowrap", render: (g) => valorDe(g, "ENVIO_DOCUMENTO") },
    {
      key: "otros",
      header: "Otros cargos",
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
      <PnSeccionHeader
        id="pn-tarifas-titulo"
        paso={modo?.sinPaso ? undefined : 3}
        titulo={modo?.titulo ?? "Tarifas del portal"}
        descripcion={
          modo?.descripcion ??
          "Cuánto paga el ciudadano por cada trámite: derechos de tránsito, servicio digital y otros cargos. Si necesitas otro valor, registra un cambio: el anterior queda en el historial y no se edita. Se ven las tarifas vigentes y próximas; para ver las anteriores, filtra por Estado."
        }
        accion={
          <CreateButton label="Nueva tarifa" icon={ReceiptText} onClick={() => abrirFormulario({ kind: "create" })} />
        }
      />
      <InlineAlert tone="info">
        Las cotizaciones que ya se emitieron no cambian cuando registras una tarifa nueva: la nueva solo aplica desde su
        fecha de inicio.
      </InlineAlert>

      {catalogoError && (
        <p role="status" className={PN_FIELD_ERROR_CLS}>
          {catalogoError} Recarga la página para crear o cambiar tarifas.
        </p>
      )}

      {status === "loading" ? (
        <CarLoaderModal label="Cargando tarifas…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no hay tarifas. Crea la primera para que el portal pueda cotizar los trámites."
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
                  No hay tarifas vigentes ni próximas. Para ver las anteriores, filtra por Estado en el encabezado de la columna.
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
          api={api}
          modo={modo}
          onClose={cerrarFormulario}
          onSaved={(nuevas) => {
            cerrarFormulario();
            show(
              nuevas.length === 1 ? "Tarifa guardada." : `Tarifa guardada: ${nuevas.length} cargos nuevos.`,
              "success",
            );
            void load();
            onCambio?.();
          }}
        />
      )}

      {historial && <TarifaHistorialModal grupo={historial} api={api} onClose={() => setHistorial(null)} />}
    </div>
  );
}
