"use client";

import { Building2, CalendarDays, Check, FileText, X } from "lucide-react";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { formatOtDate, formatOtProcedureStatus, procedureStatusTone } from "./ot-utils";

export interface TramitesProcedureListProps {
  procedures: OtClientProcedure[];
  showApprovalActions: boolean;
  onApprove?: (id: string) => void;
  onReject?: (id: string) => void;
  /** Genera/regenera el expediente consolidado del trámite (omitir = oculto). */
  onGenerarConsolidado?: (procedure: OtClientProcedure) => void;
  /** Descarga el PDF del consolidado más reciente (omitir = oculto). */
  onVerConsolidado?: (procedure: OtClientProcedure) => void;
  /** Id del trámite con acción de consolidado en curso. */
  consolidadoActingId?: string | null;
}

/** Lista de trámites con acciones condicionales al modo (HU #10218 AC3/AC4). */
export function TramitesProcedureList({
  procedures,
  showApprovalActions,
  onApprove,
  onReject,
  onGenerarConsolidado,
  onVerConsolidado,
  consolidadoActingId = null,
}: TramitesProcedureListProps) {
  return (
    <ul className="space-y-3" aria-label="Lista de trámites">
      {procedures.map((procedure) => {
        const canAct = showApprovalActions && procedure.status === "entregado";
        const hasConsolidadoActions = Boolean(onGenerarConsolidado || onVerConsolidado);
        return (
          <li
            key={procedure.id}
            className="group flex flex-col gap-4 rounded-2xl border bg-card p-4 transition-shadow hover:shadow-[0_2px_12px_rgba(22,39,68,0.08)] sm:flex-row sm:items-center"
          >
            <div
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[#557EFF]/10"
              style={{ color: "#557EFF" }}
              aria-hidden="true"
            >
              <FileText className="h-5 w-5" />
            </div>

            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <p className="truncate text-sm font-semibold text-foreground">
                  {procedure.procedureTypeName ?? procedure.procedureTypeId}
                </p>
                <StatusBadge
                  label={formatOtProcedureStatus(procedure.status)}
                  tone={procedureStatusTone(procedure.status)}
                />
              </div>
              <dl className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-foreground">
                <div className="inline-flex items-center gap-1">
                  <dt className="sr-only">Radicado</dt>
                  <dd
                    className="rounded-md px-1.5 py-0.5 font-mono font-medium tracking-tight bg-muted"
                    style={{ color: "#557EFF" }}
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

            {(canAct || hasConsolidadoActions) && (
              <div
                className="flex flex-wrap shrink-0 gap-2 border-t pt-3 sm:border-t-0 sm:pt-0"
              >
                {canAct && (
                  <>
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
                  </>
                )}
                {onGenerarConsolidado && (
                  <button
                    type="button"
                    className="rounded-lg border px-3 py-1.5 text-xs font-semibold text-foreground disabled:opacity-50"
                    disabled={consolidadoActingId === procedure.id}
                    onClick={() => onGenerarConsolidado(procedure)}
                  >
                    Generar consolidado
                  </button>
                )}
                {onVerConsolidado && (
                  <button
                    type="button"
                    className="rounded-lg border px-3 py-1.5 text-xs font-semibold text-foreground disabled:opacity-50"
                    disabled={consolidadoActingId === procedure.id}
                    onClick={() => onVerConsolidado(procedure)}
                  >
                    Ver consolidado
                  </button>
                )}
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
