'use client';

import { useMemo, useRef, useState } from 'react';
import { ChevronDown, Search } from 'lucide-react';

/**
 * Selector de catálogo con buscador (dropdown list filtrable).
 * Reemplaza &lt;select&gt; nativos en transformaciones del vehículo.
 */
export function CatalogSearchSelect({
  id,
  label,
  value,
  options,
  disabled = false,
  placeholder = 'Buscar…',
  invalid = false,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  options: readonly string[];
  disabled?: boolean;
  placeholder?: string;
  /** Borde rojo de campo obligatorio sin valor. */
  invalid?: boolean;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((o) => o.toLowerCase().includes(q));
  }, [options, query]);

  const display = value || '';

  const pick = (opt: string) => {
    onChange(opt);
    setOpen(false);
    setQuery('');
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
        onClick={() => {
          if (disabled) return;
          setOpen((v) => !v);
          setTimeout(() => inputRef.current?.focus(), 0);
        }}
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
              onChange={(e) => setQuery(e.target.value)}
              placeholder={placeholder}
              className="w-full rounded-lg border bg-transparent py-1.5 pl-8 pr-2 text-xs outline-none transition focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              aria-label={`Buscar en ${label}`}
              autoComplete="off"
            />
          </div>
          <ul className="max-h-48 overflow-y-auto">
            {filtered.length === 0 ? (
              <li className="px-2 py-2 text-xs opacity-55">Sin coincidencias</li>
            ) : (
              filtered.map((opt) => {
                const selected = opt.toUpperCase() === display.toUpperCase();
                return (
                  <li key={opt}>
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
                      {opt}
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
