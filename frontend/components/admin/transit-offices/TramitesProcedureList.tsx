"use client";

import type { OtClientProcedure } from "@/lib/api/types-ot";

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
    <ul className="space-y-2" aria-label="Lista de trámites">
      {procedures.map((procedure) => (
        <li
          key={procedure.id}
          className="flex flex-wrap items-center justify-between gap-3 rounded-xl border bg-white px-4 py-3"
          style={{ borderColor: "#DFE5ED" }}
        >
          <div>
            <p className="text-sm font-semibold">{procedure.referenceNumber}</p>
            <p className="text-[11px] opacity-60">
              Estado: {procedure.status} · Tipo: {procedure.procedureTypeId.slice(0, 8)}…
            </p>
          </div>
          {showApprovalActions && procedure.status === "entregado" && (
            <div className="flex gap-2">
              <button
                type="button"
                className="rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
                onClick={() => onApprove?.(procedure.id)}
              >
                Aprobar
              </button>
              <button
                type="button"
                className="rounded-lg border px-3 py-1.5 text-xs font-semibold"
                style={{ borderColor: "#DFE5ED", color: "#FF4E00" }}
                onClick={() => onReject?.(procedure.id)}
              >
                Rechazar
              </button>
            </div>
          )}
        </li>
      ))}
    </ul>
  );
}
