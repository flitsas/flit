"use client";

import { Check, FolderOpen, Star, X } from "lucide-react";
import { OtStatusBadge } from "./OtStatusBadge";
import { OtTablePagination } from "./OtTablePagination";
import { RowActions } from "@/components/atom/RowActions";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { formatOtDate, formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";

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
}

/** Tabla paginada tramites clientes OT ? patron CompanyListTable (HU #10220). */
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
}: ClientProceduresTableProps) {
  return (
    <div className="flex flex-1 flex-col">
      <table className="w-full border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase text-foreground">
            <th className="rounded-l-xl px-4 py-2.5 bg-muted">
              Radicado
            </th>
            <th className="px-4 py-2.5 bg-muted">
              Tipo tramite
            </th>
            <th className="px-4 py-2.5 bg-muted">
              Empresa cliente
            </th>
            <th className="px-4 py-2.5 bg-muted">
              Estado
            </th>
            <th className="px-4 py-2.5 bg-muted">
              Fecha radicacion
            </th>
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
                  {/* HU #10536 — distintivo de prioridad (solo lectura para el OT). */}
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
              <td className="border-y px-4 py-3">
                {row.procedureTypeName ?? row.procedureTypeId}
              </td>
              <td className="border-y px-4 py-3">
                {row.clientTenantName ?? row.clientTenantId}
              </td>
              <td className="border-y px-4 py-3">
                <OtStatusBadge
                  label={formatOtProcedureStatus(row.status)}
                  tone={procedureStatusTone(row.status)}
                />
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
                  {(row.status === "entregado" || row.status === "asignado") && showApprovalActions && (
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
                  {/* Feature #10587 — ruta de placa: asignar (preasignado) y revocar (preasignado/asignado). */}
                  {row.status === "preasignado" && showApprovalActions && onAssignPlate && (
                    <button
                      type="button"
                      className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold"
                      style={{ borderColor: "#557EFF", color: "#557EFF" }}
                      onClick={() => onAssignPlate(row)}
                    >
                      Asignar placa
                    </button>
                  )}
                  {(row.status === "preasignado" || row.status === "asignado") &&
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
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <OtTablePagination
        totalCount={totalCount}
        page={page}
        pageSize={pageSize}
        onPageChange={onPageChange}
      />
    </div>
  );
}
