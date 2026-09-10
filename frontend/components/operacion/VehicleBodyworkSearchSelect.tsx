'use client';

import { useEffect, useRef, useState } from 'react';
import { ChevronDown, Search } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';

export type VehicleBodyworkOption = {
  id: string;
  code: string;
  name: string;
  classVehicle: string | null;
};

/**
 * Selector de carrocerías desde el catálogo persistido (`GET /tramites/vehicle-bodyworks`).
 * Con clase del vehículo consultado: solo esa clase. Sin clase: filas de respaldo.
 * Persiste el `name` (valor FUR).
 */
export function VehicleBodyworkSearchSelect({
  id,
  label,
  value,
  vehicleClass,
  excludeName,
  disabled = false,
  placeholder = 'Selecciona…',
  invalid = false,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  /** Clase RUNT del vehículo consultado; vacío = respaldo sin clase. */
  vehicleClass: string;
  /** Valor RUNT: no se ofrece como “nuevo”. */
  excludeName?: string;
  disabled?: boolean;
  placeholder?: string;
  invalid?: boolean;
  onChange: (name: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [options, setOptions] = useState<VehicleBodyworkOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const display = value || '';

  const load = (term: string) => {
    abortRef.current?.abort();
    const ac = new AbortController();
    abortRef.current = ac;
    setLoading(true);
    setError(null);
    void tramitesClient
      .searchVehicleBodyworks(vehicleClass || undefined, term || undefined, 200, ac.signal)
      .then((items) => {
        if (ac.signal.aborted) return;
        const runt = excludeName?.trim().toUpperCase() ?? '';
        const filtered = runt
          ? items.filter((i) => i.name.trim().toUpperCase() !== runt)
          : items;
        const cur = value.trim();
        const merged =
          cur && !filtered.some((i) => i.name.toUpperCase() === cur.toUpperCase())
            ? [{ id: 'current', code: '—', name: cur.toUpperCase(), classVehicle: null }, ...filtered]
            : filtered;
        setOptions(merged);
      })
      .catch((err: unknown) => {
        if (ac.signal.aborted) return;
        setError(err instanceof Error ? err.message : 'No se pudo cargar el catálogo');
        setOptions([]);
      })
      .finally(() => {
        if (!ac.signal.aborted) setLoading(false);
      });
  };

  useEffect(() => {
    return () => {
      abortRef.current?.abort();
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  const onQueryChange = (next: string) => {
    setQuery(next);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => load(next), 250);
  };

  const pick = (opt: VehicleBodyworkOption) => {
    onChange(opt.name);
    setOpen(false);
    setQuery('');
  };

  const toggleOpen = () => {
    if (disabled) return;
    const next = !open;
    setOpen(next);
    if (next) {
      load(query);
      setTimeout(() => inputRef.current?.focus(), 0);
    } else {
      abortRef.current?.abort();
      if (debounceRef.current) clearTimeout(debounceRef.current);
    }
  };

  return (
    <div className="relative">
      <label htmlFor={id} className="block text-xs font-medium opacity-70 mb-1">
        {label}
      </label>
      <button
        type="button"
        id={id}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={label}
        onClick={toggleOpen}
        aria-invalid={invalid || undefined}
        className="flex w-full items-center justify-between gap-2 rounded-xl border bg-white px-3 py-2 text-left text-xs outline-none transition focus:border-[#557EFF] focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-60 dark:bg-[#162744]"
        style={invalid ? { borderColor: '#FF4E00', borderWidth: 2 } : undefined}
      >
        <span className={display ? 'font-medium' : 'opacity-50'}>
          {display || 'Selecciona…'}
        </span>
        <ChevronDown className="h-3.5 w-3.5 shrink-0 opacity-50" aria-hidden />
      </button>

      {open && !disabled && (
        <div
          className="absolute z-30 mt-1 w-full rounded-xl border bg-white p-2 shadow-lg dark:bg-[#162744]"
          role="listbox"
          aria-label={label}
        >
          <div className="relative mb-2">
            <Search
              className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40"
              aria-hidden
            />
            <input
              ref={inputRef}
              type="search"
              value={query}
              onChange={(e) => onQueryChange(e.target.value)}
              placeholder={placeholder}
              className="w-full rounded-lg border bg-transparent py-1.5 pl-8 pr-2 text-xs outline-none transition focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              aria-label={`Buscar en ${label}`}
              autoComplete="off"
            />
          </div>
          <ul className="max-h-48 overflow-y-auto">
            {loading && options.length === 0 ? (
              <li className="px-2 py-2 text-xs opacity-55">Buscando…</li>
            ) : error ? (
              <li className="px-2 py-2 text-xs" style={{ color: '#FF4E00' }} role="alert">
                {error}
              </li>
            ) : options.length === 0 ? (
              <li className="px-2 py-2 text-xs opacity-55">Sin coincidencias</li>
            ) : (
              options.map((opt) => {
                const selected = opt.name.toUpperCase() === display.toUpperCase();
                return (
                  <li key={`${opt.code}-${opt.name}-${opt.id}`}>
                    <button
                      type="button"
                      role="option"
                      aria-selected={selected}
                      onClick={() => pick(opt)}
                      className="w-full rounded-lg px-2 py-1.5 text-left text-xs hover:bg-[rgba(85,126,255,0.08)]"
                      style={
                        selected
                          ? { background: 'rgba(85,126,255,0.12)', color: '#557EFF', fontWeight: 600 }
                          : undefined
                      }
                    >
                      <span className="min-w-0 truncate font-medium">{opt.name}</span>
                    </button>
                  </li>
                );
              })
            )}
          </ul>
        </div>
      )}
    </div>
  );
}
