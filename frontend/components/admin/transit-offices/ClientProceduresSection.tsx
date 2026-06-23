"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { tramitesClient } from "@/lib/api/tramites-client";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import {
  approveOtClientProcedure,
  fetchOtClientProcedures,
  rejectOtClientProcedure,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { ClientProceduresTable } from "./ClientProceduresTable";
import { OT_FILTER_FORM_CLS, OT_INPUT_CLS } from "./ot-form-styles";

const PAGE_SIZE = 20;

/** Vista tenant admin — trámites de clientes OT (HU #10220). */
export function ClientProceduresSection() {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<OtClientProcedure[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState("pending_ot");
  const [typeFilter, setTypeFilter] = useState("");
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeSummary[]>([]);
  const [approveTarget, setApproveTarget] = useState<OtClientProcedure | null>(null);
  const [rejectTarget, setRejectTarget] = useState<OtClientProcedure | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [acting, setActing] = useState(false);

  useEffect(() => {
    tramitesClient
      .listPublishedProcedureTypes()
      .then(setProcedureTypes)
      .catch(() => setProcedureTypes([]));
  }, []);

  const load = useCallback(
    async (signal?: AbortSignal, targetPage = page) => {
      setStatus("loading");
      try {
        const result = await fetchOtClientProcedures(
          {
            status: statusFilter || undefined,
            procedureTypeId: typeFilter || undefined,
            page: targetPage,
            pageSize: PAGE_SIZE,
          },
          signal,
        );
        if (signal?.aborted) return;
        setRows(result.data);
        setTotalCount(result.totalCount);
        setPage(result.page);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [statusFilter, typeFilter, page],
  );

  useEffect(() => {
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(c.signal, page);
    return () => c.abort();
  }, [load, page]);

  const applyFilters = () => {
    setPage(1);
    void load(undefined, 1);
  };

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
      <form
        className={OT_FILTER_FORM_CLS}
        style={{ borderColor: "#DFE5ED" }}
        onSubmit={(e) => {
          e.preventDefault();
          applyFilters();
        }}
        aria-label="Filtros de trámites de clientes"
      >
        <label className="text-xs font-semibold" style={{ color: "#162744" }}>
          Estado
          <select
            aria-label="Filtrar por estado"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
          >
            <option value="pending_ot">Pendiente OT</option>
            <option value="approved_ot">Aprobado OT</option>
            <option value="rejected_ot">Rechazado OT</option>
            <option value="">Todos</option>
          </select>
        </label>
        <label className="text-xs font-semibold" style={{ color: "#162744" }}>
          Tipo de trámite
          <select
            aria-label="Filtrar por tipo de trámite"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
          >
            <option value="">Todos</option>
            {procedureTypes.map((pt) => (
              <option key={pt.id} value={pt.id}>
                {pt.name}
              </option>
            ))}
          </select>
        </label>
        <div className="flex items-end">
          <button
            type="submit"
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            Aplicar filtros
          </button>
        </div>
      </form>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay trámites pendientes de tus clientes."
        errorMessage="Error al cargar trámites de clientes."
        onRetry={() => void load()}
        skeletonRows={5}
      >
        <ClientProceduresTable
          rows={rows}
          totalCount={totalCount}
          page={page}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
          onApprove={setApproveTarget}
          onReject={setRejectTarget}
        />
      </UiStateBoundary>

      {approveTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Confirmar aprobación"
        >
          <div
            className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]"
            style={{ border: "1px solid #DFE5ED" }}
          >
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>
              ¿Aprobar este trámite?
            </h2>
            <p className="mt-2 text-sm opacity-80">{approveTarget.referenceNumber}</p>
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                style={{ borderColor: "#DFE5ED" }}
                onClick={() => setApproveTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting}
                onClick={() => void confirmApprove()}
              >
                {acting ? "Procesando…" : "Confirmar"}
              </button>
            </div>
          </div>
        </div>
      )}

      {rejectTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Rechazar trámite"
        >
          <div
            className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]"
            style={{ border: "1px solid #DFE5ED" }}
          >
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>
              Motivo del rechazo
            </h2>
            <textarea
              className={`mt-3 ${OT_INPUT_CLS}`}
              rows={3}
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
            />
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                style={{ borderColor: "#DFE5ED" }}
                onClick={() => setRejectTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
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
