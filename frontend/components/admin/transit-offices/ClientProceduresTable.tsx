"use client";

import { Check, X } from "lucide-react";
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
              >
                {row.referenceNumber}
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
              <td
                className="rounded-r-xl border-y border-r px-4 py-3 text-right"
              >
                {row.status === "entregado" && showApprovalActions && (
                  <RowActions
                    actions={[
                      {
                        icon: Check,
                        label: `Aprobar trámite ${row.referenceNumber}`,
                        onClick: () => onApprove(row),
                        tone: "primary",
                      },
                      {
                        icon: X,
                        label: `Rechazar trámite ${row.referenceNumber}`,
                        onClick: () => onReject(row),
                        tone: "danger",
                      },
                    ]}
                  />
                )}
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
