'use client';

import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { TransitOfficeOption } from '@/lib/api/types/procedure-runtime';
import { WIZARD_INPUT } from './wizard-field-styles';

/**
 * Selector de organismo/secretaría, en la forma de la propuesta: un campo de texto que al enfocarlo
 * despliega la lista debajo y se filtra escribiendo.
 *
 * Antes era un botón "Seleccionar" que abría un diálogo a pantalla completa con su propio buscador.
 * Elegir una secretaría no justifica sacar al gestor del formulario: es un campo más de la tarjeta
 * de radicación, y el diseño lo trata como tal.
 *
 * Patrón combobox de WAI-ARIA: el campo declara `role="combobox"` con `aria-expanded` y
 * `aria-activedescendant`, y la lista es un `listbox` navegable con flechas, Enter y Escape. El
 * ratón por sí solo no basta — este control gatea la consulta del paso 1.
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
  const [activo, setActivo] = useState(0);
  const wrapRef = useRef<HTMLDivElement>(null);
  const listId = useId();
  const inputId = useId();

  const selected = useMemo(
    () => offices.find((o) => o.id === valueId) ?? null,
    [offices, valueId],
  );

  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = q
      ? offices.filter(
          (o) => o.name.toLowerCase().includes(q) || o.code.toLowerCase().includes(q),
        )
      : offices;
    return list.slice(0, 40);
  }, [query, offices]);

  // Cerrar al pulsar fuera. Sin esto la lista queda abierta sobre el resto del formulario cuando el
  // gestor sigue a otro campo con el ratón.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  const pick = (org: TransitOfficeOption) => {
    onChange(org.id);
    setQuery('');
    setOpen(false);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') {
      setOpen(false);
      return;
    }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!open) {
        setOpen(true);
        return;
      }
      if (results.length === 0) return;
      const paso = e.key === 'ArrowDown' ? 1 : -1;
      setActivo((i) => (i + paso + results.length) % results.length);
      return;
    }
    if (e.key === 'Enter' && open && results[activo]) {
      e.preventDefault();
      pick(results[activo]);
    }
  };

  // El campo muestra lo elegido cuando está en reposo y lo tecleado mientras se busca, para que el
  // gestor no pierda de vista qué secretaría tiene puesta al abrir la lista sin escribir.
  const valorCampo = open ? query : (selected?.name ?? '');

  return (
    <div className="relative" ref={wrapRef}>
      <input
        id={inputId}
        type="text"
        role="combobox"
        // El rótulo visible lo pinta la tarjeta de radicación con su propia maqueta, así que no hay
        // <label htmlFor> que asociar: el nombre accesible viaja aquí.
        aria-label="Secretaría de tránsito"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && results[activo] ? `${listId}-${activo}` : undefined}
        aria-describedby={describedBy}
        value={valorCampo}
        disabled={disabled}
        placeholder="Escribe para buscar el organismo de tránsito"
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setQuery(e.target.value);
          setActivo(0);
          setOpen(true);
        }}
        onKeyDown={onKeyDown}
        className={WIZARD_INPUT}
      />

      {open && !disabled && (
        <div
          className="absolute z-30 mt-1 max-h-52 w-full overflow-y-auto rounded-xl border bg-white p-1 dark:bg-[#0B0F14]"
          style={{ boxShadow: '0 8px 24px rgba(15,23,20,0.12)' }}
        >
          <ul id={listId} role="listbox" aria-label="Organismos de tránsito">
            {loading && (
              <li className="px-3 py-2 text-xs opacity-60" role="status">
                Cargando organismos…
              </li>
            )}
            {!loading && results.length === 0 && (
              <li className="px-3 py-2 text-xs opacity-60">Sin resultados.</li>
            )}
            {!loading &&
              results.map((o, i) => (
                // El `role="option"` va en el ELEMENTO PULSABLE, no en el `<li>` que lo envuelve:
                // con el rol fuera, quien apunte a la opción —una prueba, un lector de pantalla al
                // activarla— pulsa un contenedor inerte y no pasa nada.
                <li key={o.id} role="presentation">
                  <button
                    type="button"
                    id={`${listId}-${i}`}
                    role="option"
                    aria-selected={o.id === valueId}
                    // La lista NO se cierra al perder el foco el campo —solo con un mousedown fuera
                    // del contenedor—, así que el clic llega entero.
                    onClick={() => pick(o)}
                    onMouseEnter={() => setActivo(i)}
                    className={`w-full rounded-lg px-3 py-2 text-left text-xs font-medium hover:bg-[#EFF6FF] dark:hover:bg-white/5 ${
                      i === activo ? 'bg-[#EFF6FF] dark:bg-white/5' : ''
                    }`}
                    style={{ color: '#162744' }}
                  >
                    {o.name}
                  </button>
                </li>
              ))}
          </ul>
        </div>
      )}
    </div>
  );
}
