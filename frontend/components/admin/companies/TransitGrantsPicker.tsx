"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Loader2, Search } from "lucide-react";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import type { TransitOffice } from "@/lib/api/types";

export interface TransitGrantsPickerProps {
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  /** Mensaje de error de validación (p. ej. ningún OT elegido). */
  error?: string;
  disabled?: boolean;
}

/** Selector embebido de organismos de tránsito (HU #12357 AC6). */
export function TransitGrantsPicker({
  selectedIds,
  onChange,
  error,
  disabled = false,
}: TransitGrantsPickerProps) {
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setLoadError(null);
    try {
      const data = await fetchTransitOffices(undefined, signal);
      if (!signal?.aborted) {
        setOffices(data);
      }
    } catch {
      if (!signal?.aborted) {
        setLoadError("No se pudo cargar el catálogo de organismos de tránsito.");
      }
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return offices;
    return offices.filter(
      (o) =>
        o.name.toLowerCase().includes(q) ||
        o.code.toLowerCase().includes(q) ||
        o.municipality?.toLowerCase().includes(q),
    );
  }, [offices, search]);

  const selected = useMemo(() => new Set(selectedIds), [selectedIds]);

  const toggle = (id: string) => {
    if (disabled) return;
    onChange(selected.has(id) ? selectedIds.filter((x) => x !== id) : [...selectedIds, id]);
  };

  if (loading) {
    return (
      <div className="flex items-center gap-2 py-4 text-xs opacity-70" role="status">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        Cargando organismos de tránsito…
      </div>
    );
  }

  if (loadError) {
    return (
      <div className="space-y-2">
        <p className="text-xs" style={{ color: "#FF4E00" }} role="alert">
          {loadError}
        </p>
        <button
          type="button"
          onClick={() => void load()}
          className="rounded-lg border px-3 py-1.5 text-xs font-semibold"
        >
          Reintentar
        </button>
      </div>
    );
  }

  return (
    <fieldset className="space-y-2" disabled={disabled}>
      <legend className="text-xs font-semibold">Organismos de tránsito de la Concesión</legend>
      <p className="text-[10px] opacity-60">
        Selecciona los organismos que esta Concesión podrá usar. La Concesión y sus clientes hijos
        verán esta lista en solo lectura.
      </p>
      <label htmlFor="ot-grants-search" className="sr-only">
        Buscar organismo de tránsito
      </label>
      <div className="relative">
        <Search className="pointer-events-none absolute left-2.5 top-2 h-3.5 w-3.5 opacity-40" aria-hidden />
        <input
          id="ot-grants-search"
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar por nombre, código o municipio…"
          className="w-full rounded-xl border py-2 pl-8 pr-3 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
          style={{ borderColor: "#DFE5ED" }}
        />
      </div>
      <div
        className="max-h-48 overflow-y-auto rounded-xl border p-2"
        style={{ borderColor: error ? "#FF4E00" : "#DFE5ED" }}
        role="group"
        aria-label="Organismos de tránsito disponibles"
      >
        {filtered.length === 0 ? (
          <p className="py-3 text-center text-xs opacity-60">No hay organismos que coincidan con la búsqueda.</p>
        ) : (
          <ul className="space-y-1">
            {filtered.map((office) => (
              <li key={office.id}>
                <label className="flex cursor-pointer items-start gap-2 rounded-lg px-2 py-1.5 hover:bg-[#557EFF]/5">
                  <input
                    type="checkbox"
                    checked={selected.has(office.id)}
                    onChange={() => toggle(office.id)}
                    className="mt-0.5"
                    aria-label={`${office.name} (${office.code})`}
                  />
                  <span className="min-w-0 flex-1 text-xs">
                    <span className="font-semibold">{office.name}</span>
                    <span className="ml-1 font-mono opacity-60">{office.code}</span>
                    {office.municipality && (
                      <span className="block text-[10px] opacity-50">{office.municipality}</span>
                    )}
                  </span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </div>
      {error && (
        <p className="text-[10px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </fieldset>
  );
}
