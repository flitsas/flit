"use client";

import { OtStatusBadge } from "./OtStatusBadge";
import { OtTablePagination } from "./OtTablePagination";
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
  /** Genera/regenera el expediente consolidado (omitir = acción oculta, p. ej. QX read-only). */
  onGenerarConsolidado?: (row: OtClientProcedure) => void;
  /** Descarga el PDF del consolidado más reciente. */
  onVerConsolidado?: (row: OtClientProcedure) => void;
  /** Adjunta la Licencia de Tránsito a un trámite ya aprobado (solo OT admin). */
  onAdjuntarLt?: (row: OtClientProcedure) => void;
  /** Id de la fila con acción de consolidado en curso (deshabilita sus botones). */
  consolidadoActingId?: string | null;
}

/** Tabla paginada trámites clientes OT — patrón CompanyListTable (HU #10220). */
export function ClientProceduresTable({
  rows,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onApprove,
  onReject,
  showApprovalActions = true,
  onGenerarConsolidado,
  onVerConsolidado,
  onAdjuntarLt,
  consolidadoActingId = null,
}: ClientProceduresTableProps) {
  return (
    <div className="flex flex-1 flex-col">
      <table className="w-full border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
            <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Radicado
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Tipo trámite
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Empresa cliente
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Estado
            </th>
            <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
              Fecha radicación
            </th>
            <th className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className="bg-white dark:bg-[#0B0F14]">
              <td
                className="rounded-l-xl border-y border-l px-4 py-3 font-semibold"
                style={{ borderColor: "#DFE5ED" }}
              >
                {row.referenceNumber}
              </td>
              <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                {row.procedureTypeName ?? row.procedureTypeId}
              </td>
              <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                {row.clientTenantName ?? row.clientTenantId}
              </td>
              <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                <OtStatusBadge
                  label={formatOtProcedureStatus(row.status)}
                  tone={procedureStatusTone(row.status)}
                />
              </td>
              <td className="border-y px-4 py-3 opacity-70" style={{ borderColor: "#DFE5ED" }}>
                {formatOtDate(row.createdAt)}
              </td>
              <td
                className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                style={{ borderColor: "#DFE5ED" }}
              >
                <div className="flex items-center justify-end gap-2">
                  {row.status === "entregado" && showApprovalActions && (
                    <>
                      <button
                        type="button"
                        className="rounded-lg px-2.5 py-1 text-[10px] font-semibold text-white"
                        style={{ background: "#557EFF" }}
                        onClick={() => onApprove(row)}
                      >
                        Aprobar
                      </button>
                      <button
                        type="button"
                        className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold"
                        style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
                        onClick={() => onReject(row)}
                      >
                        Rechazar
                      </button>
                    </>
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
                  {(row.status === "entregado" || row.status === "aprobado") && (
                    <>
                      {onGenerarConsolidado && (
                        <button
                          type="button"
                          className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold disabled:opacity-50"
                          style={{ borderColor: "#DFE5ED", color: "#162744" }}
                          disabled={consolidadoActingId === row.id}
                          onClick={() => onGenerarConsolidado(row)}
                        >
                          Generar consolidado
                        </button>
                      )}
                      {onVerConsolidado && (
                        <button
                          type="button"
                          className="rounded-lg border px-2.5 py-1 text-[10px] font-semibold disabled:opacity-50"
                          style={{ borderColor: "#DFE5ED", color: "#162744" }}
                          disabled={consolidadoActingId === row.id}
                          onClick={() => onVerConsolidado(row)}
                        >
                          Ver consolidado
                        </button>
                      )}
                    </>
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
