"use client";

import { useEffect, useState } from "react";
import {
  fetchReportingProcedures,
  type ReportingProceduresPage,
} from "@/lib/api/reporting-v2";

export function TramitesV2Tab({
  from,
  to,
  tenantId,
}: {
  from: string;
  to: string;
  tenantId?: string;
}) {
  const [data, setData] = useState<ReportingProceduresPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    fetchReportingProcedures({ from, to, tenantId, search, page: 1, pageSize: 50 })
      .then((page) => {
        if (!cancelled) setData(page);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : "Error al cargar trámites");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [from, to, tenantId, search]);

  if (loading) {
    return <div className="p-4 text-sm opacity-70" aria-busy="true">Cargando trámites…</div>;
  }
  if (error) {
    return (
      <div className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700" role="alert">
        {error}
      </div>
    );
  }
  if (!data || data.items.length === 0) {
    return <div className="p-6 text-sm opacity-60">Sin datos para el período seleccionado</div>;
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <Kpi label="Total" value={data.kpis.total} />
        <Kpi label="Aprobados" value={data.kpis.approved} />
        <Kpi label="Rechazados" value={data.kpis.rejected} />
        <Kpi label="En proceso" value={data.kpis.inProgress} />
        <Kpi
          label="Prom. horas"
          value={data.kpis.avgElapsedHours != null ? data.kpis.avgElapsedHours.toFixed(1) : "—"}
        />
      </div>
      <input
        className="w-full rounded-lg border px-3 py-2 text-sm"
        placeholder="Buscar placa, VIN, documento…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        aria-label="Buscar trámites"
      />
      <div className="overflow-x-auto rounded-xl border">
        <table className="min-w-full text-left text-xs">
          <thead className="bg-black/5 dark:bg-white/5">
            <tr>
              <th className="px-3 py-2">Referencia</th>
              <th className="px-3 py-2">Tipo</th>
              <th className="px-3 py-2">Estado</th>
              <th className="px-3 py-2">Placa</th>
              <th className="px-3 py-2">OT</th>
              <th className="px-3 py-2">Creado</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((row) => (
              <tr key={row.id} className="border-t">
                <td className="px-3 py-2">{row.referenceNumber ?? "—"}</td>
                <td className="px-3 py-2">{row.procedureType ?? "—"}</td>
                <td className="px-3 py-2">{row.status ?? "—"}</td>
                <td className="px-3 py-2">{row.plate || "—"}</td>
                <td className="px-3 py-2">{row.transitOfficeName || "—"}</td>
                <td className="px-3 py-2">{new Date(row.createdAt).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="text-[11px] opacity-60">
        {data.totalCount} registros · página {data.page}
      </p>
    </div>
  );
}

function Kpi({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-xl border p-3">
      <div className="text-[10px] uppercase opacity-60">{label}</div>
      <div className="text-lg font-semibold">{value}</div>
    </div>
  );
}
