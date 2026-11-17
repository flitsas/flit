'use client';

import { useEffect, useRef, useState } from 'react';
import { Icono } from "@flit/ui/Icono";
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
  onCatalogoVerificado,
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
  /** Avisa si el valor guardado pertenece al catálogo de la clase (true también si no hay valor). */
  onCatalogoVerificado?: (valor: string, enCatalogo: boolean) => void;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [options, setOptions] = useState<VehicleBodyworkOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Resultado de verificar el valor guardado contra el catálogo de la clase. Se ata al valor y la
  // clase verificados: si cambian, el resultado viejo no aplica (mientras tanto se muestra el valor).
  const [verificado, setVerificado] = useState<{
    valor: string;
    clase: string;
    ok: boolean;
  } | null>(null);
  const valorGuardado = value.trim();
  const fueraDeCatalogo =
    valorGuardado !== '' &&
    verificado !== null &&
    verificado.valor === valorGuardado &&
    verificado.clase === vehicleClass &&
    !verificado.ok;
  // Un valor que no pertenece al catálogo de la clase se muestra sin selección: el gestor elige.
  const display = fueraDeCatalogo ? '' : value || '';

  // Mantiene el callback fresco sin re-disparar la consulta.
  const onCatalogoRef = useRef(onCatalogoVerificado);
  useEffect(() => {
    onCatalogoRef.current = onCatalogoVerificado;
  });

  useEffect(() => {
    if (!valorGuardado) return;
    const ac = new AbortController();
    tramitesClient
      .searchVehicleBodyworks(vehicleClass || undefined, valorGuardado, 200, ac.signal)
      .then((items) => {
        if (ac.signal.aborted) return;
        const buscado = valorGuardado.toUpperCase();
        const ok = items.some((i) => i.name.trim().toUpperCase() === buscado);
        setVerificado({ valor: valorGuardado, clase: vehicleClass, ok });
        onCatalogoRef.current?.(valorGuardado, ok);
      })
      .catch(() => {
        // Sin red no se bloquea: se queda el valor guardado.
      });
    return () => ac.abort();
  }, [valorGuardado, vehicleClass]);

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
        // Solo se ofrece lo que está en el catálogo de la clase: un valor libre (p. ej. el que
        // manda una integración) no se inyecta como opción válida.
        setOptions(filtered);
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
        aria-invalid={invalid || fueraDeCatalogo || undefined}
        className="flex w-full items-center justify-between gap-2 rounded-xl border bg-card px-3 py-2 text-left text-xs outline-none transition focus:border-flit-brand focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-60"
        style={invalid || fueraDeCatalogo ?{ borderColor: 'var(--destructive)', borderWidth: 2 } : undefined}
      >
        <span className={display ? 'font-medium' : 'opacity-50'}>
          {display || 'Selecciona…'}
        </span>
        <Icono nombre="abajo" className="h-3.5 w-3.5 shrink-0 opacity-50" />
      </button>

      {open && !disabled && (
        <div
          className="absolute z-30 mt-1 w-full rounded-xl border bg-card p-2 shadow-[var(--kit-sombra)]"
          role="listbox"
          aria-label={label}
        >
          <div className="relative mb-2">
            <Icono nombre="buscar" className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40" />
            <input
              ref={inputRef}
              type="search"
              value={query}
              onChange={(e) => onQueryChange(e.target.value)}
              placeholder={placeholder}
              className="w-full rounded-lg border bg-transparent py-1.5 pl-8 pr-2 text-xs outline-none transition focus:border-flit-brand focus:ring-2 focus:ring-flit-brand/20"
              aria-label={`Buscar en ${label}`}
              autoComplete="off"
            />
          </div>
          <ul className="max-h-48 overflow-y-auto">
            {loading && options.length === 0 ? (
              <li className="px-2 py-2 text-xs opacity-55">Buscando…</li>
            ) : error ? (
              <li className="px-2 py-2 text-xs" style={{ color: 'var(--destructive)' }} role="alert">
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
                      className="w-full rounded-lg px-2 py-1.5 text-left text-xs hover:bg-[color-mix(in_srgb,var(--color-flit-brand)_8%,transparent)]"
                      style={
                        selected
                          ? { background: 'color-mix(in srgb, var(--color-flit-brand) 12%, transparent)', color: 'var(--color-flit-brand)', fontWeight: 600 }
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
