"use client";

// HU #14075 AC1 — Organismos de tránsito habilitados para el portal de Persona Natural (~1100). Buscador por nombre,
// código o municipio, filtros por columna y paginación numerada. Un organismo habilitado sin tarifa vigente muestra el
// aviso «Sin tarifa vigente» con un enlace a la pestaña Tarifas (el portal no puede cotizar ahí).
import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { SearchInput } from "@/components/atom/SearchInput";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { filtrarFilas, normalizarTexto, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import { establecerOrganismoPn, fetchOrganismosPn, type OrganismoPn } from "@/lib/api/admin-pn-config";
import { PN_FIELD_ERROR_CLS } from "./pn-ui";
import { pnError } from "./pnErrors";

export interface OrganismosSectionProps {
  /** Lleva a la pestaña Tarifas (enlace del aviso «Sin tarifa vigente»). */
  onIrATarifas: () => void;
}

export function OrganismosSection({ onIrATarifas }: OrganismosSectionProps) {
  const { show } = useToast();
  const [organismos, setOrganismos] = useState<OrganismoPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [busqueda, setBusqueda] = useState("");
  const [guardando, setGuardando] = useState<string | null>(null);
  const [errorFila, setErrorFila] = useState<{ id: string; mensaje: string } | null>(null);
  const pg = usePaginacion("pn.organismos");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);

  const columnasFiltro = useMemo<ColumnaFiltro[]>(() => {
    const departamentos = [...new Set(organismos.map((o) => o.departamento))].sort((a, b) => a.localeCompare(b, "es"));
    return [
      { id: "organismo", label: "Organismo", tipo: "texto" },
      { id: "departamento", label: "Departamento", tipo: "seleccion", opciones: departamentos.map((d) => ({ value: d, label: d })) },
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
  }, [organismos]);
  const filtros = useTablaFiltros({ columnas: columnasFiltro, onCambio: volverAPrimeraPagina });

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchOrganismosPn(signal);
      if (signal?.aborted) return;
      setOrganismos(list);
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

  const visibles = useMemo(() => {
    const porColumna = filtrarFilas(organismos, filtros.condicionesTotales, "", {
      columnas: {
        organismo: (o) => [o.nombre, o.codigo],
        departamento: (o) => o.departamento,
        habilitado: (o) => (o.habilitado ? "si" : "no"),
      },
    });
    const q = normalizarTexto(busqueda);
    if (!q) return porColumna;
    return porColumna.filter((o) =>
      [o.nombre, o.codigo, o.municipio, o.departamento].some((v) => normalizarTexto(v).includes(q)),
    );
  }, [organismos, filtros.condicionesTotales, busqueda]);

  async function cambiar(o: OrganismoPn, habilitado: boolean) {
    setGuardando(o.id);
    setErrorFila(null);
    try {
      await establecerOrganismoPn(o.id, habilitado);
      setOrganismos((prev) => prev.map((x) => (x.id === o.id ? { ...x, habilitado } : x)));
      show(`«${o.nombre}» ${habilitado ? "habilitado" : "deshabilitado"} para el portal.`, "success");
    } catch (err) {
      setErrorFila({ id: o.id, mensaje: pnError(err).message });
    } finally {
      setGuardando(null);
    }
  }

  const columns: DataTableColumn<OrganismoPn>[] = [
    {
      key: "organismo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="organismo">
          Organismo
        </EncabezadoFiltrable>
      ),
      render: (o) => (
        <>
          <span className="block font-semibold">{o.nombre}</span>
          <span className="block font-mono text-[11px] text-[#59677D] dark:text-white/70">{o.codigo}</span>
        </>
      ),
    },
    { key: "municipio", header: "Municipio", render: (o) => o.municipio },
    {
      key: "departamento",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="departamento">
          Departamento
        </EncabezadoFiltrable>
      ),
      render: (o) => o.departamento,
    },
    {
      key: "habilitado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="habilitado">
          Habilitado
        </EncabezadoFiltrable>
      ),
      render: (o) => (
        <div className="flex flex-col gap-1">
          <SwitchToggle
            checked={o.habilitado}
            disabled={guardando === o.id}
            label={`${o.habilitado ? "Deshabilitar" : "Habilitar"} ${o.nombre} en el portal`}
            onChange={(next) => void cambiar(o, next)}
          />
          {errorFila?.id === o.id && (
            <span role="alert" className={PN_FIELD_ERROR_CLS}>
              {errorFila.mensaje}
            </span>
          )}
        </div>
      ),
    },
    {
      key: "tarifa",
      header: "Tarifa vigente",
      render: (o) =>
        o.habilitado && o.tiposConTarifaVigente === 0 ? (
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
            {o.tiposConTarifaVigente} {o.tiposConTarifaVigente === 1 ? "tipo" : "tipos"}
          </span>
        ),
    },
  ];

  return (
    <section aria-labelledby="pn-organismos-titulo" className="flex flex-col gap-3">
      <div>
        <h2 id="pn-organismos-titulo" className="text-base font-bold text-flit-primary dark:text-white">
          Organismos de tránsito
        </h2>
        <p className="text-xs text-[#59677D] dark:text-white/70">
          Solo los organismos habilitados aparecen en el portal. Habilitar uno no basta: también necesita tarifa vigente.
        </p>
      </div>
      <SearchInput
        label="Buscar organismos"
        placeholder="Buscar por nombre, código o municipio…"
        value={busqueda}
        onChange={(v) => {
          setBusqueda(v);
          setPage(1);
        }}
        className="max-w-md"
      />
      {status === "loading" ? (
        <CarLoaderModal label="Cargando organismos…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="No hay organismos de tránsito para configurar."
          errorMessage="No se pudieron cargar los organismos."
          onRetry={() => void load()}
        >
          <DataTable<OrganismoPn>
            ariaLabel="Organismos del portal de Persona Natural"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(o) => o.id}
            emptyContent={
              <TablaFiltrosVacio
                onLimpiar={() => {
                  filtros.limpiar();
                  setBusqueda("");
                }}
              />
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
    </section>
  );
}
