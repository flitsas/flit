"use client";

import { useEffect, useState } from "react";
import { fetchConsolidado, fetchProductivity, type ConsolidadoPage, type ProductivityPage } from "@/lib/api/reporting-v2";

export function ConsolidadoTab({
  from,
  to,
  tenantId,
}: {
  from: string;
  to: string;
  tenantId?: string;
}) {
  const [groupBy, setGroupBy] = useState("estado");
  const [data, setData] = useState<ConsolidadoPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo: skeleton inmediato antes del fetch
    setLoading(true);
    fetchConsolidado({ from, to, groupBy, tenantId })
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
  }, [from, to, groupBy, tenantId]);

  return (
    <div className="space-y-3">
      <label className="flex items-center gap-2 text-xs">
        Agrupar por
        <select
          className="rounded border px-2 py-1"
          value={groupBy}
          onChange={(e) => setGroupBy(e.target.value)}
        >
          <option value="estado">Estado</option>
          <option value="ot">Organismo</option>
          <option value="empresa">Empresa</option>
          <option value="tipo">Tipo trámite</option>
          <option value="gestor">Gestor</option>
          <option value="mes">Mes</option>
        </select>
      </label>
      {loading && <div className="text-sm opacity-70">Cargando consolidado…</div>}
      {error && <div className="text-sm text-red-600">{error}</div>}
      {!loading && !error && (!data || data.items.length === 0) && (
        <div className="text-sm opacity-60">Sin datos para el período seleccionado</div>
      )}
      {data && data.items.length > 0 && (
        <table className="min-w-full text-left text-xs">
          <thead>
            <tr className="border-b">
              <th className="py-2">Grupo</th>
              <th>Total</th>
              <th>Aprobados</th>
              <th>Rechazados</th>
              <th>En proceso</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((row) => (
              <tr key={row.key} className="border-b/50 border-b">
                <td className="py-2">{row.label}</td>
                <td>{row.total}</td>
                <td>{row.approved}</td>
                <td>{row.rejected}</td>
                <td>{row.inProgress}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

export function ProductividadV2Tab({
  from,
  to,
  tenantId,
}: {
  from: string;
  to: string;
  tenantId?: string;
}) {
  const [dimension, setDimension] = useState("usuario");
  const [data, setData] = useState<ProductivityPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- patrón de carga del repo: skeleton inmediato antes del fetch
    setLoading(true);
    fetchProductivity({ from, to, dimension, tenantId })
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
  }, [from, to, dimension, tenantId]);

  return (
    <div className="space-y-3">
      <label className="flex items-center gap-2 text-xs">
        Dimensión
        <select
          className="rounded border px-2 py-1"
          value={dimension}
          onChange={(e) => setDimension(e.target.value)}
        >
          <option value="usuario">Usuario</option>
          <option value="gestor">Gestor</option>
          <option value="ot">OT</option>
          <option value="empresa">Empresa</option>
        </select>
      </label>
      {loading && <div className="text-sm opacity-70">Cargando productividad…</div>}
      {error && <div className="text-sm text-red-600">{error}</div>}
      {!loading && !error && (!data || data.items.length === 0) && (
        <div className="text-sm opacity-60">Sin datos para el período seleccionado</div>
      )}
      {data && data.items.length > 0 && (
        <table className="min-w-full text-left text-xs">
          <thead>
            <tr className="border-b">
              <th className="py-2">Actor</th>
              <th>Total</th>
              <th>Aprob.</th>
              <th>Rech.</th>
              <th>Prom. h</th>
              <th>Min</th>
              <th>Max</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((row, idx) => (
              <tr key={`${row.actorLabel}-${idx}`} className="border-b">
                <td className="py-2">{row.actorLabel}</td>
                <td>{row.total}</td>
                <td>{row.approved}</td>
                <td>{row.rejected}</td>
                <td>{row.avgHours?.toFixed(1) ?? "—"}</td>
                <td>{row.minHours?.toFixed(1) ?? "—"}</td>
                <td>{row.maxHours?.toFixed(1) ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
