"use client";

// HU #14075 AC1 — Organismos de tránsito habilitados para el portal de Persona Natural (~1100). Buscador por nombre,
// municipio o departamento, filtros por columna y paginación numerada. Un organismo habilitado sin tarifa vigente
// muestra «Falta tarifa» con un botón a la pestaña Tarifas (el portal no puede cotizar ahí). Dejar de ofrecer uno pide
// confirmación. La lista trae solo los organismos activos en FLIT (y los ya habilitados que dejaron de serlo, marcados
// «Ya no está activo en FLIT», que solo se pueden apagar).
import { useCallback, useEffect, useMemo, useState } from "react";
import { EyeOff } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { InlineAlert } from "@/components/atom/InlineAlert";
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
import { nombrePropio } from "./formato";
import { PnConfirmDialog, PnLinkButton, PnSeccionHeader } from "./pn-componentes";
import { pnError } from "./pnErrors";

export interface OrganismosSectionProps {
  /** Lleva a la pestaña Tarifas (botón «Crear tarifa» del aviso «Falta tarifa»). */
  onIrATarifas: () => void;
  /** Avisa a la consola para refrescar el estado de la configuración. */
  onCambio?: () => void;
}

export function OrganismosSection({ onIrATarifas, onCambio }: OrganismosSectionProps) {
  const { show } = useToast();
  const [organismos, setOrganismos] = useState<OrganismoPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [busqueda, setBusqueda] = useState("");
  const [guardando, setGuardando] = useState<string | null>(null);
  const [errorFila, setErrorFila] = useState<{ id: string; mensaje: string } | null>(null);
  const [apagando, setApagando] = useState<OrganismoPn | null>(null);
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
        label: "Ofrecer en el portal",
        tipo: "seleccion",
        opciones: [
          { value: "si", label: "Se ofrece" },
          { value: "no", label: "No se ofrece" },
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
      // Solo cambia cómo se ve el texto; la búsqueda ignora mayúsculas y tildes, y las acciones usan el id.
      setOrganismos(
        list.map((o) => ({
          ...o,
          nombre: nombrePropio(o.nombre),
          municipio: nombrePropio(o.municipio),
          departamento: nombrePropio(o.departamento),
        })),
      );
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

  /** Lanza el cambio; si falla lanza el error para que quien llama (fila o diálogo) lo muestre donde corresponde. */
  async function aplicar(o: OrganismoPn, habilitado: boolean) {
    await establecerOrganismoPn(o.id, habilitado);
    setOrganismos((prev) => prev.map((x) => (x.id === o.id ? { ...x, habilitado } : x)));
    show(`«${o.nombre}» ${habilitado ? "ahora se ofrece" : "ya no se ofrece"} en el portal.`, "success");
    onCambio?.();
  }

  async function encender(o: OrganismoPn) {
    setGuardando(o.id);
    setErrorFila(null);
    try {
      await aplicar(o, true);
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
          {o.activoEnFlit === false && <StatusBadge label="Ya no está activo en FLIT" tone="warning" />}
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
          Ofrecer en el portal
        </EncabezadoFiltrable>
      ),
      render: (o) => (
        <div className="flex flex-col items-start gap-1">
          <SwitchToggle
            checked={o.habilitado}
            disabled={guardando === o.id || (o.activoEnFlit === false && !o.habilitado)}
            label={`${o.habilitado ? "Dejar de ofrecer" : "Ofrecer"} ${o.nombre} en el portal`}
            onChange={(next) => (next ? void encender(o) : setApagando(o))}
          />
          {o.activoEnFlit === false && (
            <span className="max-w-[240px] text-xs text-[#59677D] dark:text-white/70">
              {o.habilitado ? "Solo puedes dejar de ofrecerlo." : "No se puede ofrecer."}
            </span>
          )}
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
            <StatusBadge label="Falta tarifa" tone="warning" />
            <PnLinkButton onClick={onIrATarifas} ariaLabel={`Crear tarifa para ${o.nombre}`}>
              Crear tarifa
            </PnLinkButton>
          </div>
        ) : (
          <span className="text-xs">
            {o.tiposConTarifaVigente === 0
              ? "—"
              : `${o.tiposConTarifaVigente} ${o.tiposConTarifaVigente === 1 ? "trámite" : "trámites"} con tarifa`}
          </span>
        ),
    },
  ];

  return (
    <section aria-labelledby="pn-organismos-titulo" className="flex flex-col gap-3">
      <PnSeccionHeader
        id="pn-organismos-titulo"
        paso={2}
        titulo="Organismos de tránsito"
        descripcion="Habilita los organismos donde el ciudadano podrá hacer sus trámites. Habilitar un organismo no basta: también necesita una tarifa vigente."
      />
      <InlineAlert tone="info" title="Solo organismos activos en FLIT">
        Aquí solo aparecen los organismos de tránsito que están activos en FLIT. Si uno deja de estar activo, sigue en la
        lista marcado «Ya no está activo en FLIT» y solo podrás dejar de ofrecerlo.
      </InlineAlert>
      <SearchInput
        label="Buscar organismo"
        placeholder="Buscar por nombre, municipio o departamento…"
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
          emptyMessage="No hay organismos de tránsito para configurar. Cuando FLIT cargue el catálogo, aparecerán aquí."
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

      {apagando && (
        <PnConfirmDialog
          title="Dejar de ofrecer este organismo"
          icon={EyeOff}
          tone="danger"
          confirmLabel="Dejar de ofrecer"
          onClose={() => setApagando(null)}
          onConfirm={async () => {
            await aplicar(apagando, false);
            setApagando(null);
          }}
        >
          <p>
            «{apagando.nombre}» dejará de aparecer en el portal: los ciudadanos no podrán elegirlo para sus trámites
            hasta que lo vuelvas a habilitar.
          </p>
        </PnConfirmDialog>
      )}
    </section>
  );
}
