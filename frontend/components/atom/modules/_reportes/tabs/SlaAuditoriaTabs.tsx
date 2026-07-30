"use client";

import { useEffect, useState } from "react";
import {
  fetchProcedureAudit,
  fetchReportingProcedures,
  fetchSla,
  type ReportingAudit,
  type SlaPage,
} from "@/lib/api/reporting-v2";
import { usePermissions } from "@/hooks/usePermissions";
import { HistoryUnavailableBadge } from "../HistoryUnavailableBadge";
import { CompanyNotice } from "../CompanyNotice";

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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo
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
  if (!data) return <div className="text-sm opacity-60">Sin datos para el período seleccionado</div>;

  const slaConfigured = data.slaConfigured !== false;
  const showCompliance = slaConfigured;

  return (
    <div className="space-y-3">
      {!slaConfigured && (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900"
          role="status"
          data-testid="sla-not-configured-banner"
        >
          Sin configuración de SLA. Configure los objetivos en Ajustes.
        </div>
      )}
      {data.items.length === 0 ? (
        <div className="text-sm opacity-60">Sin datos para el período seleccionado</div>
      ) : (
        <table className="min-w-full text-left text-xs">
          <thead>
            <tr className="border-b">
              <th className="py-2">Tipo</th>
              <th>OT</th>
              {showCompliance && <th>SLA h</th>}
              <th>Total</th>
              {showCompliance && <th>Dentro</th>}
              {showCompliance && <th>Fuera</th>}
              <th>Prom. h</th>
              {showCompliance && <th>% Cumpl.</th>}
            </tr>
          </thead>
          <tbody>
            {data.items.map((row, idx) => {
              const ok = row.compliancePct >= 80;
              return (
                <tr
                  key={`${row.procedureType}-${idx}`}
                  className={`border-b ${showCompliance ? (ok ? "bg-emerald-50/60" : "bg-red-50/60") : ""}`}
                  data-testid={`sla-row-${idx}`}
                  data-compliance={showCompliance ? (ok ? "within" : "outside") : "na"}
                >
                  <td className="py-2">{row.procedureType}</td>
                  <td>{row.transitOfficeName ?? "—"}</td>
                  {showCompliance && <td>{row.slaHours}</td>}
                  <td>{row.total}</td>
                  {showCompliance && <td>{row.withinSla}</td>}
                  {showCompliance && <td>{row.outsideSla}</td>}
                  <td>{row.avgBusinessHours?.toFixed(1) ?? "—"}</td>
                  {showCompliance && (
                    <td className={ok ? "font-semibold text-emerald-700" : "font-semibold text-red-700"}>
                      {row.compliancePct.toFixed(1)}%
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
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
  const { permissions, isSuperAdmin } = usePermissions();
  const canAudit = isSuperAdmin || permissions.includes("reporting.audit");
  const needsCompany = isSuperAdmin && !tenantId;

  const [procedureId, setProcedureId] = useState<string>("");
  const [audit, setAudit] = useState<ReportingAudit | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!canAudit || needsCompany) return;
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
  }, [from, to, tenantId, procedureId, canAudit, needsCompany]);

  useEffect(() => {
    if (!canAudit || needsCompany || !procedureId) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo
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
  }, [procedureId, tenantId, canAudit, needsCompany]);

  if (!canAudit) {
    return (
      <div className="rounded-xl border p-4 text-sm" role="status" data-testid="auditoria-sin-permiso">
        No tienes permiso para ver el historial de auditoría
      </div>
    );
  }

  if (needsCompany) {
    return (
      <div data-testid="auditoria-selector-empresa">
        <CompanyNotice />
        <p className="mt-2 text-xs opacity-70">
          Selecciona una empresa en los filtros globales para consultar auditoría.
        </p>
      </div>
    );
  }

  const historyUnavailable = audit !== null && audit.historyAvailable === false;

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
      {historyUnavailable && <HistoryUnavailableBadge />}
      {audit && audit.historyAvailable && audit.entries.length === 0 && (
        <div className="text-sm opacity-60">Sin eventos de auditoría</div>
      )}
      {audit && audit.historyAvailable && audit.entries.length > 0 && (
        <table className="min-w-full text-left text-xs">
          <thead>
            <tr className="border-b">
              <th className="py-2">Fecha</th>
              <th>Usuario</th>
              <th>Rol</th>
              <th>Organización</th>
              <th>De</th>
              <th>A</th>
              <th>Obs.</th>
            </tr>
          </thead>
          <tbody>
            {audit.entries.map((e, idx) => (
              <tr key={`${e.changedAt}-${idx}`} className="border-b">
                <td className="py-2">{new Date(e.changedAt).toLocaleString()}</td>
                <td>{e.changedByDisplayName ?? "—"}</td>
                <td>{e.roleIdAtTime ?? "—"}</td>
                <td>
                  {[e.organizationTypeAtTime, e.organizationIdAtTime].filter(Boolean).join(" · ") || "—"}
                </td>
                <td>{e.fromStatus ?? "—"}</td>
                <td>{e.toStatus ?? "—"}</td>
                <td>{e.reason ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
