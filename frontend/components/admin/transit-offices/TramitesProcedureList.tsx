"use client";

import { Building2, CalendarDays, Check, FileText, X } from "lucide-react";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { OtStatusBadge } from "./OtStatusBadge";
import { formatOtDate, formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";

export interface TramitesProcedureListProps {
  procedures: OtClientProcedure[];
  showApprovalActions: boolean;
  onApprove?: (id: string) => void;
  onReject?: (id: string) => void;
}

/** Lista de trámites con acciones condicionales al modo (HU #10218 AC3/AC4). */
export function TramitesProcedureList({
  procedures,
  showApprovalActions,
  onApprove,
  onReject,
}: TramitesProcedureListProps) {
  return (
    <ul className="space-y-3" aria-label="Lista de trámites">
      {procedures.map((procedure) => {
        const canAct = showApprovalActions && procedure.status === "entregado";
        return (
          <li
            key={procedure.id}
            className="group flex flex-col gap-4 rounded-2xl border bg-white p-4 transition-shadow hover:shadow-[0_2px_12px_rgba(22,39,68,0.08)] sm:flex-row sm:items-center dark:bg-[#0B0F14]"
            style={{ borderColor: "#DFE5ED" }}
          >
            <div
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl"
              style={{ background: "#EEF5FF", color: "#557EFF" }}
              aria-hidden="true"
            >
              <FileText className="h-5 w-5" />
            </div>

            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <p className="truncate text-sm font-semibold" style={{ color: "#162744" }}>
                  {procedure.procedureTypeName ?? procedure.procedureTypeId}
                </p>
                <OtStatusBadge
                  label={formatOtProcedureStatus(procedure.status)}
                  tone={procedureStatusTone(procedure.status)}
                />
              </div>
              <dl className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px]" style={{ color: "#162744" }}>
                <div className="inline-flex items-center gap-1">
                  <dt className="sr-only">Radicado</dt>
                  <dd
                    className="rounded-md px-1.5 py-0.5 font-mono font-medium tracking-tight"
                    style={{ background: "#F4F7FC", color: "#557EFF" }}
                  >
                    {procedure.referenceNumber}
                  </dd>
                </div>
                <div className="inline-flex items-center gap-1 opacity-70">
                  <Building2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <dt className="sr-only">Empresa cliente</dt>
                  <dd className="truncate">{procedure.clientTenantName ?? procedure.clientTenantId}</dd>
                </div>
                <div className="inline-flex items-center gap-1 opacity-70">
                  <CalendarDays className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <dt className="sr-only">Fecha de radicación</dt>
                  <dd>{formatOtDate(procedure.createdAt)}</dd>
                </div>
              </dl>
            </div>

            {canAct && (
              <div className="flex shrink-0 gap-2 border-t pt-3 sm:border-t-0 sm:pt-0" style={{ borderColor: "#DFE5ED" }}>
                <button
                  type="button"
                  className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-xs font-semibold transition-colors hover:bg-[#FFF4EC]"
                  style={{ borderColor: "#FFD9C7", color: "#FF4E00" }}
                  onClick={() => onReject?.(procedure.id)}
                >
                  <X className="h-4 w-4" aria-hidden="true" />
                  Rechazar
                </button>
                <button
                  type="button"
                  className="inline-flex items-center gap-1.5 rounded-xl px-3.5 py-2 text-xs font-semibold text-white shadow-sm transition-colors hover:brightness-95"
                  style={{ background: "#557EFF" }}
                  onClick={() => onApprove?.(procedure.id)}
                >
                  <Check className="h-4 w-4" aria-hidden="true" />
                  Aprobar
                </button>
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
