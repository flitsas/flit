"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  approveOtClientProcedure,
  fetchOtClientProcedures,
  rejectOtClientProcedure,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { formatOtProcedureStatus } from "./ot-utils";

const PROCEDURE_TYPE_FILTER = "matricula_inicial";

/** Vista tenant admin — trámites de clientes OT (HU #10220). */
export function ClientProceduresSection() {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<OtClientProcedure[]>([]);
  const [statusFilter, setStatusFilter] = useState("pending_ot");
  const [typeFilter, setTypeFilter] = useState("");
  const [approveTarget, setApproveTarget] = useState<OtClientProcedure | null>(null);
  const [rejectTarget, setRejectTarget] = useState<OtClientProcedure | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [acting, setActing] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchOtClientProcedures(
          {
            status: statusFilter || undefined,
            procedureTypeId: typeFilter || undefined,
            page: 1,
            pageSize: 50,
          },
          signal,
        );
        if (signal?.aborted) return;
        setRows(result.data);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [statusFilter, typeFilter],
  );

  useEffect(() => {
    const c = new AbortController();
    void load(c.signal);
    return () => c.abort();
  }, [load]);

  const confirmApprove = async () => {
    if (!approveTarget) return;
    setActing(true);
    try {
      const updated = await approveOtClientProcedure(approveTarget.id);
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setApproveTarget(null);
      show("Trámite aprobado.", "success");
    } catch {
      show("No se pudo aprobar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmReject = async () => {
    if (!rejectTarget || !rejectReason.trim()) return;
    setActing(true);
    try {
      const updated = await rejectOtClientProcedure(rejectTarget.id, {
        reason: rejectReason.trim(),
      });
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setRejectTarget(null);
      setRejectReason("");
      show("Trámite rechazado.", "success");
    } catch {
      show("No se pudo rechazar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2">
        <select
          aria-label="Filtrar por estado"
          className="rounded-lg border px-2 py-1 text-xs"
          style={{ borderColor: "#DFE5ED" }}
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
        >
          <option value="pending_ot">Pendiente OT</option>
          <option value="approved_ot">Aprobado OT</option>
          <option value="rejected_ot">Rechazado OT</option>
          <option value="">Todos</option>
        </select>
        <label className="flex items-center gap-2 text-xs">
          <input
            type="checkbox"
            checked={typeFilter === PROCEDURE_TYPE_FILTER}
            onChange={(e) =>
              setTypeFilter(e.target.checked ? PROCEDURE_TYPE_FILTER : "")
            }
          />
          Tipo: Matrícula inicial
        </label>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay trámites pendientes de tus clientes."
        errorMessage="Error al cargar trámites de clientes."
        onRetry={() => void load()}
        skeletonRows={5}
      >
        <div className="overflow-x-auto rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b" style={{ borderColor: "#DFE5ED" }}>
                <th className="px-3 py-2">Radicado</th>
                <th className="px-3 py-2">Tipo trámite</th>
                <th className="px-3 py-2">Cliente</th>
                <th className="px-3 py-2">Estado</th>
                <th className="px-3 py-2">Fecha</th>
                <th className="px-3 py-2">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id} className="border-b" style={{ borderColor: "#DFE5ED" }}>
                  <td className="px-3 py-2 font-semibold">{row.referenceNumber}</td>
                  <td className="px-3 py-2">{row.procedureTypeId.slice(0, 8)}…</td>
                  <td className="px-3 py-2">{row.clientTenantId.slice(0, 8)}…</td>
                  <td className="px-3 py-2">{formatOtProcedureStatus(row.status)}</td>
                  <td className="px-3 py-2">{new Date(row.createdAt).toLocaleDateString()}</td>
                  <td className="px-3 py-2">
                    {row.status === "pending_ot" && (
                      <div className="flex gap-2">
                        <button
                          type="button"
                          className="font-semibold text-[#557EFF]"
                          onClick={() => setApproveTarget(row)}
                        >
                          Aprobar
                        </button>
                        <button
                          type="button"
                          className="font-semibold text-[#FF4E00]"
                          onClick={() => setRejectTarget(row)}
                        >
                          Rechazar
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {approveTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" role="dialog" aria-label="Confirmar aprobación">
          <div className="w-full max-w-sm rounded-2xl bg-white p-6" style={{ border: "1px solid #DFE5ED" }}>
            <p className="text-sm font-semibold mb-4">¿Aprobar este trámite?</p>
            <p className="text-xs opacity-70 mb-4">{approveTarget.referenceNumber}</p>
            <div className="flex gap-2 justify-end">
              <button type="button" className="text-xs" onClick={() => setApproveTarget(null)} disabled={acting}>
                Cancelar
              </button>
              <button
                type="button"
                className="rounded-lg px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
                disabled={acting}
                onClick={() => void confirmApprove()}
              >
                Confirmar
              </button>
            </div>
          </div>
        </div>
      )}

      {rejectTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" role="dialog" aria-label="Rechazar trámite">
          <div className="w-full max-w-sm rounded-2xl bg-white p-6" style={{ border: "1px solid #DFE5ED" }}>
            <p className="text-sm font-semibold mb-2">Motivo del rechazo</p>
            <textarea
              className="w-full rounded-lg border p-2 text-xs mb-4"
              style={{ borderColor: "#DFE5ED" }}
              rows={3}
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
            />
            <div className="flex gap-2 justify-end">
              <button type="button" className="text-xs" onClick={() => setRejectTarget(null)} disabled={acting}>
                Cancelar
              </button>
              <button
                type="button"
                className="rounded-lg px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: "#FF4E00" }}
                disabled={acting || !rejectReason.trim()}
                onClick={() => void confirmReject()}
              >
                Confirmar rechazo
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
