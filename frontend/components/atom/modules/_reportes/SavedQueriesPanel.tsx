"use client";

import { useCallback, useEffect, useId, useState } from "react";
import { Bookmark, X } from "lucide-react";
import {
  createSavedQuery,
  listSavedQueries,
  type SavedQuery,
} from "@/lib/api/reporting-v2";
import { useReportFilters, type ReportingV2Filters } from "./ReportFilterContext";
import { MAX_SAVED_QUERIES } from "./dashboardPreferences";

function filtersFromQuery(raw: unknown): Partial<ReportingV2Filters> {
  if (!raw || typeof raw !== "object") return {};
  const o = raw as Record<string, unknown>;
  const out: Partial<ReportingV2Filters> = {};
  if (typeof o.status === "string") out.status = o.status;
  if (typeof o.procedureType === "string") out.procedureType = o.procedureType;
  if (typeof o.from === "string") out.from = o.from;
  if (typeof o.to === "string") out.to = o.to;
  if (typeof o.dateType === "string") out.dateType = o.dateType;
  if (typeof o.search === "string") out.search = o.search;
  if (typeof o.tenantId === "string") out.tenantId = o.tenantId;
  if (typeof o.transitOfficeId === "string") out.transitOfficeId = o.transitOfficeId;
  return out;
}

export function SavedQueriesPanel({
  open,
  onClose,
  tenantId,
}: {
  open: boolean;
  onClose: () => void;
  tenantId?: string;
}) {
  const titleId = useId();
  const { filters, patchFilters } = useReportFilters();
  const [items, setItems] = useState<SavedQuery[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [limitMsg, setLimitMsg] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await listSavedQueries(tenantId);
      setItems(res.items);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar consultas");
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al abrir panel
    void reload();
  }, [open, reload]);

  const applyQuery = (query: SavedQuery) => {
    const patch = filtersFromQuery(query.filtersJson);
    patchFilters({ ...patch, page: 1 });
  };

  const saveCurrent = async () => {
    setLimitMsg(null);
    if (items.length >= MAX_SAVED_QUERIES) {
      setLimitMsg("Límite de consultas guardadas alcanzado");
      return;
    }
    const trimmed = name.trim();
    if (!trimmed) {
      setError("Indica un nombre para la consulta");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const created = await createSavedQuery({
        name: trimmed,
        filters: {
          from: filters.from,
          to: filters.to,
          dateType: filters.dateType,
          status: filters.status,
          procedureType: filters.procedureType,
          tenantId: filters.tenantId || tenantId,
          transitOfficeId: filters.transitOfficeId,
          search: filters.search,
        },
        tenantId,
      });
      setItems((prev) => [created, ...prev]);
      setName("");
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "No se pudo guardar la consulta");
    } finally {
      setSaving(false);
    }
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-40 flex justify-end bg-black/30"
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
    >
      <button type="button" className="flex-1 cursor-default" aria-label="Cerrar consultas" onClick={onClose} />
      <aside className="h-full w-full max-w-md overflow-y-auto border-l bg-white p-4 shadow-xl dark:bg-[#0B0F14]">
        <div className="mb-4 flex items-center justify-between gap-2">
          <h2 id={titleId} className="text-sm font-bold inline-flex items-center gap-2">
            <Bookmark className="h-4 w-4" aria-hidden />
            Consultas guardadas
          </h2>
          <button type="button" onClick={onClose} className="rounded-lg border p-1.5" aria-label="Cerrar">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mb-4 space-y-2 rounded-xl border p-3">
          <label className="block text-xs font-medium">
            Guardar consulta actual
            <input
              className="mt-1 w-full rounded-lg border px-2 py-1.5 text-sm"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Ej. Q-OT1"
              aria-label="Nombre de la consulta"
              data-testid="saved-query-name"
            />
          </label>
          <button
            type="button"
            disabled={saving}
            onClick={() => void saveCurrent()}
            className="rounded-lg border px-3 py-1.5 text-xs font-medium disabled:opacity-50"
            data-testid="saved-query-save"
          >
            Guardar consulta actual
          </button>
          {limitMsg && (
            <p className="text-xs text-amber-700" role="status" data-testid="saved-query-limit">
              {limitMsg}
            </p>
          )}
        </div>

        {loading && <p className="text-sm opacity-70">Cargando…</p>}
        {error && (
          <p className="mb-2 text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
        {!loading && items.length === 0 && (
          <p className="text-sm opacity-60">No hay consultas guardadas</p>
        )}
        <ul className="space-y-2">
          {items.map((q) => (
            <li key={q.id} className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium">{q.name}</p>
                {q.description && <p className="truncate text-[11px] opacity-60">{q.description}</p>}
              </div>
              <button
                type="button"
                className="shrink-0 rounded-lg border px-2 py-1 text-xs"
                onClick={() => applyQuery(q)}
                data-testid={`saved-query-apply-${q.id}`}
              >
                Aplicar
              </button>
            </li>
          ))}
        </ul>
      </aside>
    </div>
  );
}
