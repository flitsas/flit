"use client";

import { ArrowDown, ArrowUp, ArrowUpDown, Check, FolderOpen, Star, X } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  TABLA_CELDA_SECUNDARIA_CLS,
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { OtTablePagination } from "./OtTablePagination";
import { ActionsMenu, type ActionsMenuItem } from "@/components/atom/ActionsMenu";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { formatOtDate, formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";
import {
  esperandoProcesoDelGestor,
  plateFlowChipStyle,
  plateFlowLabel,
  puedeDecidirOt,
} from "@/lib/tramites/estados";
import {
  OT_PROCEDURES_COLUMNS,
  otColumnToSortBy,
} from "@/lib/admin/ot-procedures-columns";

export interface ClientProceduresTableProps {
  rows: OtClientProcedure[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onApprove: (row: OtClientProcedure) => void;
  onReject: (row: OtClientProcedure) => void;
  showApprovalActions?: boolean;
  /**
   * Botón único "Ver consolidado" (Feature #10701): muestra el consolidado maestro vigente y, si no
   * lo está (nunca generado o invalidado por un cambio de estado / LT), lo genera y lo muestra. El
   * backend decide regenerar-o-reutilizar por la marca `consolidado_maestro_vigente`.
   */
  onConsolidado?: (row: OtClientProcedure) => void;
  /** Adjunta la Licencia de Transito a un tramite ya aprobado (solo OT admin). */
  onAdjuntarLt?: (row: OtClientProcedure) => void;
  /** Feature #10587 — asignar placa a un trámite en preasignado (Flujo B). */
  onAssignPlate?: (row: OtClientProcedure) => void;
  /** Feature #10587 — revocar la preasignación de un trámite. */
  onRevoke?: (row: OtClientProcedure) => void;
  /** Id de la fila con accion de consolidado en curso (deshabilita sus botones). */
  consolidadoActingId?: string | null;
  /** Abre el panel de documentos del expediente para el trámite. */
  onVerDocumentos?: (row: OtClientProcedure) => void;
  /**
   * Abre el detalle del trámite. Lo disparan DOS cosas: la acción del menú y la fila entera —el
   * detalle es a lo que se entra el 90% de las veces, y obligar a pasar por un menú de ocho
   * opciones para llegar a él era peaje puro (prototipo del Feature #12059).
   */
  onVerDetalle?: (row: OtClientProcedure) => void;
  /** sortBy actual del API (vin, placa, vendedor, …). */
  sortBy?: string;
  sortDir?: "asc" | "desc";
  onSortChange?: (sortBy: string, sortDir: "asc" | "desc") => void;
}

function SortableTh({
  label,
  sortLabel,
  columnKey,
  sortBy,
  sortDir,
  onSortChange,
  className = "",
}: {
  label: string;
  /** Por qué ordena, si no es lo que dice el rótulo (ver `sortLabel` en el catálogo). */
  sortLabel?: string;
  columnKey: string;
  sortBy?: string;
  sortDir?: "asc" | "desc";
  onSortChange?: (sortBy: string, sortDir: "asc" | "desc") => void;
  className?: string;
}) {
  const apiKey = otColumnToSortBy(columnKey);
  const active = sortBy === apiKey || (columnKey === "fechaRadicacion" && sortBy === "createdAt");
  const nextDir: "asc" | "desc" = active && sortDir === "asc" ? "desc" : "asc";
  const Icon = !active ? ArrowUpDown : sortDir === "asc" ? ArrowUp : ArrowDown;

  const cls = `${TABLA_HEADER_CELL_CLS} ${className}`.trim();
  const style = { background: TABLA_HEADER_BG, color: TABLA_HEADER_FG };

  if (!onSortChange) {
    return (
      <th className={cls} style={style}>
        {label}
      </th>
    );
  }

  return (
    <th className={cls} style={style}>
      <button
        type="button"
        className="inline-flex items-center gap-1 uppercase hover:opacity-80"
        aria-label={`Ordenar por ${sortLabel ?? label}${active ? ` (${sortDir === "asc" ? "ascendente" : "descendente"})` : ""}`}
        onClick={() => onSortChange(apiKey, nextDir)}
      >
        {label}
        <Icon className="h-3 w-3 opacity-60" aria-hidden="true" />
      </button>
    </th>
  );
}

/** Tabla paginada tramites clientes OT — patron CompanyListTable (HU #10220). */
export function ClientProceduresTable({
  rows,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onApprove,
  onReject,
  showApprovalActions = true,
  onConsolidado,
  onAdjuntarLt,
  onAssignPlate,
  onRevoke,
  consolidadoActingId = null,
  onVerDocumentos,
  onVerDetalle,
  sortBy,
  sortDir,
  onSortChange,
}: ClientProceduresTableProps) {
  const col = (key: string) => OT_PROCEDURES_COLUMNS.find((c) => c.key === key)!;

  /**
   * Acciones disponibles para una fila, en el orden en que el operador las necesita: primero
   * decidir, luego la placa, luego consultar. Cada una aparece bajo la MISMA condición con la que
   * se pintaba su botón — el menú cambia dónde viven, no cuándo existen.
   */
  const buildRowActions = (row: OtClientProcedure): ActionsMenuItem[] => {
    const items: ActionsMenuItem[] = [];
    const decidible =
      row.status === "entregado" &&
      puedeDecidirOt(row.plateFlowStatus, row.soatEstado) &&
      showApprovalActions;

    if (decidible) {
      items.push({
        key: "aprobar",
        label: "Aprobar",
        icon: Check,
        onSelect: () => onApprove(row),
      });
      items.push({
        key: "rechazar",
        label: "Rechazar",
        icon: X,
        onSelect: () => onReject(row),
      });
    }

    if (row.plateFlowStatus === "preasignado" && showApprovalActions && onAssignPlate) {
      items.push({
        key: "asignar-placa",
        label: "Asignar placa",
        onSelect: () => onAssignPlate(row),
      });
    }

    if (
      (row.plateFlowStatus === "preasignado" || row.plateFlowStatus === "asignado") &&
      showApprovalActions &&
      onRevoke
    ) {
      items.push({ key: "revocar", label: "Revocar", onSelect: () => onRevoke(row) });
    }

    if (row.status === "aprobado" && onAdjuntarLt) {
      items.push({ key: "adjuntar-lt", label: "Adjuntar LT", onSelect: () => onAdjuntarLt(row) });
    }

    if ((row.status === "entregado" || row.status === "aprobado") && onConsolidado) {
      items.push({
        key: "consolidado",
        label: consolidadoActingId === row.id ? "Abriendo…" : "Ver consolidado",
        // Se deshabilita mientras se abre: el consolidado puede tener que generarse, y un segundo
        // clic dispararía una segunda generación del mismo expediente.
        disabled: consolidadoActingId === row.id,
        disabledReason: "Abriendo el consolidado…",
        onSelect: () => onConsolidado(row),
      });
    }

    if (onVerDocumentos) {
      items.push({
        key: "documentos",
        label: "Ver documentos",
        icon: FolderOpen,
        onSelect: () => onVerDocumentos(row),
      });
    }

    if (onVerDetalle) {
      items.push({
        key: "detalle",
        label: "Detalle del trámite",
        onSelect: () => onVerDetalle(row),
      });
    }

    return items;
  };

  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      <table className="w-full min-w-[1100px] border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr>
            <SortableTh
              label={col("radicado").label}
              columnKey="radicado"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={col("radicado").sortable ? onSortChange : undefined}
              className="rounded-l-xl"
            />
            <SortableTh
              label={col("vin").label}
              columnKey="vin"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <SortableTh
              label={col("placa").label}
              columnKey="placa"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <SortableTh
              label={col("vendedor").label}
              columnKey="vendedor"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <SortableTh
              label={col("comprador").label}
              columnKey="comprador"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <th
              className={TABLA_HEADER_CELL_CLS}
              style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
            >
              {col("tipoTramite").label}
            </th>
            <SortableTh
              label={col("empresaGestor").label}
              sortLabel={col("empresaGestor").sortLabel}
              columnKey="empresaGestor"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <SortableTh
              label={col("estado").label}
              columnKey="estado"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <SortableTh
              label={col("fechaRadicacion").label}
              columnKey="fechaRadicacion"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <th
              className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
              style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
            >
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={row.id}
              className={`bg-card ${TABLA_ROW_HOVER_CLS} ${onVerDetalle ? "cursor-pointer" : ""}`}
              onClick={onVerDetalle ? () => onVerDetalle(row) : undefined}
              // La fila no es un `<button>`: sigue siendo una fila de tabla, así que el teclado
              // necesita su propia puerta. Enter y Espacio hacen lo mismo que el clic, y el menú
              // de acciones sigue siendo el camino accesible de siempre para el resto.
              tabIndex={onVerDetalle ? 0 : undefined}
              role={onVerDetalle ? "button" : undefined}
              aria-label={onVerDetalle ? `Ver el detalle del trámite ${row.referenceNumber}` : undefined}
              onKeyDown={
                onVerDetalle
                  ? (e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        onVerDetalle(row);
                      }
                    }
                  : undefined
              }
            >
              <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold">
                <span className="flex items-center gap-1.5">
                  {row.prioritario && (
                    <Star
                      className="h-3.5 w-3.5 shrink-0"
                      style={{ color: "#F59E0B", fill: "#F59E0B" }}
                      aria-label="Trámite prioritario"
                    />
                  )}
                  {row.referenceNumber}
                </span>
              </td>
              <td className="border-y px-4 py-3 font-mono text-[11px]">
                {row.vin?.trim() || "—"}
              </td>
              <td className="border-y px-4 py-3 font-semibold">
                {row.placa?.trim() || "—"}
              </td>
              <td className="border-y px-4 py-3">
                {row.vendedorNombre?.trim() || "—"}
              </td>
              <td className="border-y px-4 py-3">
                {row.compradorNombre?.trim() || "—"}
              </td>
              <td className="border-y px-4 py-3">
                {row.procedureTypeName ?? row.procedureTypeId}
              </td>
              {/* Empresa arriba, gestor debajo y atenuado: la empresa es la responsable del
                  trámite; el gestor, la persona concreta con la que hablar. */}
              <td className="border-y px-4 py-3">
                <span className="block min-w-0">
                  <span className="block truncate font-semibold">
                    {row.clientTenantName ?? row.clientTenantId}
                  </span>
                  <span className={`block truncate ${TABLA_CELDA_SECUNDARIA_CLS}`}>
                    {row.gestorNombre?.trim() || "—"}
                  </span>
                </span>
              </td>
              <td className="border-y px-4 py-3">
                <div className="flex flex-wrap items-center gap-1.5">
                  <StatusBadge
                    label={formatOtProcedureStatus(row.status)}
                    tone={procedureStatusTone(row.status)}
                  />
                  {plateFlowChipStyle(row.plateFlowStatus) && (
                    <span
                      title="Progreso de la placa (sub-estado interno; el trámite sigue en Entregado)"
                      className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                      style={{
                        background: plateFlowChipStyle(row.plateFlowStatus)!.bg,
                        color: plateFlowChipStyle(row.plateFlowStatus)!.color,
                        border: `1px solid ${plateFlowChipStyle(row.plateFlowStatus)!.border}`,
                      }}
                    >
                      {plateFlowLabel(row.plateFlowStatus)}
                    </span>
                  )}
                </div>
              </td>
              <td className="border-y px-4 py-3 opacity-70">
                {formatOtDate(row.createdAt)}
              </td>
              {/*
                Las ACCIONES van en un menú (`ActionsMenu`, el mismo del listado del gestor). Sueltas
                eran hasta ocho botones condicionales en una celda: la columna crecía o encogía según
                el estado de cada fila y la tabla nunca tenía el mismo ancho dos filas seguidas.

                Lo que NO entra en el menú es lo INFORMATIVO —"Esperando proceso del gestor" y los
                sellos de SOAT/Impuesto—: no son cosas que el operador pueda ejecutar, y escondidas
                tras un clic dejarían de avisar, que es justo para lo que están.
              */}
              {/* Las acciones cortan la propagación: pulsar «Aprobar» abriría además el detalle. */}
              <td
                className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                onClick={(e) => e.stopPropagation()}
                onKeyDown={(e) => e.stopPropagation()}
              >
                <div className="flex items-center justify-end gap-2">
                  {row.status === "entregado" &&
                    esperandoProcesoDelGestor(row.plateFlowStatus) &&
                    showApprovalActions && (
                      <span
                        className="text-[10px] font-medium italic"
                        style={{ color: "#b45309" }}
                        title="El gestor debe procesar el trámite (Asignado → Terminado) antes de que el OT apruebe o rechace."
                      >
                        Esperando proceso del gestor
                      </span>
                    )}
                  {row.plateFlowStatus === "terminado" && (
                    <span className="flex flex-wrap justify-end gap-1">
                      {row.soatPagado && (
                        <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-700">
                          SOAT
                        </span>
                      )}
                      {row.impuestoDepartamentalPagado && (
                        <span className="rounded-full bg-sky-50 px-2 py-0.5 text-[10px] font-semibold text-sky-700">
                          Impuesto
                        </span>
                      )}
                    </span>
                  )}
                  <ActionsMenu
                    ariaLabel={`Acciones del trámite ${row.referenceNumber}`}
                    items={buildRowActions(row)}
                  />
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      </div>
      <OtTablePagination
        totalCount={totalCount}
        page={page}
        pageSize={pageSize}
        onPageChange={onPageChange}
      />
    </div>
  );
}
