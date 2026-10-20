"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Loader2, Search } from "lucide-react";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";
import type { TransitOffice } from "@/lib/api/types";

export interface TransitGrantsPickerProps {
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  /** Mensaje de error de validación (p. ej. ningún OT elegido). */
  error?: string;
  disabled?: boolean;
  /**
   * `marca-blanca`: la selección es la lista de OT con acceso (lo no elegido se bloquea) y no elegir
   * ninguno deja la red con todos los OT activos de FLIT. Solo cambian los textos.
   */
  variant?: "concesion" | "marca-blanca";
  /** Recibe los OT operativos listados: la Marca Blanca bloquea los que queden sin elegir. */
  onOfficesLoaded?: (offices: TransitOffice[]) => void;
}

/** Selector embebido de organismos de tránsito (HU #12357 AC6). */
export function TransitGrantsPicker({
  selectedIds,
  onChange,
  error,
  disabled = false,
  variant = "concesion",
  onOfficesLoaded,
}: TransitGrantsPickerProps) {
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setLoadError(null);
    try {
      const [catalog, operational] = await Promise.all([
        fetchTransitOffices(undefined, signal),
        fetchTransitOfficesOperationalStatus(signal),
      ]);
      if (signal?.aborted) return;
      const activeIds = new Set(
        operational.filter((s) => s.hasTenant && s.estadoActivo === true).map((s) => s.id),
      );
      const active = catalog.filter((office) => activeIds.has(office.id));
      setOffices(active);
      onOfficesLoaded?.(active);
    } catch {
      if (!signal?.aborted) {
        setLoadError("No se pudo cargar el catálogo de organismos de tránsito.");
      }
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, [onOfficesLoaded]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
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
        o.cityCode.toLowerCase().includes(q) ||
        o.departmentCode.toLowerCase().includes(q),
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
      <legend className="text-xs font-semibold">
        {variant === "marca-blanca"
          ? "Organismos de tránsito de la Marca Blanca"
          : "Organismos de tránsito de la Concesión"}
      </legend>
      <p className="text-xs opacity-60">
        {variant === "marca-blanca"
          ? "Selecciona los organismos a los que tendrá acceso esta Marca Blanca; los demás quedan bloqueados para la red. Si no eliges ninguno, tendrá acceso a todos los organismos activos en FLIT. Podrás ajustarlo después desde la ficha."
          : "Selecciona los organismos que esta Concesión podrá usar. La Concesión y sus clientes hijos verán esta lista en solo lectura."}
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
          className="w-full rounded-xl border py-2 pl-8 pr-3 text-xs focus-visible:outline focus-visible:outline-2 focus-visible:outline-[#557EFF]"
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
          <p className="py-3 text-center text-xs opacity-60">
            {offices.length === 0
              ? "No hay organismos de tránsito activos en la plataforma."
              : "No hay organismos que coincidan con la búsqueda."}
          </p>
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
                    {office.cityCode && (
                      <span className="block text-xs opacity-50">{office.cityCode}</span>
                    )}
                  </span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </div>
      {error && (
        <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </fieldset>
  );
}
