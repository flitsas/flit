"use client";

import { useEffect, useMemo, useState } from "react";
import {
  listPlateDetails,
  PLATE_STATE_LABELS,
  type PlateDetail,
  type PlateState,
} from "@/lib/api/admin-plate-ranges";

// HU #10653 (Feature #10587) — visualización de placas preasignadas por OT, con filtros.
export interface PlatePreassignViewerProps {
  tenantId: string;
}

const STATE_FILTERS: (PlateState | "")[] = ["", "disponible", "preasignada", "utilizada", "bloqueada", "revocada"];

export function PlatePreassignViewer({ tenantId }: PlatePreassignViewerProps) {
  const [plates, setPlates] = useState<PlateDetail[]>([]);
  const [stateFilter, setStateFilter] = useState<PlateState | "">("");
  const [officeFilter, setOfficeFilter] = useState<string>("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    listPlateDetails(tenantId, {
      state: stateFilter || undefined,
      signal: controller.signal,
    })
      .then(setPlates)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "Error al cargar placas");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [tenantId, stateFilter]);

  const offices = useMemo(
    () => [...new Set(plates.map((p) => p.transitOfficeId))].sort(),
    [plates],
  );

  const filtered = useMemo(
    () => (officeFilter ? plates.filter((p) => p.transitOfficeId === officeFilter) : plates),
    [plates, officeFilter],
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-xs font-semibold">
          Estado
          <select
            value={stateFilter}
            onChange={(e) => setStateFilter(e.target.value as PlateState | "")}
            className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          >
            {STATE_FILTERS.map((s) => (
              <option key={s || "all"} value={s}>
                {s ? PLATE_STATE_LABELS[s] : "Todos"}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs font-semibold">
          Organismo de tránsito
          <select
            value={officeFilter}
            onChange={(e) => setOfficeFilter(e.target.value)}
            className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          >
            <option value="">Todos</option>
            {offices.map((o) => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </select>
        </label>
      </div>

      {loading && <p className="text-xs opacity-60">Cargando placas…</p>}
      {error && (
        <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
      {!loading && !error && filtered.length === 0 && (
        <p className="text-xs opacity-60">No hay placas para los filtros seleccionados.</p>
      )}

      {!loading && !error && filtered.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b opacity-70">
                <th className="px-2 py-2">Placa</th>
                <th className="px-2 py-2">Estado</th>
                <th className="px-2 py-2">Organismo</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((p) => (
                <tr key={p.id} className="border-b border-black/5 dark:border-white/5">
                  <td className="px-2 py-2 font-mono font-semibold">{p.plate}</td>
                  <td className="px-2 py-2">{PLATE_STATE_LABELS[p.state]}</td>
                  <td className="px-2 py-2 font-mono opacity-70">{p.transitOfficeId}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
