"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Link2, Loader2, Search } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { fetchCompaniesIndex, linkCompanyToParent } from "@/lib/api/admin-companies";
import { ApiValidationError, isHeadTenantType, tenantTypeLabel } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";

export interface LinkCompanyDialogProps {
  open: boolean;
  headTenantId: string;
  headName: string;
  /** IDs ya vinculados como hijos (para excluirlos). */
  linkedChildIds: string[];
  onClose: () => void;
  onLinked: (company: CompanyListItem) => void;
}

/** HU #12357 AC2 — vincular cliente existente sin padre ni tipo cabeza. */
export function LinkCompanyDialog({
  open,
  headTenantId,
  headName,
  linkedChildIds,
  onClose,
  onLinked,
}: LinkCompanyDialogProps) {
  const [candidates, setCandidates] = useState<CompanyListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setLoadError(null);
    try {
      const result = await fetchCompaniesIndex({ pageSize: 500, excludeTransitOffices: true }, signal);
      if (!signal?.aborted) {
        setCandidates(result.data);
      }
    } catch {
      if (!signal?.aborted) {
        setLoadError("No se pudo cargar el catálogo de compañías.");
      }
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    if (!open) return;
    setSelectedId(null);
    setSearch("");
    setError(null);
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [open, load]);

  const eligible = useMemo(() => {
    const linked = new Set(linkedChildIds);
    return candidates.filter(
      (c) =>
        c.id !== headTenantId &&
        !linked.has(c.id) &&
        !isHeadTenantType(c.tenantType) &&
        !c.isGroupParent &&
        !c.parentTenantId,
    );
  }, [candidates, headTenantId, linkedChildIds]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return eligible;
    return eligible.filter(
      (c) =>
        c.razonSocial.toLowerCase().includes(q) ||
        c.nit.toLowerCase().includes(q) ||
        c.code.toLowerCase().includes(q),
    );
  }, [eligible, search]);

  if (!open) return null;

  const submit = async () => {
    if (!selectedId) {
      setError("Selecciona un cliente para vincular.");
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const linked = await linkCompanyToParent(selectedId, headTenantId);
      onLinked(linked);
    } catch (err) {
      if (err instanceof ApiValidationError && err.errors[0]) {
        setError(err.errors[0].message);
      } else {
        setError("No se pudo vincular el cliente. Verifica que no tenga padre ni sea cabeza de grupo.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={submitting}
      icon={Link2}
      title="Vincular cliente existente"
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <p className="mb-3 text-xs opacity-80">
        El cliente quedará como hijo de <strong>{headName}</strong>. Solo se listan compañías sin padre
        y que no son cabeza de grupo.
      </p>

      {loading ? (
        <div className="flex items-center gap-2 py-6 text-xs opacity-70" role="status">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          Cargando compañías…
        </div>
      ) : loadError ? (
        <div className="space-y-2 py-4">
          <p className="text-xs" style={{ color: "#FF4E00" }} role="alert">
            {loadError}
          </p>
          <button type="button" onClick={() => void load()} className="rounded-lg border px-3 py-1.5 text-xs font-semibold">
            Reintentar
          </button>
        </div>
      ) : (
        <>
          <label htmlFor="link-search" className="sr-only">
            Buscar compañía
          </label>
          <div className="relative mb-2">
            <Search className="pointer-events-none absolute left-2.5 top-2 h-3.5 w-3.5 opacity-40" aria-hidden />
            <input
              id="link-search"
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Buscar por razón social, NIT o código…"
              className="w-full rounded-xl border py-2 pl-8 pr-3 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            />
          </div>
          <div
            className="max-h-52 overflow-y-auto rounded-xl border"
            role="listbox"
            aria-label="Clientes elegibles para vincular"
          >
            {filtered.length === 0 ? (
              <p className="py-6 text-center text-xs opacity-60">No hay clientes elegibles para vincular.</p>
            ) : (
              <ul>
                {filtered.map((c) => (
                  <li key={c.id}>
                    <button
                      type="button"
                      role="option"
                      aria-selected={selectedId === c.id}
                      onClick={() => setSelectedId(c.id)}
                      className="flex w-full flex-col gap-0.5 border-b px-3 py-2 text-left text-xs last:border-b-0 hover:bg-[#557EFF]/5"
                      style={{
                        background: selectedId === c.id ? "rgba(85,126,255,0.08)" : undefined,
                      }}
                    >
                      <span className="font-semibold">{c.razonSocial}</span>
                      <span className="font-mono opacity-70">{c.nit}</span>
                      <span className="text-[10px] opacity-50">
                        {tenantTypeLabel(c.tenantType)} · {c.code}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}

      {error && (
        <p className="mt-2 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}

      <div className="mt-4 flex justify-end gap-2">
        <button type="button" onClick={onClose} disabled={submitting} className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50">
          Cancelar
        </button>
        <button
          type="button"
          onClick={() => void submit()}
          disabled={submitting || loading || Boolean(loadError)}
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />}
          Vincular
        </button>
      </div>
    </Modal>
  );
}
