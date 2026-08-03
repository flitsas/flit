'use client';

import { useMemo, useState } from 'react';
import { Building2, Search, X } from 'lucide-react';
import type { TransitOfficeOption } from '@/lib/api/types/procedure-runtime';

const INPUT_BASE =
  'w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]';

/**
 * Selector de organismo/secretaría con buscador interno (mismo patrón del modal del paso FUR).
 * En el paso 1 de matrícula reemplaza el &lt;select&gt; nativo.
 */
export function TransitOfficeSearchPicker({
  offices,
  valueId,
  onChange,
  disabled = false,
  loading = false,
  describedBy,
}: {
  offices: TransitOfficeOption[];
  valueId: string;
  onChange: (id: string) => void;
  disabled?: boolean;
  loading?: boolean;
  describedBy?: string;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');

  const selected = useMemo(
    () => offices.find((o) => o.id === valueId) ?? null,
    [offices, valueId],
  );

  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = q
      ? offices.filter(
          (o) =>
            o.name.toLowerCase().includes(q) || o.code.toLowerCase().includes(q),
        )
      : offices;
    return list.slice(0, 40);
  }, [query, offices]);

  const pick = (org: TransitOfficeOption) => {
    onChange(org.id);
    setOpen(false);
    setQuery('');
  };

  return (
    <div className="space-y-2" aria-describedby={describedBy}>
      {!disabled && (
        <div>
          <button
            type="button"
            onClick={() => setOpen(true)}
            className="inline-flex shrink-0 items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[11px] font-semibold"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
            aria-label={
              selected ? 'Cambiar secretaría de tránsito' : 'Seleccionar secretaría de tránsito'
            }
          >
            <Building2 className="h-3 w-3" aria-hidden />
            {selected ? 'Cambiar' : 'Seleccionar'}
          </button>
        </div>
      )}
      {selected ? (
        <div
          className="flex min-w-0 items-center gap-3 rounded-xl border p-3"
          style={{ borderColor: '#8CC63F' }}
        >
          <Building2 className="h-4 w-4 shrink-0" style={{ color: '#5B8A1F' }} aria-hidden />
          <div className="min-w-0">
            <p className="truncate text-xs font-semibold" style={{ color: '#162744' }}>
              {selected.name}
            </p>
            <p className="truncate text-[11px] opacity-70">
              {[selected.cityCode, selected.code].filter(Boolean).join(' · ')}
            </p>
          </div>
        </div>
      ) : (
        <div
          className="rounded-xl border p-3 text-xs"
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#F9AC00' }}
          role="status"
        >
          Aún no has seleccionado la secretaría de tránsito.
        </div>
      )}

      {open && (
        <div
          className="fixed inset-0 z-50 grid place-items-center bg-black/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Seleccionar secretaría de tránsito"
        >
          <div className="flex max-h-[85vh] w-full max-w-lg flex-col rounded-2xl border bg-white p-6 dark:bg-[#0B0F14]">
            <div className="mb-3 flex items-start justify-between">
              <div>
                <h3 className="text-sm font-bold">Secretaría de tránsito</h3>
                <p className="text-[11px] opacity-70">
                  Busca y elige dónde se radicará el trámite.
                </p>
              </div>
              <button
                type="button"
                onClick={() => {
                  setOpen(false);
                  setQuery('');
                }}
                aria-label="Cerrar"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            <div className="relative mb-3">
              <Search
                className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-50"
                aria-hidden
              />
              <input
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Buscar por nombre o código…"
                aria-label="Buscar secretaría de tránsito"
                className={`${INPUT_BASE} pl-9`}
                autoFocus
              />
            </div>

            <ul className="space-y-1.5 overflow-y-auto" aria-label="Catálogo de organismos">
              {results.map((o) => (
                <li key={o.id}>
                  <button
                    type="button"
                    onClick={() => pick(o)}
                    className="w-full rounded-xl border p-2.5 text-left hover:border-[#557EFF]"
                  >
                    <p className="text-xs font-semibold">{o.name}</p>
                    <p className="text-[11px] opacity-70">
                      {[o.code, o.cityCode].filter(Boolean).join(' · ')}
                    </p>
                  </button>
                </li>
              ))}
              {loading && (
                <li className="py-3 text-center text-[11px] opacity-60">
                  Cargando organismos habilitados…
                </li>
              )}
              {!loading && offices.length === 0 && (
                <li className="py-3 text-center text-[11px]" style={{ color: '#F9AC00' }}>
                  Tu compañía no tiene organismos de tránsito habilitados. Contacta al
                  administrador para habilitarlos.
                </li>
              )}
              {!loading && offices.length > 0 && results.length === 0 && (
                <li className="py-3 text-center text-[11px] opacity-60">
                  Sin resultados para «{query}».
                </li>
              )}
            </ul>
          </div>
        </div>
      )}
    </div>
  );
}
