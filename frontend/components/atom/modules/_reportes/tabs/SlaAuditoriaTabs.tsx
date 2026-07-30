"use client";

import { useEffect, useState } from "react";
import {
  fetchProcedureAudit,
  fetchReportingProcedures,
  fetchSla,
  type ReportingAudit,
  type SlaPage,
} from "@/lib/api/reporting-v2";

export function SlaTab({
  from,
  to,
  tenantId,
}: {
  from: string;
  to: string;
  tenantId?: string;
}) {
  const [data, setData] = useState<SlaPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo: skeleton inmediato antes del fetch
    setLoading(true);
    fetchSla({ from, to, tenantId })
      .then((page) => {
        if (!cancelled) setData(page);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : "Error");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [from, to, tenantId]);

  if (loading) return <div className="text-sm opacity-70">Cargando SLA…</div>;
  if (error) return <div className="text-sm text-red-600">{error}</div>;
  if (!data || data.items.length === 0) {
    return <div className="text-sm opacity-60">Sin datos para el período seleccionado</div>;
  }

  return (
    <table className="min-w-full text-left text-xs">
      <thead>
        <tr className="border-b">
          <th className="py-2">Tipo</th>
          <th>OT</th>
          <th>SLA h</th>
          <th>Total</th>
          <th>Dentro</th>
          <th>Fuera</th>
          <th>% Cumpl.</th>
        </tr>
      </thead>
      <tbody>
        {data.items.map((row, idx) => (
          <tr key={`${row.procedureType}-${idx}`} className="border-b">
            <td className="py-2">{row.procedureType}</td>
            <td>{row.transitOfficeName ?? "—"}</td>
            <td>{row.slaHours}</td>
            <td>{row.total}</td>
            <td>{row.withinSla}</td>
            <td>{row.outsideSla}</td>
            <td>{row.compliancePct.toFixed(1)}%</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function AuditoriaTab({
  from,
  to,
  tenantId,
}: {
  from: string;
  to: string;
  tenantId?: string;
}) {
  const [procedureId, setProcedureId] = useState<string>("");
  const [audit, setAudit] = useState<ReportingAudit | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetchReportingProcedures({ from, to, tenantId, page: 1, pageSize: 1 })
      .then((page) => {
        if (!cancelled && page.items[0] && !procedureId) {
          setProcedureId(page.items[0].id);
        }
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [from, to, tenantId, procedureId]);

  useEffect(() => {
    if (!procedureId) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo: skeleton inmediato antes del fetch
    setLoading(true);
    setError(null);
    fetchProcedureAudit(procedureId, tenantId)
      .then((result) => {
        if (!cancelled) setAudit(result);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : "Error");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [procedureId, tenantId]);

  return (
    <div className="space-y-3">
      <label className="flex flex-col gap-1 text-xs">
        ID trámite
        <input
          className="rounded border px-2 py-1 font-mono"
          value={procedureId}
          onChange={(e) => setProcedureId(e.target.value)}
          aria-label="ID del trámite para auditoría"
        />
      </label>
      {loading && <div className="text-sm opacity-70">Cargando auditoría…</div>}
      {error && <div className="text-sm text-red-600">{error}</div>}
      {audit && !audit.historyAvailable && (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
          role="status"
        >
          Historial no disponible
        </div>
      )}
      {audit && audit.entries.length === 0 && (
        <div className="text-sm opacity-60">Sin eventos de auditoría</div>
      )}
      {audit && audit.entries.length > 0 && (
        <table className="min-w-full text-left text-xs">
          <thead>
            <tr className="border-b">
              <th className="py-2">Fecha</th>
              <th>Usuario</th>
              <th>De</th>
              <th>A</th>
              <th>Obs.</th>
              <th>Hist.</th>
            </tr>
          </thead>
          <tbody>
            {audit.entries.map((e, idx) => (
              <tr key={`${e.changedAt}-${idx}`} className="border-b">
                <td className="py-2">{new Date(e.changedAt).toLocaleString()}</td>
                <td>{e.changedByDisplayName ?? "—"}</td>
                <td>{e.fromStatus ?? "—"}</td>
                <td>{e.toStatus ?? "—"}</td>
                <td>{e.reason ?? "—"}</td>
                <td>{e.historyAvailable ? "OK" : "N/D"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
