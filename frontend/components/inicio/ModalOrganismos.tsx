"use client";

import { useMemo, useState } from "react";
import { Modal } from "@flit/ui/Modal";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { StatusBadge } from "@flit/ui/StatusBadge";
import { usePaginacion } from "@flit/ui/usePaginacion";
import { TRAMITES_INPUT_CLS } from "@/components/operacion/tramites-control-styles";
import type { SuperAdminOrganismo } from "@/lib/api/analytics-superadmin";

type Orden = "volumen" | "rapido" | "lento" | "nombre";

const ORDENES: { valor: Orden; etiqueta: string }[] = [
  { valor: "volumen", etiqueta: "Más trámites" },
  { valor: "rapido", etiqueta: "Respuesta más rápida" },
  { valor: "lento", etiqueta: "Respuesta más lenta" },
  { valor: "nombre", etiqueta: "Nombre (A-Z)" },
];

/** Sin decisiones no hay tiempo: va siempre al final, ordene como ordene. */
function compararTiempo(a: SuperAdminOrganismo, b: SuperAdminOrganismo, signo: 1 | -1): number {
  if (a.tiempoPromedioDias === null) return b.tiempoPromedioDias === null ? 0 : 1;
  if (b.tiempoPromedioDias === null) return -1;
  return signo * (a.tiempoPromedioDias - b.tiempoPromedioDias);
}

function ordenar(lista: SuperAdminOrganismo[], orden: Orden): SuperAdminOrganismo[] {
  const porNombre = (a: SuperAdminOrganismo, b: SuperAdminOrganismo) => a.nombre.localeCompare(b.nombre, "es");
  return [...lista].sort((a, b) => {
    switch (orden) {
      case "volumen":
        return b.volumen - a.volumen || porNombre(a, b);
      case "rapido":
        return compararTiempo(a, b, 1) || porNombre(a, b);
      case "lento":
        return compararTiempo(a, b, -1) || porNombre(a, b);
      default:
        return porNombre(a, b);
    }
  });
}

/** Comparación sin tildes ni mayúsculas («bogota» encuentra «BOGOTÁ»). */
const normalizar = (s: string) => s.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase();

export interface ModalOrganismosProps {
  abierto: boolean;
  onCerrar: () => void;
  organismos: SuperAdminOrganismo[];
  /** «Todas las compañías» o el nombre de la compañía elegida. */
  alcance: string;
  elegidoId: string | null;
  onElegir: (transitOfficeId: string) => void;
  formatoDias: (dias: number) => string;
}

/**
 * Todos los organismos del periodo en una tabla, en un modal centrado (rediseño del Inicio, 2026-11-18; Samuel eligió
 * el modal sobre el panel lateral). El Inicio muestra los 5 de más volumen en tarjetas; aquí se recorren todos:
 * buscador, orden y una fila por organismo. Pulsar una fila filtra «Distribución de operaciones» y cierra el modal.
 */
export function ModalOrganismos({
  abierto,
  onCerrar,
  organismos,
  alcance,
  elegidoId,
  onElegir,
  formatoDias,
}: ModalOrganismosProps) {
  const [busqueda, setBusqueda] = useState("");
  const [orden, setOrden] = useState<Orden>("volumen");
  const pg = usePaginacion("inicio.SuperAdminDashboard.organismos");

  const filas = useMemo(() => {
    const q = normalizar(busqueda.trim());
    const filtrados = q ? organismos.filter((o) => normalizar(o.nombre).includes(q)) : organismos;
    return ordenar(filtrados, orden);
  }, [organismos, busqueda, orden]);

  const elegir = (id: string) => {
    onElegir(id);
    onCerrar();
  };

  const numero = (v: number) => <span className="tabular-nums">{v.toLocaleString("es-CO")}</span>;
  const columnas: DataTableColumn<SuperAdminOrganismo>[] = [
    {
      key: "nombre",
      header: "Organismo",
      render: (o) => (
        <span className="flex flex-wrap items-center gap-2">
          {/* Botón real: con teclado se elige igual que con el ratón (la fila entera responde al clic). */}
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              elegir(o.transitOfficeId);
            }}
            aria-label={`Filtrar la distribución por ${o.nombre}`}
            className="text-left font-medium text-[color:var(--di-text-primary)] underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]"
          >
            {o.nombre}
          </button>
          {o.transitOfficeId === elegidoId && <StatusBadge label="Filtrando" tone="info" />}
        </span>
      ),
    },
    { key: "volumen", header: "Trámites", align: "right", render: (o) => numero(o.volumen) },
    { key: "decididos", header: "Decididos", align: "right", render: (o) => numero(o.decididos) },
    {
      key: "tiempo",
      header: "Tiempo de respuesta",
      align: "right",
      render: (o) =>
        o.tiempoPromedioDias === null ? (
          <span className="text-[color:var(--di-text-tertiary)]">Sin decisiones</span>
        ) : (
          <span className="tabular-nums">{formatoDias(o.tiempoPromedioDias)}</span>
        ),
    },
  ];

  return (
    <Modal
      open={abierto}
      onClose={onCerrar}
      title="Organismos de tránsito"
      icono="banco"
      description={`${alcance} · ${organismos.length} con trámites en el periodo. Pulsa uno para filtrar la distribución.`}
      size="xl"
    >
      {/* Alto mínimo: el modal no salta de tamaño al filtrar con el buscador. */}
      <div className="flex min-h-[26rem] flex-col gap-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
          <label className="flex min-w-0 flex-col gap-1 text-[0.8125rem] font-semibold text-[color:var(--di-text-primary)]">
            Buscar organismo
            <input
              id="panel-organismos-buscar"
              type="search"
              value={busqueda}
              onChange={(e) => {
                setBusqueda(e.target.value);
                pg.setPage(1);
              }}
              placeholder="Nombre del organismo"
              className={`${TRAMITES_INPUT_CLS} w-full`}
            />
          </label>
          <label className="flex flex-col gap-1 text-[0.8125rem] font-semibold text-[color:var(--di-text-primary)]">
            Ordenar por
            <select
              id="panel-organismos-orden"
              value={orden}
              onChange={(e) => {
                setOrden(e.target.value as Orden);
                pg.setPage(1);
              }}
              className={TRAMITES_INPUT_CLS}
            >
              {ORDENES.map((o) => (
                <option key={o.valor} value={o.valor}>
                  {o.etiqueta}
                </option>
              ))}
            </select>
          </label>
        </div>
        <DataTable
          columns={columnas}
          rows={pg.paginar(filas)}
          getRowKey={(o) => o.transitOfficeId}
          onRowClick={(o) => elegir(o.transitOfficeId)}
          status="ready"
          emptyMessage={busqueda.trim() ? `Ningún organismo coincide con «${busqueda.trim()}».` : "No hay organismos."}
          ariaLabel="Organismos de tránsito"
          tarjeta={false}
          minWidth={520}
          pagination={{
            page: pg.page,
            pageSize: pg.pageSize,
            totalCount: filas.length,
            onPageChange: pg.setPage,
            onPageSizeChange: pg.setPageSize,
          }}
        />
      </div>
    </Modal>
  );
}
