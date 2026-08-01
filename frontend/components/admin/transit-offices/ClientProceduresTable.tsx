"use client";

import { ArrowDown, ArrowUp, ArrowUpDown, Check, FolderOpen, Star, X } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { OtTablePagination } from "./OtTablePagination";
import { RowActions } from "@/components/atom/RowActions";
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
  /** Abre el panel lateral con el detalle del trámite. */
  onVerDetalle?: (row: OtClientProcedure) => void;
  /** sortBy actual del API (vin, placa, vendedor, …). */
  sortBy?: string;
  sortDir?: "asc" | "desc";
  onSortChange?: (sortBy: string, sortDir: "asc" | "desc") => void;
}

function SortableTh({
  label,
  columnKey,
  sortBy,
  sortDir,
  onSortChange,
  className = "",
}: {
  label: string;
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

  if (!onSortChange) {
    return <th className={`px-4 py-2.5 bg-muted ${className}`.trim()}>{label}</th>;
  }

  return (
    <th className={`px-4 py-2.5 bg-muted ${className}`.trim()}>
      <button
        type="button"
        className="inline-flex items-center gap-1 uppercase hover:opacity-80"
        aria-label={`Ordenar por ${label}${active ? ` (${sortDir === "asc" ? "ascendente" : "descendente"})` : ""}`}
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

  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      <table className="w-full min-w-[1100px] border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase text-foreground">
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
            <SortableTh
              label={col("gestor").label}
              columnKey="gestor"
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
            <th className="px-4 py-2.5 bg-muted">{col("tipoTramite").label}</th>
            <th className="px-4 py-2.5 bg-muted">{col("empresaCliente").label}</th>
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
            <th className="rounded-r-xl px-4 py-2.5 text-right bg-muted">
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className="bg-card">
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
                {row.gestorNombre?.trim() || "—"}
              </td>
              <td className="border-y px-4 py-3">
                {row.procedureTypeName ?? row.procedureTypeId}
              </td>
              <td className="border-y px-4 py-3">
                {row.clientTenantName ?? row.clientTenantId}
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
              <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                <div className="flex items-center justify-end gap-2">
                  {onVerDocumentos && (
                    <button
                      type="button"
                      className="rounded-lg border p-1.5"
                      style={{ color: "#557EFF" }}
                      aria-label={`Ver documentos del trámite ${row.referenceNumber}`}
                      title="Ver documentos del expediente"
                      onClick={() => onVerDocumentos(row)}
                    >
                      <FolderOpen className="h-3.5 w-3.5" aria-hidden="true" />
                    </button>
                  )}
                  {row.status === "entregado" &&
                    puedeDecidirOt(row.plateFlowStatus, row.soatEstado) &&
                    showApprovalActions && (
                    <RowActions
                      actions={[
                        {
                          icon: Check,
                          label: `Aprobar tramite ${row.referenceNumber}`,
                          onClick: () => onApprove(row),
                          tone: "primary",
                        },
                        {
                          icon: X,
                          label: `Rechazar tramite ${row.referenceNumber}`,
                          onClick: () => onReject(row),
                          tone: "danger",
                        },
                      ]}
                    />
                  )}
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
                  {row.plateFlowStatus === "preasignado" && showApprovalActions && onAssignPlate && (
                    <button
                      type="button"
                      className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold"
                      style={{ borderColor: "#557EFF", color: "#557EFF" }}
                      onClick={() => onAssignPlate(row)}
                    >
                      Asignar placa
                    </button>
                  )}
                  {(row.plateFlowStatus === "preasignado" || row.plateFlowStatus === "asignado") &&
                    showApprovalActions &&
                    onRevoke && (
                      <button
                        type="button"
                        className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold"
                        style={{ borderColor: "#fca5a5", color: "#b91c1c" }}
                        onClick={() => onRevoke(row)}
                      >
                        Revocar
                      </button>
                    )}
                  {row.status === "aprobado" && onAdjuntarLt && (
                    <button
                      type="button"
                      className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold"
                      style={{ borderColor: "#557EFF", color: "#557EFF" }}
                      onClick={() => onAdjuntarLt(row)}
                    >
                      Adjuntar LT
                    </button>
                  )}
                  {(row.status === "entregado" || row.status === "aprobado") && onConsolidado && (
                    <button
                      type="button"
                      className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold disabled:opacity-50 text-foreground"
                      disabled={consolidadoActingId === row.id}
                      title="Muestra el consolidado del expediente; lo genera si aún no está o si cambió el trámite"
                      onClick={() => onConsolidado(row)}
                    >
                      {consolidadoActingId === row.id ? "Abriendo…" : "Ver consolidado"}
                    </button>
                  )}
                  {onVerDetalle && (
                    <button
                      type="button"
                      className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold text-foreground"
                      aria-label={`Ver detalle del trámite ${row.referenceNumber}`}
                      title="Ver detalle del trámite"
                      onClick={() => onVerDetalle(row)}
                    >
                      Detalle
                    </button>
                  )}
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
